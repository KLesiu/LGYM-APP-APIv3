using FluentAssertions;
using LgymApi.Application.Coaching;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Coaching.Progress.EloChart;
using LgymApi.Application.Coaching.Progress.ExerciseScoresChart;
using LgymApi.Application.Coaching.Progress.MainRecordsHistory;
using LgymApi.Application.Coaching.Progress.TrainingByDate;
using LgymApi.Application.Coaching.Progress.TrainingDates;
using LgymApi.Application.Coaching.Relationships.TrainerDashboard;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Pagination;
using LgymApi.Application.WorkoutProgress.Dashboard;
using LgymApi.Application.WorkoutProgress.Dashboard.Models;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CoachingDashboardProgressSliceTests
{
    [Test]
    public async Task Dashboard_BatchEnrichesBeforeSearchStatusAndPagination()
    {
        var trainerId = Id<User>.New();
        var matchingId = Id<User>.New();
        var missingId = Id<User>.New();
        var revokedId = Id<User>.New();
        var now = DateTimeOffset.UtcNow;
        var services = CreateServices(out _, out var facts, out var accounts, out var pagination, out _);
        var access = Resolve<ICoachingRelationshipAccessService>(services);
        access.GetAccessDecisionAsync(trainerId, Id<User>.Empty, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, false));
        facts.GetDashboardFactsAsync(trainerId, Arg.Any<CancellationToken>()).Returns(
        [
            DashboardFact(trainerId, matchingId, TrainerInvitationStatus.Pending, now.AddDays(1)),
            DashboardFact(trainerId, missingId, TrainerInvitationStatus.Pending, now.AddDays(1)),
            DashboardFact(trainerId, revokedId, TrainerInvitationStatus.Revoked, now.AddDays(1))
        ]);
        accounts.GetByIdsAsync(Arg.Any<IReadOnlyList<Id<User>>>(), Arg.Any<CancellationToken>()).Returns(
        [
            Account(matchingId, "MIXED active", now.AddDays(-3)),
            Account(revokedId, "MIXED revoked", now.AddDays(-2))
        ]);
        pagination.ExecuteAsync<TrainerDashboardTraineeReadModel>(
                Arg.Any<Func<IQueryable<TrainerDashboardTraineeReadModel>>>(),
                Arg.Any<FilterInput>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var rows = call.Arg<Func<IQueryable<TrainerDashboardTraineeReadModel>>>()().ToList();
                var filter = call.Arg<FilterInput>();
                return Result<Pagination<TrainerDashboardTraineeReadModel>, AppError>.Success(new Pagination<TrainerDashboardTraineeReadModel>
                {
                    Items = rows,
                    Page = filter.Page,
                    PageSize = filter.PageSize,
                    TotalCount = rows.Count
                });
            });

        var result = await Resolve<IGetTrainerDashboardUseCase>(services).ExecuteAsync(
            new GetTrainerDashboardQuery(trainerId, "mixed", "InvitationPending", "status", "asc", 2, 1));

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle(item => item.Id == matchingId);
        result.Value.TotalCount.Should().Be(1);
        await facts.Received(1).GetDashboardFactsAsync(trainerId, Arg.Any<CancellationToken>());
        await accounts.Received(1).GetByIdsAsync(
            Arg.Is<IReadOnlyList<Id<User>>>(ids => ids.SequenceEqual(new[] { matchingId, missingId, revokedId })),
            Arg.Any<CancellationToken>());
        await pagination.Received(1).ExecuteAsync<TrainerDashboardTraineeReadModel>(
            Arg.Any<Func<IQueryable<TrainerDashboardTraineeReadModel>>>(),
            Arg.Is<FilterInput>(filter => filter.Page == 2
                && filter.PageSize == 1
                && filter.SortDescriptors[0].FieldName == "statusOrder"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Dashboard_WhenCallerIsNotTrainerReturnsForbiddenWithoutReads()
    {
        var trainerId = Id<User>.New();
        var services = CreateServices(out _, out var facts, out var accounts, out var pagination, out _);
        var access = Resolve<ICoachingRelationshipAccessService>(services);
        access.GetAccessDecisionAsync(trainerId, Id<User>.Empty, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(false, false));

        var result = await Resolve<IGetTrainerDashboardUseCase>(services).ExecuteAsync(
            new GetTrainerDashboardQuery(trainerId));

        result.Error.Should().BeOfType<TrainerRelationshipForbiddenError>();
        await facts.DidNotReceive().GetDashboardFactsAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>());
        await accounts.DidNotReceive().GetByIdsAsync(Arg.Any<IReadOnlyList<Id<User>>>(), Arg.Any<CancellationToken>());
        await pagination.DidNotReceiveWithAnyArgs().ExecuteAsync<TrainerDashboardTraineeReadModel>(default!, default!, default);
    }

    [Test]
    public async Task ProgressReads_AuthorizeThenCallEachWorkoutProgressOperationExactlyOnce()
    {
        var trainerId = Id<AccountReference>.New();
        var traineeId = Id<AccountReference>.New();
        var exerciseId = Id<Exercise>.New();
        var createdAt = new DateTime(2026, 7, 1);
        var services = CreateServices(out var access, out _, out _, out _, out var progress);
        access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        progress.GetTrainingDatesAsync(traineeId, Arg.Any<CancellationToken>())
            .Returns(Result<List<DateTime>, AppError>.Success([createdAt]));
        progress.GetTrainingByDateAsync(traineeId, createdAt, Arg.Any<CancellationToken>())
            .Returns(Result<List<WorkoutProgressDashboardTrainingReadModel>, AppError>.Success([]));
        progress.GetExerciseScoreChartAsync(traineeId, exerciseId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result<List<ExerciseScoreChartPoint>, AppError>.Success([]));
        progress.GetEloChartAsync(traineeId, Arg.Any<CancellationToken>())
            .Returns(Result<List<EloChartPoint>, AppError>.Success([]));
        progress.GetMainRecordHistoryAsync(traineeId, Arg.Any<CancellationToken>())
            .Returns(Result<List<MainRecordReadModel>, AppError>.Success([]));

        (await Resolve<IGetTrainingDatesUseCase>(services).ExecuteAsync(new GetTrainingDatesQuery(trainerId, traineeId))).IsSuccess.Should().BeTrue();
        (await Resolve<IGetTrainingByDateUseCase>(services).ExecuteAsync(new GetTrainingByDateQuery(trainerId, traineeId, createdAt))).IsSuccess.Should().BeTrue();
        (await Resolve<IGetExerciseScoresChartUseCase>(services).ExecuteAsync(new GetExerciseScoresChartQuery(trainerId, traineeId, exerciseId))).IsSuccess.Should().BeTrue();
        (await Resolve<IGetEloChartUseCase>(services).ExecuteAsync(new GetEloChartQuery(trainerId, traineeId))).IsSuccess.Should().BeTrue();
        (await Resolve<IGetMainRecordsHistoryUseCase>(services).ExecuteAsync(new GetMainRecordsHistoryQuery(trainerId, traineeId))).IsSuccess.Should().BeTrue();

        await access.Received(5).GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>());
        await progress.Received(1).GetTrainingDatesAsync(traineeId, Arg.Any<CancellationToken>());
        await progress.Received(1).GetTrainingByDateAsync(traineeId, createdAt, Arg.Any<CancellationToken>());
        await progress.Received(1).GetExerciseScoreChartAsync(traineeId, exerciseId.ToString(), Arg.Any<CancellationToken>());
        await progress.Received(1).GetEloChartAsync(traineeId, Arg.Any<CancellationToken>());
        await progress.Received(1).GetMainRecordHistoryAsync(traineeId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProgressReads_WhenRelationshipIsForeignReturnNotFoundWithoutWorkoutProgressCalls()
    {
        var trainerId = Id<AccountReference>.New();
        var traineeId = Id<AccountReference>.New();
        var services = CreateServices(out var access, out _, out _, out _, out var progress);
        access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, false));

        var results = new AppError[]
        {
            (await Resolve<IGetTrainingDatesUseCase>(services).ExecuteAsync(new GetTrainingDatesQuery(trainerId, traineeId))).Error,
            (await Resolve<IGetTrainingByDateUseCase>(services).ExecuteAsync(new GetTrainingByDateQuery(trainerId, traineeId, DateTime.UtcNow))).Error,
            (await Resolve<IGetExerciseScoresChartUseCase>(services).ExecuteAsync(new GetExerciseScoresChartQuery(trainerId, traineeId, Id<Exercise>.New()))).Error,
            (await Resolve<IGetEloChartUseCase>(services).ExecuteAsync(new GetEloChartQuery(trainerId, traineeId))).Error,
            (await Resolve<IGetMainRecordsHistoryUseCase>(services).ExecuteAsync(new GetMainRecordsHistoryQuery(trainerId, traineeId))).Error
        };

        results.Should().OnlyContain(error => error is TrainerRelationshipNotFoundError);
        await progress.DidNotReceiveWithAnyArgs().GetTrainingDatesAsync(default, default);
        await progress.DidNotReceiveWithAnyArgs().GetTrainingByDateAsync(default, default, default);
        await progress.DidNotReceiveWithAnyArgs().GetExerciseScoreChartAsync(default, default!, default);
        await progress.DidNotReceiveWithAnyArgs().GetEloChartAsync(default, default);
        await progress.DidNotReceiveWithAnyArgs().GetMainRecordHistoryAsync(default, default);
    }

    [Test]
    public async Task ProgressReads_MapEveryDownstreamFailureToRelationshipNotFound()
    {
        var trainerId = Id<AccountReference>.New();
        var traineeId = Id<AccountReference>.New();
        var exerciseId = Id<Exercise>.New();
        var createdAt = DateTime.UtcNow;
        var services = CreateServices(out var access, out _, out _, out _, out var progress);
        access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        progress.GetTrainingDatesAsync(traineeId, Arg.Any<CancellationToken>()).Returns(Failure<DateTime>());
        progress.GetTrainingByDateAsync(traineeId, createdAt, Arg.Any<CancellationToken>()).Returns(Failure<WorkoutProgressDashboardTrainingReadModel>());
        progress.GetExerciseScoreChartAsync(traineeId, exerciseId.ToString(), Arg.Any<CancellationToken>()).Returns(Failure<ExerciseScoreChartPoint>());
        progress.GetEloChartAsync(traineeId, Arg.Any<CancellationToken>()).Returns(Failure<EloChartPoint>());
        progress.GetMainRecordHistoryAsync(traineeId, Arg.Any<CancellationToken>()).Returns(Failure<MainRecordReadModel>());

        var errors = new AppError[]
        {
            (await Resolve<IGetTrainingDatesUseCase>(services).ExecuteAsync(new GetTrainingDatesQuery(trainerId, traineeId))).Error,
            (await Resolve<IGetTrainingByDateUseCase>(services).ExecuteAsync(new GetTrainingByDateQuery(trainerId, traineeId, createdAt))).Error,
            (await Resolve<IGetExerciseScoresChartUseCase>(services).ExecuteAsync(new GetExerciseScoresChartQuery(trainerId, traineeId, exerciseId))).Error,
            (await Resolve<IGetEloChartUseCase>(services).ExecuteAsync(new GetEloChartQuery(trainerId, traineeId))).Error,
            (await Resolve<IGetMainRecordsHistoryUseCase>(services).ExecuteAsync(new GetMainRecordsHistoryQuery(trainerId, traineeId))).Error
        };

        errors.Should().OnlyContain(error => error is TrainerRelationshipNotFoundError && error.Message == "downstream failure");
    }

    [Test]
    public async Task ExerciseScoresChart_WhenExerciseIdIsEmptyReturnsInvalidWithoutWorkoutRead()
    {
        var trainerId = Id<AccountReference>.New();
        var traineeId = Id<AccountReference>.New();
        var services = CreateServices(out var access, out _, out _, out _, out var progress);
        access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));

        var result = await Resolve<IGetExerciseScoresChartUseCase>(services).ExecuteAsync(
            new GetExerciseScoresChartQuery(trainerId, traineeId, Id<Exercise>.Empty));

        result.Error.Should().BeOfType<InvalidTrainerRelationshipError>();
        await progress.DidNotReceiveWithAnyArgs().GetExerciseScoreChartAsync(default, default!, default);
    }

    private static Result<List<T>, AppError> Failure<T>()
        => Result<List<T>, AppError>.Failure(new BadRequestError("downstream failure"));

    private static ServiceCollection CreateServices(
        out IMarkerCoachingRelationshipAccessService access,
        out ICoachingFactReader facts,
        out IAccountReadService accounts,
        out IQueryPaginationService pagination,
        out IWorkoutProgressDashboardReadService progress)
    {
        access = Substitute.For<IMarkerCoachingRelationshipAccessService>();
        facts = Substitute.For<ICoachingFactReader>();
        accounts = Substitute.For<IAccountReadService>();
        pagination = Substitute.For<IQueryPaginationService>();
        progress = Substitute.For<IWorkoutProgressDashboardReadService>();
        var legacyRelationshipAccess = Substitute.For<ICoachingRelationshipAccessService>();
        var relationshipAccess = access;
        var factReader = facts;
        var accountReader = accounts;
        var paginationService = pagination;
        var progressService = progress;
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
        services.AddCoachingModule();
        services.AddScoped(_ => legacyRelationshipAccess);
        services.AddScoped(_ => relationshipAccess);
        services.AddScoped(_ => factReader);
        services.AddScoped(_ => accountReader);
        services.AddScoped(_ => paginationService);
        services.AddScoped(_ => progressService);
        return services;
    }

    private static TContract Resolve<TContract>(ServiceCollection services) where TContract : notnull
    {
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TContract>();
    }

    private static AccountReadModel Account(Id<User> id, string name, DateTimeOffset createdAt)
        => new(id, name, $"{id}@example.test", null, "en", "UTC", createdAt);

    private static CoachingDashboardFact DashboardFact(
        Id<User> trainerId,
        Id<User> traineeId,
        TrainerInvitationStatus status,
        DateTimeOffset expiresAt)
    {
        var timestamp = expiresAt.AddDays(-7);
        return new CoachingDashboardFact(
            traineeId,
            null,
            new CoachingInvitationFact(
                Id<TrainerInvitation>.New(),
                trainerId,
                $"{traineeId}@example.test",
                traineeId,
                "CODE00000001",
                status,
                expiresAt,
                null,
                timestamp,
                timestamp));
    }
}
