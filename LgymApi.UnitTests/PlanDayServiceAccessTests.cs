using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.TrainingPlanning.PlanDay;
using LgymApi.Application.TrainingPlanning.PlanDay.Persistence;
using LgymApi.Application.Repositories;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning;
using LgymApi.TrainingPlanning.Contracts;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PlanDayServiceAccessTests
{
    [Test]
    public async Task CreateAsync_WhenCurrentAccountOwnsPlan_AllowsWithoutRelationshipLookup()
    {
        var ownerId = Id<AccountReference>.New();
        var harness = CreateHarness(ownerId);

        var result = await CreateAsync(harness.Service, ownerId, harness.PlanId);

        result.IsSuccess.Should().BeTrue();
        await harness.RelationshipAccess.DidNotReceiveWithAnyArgs().HasActiveRelationshipAsync(default, default);
        await harness.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateAsync_WhenLinkedTrainerAccessIsGranted_ForwardsMarkerIdsAndCancellation()
    {
        var trainerId = Id<AccountReference>.New();
        var ownerId = Id<AccountReference>.New();
        using var cancellation = new CancellationTokenSource();
        var harness = CreateHarness(ownerId);
        harness.RelationshipAccess.HasActiveRelationshipAsync(trainerId, ownerId, cancellation.Token).Returns(true);

        var result = await CreateAsync(harness.Service, trainerId, harness.PlanId, cancellation.Token);

        result.IsSuccess.Should().BeTrue();
        await harness.RelationshipAccess.Received(1).HasActiveRelationshipAsync(trainerId, ownerId, cancellation.Token);
        await harness.UnitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
    }

    [Test]
    public async Task CreateAsync_WhenRelationshipPortDeniesForeignAccess_ReturnsForbiddenWithoutWrite()
    {
        var actorId = Id<AccountReference>.New();
        var ownerId = Id<AccountReference>.New();
        var harness = CreateHarness(ownerId);
        harness.RelationshipAccess.HasActiveRelationshipAsync(actorId, ownerId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateAsync(harness.Service, actorId, harness.PlanId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<PlanDayForbiddenError>();
        harness.Persistence.CreateCalls.Should().BeEmpty();
        await harness.UnitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task CreateAsync_WhenTrainerIsLinkedToDifferentOwner_DoesNotGrantForeignPlanAccess()
    {
        var trainerId = Id<AccountReference>.New();
        var linkedOwnerId = Id<AccountReference>.New();
        var foreignOwnerId = Id<AccountReference>.New();
        var harness = CreateHarness(foreignOwnerId);
        harness.RelationshipAccess.HasActiveRelationshipAsync(trainerId, linkedOwnerId, Arg.Any<CancellationToken>()).Returns(true);
        harness.RelationshipAccess.HasActiveRelationshipAsync(trainerId, foreignOwnerId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateAsync(harness.Service, trainerId, harness.PlanId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<PlanDayForbiddenError>();
        await harness.RelationshipAccess.Received(1).HasActiveRelationshipAsync(trainerId, foreignOwnerId, Arg.Any<CancellationToken>());
        await harness.RelationshipAccess.DidNotReceive().HasActiveRelationshipAsync(trainerId, linkedOwnerId, Arg.Any<CancellationToken>());
    }

    [Test]
    public void TrainingPlanningModule_RegistersMarkerOnlyPlanDayService()
    {
        var services = new ServiceCollection();
        services.AddTrainingPlanningModule();
        services.AddScoped<IPlanDayPersistence>(_ => new PlanDayPersistenceFake());
        services.AddScoped(_ => Substitute.For<IPlanDayRelationshipAccessPort>());
        services.AddScoped(_ => Substitute.For<IPlanExerciseCatalogPort>());
        services.AddScoped(_ => Substitute.For<IPlanTrainingActivityPort>());
        services.AddScoped(_ => Substitute.For<IUnitOfWork>());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IPlanDayService>().Should().NotBeNull();
    }

    private static Task<Result<Unit, AppError>> CreateAsync(
        IPlanDayService service,
        Id<AccountReference> currentAccountId,
        Id<PlanReference> planId,
        CancellationToken cancellationToken = default)
        => service.CreateAsync(
            new CreatePlanDayCommand(
                currentAccountId,
                planId,
                new PlanDayWriteModel("Access test day", [new PlanDayExerciseWriteModel(Id<PlanExerciseReference>.New(), 3, "8")])),
            cancellationToken);

    private static Harness CreateHarness(Id<AccountReference> ownerId)
    {
        var planId = Id<PlanReference>.New();
        var persistence = new PlanDayPersistenceFake
        {
            Plan = new PlanDayPlanPersistenceModel(planId, ownerId)
        };

        var relationshipAccess = Substitute.For<IPlanDayRelationshipAccessPort>();
        var exerciseCatalog = Substitute.For<IPlanExerciseCatalogPort>();
        var trainingActivity = Substitute.For<IPlanTrainingActivityPort>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        return new Harness(
            new PlanDayService(persistence, relationshipAccess, exerciseCatalog, trainingActivity, unitOfWork),
            planId,
            persistence,
            relationshipAccess,
            unitOfWork);
    }

    private sealed record Harness(
        IPlanDayService Service,
        Id<PlanReference> PlanId,
        PlanDayPersistenceFake Persistence,
        IPlanDayRelationshipAccessPort RelationshipAccess,
        IUnitOfWork UnitOfWork);
}

internal sealed class PlanDayPersistenceFake : IPlanDayPersistence
{
    public PlanDayPlanPersistenceModel? Plan { get; set; }
    public IReadOnlyList<PlanDayPersistenceModel> PlanDaysByIds { get; set; } = [];
    public List<(IReadOnlyList<Id<PlanDayReference>> PlanDayIds, CancellationToken CancellationToken)> GetPlanDaysByIdsCalls { get; } = [];
    public List<(Id<PlanReference> PlanId, PlanDayWriteModel Input, CancellationToken CancellationToken)> CreateCalls { get; } = [];

    public Task<PlanDayPlanPersistenceModel?> FindPlanAsync(
        Id<PlanReference> planId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Plan);

    public Task<PlanDayPlanPersistenceModel?> FindActivePlanAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<PlanDayPlanPersistenceModel?>(null);

    public Task<PlanDayPersistenceModel?> FindPlanDayAsync(
        Id<PlanDayReference> planDayId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<PlanDayPersistenceModel?>(null);

    public Task<IReadOnlyList<PlanDayPersistenceModel>> GetPlanDaysAsync(
        Id<PlanReference> planId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PlanDayPersistenceModel>>([]);

    public Task<IReadOnlyList<PlanDayPersistenceModel>> GetPlanDaysByIdsAsync(
        IReadOnlyCollection<Id<PlanDayReference>> planDayIds,
        CancellationToken cancellationToken = default)
    {
        GetPlanDaysByIdsCalls.Add((planDayIds.ToArray(), cancellationToken));
        return Task.FromResult(PlanDaysByIds);
    }

    public Task<IReadOnlyList<PlanDayExercisePersistenceModel>> GetPlanDayExercisesAsync(
        IReadOnlyCollection<Id<PlanDayReference>> planDayIds,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PlanDayExercisePersistenceModel>>([]);

    public Task CreatePlanDayAsync(
        Id<PlanReference> planId,
        PlanDayWriteModel input,
        CancellationToken cancellationToken = default)
    {
        CreateCalls.Add((planId, input, cancellationToken));
        return Task.CompletedTask;
    }

    public Task UpdatePlanDayAsync(
        Id<PlanDayReference> planDayId,
        string name,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ReplacePlanDayExercisesAsync(
        Id<PlanDayReference> planDayId,
        IReadOnlyList<PlanDayExerciseWriteModel> exercises,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task MarkPlanDayDeletedAsync(
        Id<PlanDayReference> planDayId,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
