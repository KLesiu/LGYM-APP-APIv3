using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.TrainingPlanning;
using LgymApi.Application.TrainingPlanning.Plan.ActivePlanPointer;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning;
using LgymApi.TrainingPlanning.Contracts;
using LgymApi.Resources;
using LgymApi.TestUtils.Fakes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ManagedPlanContractRegistrationTests
{
    [Test]
    public void ManagedPlanContracts_ArePublic()
    {
        typeof(IGetManagedPlansUseCase).IsPublic.Should().BeTrue();
        typeof(ICreateManagedPlanUseCase).IsPublic.Should().BeTrue();
        typeof(IUpdateManagedPlanUseCase).IsPublic.Should().BeTrue();
        typeof(IDeleteManagedPlanUseCase).IsPublic.Should().BeTrue();
        typeof(IAssignManagedPlanUseCase).IsPublic.Should().BeTrue();
        typeof(IUnassignManagedPlanUseCase).IsPublic.Should().BeTrue();
        typeof(IGetActiveAssignedPlanUseCase).IsPublic.Should().BeTrue();
    }

    [Test]
    public void ManagedPlanMappingProfile_MapsEveryReadField()
    {
        var plan = CreatePlan(Id<User>.New(), "Mapped", isActive: true, createdAt: DateTimeOffset.UtcNow);
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        var result = mapper.Map<Plan, ManagedPlanReadModel>(plan, mapper.CreateContext());

        result.Id.Should().Be(plan.Id.Rebind<PlanReference>());
        result.Name.Should().Be(plan.Name);
        result.IsActive.Should().Be(plan.IsActive);
        result.CreatedAt.Should().Be(plan.CreatedAt);
    }

    [Test]
    public void AddTrainingPlanningModule_RegistersManagedPlanContractsExactlyOnceAndResolvesThem()
    {
        var services = CreateServices(
            new PlanRepositoryFake(),
            new ActivePlanPointerStoreFake(),
            Substitute.For<IAccountReadService>(),
            new FakeUnitOfWork(),
            Substitute.For<IPlanExerciseClonePort>());

        var contracts = new[]
        {
            typeof(IGetManagedPlansUseCase),
            typeof(ICreateManagedPlanUseCase),
            typeof(IUpdateManagedPlanUseCase),
            typeof(IDeleteManagedPlanUseCase),
            typeof(IAssignManagedPlanUseCase),
            typeof(IUnassignManagedPlanUseCase),
            typeof(IGetActiveAssignedPlanUseCase)
        };

        foreach (var contract in contracts)
        {
            services.Count(descriptor => descriptor.ServiceType == contract).Should().Be(1);
        }

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        foreach (var contract in contracts)
        {
            scope.ServiceProvider.GetServices(contract).Should().ContainSingle();
        }
    }

    [Test]
    public async Task GetManagedPlansAsync_SortsTraineePlansAndForwardsCancellation()
    {
        var traineeId = Id<User>.New();
        var cancellationToken = new CancellationTokenSource().Token;
        var oldPlan = CreatePlan(traineeId, "Old", createdAt: DateTimeOffset.UtcNow.AddDays(-1));
        var newPlan = CreatePlan(traineeId, "New", createdAt: DateTimeOffset.UtcNow);
        var planRepository = new PlanRepositoryFake();
        var traineeReference = traineeId.Rebind<AccountReference>();
        planRepository.PlansByUserId[traineeId] = [oldPlan, newPlan];

        var useCase = Resolve<IGetManagedPlansUseCase>(planRepository);

        var result = await useCase.ExecuteAsync(new GetManagedPlansQuery(traineeId), cancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(plan => plan.Name).Should().Equal("New", "Old");
        planRepository.GetByUserCalls.Should().ContainSingle(call => call.UserId == traineeId && call.CancellationToken == cancellationToken);
    }

    [Test]
    public async Task GetManagedPlansAsync_WhenRepositoryCancels_PropagatesCancellation()
    {
        var traineeId = Id<User>.New();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var planRepository = new PlanRepositoryFake { GetByUserException = new TaskCanceledException("Canceled", null, cancellation.Token) };
        var useCase = Resolve<IGetManagedPlansUseCase>(planRepository);

        Func<Task> action = () => useCase.ExecuteAsync(new GetManagedPlansQuery(traineeId), cancellation.Token);

        await action.Should().ThrowAsync<TaskCanceledException>();
    }

    [Test]
    public async Task GetManagedPlansAsync_WhenTraineeIdIsEmpty_ReturnsOwnerValidationError()
    {
        var result = await Resolve<IGetManagedPlansUseCase>().ExecuteAsync(new GetManagedPlansQuery(Id<User>.Empty));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidPlanError>().Which.Message.Should().Be(Messages.UserIdRequired);
        result.Error.HttpStatusCode.Should().Be(400);
    }

    [Test]
    public async Task CreateManagedPlanAsync_CreatesInactiveTrainerOwnedTrimmedPlan()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var planRepository = new PlanRepositoryFake();
        var unitOfWork = new FakeUnitOfWork();
        Plan? stagedPlan = null;
        planRepository.OnAdd = plan => stagedPlan = plan;
        var useCase = Resolve<ICreateManagedPlanUseCase>(planRepository, unitOfWork: unitOfWork);

        var result = await useCase.ExecuteAsync(new CreateManagedPlanCommand(trainerId, traineeId, "  Template  "));

        result.IsSuccess.Should().BeTrue();
        stagedPlan.Should().NotBeNull();
        stagedPlan!.UserId.Should().Be(trainerId);
        stagedPlan.Name.Should().Be("Template");
        stagedPlan.IsActive.Should().BeFalse();
        stagedPlan.IsDeleted.Should().BeFalse();
        result.Value.Id.Should().Be(stagedPlan.Id.Rebind<PlanReference>());
        unitOfWork.SaveChangesCalls.Should().Be(1);
        unitOfWork.BeginTransactionCalls.Should().Be(0);
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task CreateManagedPlanAsync_WhenNameIsInvalid_ReturnsLegacyValidationError(string name)
    {
        var useCase = Resolve<ICreateManagedPlanUseCase>();

        var result = await useCase.ExecuteAsync(new CreateManagedPlanCommand(Id<User>.New(), Id<User>.New(), name));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidPlanError>();
    }

    [Test]
    public async Task UpdateManagedPlanAsync_AllowsTrainerAndTraineePlansButRejectsForeignPlan()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = CreatePlan(trainerId, "Old");
        var planRepository = new PlanRepositoryFake();
        var unitOfWork = new FakeUnitOfWork();
        planRepository.PlansById[plan.Id] = plan;
        var useCase = Resolve<IUpdateManagedPlanUseCase>(planRepository, unitOfWork: unitOfWork);

        var trainerResult = await useCase.ExecuteAsync(new UpdateManagedPlanCommand(trainerId, traineeId, plan.Id, "  Trainer update  "));

        trainerResult.IsSuccess.Should().BeTrue();
        plan.Name.Should().Be("Trainer update");
        unitOfWork.SaveChangesCalls.Should().Be(1);

        var traineePlan = CreatePlan(traineeId, "Trainee old");
        planRepository.PlansById[traineePlan.Id] = traineePlan;

        var traineeResult = await useCase.ExecuteAsync(new UpdateManagedPlanCommand(trainerId, traineeId, traineePlan.Id, "Trainee update"));

        traineeResult.IsSuccess.Should().BeTrue();
        traineePlan.Name.Should().Be("Trainee update");

        var foreignPlan = CreatePlan(Id<User>.New(), "Foreign");
        planRepository.PlansById[foreignPlan.Id] = foreignPlan;

        var foreignResult = await useCase.ExecuteAsync(new UpdateManagedPlanCommand(trainerId, traineeId, foreignPlan.Id, "Nope"));

        foreignResult.IsFailure.Should().BeTrue();
        foreignResult.Error.Should().BeOfType<PlanNotFoundError>();
        planRepository.UpdatedPlans.Should().NotContain(foreignPlan);
    }

    [Test]
    public async Task UpdateManagedPlanAsync_WhenPlanIsMissing_ReturnsNotFound()
    {
        var planRepository = new PlanRepositoryFake();
        var useCase = Resolve<IUpdateManagedPlanUseCase>(planRepository);

        var result = await useCase.ExecuteAsync(
            new UpdateManagedPlanCommand(Id<User>.New(), Id<User>.New(), Id<Plan>.New(), "Updated"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<PlanNotFoundError>();
    }

    [Test]
    public async Task UpdateManagedPlanAsync_WhenPlanIdOrNameIsInvalid_ReturnsLegacyValidationError()
    {
        var useCase = Resolve<IUpdateManagedPlanUseCase>();

        var result = await useCase.ExecuteAsync(
            new UpdateManagedPlanCommand(Id<User>.New(), Id<User>.New(), Id<Plan>.Empty, " "));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidPlanError>();
    }

    [Test]
    public async Task DeleteManagedPlanAsync_DeletesAssignedPlanAndClearsMatchingPointer()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = CreatePlan(traineeId, "Assigned", isActive: true);
        var planRepository = new PlanRepositoryFake();
        var pointerStore = new ActivePlanPointerStoreFake();
        var unitOfWork = new FakeUnitOfWork();
        var traineeReference = traineeId.Rebind<AccountReference>();
        planRepository.PlansById[plan.Id] = plan;
        pointerStore.ActivePlanId = plan.Id;
        var useCase = Resolve<IDeleteManagedPlanUseCase>(
            planRepository,
            pointerStore,
            ExistingAccount(traineeId),
            unitOfWork);

        var result = await useCase.ExecuteAsync(new DeleteManagedPlanCommand(trainerId, traineeId, plan.Id));

        result.IsSuccess.Should().BeTrue();
        plan.IsDeleted.Should().BeTrue();
        plan.IsActive.Should().BeFalse();
        unitOfWork.SaveChangesCalls.Should().Be(1);
        unitOfWork.Transaction!.CommitCalls.Should().Be(1);
        pointerStore.StagedPlanIds.Should().ContainSingle().Which.Should().BeNull();
    }

    [Test]
    public async Task DeleteManagedPlanAsync_WhenTraineeIsMissing_ReturnsNotFoundWithoutStaging()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = CreatePlan(traineeId, "Assigned");
        var planRepository = new PlanRepositoryFake();
        planRepository.PlansById[plan.Id] = plan;
        var useCase = Resolve<IDeleteManagedPlanUseCase>(planRepository);

        var result = await useCase.ExecuteAsync(new DeleteManagedPlanCommand(trainerId, traineeId, plan.Id));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<PlanNotFoundError>();
        plan.IsDeleted.Should().BeFalse();
    }

    [Test]
    public async Task AssignManagedPlanAsync_ClonesTrainerPlanStagesClonePointerAndCommits()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var template = CreatePlan(trainerId, "Template");
        var clone = CreatePlan(traineeId, "Clone", isActive: true);
        var planRepository = new PlanRepositoryFake();
        var pointerStore = new ActivePlanPointerStoreFake();
        var unitOfWork = new FakeUnitOfWork();
        var traineeReference = traineeId.Rebind<AccountReference>();
        var clonePort = Substitute.For<IPlanExerciseClonePort>();
        var exerciseIdMap = new Dictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>>();
        planRepository.PlansById[template.Id] = template;
        planRepository.ExerciseIds = [];
        clonePort.StageClonesAsync(traineeReference, [], Arg.Any<CancellationToken>()).Returns(exerciseIdMap);
        planRepository.CloneResult = clone;
        var useCase = Resolve<IAssignManagedPlanUseCase>(
            planRepository,
            pointerStore,
            ExistingAccount(traineeId),
            unitOfWork,
            clonePort);

        var result = await useCase.ExecuteAsync(new AssignManagedPlanCommand(trainerId, traineeId, template.Id));

        result.IsSuccess.Should().BeTrue();
        planRepository.FindByIdCalls.Should().ContainSingle(call => call.PlanId == template.Id);
        planRepository.ClearedActiveUsers.Should().ContainSingle().Which.Should().Be(traineeId);
        await clonePort.Received(1).StageClonesAsync(traineeReference, [], Arg.Any<CancellationToken>());
        planRepository.CloneCalls.Should().ContainSingle(call => call.SourcePlanId == template.Id && call.UserId == traineeId && ReferenceEquals(call.ExerciseIdMap, exerciseIdMap));
        pointerStore.StagedPlanIds.Should().ContainSingle().Which.Should().Be(clone.Id);
        unitOfWork.SaveChangesCalls.Should().Be(1);
        unitOfWork.Transaction!.CommitCalls.Should().Be(1);
    }

    [Test]
    public async Task AssignManagedPlanAsync_ActivatesTraineePlanAndRollsBackWhenSaveFails()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = CreatePlan(traineeId, "Trainee plan");
        var planRepository = new PlanRepositoryFake();
        var pointerStore = new ActivePlanPointerStoreFake();
        var unitOfWork = new FakeUnitOfWork { SaveChangesException = new InvalidOperationException("save failed") };
        var traineeReference = traineeId.Rebind<AccountReference>();
        var planReference = plan.Id.Rebind<PlanReference>();
        planRepository.PlansById[plan.Id] = plan;
        var useCase = Resolve<IAssignManagedPlanUseCase>(
            planRepository,
            pointerStore,
            ExistingAccount(traineeId),
            unitOfWork);

        Func<Task> action = () => useCase.ExecuteAsync(new AssignManagedPlanCommand(trainerId, traineeId, plan.Id));

        await action.Should().ThrowAsync<InvalidOperationException>();
        planRepository.SetActiveCalls.Should().ContainSingle(call => call.UserId == traineeId && call.PlanId == plan.Id);
        pointerStore.StagedPlanIds.Should().ContainSingle().Which.Should().Be(plan.Id);
        unitOfWork.Transaction!.CommitCalls.Should().Be(0);
        unitOfWork.Transaction.RollbackCalls.Should().Be(1);
    }

    [Test]
    public async Task AssignManagedPlanAsync_WhenCommitFails_RollsBackWithoutReturningSuccess()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = CreatePlan(traineeId, "Trainee plan");
        var transaction = new FakeUnitOfWorkTransaction { CommitException = new InvalidOperationException("commit failed") };
        var unitOfWork = new FakeUnitOfWork(transaction);
        var planRepository = new PlanRepositoryFake();
        planRepository.PlansById[plan.Id] = plan;
        var useCase = Resolve<IAssignManagedPlanUseCase>(
            planRepository,
            new ActivePlanPointerStoreFake(),
            ExistingAccount(traineeId),
            unitOfWork);

        Func<Task> action = () => useCase.ExecuteAsync(new AssignManagedPlanCommand(trainerId, traineeId, plan.Id));

        await action.Should().ThrowAsync<InvalidOperationException>();
        transaction.CommitCalls.Should().Be(1);
        transaction.RollbackCalls.Should().Be(1);
    }

    [Test]
    public async Task AssignManagedPlanAsync_WhenPlanIsForeignOrTraineeIsMissing_ReturnsNotFound()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var foreignPlan = CreatePlan(Id<User>.New(), "Foreign");
        var planRepository = new PlanRepositoryFake();
        planRepository.PlansById[foreignPlan.Id] = foreignPlan;
        var useCase = Resolve<IAssignManagedPlanUseCase>(planRepository);

        var foreignResult = await useCase.ExecuteAsync(new AssignManagedPlanCommand(trainerId, traineeId, foreignPlan.Id));

        foreignResult.IsFailure.Should().BeTrue();
        foreignResult.Error.Should().BeOfType<PlanNotFoundError>();

        var traineePlan = CreatePlan(traineeId, "Trainee");
        planRepository.PlansById[traineePlan.Id] = traineePlan;

        var missingTraineeResult = await useCase.ExecuteAsync(new AssignManagedPlanCommand(trainerId, traineeId, traineePlan.Id));

        missingTraineeResult.IsFailure.Should().BeTrue();
        missingTraineeResult.Error.Should().BeOfType<PlanNotFoundError>();
    }

    [Test]
    public async Task UnassignManagedPlanAsync_ClearsActivePlansAndPointer()
    {
        var traineeId = Id<User>.New();
        var planRepository = new PlanRepositoryFake();
        var pointerStore = new ActivePlanPointerStoreFake();
        var unitOfWork = new FakeUnitOfWork();
        var traineeReference = traineeId.Rebind<AccountReference>();
        var useCase = Resolve<IUnassignManagedPlanUseCase>(
            planRepository,
            pointerStore,
            ExistingAccount(traineeId),
            unitOfWork);

        var result = await useCase.ExecuteAsync(new UnassignManagedPlanCommand(traineeId));

        result.IsSuccess.Should().BeTrue();
        planRepository.ClearedActiveUsers.Should().ContainSingle().Which.Should().Be(traineeId);
        pointerStore.StagedPlanIds.Should().ContainSingle().Which.Should().BeNull();
        unitOfWork.Transaction!.CommitCalls.Should().Be(1);
    }

    [Test]
    public async Task GetActiveAssignedPlanAsync_ReturnsActivePlanOrNotFound()
    {
        var traineeId = Id<User>.New();
        var activePlan = CreatePlan(traineeId, "Active", isActive: true);
        var planRepository = new PlanRepositoryFake();
        var traineeReference = traineeId.Rebind<AccountReference>();
        planRepository.ActivePlan = activePlan;
        var useCase = Resolve<IGetActiveAssignedPlanUseCase>(planRepository);

        var success = await useCase.ExecuteAsync(new GetActiveAssignedPlanQuery(traineeId));

        success.IsSuccess.Should().BeTrue();
        success.Value.Id.Should().Be(activePlan.Id.Rebind<PlanReference>());

        planRepository.ActivePlan = null;

        var missing = await useCase.ExecuteAsync(new GetActiveAssignedPlanQuery(traineeId));

        missing.IsFailure.Should().BeTrue();
        missing.Error.Should().BeOfType<PlanNotFoundError>();
    }

    [Test]
    public void ManagedPlanPublicModels_ExposeOnlyScalarAndTypedIdentifierValues()
    {
        var models = new[]
        {
            typeof(GetManagedPlansQuery),
            typeof(CreateManagedPlanCommand),
            typeof(UpdateManagedPlanCommand),
            typeof(DeleteManagedPlanCommand),
            typeof(AssignManagedPlanCommand),
            typeof(UnassignManagedPlanCommand),
            typeof(GetActiveAssignedPlanQuery),
            typeof(ManagedPlanReadModel)
        };

        foreach (var propertyType in models.SelectMany(model => model.GetProperties()).Select(property => property.PropertyType))
        {
            propertyType.Should().NotBe(typeof(Plan));
            propertyType.Should().NotBe(typeof(User));
            propertyType.Should().NotBe(typeof(IPlanRepository));
            propertyType.Should().NotBe(typeof(IActivePlanPointerStore));
        }
    }

    private static ServiceCollection CreateServices(
        IPlanRepository planRepository,
        IActivePlanPointerStore activePlanPointerStore,
        IAccountReadService accountReadService,
        IUnitOfWork unitOfWork,
        IPlanExerciseClonePort exerciseClone)
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
        services.AddTrainingPlanningModule();
        services.AddScoped(_ => planRepository);
        services.AddScoped(_ => activePlanPointerStore);
        services.AddScoped(_ => exerciseClone);
        services.AddScoped(_ => accountReadService);
        services.AddScoped(_ => unitOfWork);
        return services;
    }

    private static TContract Resolve<TContract>(
        IPlanRepository? planRepository = null,
        IActivePlanPointerStore? activePlanPointerStore = null,
        IAccountReadService? accountReadService = null,
        IUnitOfWork? unitOfWork = null,
        IPlanExerciseClonePort? exerciseClone = null)
        where TContract : notnull
    {
        var services = CreateServices(
            planRepository ?? new PlanRepositoryFake(),
            activePlanPointerStore ?? new ActivePlanPointerStoreFake(),
            accountReadService ?? Substitute.For<IAccountReadService>(),
            unitOfWork ?? new FakeUnitOfWork(),
            exerciseClone ?? Substitute.For<IPlanExerciseClonePort>());
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();

        return scope.ServiceProvider.GetRequiredService<TContract>();
    }

    private static IAccountReadService ExistingAccount(Id<User> accountId)
    {
        var accountReadService = Substitute.For<IAccountReadService>();
        accountReadService.GetByIdAsync(accountId, Arg.Any<CancellationToken>())
            .Returns(new AccountReadModel(accountId, "Trainee", "trainee@example.com", null, "en", "UTC"));
        return accountReadService;
    }

    private static Plan CreatePlan(
        Id<User> userId,
        string name,
        bool isActive = false,
        DateTimeOffset? createdAt = null)
    {
        return new Plan
        {
            Id = Id<Plan>.New(),
            UserId = userId,
            Name = name,
            IsActive = isActive,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        };
    }

    private sealed class PlanRepositoryFake : IPlanRepository
    {
        public Dictionary<Id<Plan>, Plan> PlansById { get; } = [];
        public Dictionary<Id<User>, List<Plan>> PlansByUserId { get; } = [];
        public List<(Id<User> UserId, CancellationToken CancellationToken)> GetByUserCalls { get; } = [];
        public List<(Id<Plan> PlanId, CancellationToken CancellationToken)> FindByIdCalls { get; } = [];
        public List<Plan> UpdatedPlans { get; } = [];
        public List<Id<User>> ClearedActiveUsers { get; } = [];
        public List<(Id<User> UserId, Id<Plan> PlanId)> SetActiveCalls { get; } = [];
        public List<(Id<Plan> SourcePlanId, Id<User> UserId, IReadOnlyDictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>> ExerciseIdMap)> CloneCalls { get; } = [];
        public Action<Plan>? OnAdd { get; set; }
        public Exception? GetByUserException { get; set; }
        public Plan? ActivePlan { get; set; }
        public IReadOnlyCollection<Id<PlanExerciseReference>> ExerciseIds { get; set; } = [];
        public Plan? CloneResult { get; set; }

        public Task<Plan?> FindByIdAsync(Id<Plan> id, CancellationToken cancellationToken = default)
        {
            FindByIdCalls.Add((id, cancellationToken));
            return Task.FromResult(PlansById.GetValueOrDefault(id));
        }

        public Task<Plan?> FindActiveByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
            => Task.FromResult(ActivePlan);

        public Task<LgymApi.Application.TrainingPlanning.Plan.Models.PlanReadModel?> FindActiveReadModelByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
            => Task.FromResult<LgymApi.Application.TrainingPlanning.Plan.Models.PlanReadModel?>(null);

        public Task<Plan?> FindLastActiveByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
            => Task.FromResult<Plan?>(null);

        public Task<List<Plan>> GetByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
        {
            GetByUserCalls.Add((userId, cancellationToken));
            return GetByUserException is null
                ? Task.FromResult(PlansByUserId.GetValueOrDefault(userId, []))
                : Task.FromException<List<Plan>>(GetByUserException);
        }

        public Task<List<LgymApi.Application.TrainingPlanning.Plan.Models.PlanReadModel>> GetReadModelsByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
            => Task.FromResult<List<LgymApi.Application.TrainingPlanning.Plan.Models.PlanReadModel>>([]);

        public Task AddAsync(Plan plan, CancellationToken cancellationToken = default)
        {
            OnAdd?.Invoke(plan);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Plan plan, CancellationToken cancellationToken = default)
        {
            UpdatedPlans.Add(plan);
            return Task.CompletedTask;
        }

        public Task SetActivePlanAsync(Id<User> userId, Id<Plan> planId, CancellationToken cancellationToken = default)
        {
            SetActiveCalls.Add((userId, planId));
            return Task.CompletedTask;
        }

        public Task ClearActivePlansAsync(Id<User> userId, CancellationToken cancellationToken = default)
        {
            ClearedActiveUsers.Add(userId);
            return Task.CompletedTask;
        }

        public Task<Plan?> FindByShareCodeAsync(string shareCode, CancellationToken cancellationToken = default)
            => Task.FromResult<Plan?>(null);

        public Task<IReadOnlyCollection<Id<PlanExerciseReference>>> GetPlanExerciseIdsAsync(Id<Plan> planId, CancellationToken cancellationToken = default)
            => Task.FromResult(ExerciseIds);

        public Task<Plan> ClonePlanAsync(
            Id<Plan> sourcePlanId,
            Id<User> userId,
            IReadOnlyDictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>> exerciseIdMap,
            bool isActive = true,
            CancellationToken cancellationToken = default)
        {
            CloneCalls.Add((sourcePlanId, userId, exerciseIdMap));
            return CloneResult is null
                ? Task.FromException<Plan>(new InvalidOperationException("A clone result must be configured."))
                : Task.FromResult(CloneResult);
        }

        public Task<string> GenerateShareCodeAsync(Id<Plan> planId, Id<User> userId, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }

    private sealed class ActivePlanPointerStoreFake : IActivePlanPointerStore
    {
        public Id<Plan>? ActivePlanId { get; set; }
        public List<Id<Plan>?> StagedPlanIds { get; } = [];

        public Task<Id<Plan>?> GetActivePlanIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
            => Task.FromResult(ActivePlanId);

        public Task StageActivePlanIdAsync(Id<User> userId, Id<Plan>? planId, CancellationToken cancellationToken = default)
        {
            StagedPlanIds.Add(planId);
            return Task.CompletedTask;
        }
    }
}
