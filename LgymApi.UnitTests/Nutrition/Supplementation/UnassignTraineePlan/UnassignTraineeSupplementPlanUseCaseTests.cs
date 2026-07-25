using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Nutrition.Errors;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Nutrition.Supplementation.UnassignTraineePlan;
using LgymApi.Application.Nutrition.Supplementation.UnassignTraineePlan.Contracts;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories.Nutrition;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.UnitTests.Nutrition.Supplementation.UnassignTraineePlan;

[TestFixture]
public sealed class UnassignTraineeSupplementPlanUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_WhenActivePlanIsOwned_DeactivatesAndSavesOnce()
    {
        var dependencies = new Dependencies();
        var plan = CreatePlan(dependencies.TrainerId, dependencies.TraineeId, true);
        dependencies.GrantAccess();
        dependencies.Plans.GetTrackedActivePlanForTraineeAsync(dependencies.TraineeId, Arg.Any<CancellationToken>())
            .Returns(plan);

        var result = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command());

        result.IsSuccess.Should().BeTrue();
        plan.IsActive.Should().BeFalse();
        await dependencies.Plans.Received(1).GetTrackedActivePlanForTraineeAsync(
            dependencies.TraineeId,
            Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_WhenNoActivePlan_ReturnsSuccessWithoutSave()
    {
        var dependencies = new Dependencies();
        dependencies.GrantAccess();
        dependencies.Plans.GetTrackedActivePlanForTraineeAsync(dependencies.TraineeId, Arg.Any<CancellationToken>())
            .Returns((SupplementPlan?)null);

        var result = await dependencies.CreateUseCase().ExecuteAsync(dependencies.Command());

        result.IsSuccess.Should().BeTrue();
        await AssertNoSaveAsync(dependencies);
    }

    [Test]
    public async Task ExecuteAsync_WhenActivePlanBelongsToAnotherTrainer_ReturnsSuccessWithoutPersistedMutation()
    {
        var databaseName = $"supplement-plan-unassign-{Id<UnassignTraineeSupplementPlanUseCaseTests>.New():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options;
        var trainerId = Id<UserEntity>.New();
        var foreignTrainerId = Id<UserEntity>.New();
        var traineeId = Id<UserEntity>.New();
        var foreignPlan = CreatePlan(foreignTrainerId, traineeId, true);

        await using (var seedContext = new AppDbContext(options))
        {
            seedContext.SupplementPlans.Add(foreignPlan);
            await seedContext.SaveChangesAsync();
        }

        var access = Substitute.For<ICoachingRelationshipAccessService>();
        access.GetAccessDecisionAsync(trainerId, traineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        await using (var context = new AppDbContext(options))
        {
            var useCase = new UnassignTraineeSupplementPlanUseCase(
                access,
                new SupplementationPersistenceRepository(context),
                unitOfWork);

            var result = await useCase.ExecuteAsync(new(trainerId, traineeId));

            result.IsSuccess.Should().BeTrue();
        }

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        await using var readContext = new AppDbContext(options);
        var persistedPlan = await readContext.SupplementPlans
            .AsNoTracking()
            .SingleAsync(plan => plan.Id == foreignPlan.Id);
        persistedPlan.IsActive.Should().BeTrue();
    }

    [Test]
    public async Task ExecuteAsync_WhenAccessIsInvalid_PreservesErrorOrderWithoutPersistence()
    {
        var forbidden = new Dependencies();
        forbidden.Access.GetAccessDecisionAsync(forbidden.TrainerId, Id<UserEntity>.Empty, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(false, true));
        var forbiddenResult = await forbidden.CreateUseCase().ExecuteAsync(
            new(forbidden.TrainerId, Id<UserEntity>.Empty));

        forbiddenResult.Error.Should().BeOfType<SupplementationForbiddenError>();
        await AssertNoPersistenceAsync(forbidden);

        var emptyTrainee = new Dependencies();
        emptyTrainee.Access.GetAccessDecisionAsync(emptyTrainee.TrainerId, Id<UserEntity>.Empty, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        var emptyResult = await emptyTrainee.CreateUseCase().ExecuteAsync(
            new(emptyTrainee.TrainerId, Id<UserEntity>.Empty));

        emptyResult.Error.Should().BeOfType<InvalidSupplementationError>();
        await AssertNoPersistenceAsync(emptyTrainee);

        var noLink = new Dependencies();
        noLink.Access.GetAccessDecisionAsync(noLink.TrainerId, noLink.TraineeId, Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(true, false));
        var noLinkResult = await noLink.CreateUseCase().ExecuteAsync(noLink.Command());

        noLinkResult.Error.Should().BeOfType<SupplementationNotFoundError>();
        await AssertNoPersistenceAsync(noLink);
    }

    [Test]
    public async Task ExecuteAsync_ForwardsCancellationToAccessAndTrackedRead()
    {
        var dependencies = new Dependencies();
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        dependencies.Access.GetAccessDecisionAsync(dependencies.TrainerId, dependencies.TraineeId, cancellationToken)
            .Returns(new CoachingRelationshipAccessDecision(true, true));
        dependencies.Plans.GetTrackedActivePlanForTraineeAsync(dependencies.TraineeId, cancellationToken)
            .Returns((SupplementPlan?)null);

        var result = await dependencies.CreateUseCase().ExecuteAsync(
            dependencies.Command(),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        await dependencies.Access.Received(1).GetAccessDecisionAsync(
            dependencies.TrainerId,
            dependencies.TraineeId,
            cancellationToken);
        await dependencies.Plans.Received(1).GetTrackedActivePlanForTraineeAsync(
            dependencies.TraineeId,
            cancellationToken);
        await AssertNoSaveAsync(dependencies);
    }

    private static SupplementPlan CreatePlan(Id<UserEntity> trainerId, Id<UserEntity> traineeId, bool isActive)
        => new()
        {
            Id = Id<SupplementPlan>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            Name = "Supplement plan",
            IsActive = isActive
        };

    private static async Task AssertNoPersistenceAsync(Dependencies dependencies)
    {
        dependencies.Plans.ReceivedCalls().Should().BeEmpty();
        await AssertNoSaveAsync(dependencies);
    }

    private static async Task AssertNoSaveAsync(Dependencies dependencies)
    {
        await dependencies.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await dependencies.UnitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    private sealed class Dependencies
    {
        public Id<UserEntity> TrainerId { get; } = Id<UserEntity>.New();
        public Id<UserEntity> TraineeId { get; } = Id<UserEntity>.New();
        public ICoachingRelationshipAccessService Access { get; } = Substitute.For<ICoachingRelationshipAccessService>();
        public ISupplementationPersistence Plans { get; } = Substitute.For<ISupplementationPersistence>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

        public void GrantAccess()
            => Access.GetAccessDecisionAsync(TrainerId, TraineeId, Arg.Any<CancellationToken>())
                .Returns(new CoachingRelationshipAccessDecision(true, true));

        public UnassignTraineeSupplementPlanCommand Command()
            => new(TrainerId, TraineeId);

        public IUnassignTraineeSupplementPlanUseCase CreateUseCase()
            => new UnassignTraineeSupplementPlanUseCase(Access, Plans, UnitOfWork);
    }
}
