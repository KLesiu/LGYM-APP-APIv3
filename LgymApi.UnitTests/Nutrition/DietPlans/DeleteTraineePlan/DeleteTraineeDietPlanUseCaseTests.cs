using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.DietPlans;
using LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan.Models;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories.Nutrition;
using LgymApi.Infrastructure.UnitOfWork;
using LgymApi.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests.Nutrition.DietPlans.DeleteTraineePlan;

[TestFixture]
public sealed class DeleteTraineeDietPlanUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_WhenPlanIsOwned_SoftDeletesStagesDeletedSnapshotAndSavesOnce()
    {
        var dependencies = new Dependencies();
        var plan = CreatePlan(dependencies.TrainerId, dependencies.TraineeId, isActive: true);
        var operations = new List<string>();
        DietPlanHistory? history = null;
        dependencies.GrantAccess(operations);
        dependencies.Plans.FindTrackedPlanByIdAsync(plan.Id, Arg.Any<CancellationToken>())
            .Returns(plan)
            .AndDoes(_ => operations.Add("plan"));
        dependencies.Plans.AddHistoryEntryAsync(Arg.Any<DietPlanHistory>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                history = call.Arg<DietPlanHistory>();
                operations.Add("history");
            });
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(1)
            .AndDoes(_ => operations.Add("save"));

        var result = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(plan.Id));

        result.IsSuccess.Should().BeTrue();
        plan.IsDeleted.Should().BeTrue();
        plan.IsActive.Should().BeFalse();
        history.Should().NotBeNull();
        history!.ChangeType.Should().Be("Deleted");
        history.SnapshotJson.Should().Contain("\"IsActive\":false");
        operations.Should().Equal("access", "plan", "history", "save");
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenTraineeIsEmpty_ReturnsBadRequestBeforeAccess()
    {
        var dependencies = new Dependencies();

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            new(dependencies.TrainerId, Id<User>.Empty, Id<DietPlan>.New()));

        result.Error.Should().BeOfType<BadRequestError>().Which.Message.Should().Be(Messages.UserIdRequired);
        await dependencies.Access.DidNotReceive().GetAccessDecisionAsync(
            Arg.Any<Id<User>>(), Arg.Any<Id<User>>(), Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenAccessIsForbiddenOrMissing_ReturnsExpectedErrorBeforePlanRead()
    {
        var forbidden = new Dependencies();
        forbidden.Access.GetAccessDecisionAsync(forbidden.TrainerId, forbidden.TraineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(false, true));
        var forbiddenResult = await forbidden.CreateUseCase().ExecuteAsync(forbidden.Command(Id<DietPlan>.New()));

        forbiddenResult.Error.Should().BeOfType<TrainerRelationshipForbiddenError>();
        await forbidden.Plans.DidNotReceive().FindTrackedPlanByIdAsync(Arg.Any<Id<DietPlan>>(), Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(forbidden);

        var missing = new Dependencies();
        missing.Access.GetAccessDecisionAsync(missing.TrainerId, missing.TraineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, false));
        var missingResult = await missing.CreateUseCase().ExecuteAsync(missing.Command(Id<DietPlan>.New()));

        missingResult.Error.Should().BeOfType<NotFoundError>().Which.Message.Should().Be(Messages.DidntFind);
        await missing.Plans.DidNotReceive().FindTrackedPlanByIdAsync(Arg.Any<Id<DietPlan>>(), Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(missing);
    }

    [Test]
    public async Task ExecuteAsync_WhenPlanIdentifierOrOwnershipIsUnavailable_ReturnsFailureWithoutWrites()
    {
        var emptyPlan = new Dependencies();
        emptyPlan.GrantAccess();
        var emptyResult = await emptyPlan.CreateUseCase().ExecuteAsync(emptyPlan.Command(Id<DietPlan>.Empty));

        emptyResult.Error.Should().BeOfType<BadRequestError>().Which.Message.Should().Be(Messages.FieldRequired);
        await emptyPlan.Plans.DidNotReceive().FindTrackedPlanByIdAsync(Arg.Any<Id<DietPlan>>(), Arg.Any<CancellationToken>());
        await AssertNoWritesAsync(emptyPlan);

        var missing = new Dependencies();
        missing.GrantAccess();
        missing.Plans.FindTrackedPlanByIdAsync(Arg.Any<Id<DietPlan>>(), Arg.Any<CancellationToken>()).Returns((DietPlan?)null);
        var missingResult = await missing.CreateUseCase().ExecuteAsync(missing.Command(Id<DietPlan>.New()));
        missingResult.Error.Should().BeOfType<NotFoundError>();
        await AssertNoWritesAsync(missing);

        var foreign = new Dependencies();
        foreign.GrantAccess();
        foreign.Plans.FindTrackedPlanByIdAsync(Arg.Any<Id<DietPlan>>(), Arg.Any<CancellationToken>())
            .Returns(CreatePlan(Id<User>.New(), foreign.TraineeId));
        var foreignResult = await foreign.CreateUseCase().ExecuteAsync(foreign.Command(Id<DietPlan>.New()));
        foreignResult.Error.Should().BeOfType<NotFoundError>();
        await AssertNoWritesAsync(foreign);
    }

    [Test]
    public async Task ExecuteAsync_WhenSaveFails_PropagatesFailureAfterStaging()
    {
        var dependencies = new Dependencies();
        var plan = CreatePlan(dependencies.TrainerId, dependencies.TraineeId);
        dependencies.GrantAccess();
        dependencies.Plans.FindTrackedPlanByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        dependencies.Plans.AddHistoryEntryAsync(Arg.Any<DietPlanHistory>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("commit failed")));

        Func<Task> act = () => dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(plan.Id));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("commit failed");
        plan.IsDeleted.Should().BeTrue();
        await dependencies.Plans.Received(1).AddHistoryEntryAsync(Arg.Any<DietPlanHistory>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenTrackedReadReturnsDeletedPlan_RejectsVisibleIgnoreFilterAdversary()
    {
        var dependencies = new Dependencies();
        dependencies.GrantAccess();
        dependencies.Plans.FindTrackedPlanByIdAsync(Arg.Any<Id<DietPlan>>(), Arg.Any<CancellationToken>())
            .Returns(CreatePlan(dependencies.TrainerId, dependencies.TraineeId, isDeleted: true));

        var result = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(Id<DietPlan>.New()));

        result.Error.Should().BeOfType<NotFoundError>();
        await AssertNoWritesAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenPersistedPlanIsDeleted_HidesItFromNormalReadAndPreservesUnrelatedRow()
    {
        var databaseName = $"diet-plan-delete-{Id<DeleteTraineeDietPlanUseCaseTests>.New():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = CreatePlan(trainerId, traineeId, isActive: true);
        var unrelatedPlan = CreatePlan(trainerId, traineeId, isActive: true);

        await using (var seedContext = new AppDbContext(options))
        {
            seedContext.DietPlans.AddRange(plan, unrelatedPlan);
            await seedContext.SaveChangesAsync();
        }

        var access = Substitute.For<ICoachingRelationshipAccessService>();
        access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        await using (var deleteContext = new AppDbContext(options))
        {
            var useCase = new DeleteTraineeDietPlanUseCase(
                access,
                new DietPlanPersistenceRepository(deleteContext),
                new DietPlanHistorySnapshotFactory(CreateMapper()),
                new EfUnitOfWork(deleteContext));

            var result = await useCase.ExecuteAsync(new(trainerId, traineeId, plan.Id));

            result.IsSuccess.Should().BeTrue();
        }

        await using var readContext = new AppDbContext(options);
        var repository = new DietPlanPersistenceRepository(readContext);
        var normalRead = await repository.GetPlanByIdAsync(plan.Id);
        normalRead.Should().BeNull();

        var allRows = await readContext.DietPlans
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync();
        allRows.Should().HaveCount(2);
        allRows.Single(row => row.Id == plan.Id).IsDeleted.Should().BeTrue();
        allRows.Single(row => row.Id == plan.Id).IsActive.Should().BeFalse();
        allRows.Single(row => row.Id == unrelatedPlan.Id).IsDeleted.Should().BeFalse();
        allRows.Single(row => row.Id == unrelatedPlan.Id).IsActive.Should().BeTrue();
    }

    private static DietPlan CreatePlan(Id<User> trainerId, Id<User> traineeId, bool isActive = false, bool isDeleted = false)
        => new()
        {
            Id = Id<DietPlan>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            Name = "Nutrition plan",
            IsActive = isActive,
            IsDeleted = isDeleted
        };

    private static async Task AssertNoWritesAsync(Dependencies dependencies)
    {
        await dependencies.Plans.DidNotReceive().AddHistoryEntryAsync(Arg.Any<DietPlanHistory>(), Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static IMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(typeof(IMappingProfile).Assembly);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMapper>();
    }

    private sealed class Dependencies
    {
        public Id<User> TrainerId { get; } = Id<User>.New();
        public Id<User> TraineeId { get; } = Id<User>.New();
        public ICoachingRelationshipAccessService Access { get; } = Substitute.For<ICoachingRelationshipAccessService>();
        public IDietPlanPersistence Plans { get; } = Substitute.For<IDietPlanPersistence>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

        public void GrantAccess(List<string>? operations = null)
            => Access.GetAccessDecisionAsync(TrainerId, TraineeId, Arg.Any<CancellationToken>())
                .Returns(new CoachingRelationshipAccessDecision(true, true))
                .AndDoes(_ => operations?.Add("access"));

        public DeleteTraineeDietPlanCommand Command(Id<DietPlan> planId)
            => new(TrainerId, TraineeId, planId);

        public DeleteTraineeDietPlanUseCase CreateUseCase()
            => new(Access, Plans, new DietPlanHistorySnapshotFactory(DeleteTraineeDietPlanUseCaseTests.CreateMapper()), UnitOfWork);
    }
}
