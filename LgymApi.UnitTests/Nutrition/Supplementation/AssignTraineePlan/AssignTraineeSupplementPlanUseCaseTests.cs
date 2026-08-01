using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Nutrition.Errors;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Nutrition.Supplementation.AssignTraineePlan;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories.Nutrition;
using LgymApi.Infrastructure.UnitOfWork;
using LgymApi.Resources;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace LgymApi.UnitTests.Nutrition.Supplementation.AssignTraineePlan;

[TestFixture]
public sealed class AssignTraineeSupplementPlanUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_WhenTargetIsInactive_DeactivatesSameOwnerCompetitorsAndPersistsRows()
    {
        var databaseName = $"supplement-assign-{Id<AssignTraineeSupplementPlanUseCaseTests>.New():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var target = CreatePlan(trainerId, traineeId, isActive: false);
        var competitor = CreatePlan(trainerId, traineeId, isActive: true);
        var unrelated = CreatePlan(Id<User>.New(), traineeId, isActive: true);

        await using (var seedContext = new AppDbContext(options))
        {
            seedContext.SupplementPlans.AddRange(target, competitor, unrelated);
            await seedContext.SaveChangesAsync();
        }

        var access = Substitute.For<ICoachingRelationshipAccessService>();
        access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        await using (var assignContext = new AppDbContext(options))
        {
            var useCase = new AssignTraineeSupplementPlanUseCase(
                access,
                new SupplementationPersistenceRepository(assignContext),
                new EfUnitOfWork(assignContext));

            var result = await useCase.ExecuteAsync(new(trainerId, traineeId, target.Id));

            result.IsSuccess.Should().BeTrue();
        }

        await using var readContext = new AppDbContext(options);
        var rows = await readContext.SupplementPlans.AsNoTracking().ToListAsync();
        rows.Single(row => row.Id == target.Id).IsActive.Should().BeTrue();
        rows.Single(row => row.Id == competitor.Id).IsActive.Should().BeFalse();
        rows.Single(row => row.Id == unrelated.Id).IsActive.Should().BeTrue();
    }

    [Test]
    public async Task ExecuteAsync_WhenTargetAlreadyOnlyActive_StillSavesAndLeavesInactiveRowsUnchanged()
    {
        var dependencies = new Dependencies();
        var target = CreatePlan(dependencies.TrainerId, dependencies.TraineeId, isActive: true);
        var inactive = CreatePlan(dependencies.TrainerId, dependencies.TraineeId, isActive: false);
        dependencies.GrantAccess();
        dependencies.Plans.FindTrackedPlanByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        dependencies.Plans.ListTrackedPlansByTrainerAndTraineeAsync(
                dependencies.TrainerId,
                dependencies.TraineeId,
                Arg.Any<CancellationToken>())
            .Returns([target, inactive]);
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        var result = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(target.Id));

        result.IsSuccess.Should().BeTrue();
        target.IsActive.Should().BeTrue();
        inactive.IsActive.Should().BeFalse();
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenAccessOrOwnershipFails_ReturnsFailureWithoutMutation()
    {
        var dependencies = new Dependencies();
        var target = CreatePlan(dependencies.TrainerId, dependencies.TraineeId);
        dependencies.Access.GetAccessDecisionAsync(
                dependencies.TrainerId,
                dependencies.TraineeId,
                Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(false, true));

        var forbidden = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(target.Id));

        forbidden.Error.Should().BeOfType<SupplementationForbiddenError>();
        dependencies.Plans.ReceivedCalls().Should().BeEmpty();

        dependencies.GrantAccess();
        dependencies.Plans.FindTrackedPlanByIdAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(CreatePlan(Id<User>.New(), dependencies.TraineeId));
        var foreign = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(target.Id));

        foreign.Error.Should().BeOfType<SupplementationNotFoundError>();
        await dependencies.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_ForwardsCancellationToEveryCollaborator()
    {
        var dependencies = new Dependencies();
        var cancellationToken = new CancellationTokenSource().Token;
        var target = CreatePlan(dependencies.TrainerId, dependencies.TraineeId);
        dependencies.GrantAccess(cancellationToken);
        dependencies.Plans.FindTrackedPlanByIdAsync(target.Id, cancellationToken).Returns(target);
        dependencies.Plans.ListTrackedPlansByTrainerAndTraineeAsync(
                dependencies.TrainerId,
                dependencies.TraineeId,
                cancellationToken)
            .Returns([target]);
        dependencies.UnitOfWork.SaveChangesAsync(cancellationToken).Returns(1);

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            dependencies.Command(target.Id),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        await dependencies.Access.Received(1).GetAccessDecisionAsync(
            dependencies.TrainerId,
            dependencies.TraineeId,
            cancellationToken);
        await dependencies.Plans.Received(1).FindTrackedPlanByIdAsync(target.Id, cancellationToken);
        await dependencies.Plans.Received(1).ListTrackedPlansByTrainerAndTraineeAsync(
            dependencies.TrainerId,
            dependencies.TraineeId,
            cancellationToken);
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(cancellationToken);
    }

    [Test]
    public async Task ExecuteAsync_WhenCandidateBelongsToAnotherTrainer_DoesNotDeactivateIt()
    {
        var dependencies = new Dependencies();
        var target = CreatePlan(dependencies.TrainerId, dependencies.TraineeId);
        var otherTrainerCandidate = CreatePlan(Id<User>.New(), dependencies.TraineeId, isActive: true);
        dependencies.GrantAccess();
        dependencies.Plans.FindTrackedPlanByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        dependencies.Plans.ListTrackedPlansByTrainerAndTraineeAsync(
                dependencies.TrainerId,
                dependencies.TraineeId,
                Arg.Any<CancellationToken>())
            .Returns([target, otherTrainerCandidate]);
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        var result = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(target.Id));

        result.IsSuccess.Should().BeTrue();
        target.IsActive.Should().BeTrue();
        otherTrainerCandidate.IsActive.Should().BeTrue();
    }

    [Test]
    public async Task ExecuteAsync_WhenSaveFails_PropagatesFailureAfterStagingSingleActiveMutation()
    {
        var dependencies = new Dependencies();
        var target = CreatePlan(dependencies.TrainerId, dependencies.TraineeId);
        var competitor = CreatePlan(dependencies.TrainerId, dependencies.TraineeId, isActive: true);
        dependencies.GrantAccess();
        dependencies.Plans.FindTrackedPlanByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        dependencies.Plans.ListTrackedPlansByTrainerAndTraineeAsync(
                dependencies.TrainerId,
                dependencies.TraineeId,
                Arg.Any<CancellationToken>())
            .Returns([target, competitor]);
        dependencies.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("save failed")));

        await FluentActions.Awaiting(() => dependencies.CreateUseCase().ExecuteAsync(dependencies.Command(target.Id)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("save failed");

        target.IsActive.Should().BeTrue();
        competitor.IsActive.Should().BeFalse();
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static SupplementPlan CreatePlan(
        Id<User> trainerId,
        Id<User> traineeId,
        bool isActive = false)
        => new()
        {
            Id = Id<SupplementPlan>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            Name = "Supplement plan",
            IsActive = isActive,
            IsDeleted = false
        };

    private sealed class Dependencies
    {
        public Id<User> TrainerId { get; } = Id<User>.New();
        public Id<User> TraineeId { get; } = Id<User>.New();
        public ICoachingRelationshipAccessService Access { get; } = Substitute.For<ICoachingRelationshipAccessService>();
        public ISupplementationPersistence Plans { get; } = Substitute.For<ISupplementationPersistence>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

        public void GrantAccess(CancellationToken cancellationToken = default)
            => Access.GetAccessDecisionAsync(TrainerId, TraineeId, cancellationToken)
                .Returns(new CoachingRelationshipAccessDecision(true, true));

        public AssignTraineeSupplementPlanCommand Command(Id<SupplementPlan> planId)
            => new(TrainerId, TraineeId, planId);

        public AssignTraineeSupplementPlanUseCase CreateUseCase()
            => new(Access, Plans, UnitOfWork);
    }
}
