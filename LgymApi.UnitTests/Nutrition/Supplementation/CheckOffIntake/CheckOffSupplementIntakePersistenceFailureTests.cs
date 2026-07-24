using FluentAssertions;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Models;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.UnitTests.Nutrition.Supplementation.CheckOffIntake;

[TestFixture]
public sealed class CheckOffSupplementIntakePersistenceFailureTests
{
    private const string IntakeLogUniqueIndexName = "IX_SupplementIntakeLogs_TraineeId_PlanItemId_IntakeDate";
    private static readonly DateOnly Monday = new(2026, 7, 27);

    [Test]
    public async Task ExecuteAsync_WhenIntakeUniqueConstraintHasWinner_ReloadsItWithoutRetryingSave()
    {
        var dependencies = new Dependencies();
        var item = dependencies.GrantActiveScheduledPlan();
        var winner = dependencies.ExistingLog(item.Id);
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(CreateUniqueViolation(IntakeLogUniqueIndexName)));
        dependencies.Plans.FindIntakeLogAsync(dependencies.TraineeId, item.Id, Monday, Arg.Any<CancellationToken>())
            .Returns(winner);

        var result = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(item.Id));

        result.IsSuccess.Should().BeTrue();
        result.Value.TakenAt.Should().Be(winner.TakenAt);
        await dependencies.Plans.Received(1).FindIntakeLogAsync(
            dependencies.TraineeId,
            item.Id,
            Monday,
            Arg.Any<CancellationToken>());
        dependencies.Plans.Received(1).DetachIntakeLog(Arg.Any<SupplementIntakeLog>());
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenIntakeUniqueConstraintHasNoWinner_RethrowsWithoutRetryingSave()
    {
        var dependencies = new Dependencies();
        var item = dependencies.GrantActiveScheduledPlan();
        var exception = CreateUniqueViolation(IntakeLogUniqueIndexName);
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(exception));
        dependencies.Plans.FindIntakeLogAsync(dependencies.TraineeId, item.Id, Monday, Arg.Any<CancellationToken>())
            .Returns((SupplementIntakeLog?)null);

        var thrown = await FluentActions.Awaiting(() => dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(item.Id)))
            .Should().ThrowAsync<DbUpdateException>();

        thrown.Which.Should().BeSameAs(exception);
        await dependencies.Plans.Received(1).FindIntakeLogAsync(
            dependencies.TraineeId,
            item.Id,
            Monday,
            Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenSqlStateIsNotUnique_PropagatesWithoutWinnerReload()
    {
        var dependencies = new Dependencies();
        var item = dependencies.GrantActiveScheduledPlan();
        var exception = CreatePostgresFailure(PostgresErrorCodes.ForeignKeyViolation, IntakeLogUniqueIndexName);
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(exception));

        var thrown = await FluentActions.Awaiting(() => dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(item.Id)))
            .Should().ThrowAsync<DbUpdateException>();

        thrown.Which.Should().BeSameAs(exception);
        await AssertNoWinnerReloadAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenProviderFailureIsNotPostgreSql_PropagatesWithoutWinnerReload()
    {
        var dependencies = new Dependencies();
        var item = dependencies.GrantActiveScheduledPlan();
        var exception = new DbUpdateException("provider failure", new InvalidOperationException());
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(exception));

        var thrown = await FluentActions.Awaiting(() => dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(item.Id)))
            .Should().ThrowAsync<DbUpdateException>();

        thrown.Which.Should().BeSameAs(exception);
        await AssertNoWinnerReloadAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenWinnerReloadThrows_PropagatesWithoutDetaching()
    {
        var dependencies = new Dependencies();
        var item = dependencies.GrantActiveScheduledPlan();
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(CreateUniqueViolation(IntakeLogUniqueIndexName)));
        dependencies.Plans.FindIntakeLogAsync(dependencies.TraineeId, item.Id, Monday, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SupplementIntakeLog?>(new InvalidOperationException()));

        await FluentActions.Awaiting(() => dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(item.Id)))
            .Should().ThrowAsync<InvalidOperationException>();

        dependencies.Plans.DidNotReceive().DetachIntakeLog(Arg.Any<SupplementIntakeLog>());
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenSaveFailsForAnotherConstraint_PropagatesWithoutWinnerReload()
    {
        var dependencies = new Dependencies();
        var item = dependencies.GrantActiveScheduledPlan();
        var exception = CreateUniqueViolation("IX_SupplementIntakeLogs_AnotherConstraint");
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(exception));

        var thrown = await FluentActions.Awaiting(() => dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(item.Id)))
            .Should().ThrowAsync<DbUpdateException>();

        thrown.Which.Should().BeSameAs(exception);
        await dependencies.Plans.DidNotReceive().FindIntakeLogAsync(
            Arg.Any<Id<UserEntity>>(),
            Arg.Any<Id<SupplementPlanItem>>(),
            Arg.Any<DateOnly>(),
            Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenSaveIsCanceled_PropagatesWithoutWinnerReload()
    {
        var dependencies = new Dependencies();
        var item = dependencies.GrantActiveScheduledPlan();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        dependencies.UnitOfWork.SaveChangesAsync(cancellationSource.Token)
            .Returns(Task.FromCanceled<int>(cancellationSource.Token));

        await FluentActions.Awaiting(() => dependencies.CreateUseCase().ExecuteAsync(
                dependencies.Command(item.Id),
                cancellationSource.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        await dependencies.Plans.DidNotReceive().FindIntakeLogAsync(
            Arg.Any<Id<UserEntity>>(),
            Arg.Any<Id<SupplementPlanItem>>(),
            Arg.Any<DateOnly>(),
            Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(cancellationSource.Token);
    }

    private static DbUpdateException CreateUniqueViolation(string constraintName)
        => CreatePostgresFailure(PostgresErrorCodes.UniqueViolation, constraintName);

    private static DbUpdateException CreatePostgresFailure(string sqlState, string constraintName)
    {
        var postgresException = new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            sqlState,
            constraintName: constraintName);
        return new DbUpdateException("duplicate intake log", postgresException);
    }

    private static async Task AssertNoWinnerReloadAsync(Dependencies dependencies)
    {
        await dependencies.Plans.DidNotReceive().FindIntakeLogAsync(
            Arg.Any<Id<UserEntity>>(),
            Arg.Any<Id<SupplementPlanItem>>(),
            Arg.Any<DateOnly>(),
            Arg.Any<CancellationToken>());
        dependencies.Plans.DidNotReceive().DetachIntakeLog(Arg.Any<SupplementIntakeLog>());
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private sealed class Dependencies
    {
        public Id<UserEntity> TraineeId { get; } = Id<UserEntity>.New();
        public ISupplementationPersistence Plans { get; } = Substitute.For<ISupplementationPersistence>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

        public SupplementPlanItem GrantActiveScheduledPlan()
        {
            var plan = new SupplementPlan
            {
                Id = Id<SupplementPlan>.New(),
                TraineeId = TraineeId,
                IsActive = true
            };
            var item = new SupplementPlanItem
            {
                Id = Id<SupplementPlanItem>.New(),
                PlanId = plan.Id,
                SupplementName = "Magnesium",
                Dosage = "1 tablet",
                Order = 1,
                DaysOfWeekMask = DaysOfWeekSet.Monday,
                TimeOfDay = new TimeSpan(8, 0, 0)
            };
            plan.Items.Add(item);
            Plans.GetActivePlanForTraineeAsync(TraineeId, Arg.Any<CancellationToken>()).Returns(plan);
            return item;
        }

        public SupplementIntakeLog ExistingLog(Id<SupplementPlanItem> planItemId)
            => new()
            {
                Id = Id<SupplementIntakeLog>.New(),
                TraineeId = TraineeId,
                PlanItemId = planItemId,
                IntakeDate = Monday,
                TakenAt = new DateTimeOffset(2026, 7, 27, 8, 15, 0, TimeSpan.Zero)
            };

        public CheckOffSupplementIntakeCommand Command(Id<SupplementPlanItem> planItemId)
            => new(TraineeId, planItemId, Monday, null);

        public CheckOffSupplementIntakeUseCase CreateUseCase()
            => new(Plans, UnitOfWork);
    }
}
