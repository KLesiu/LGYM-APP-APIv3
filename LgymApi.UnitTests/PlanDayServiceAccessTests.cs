using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.PlanDay;
using LgymApi.Application.Features.PlanDay.Models;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PlanDayServiceAccessTests
{
    [Test]
    public async Task CreatePlanDayAsync_WhenCurrentUserOwnsPlan_AllowsWithoutRelationshipLookup()
    {
        var ownerId = Id<User>.New();
        var harness = CreateHarness(ownerId);

        var result = await CreatePlanDayAsync(harness.Service, ownerId, harness.PlanId);

        result.IsSuccess.Should().BeTrue();
        await harness.RelationshipAccess.DidNotReceiveWithAnyArgs()
            .HasActiveRelationshipAsync(default, default);
        await harness.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreatePlanDayAsync_WhenLinkedTrainerAccessIsGranted_ForwardsIdsAndCancellation()
    {
        var trainerId = Id<User>.New();
        var ownerId = Id<User>.New();
        using var cancellation = new CancellationTokenSource();
        var harness = CreateHarness(ownerId);
        harness.RelationshipAccess
            .HasActiveRelationshipAsync(trainerId, ownerId, cancellation.Token)
            .Returns(true);

        var result = await CreatePlanDayAsync(harness.Service, trainerId, harness.PlanId, cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        await harness.RelationshipAccess.Received(1)
            .HasActiveRelationshipAsync(trainerId, ownerId, cancellation.Token);
        await harness.UnitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
    }

    [Test]
    public async Task CreatePlanDayAsync_WhenRelationshipPortDeniesUnlinkedOrNonTrainer_ReturnsForbiddenWithoutWrite()
    {
        var actorId = Id<User>.New();
        var ownerId = Id<User>.New();
        var harness = CreateHarness(ownerId);
        harness.RelationshipAccess
            .HasActiveRelationshipAsync(actorId, ownerId, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await CreatePlanDayAsync(harness.Service, actorId, harness.PlanId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<PlanDayForbiddenError>();
        await harness.PlanDayRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await harness.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task CreatePlanDayAsync_WhenTrainerIsLinkedToDifferentOwner_DoesNotGrantForeignPlanAccess()
    {
        var trainerId = Id<User>.New();
        var linkedOwnerId = Id<User>.New();
        var foreignOwnerId = Id<User>.New();
        var harness = CreateHarness(foreignOwnerId);
        harness.RelationshipAccess
            .HasActiveRelationshipAsync(trainerId, linkedOwnerId, Arg.Any<CancellationToken>())
            .Returns(true);
        harness.RelationshipAccess
            .HasActiveRelationshipAsync(trainerId, foreignOwnerId, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await CreatePlanDayAsync(harness.Service, trainerId, harness.PlanId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<PlanDayForbiddenError>();
        await harness.RelationshipAccess.Received(1)
            .HasActiveRelationshipAsync(trainerId, foreignOwnerId, Arg.Any<CancellationToken>());
        await harness.RelationshipAccess.DidNotReceive()
            .HasActiveRelationshipAsync(trainerId, linkedOwnerId, Arg.Any<CancellationToken>());
    }

    private static async Task<Result<Unit, AppError>> CreatePlanDayAsync(
        PlanDayService service,
        Id<User> currentUserId,
        Id<Plan> planId,
        CancellationToken cancellationToken = default)
    {
        return await service.CreatePlanDayAsync(
            new User { Id = currentUserId },
            planId,
            "Access test day",
            [new PlanDayExerciseInput { ExerciseId = Id<Exercise>.New(), Series = 3, Reps = "8" }],
            cancellationToken);
    }

    private static Harness CreateHarness(Id<User> ownerId)
    {
        var planId = Id<Plan>.New();
        var planRepository = Substitute.For<IPlanRepository>();
        planRepository.FindByIdAsync(planId, Arg.Any<CancellationToken>())
            .Returns(new Plan { Id = planId, UserId = ownerId });

        var relationshipAccess = Substitute.For<IPlanDayRelationshipAccessPort>();
        var planDayRepository = Substitute.For<IPlanDayRepository>();
        var planDayExerciseRepository = Substitute.For<IPlanDayExerciseRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        var dependencies = Substitute.For<IPlanDayServiceDependencies>();
        dependencies.PlanRepository.Returns(planRepository);
        dependencies.RelationshipAccess.Returns(relationshipAccess);
        dependencies.PlanDayRepository.Returns(planDayRepository);
        dependencies.PlanDayExerciseRepository.Returns(planDayExerciseRepository);
        dependencies.ExerciseRepository.Returns(Substitute.For<IExerciseRepository>());
        dependencies.TrainingRepository.Returns(Substitute.For<ITrainingRepository>());
        dependencies.UnitOfWork.Returns(unitOfWork);

        return new Harness(
            new PlanDayService(dependencies),
            planId,
            relationshipAccess,
            planDayRepository,
            unitOfWork);
    }

    private sealed record Harness(
        PlanDayService Service,
        Id<Plan> PlanId,
        IPlanDayRelationshipAccessPort RelationshipAccess,
        IPlanDayRepository PlanDayRepository,
        IUnitOfWork UnitOfWork);
}
