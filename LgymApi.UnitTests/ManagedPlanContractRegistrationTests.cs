using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.TrainingPlanning;
using LgymApi.Application.TrainingPlanning.Plan.ActivePlanPointer;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
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
        services.AddApplicationMapping(typeof(IMappingProfile).Assembly);

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        var result = mapper.Map<Plan, ManagedPlanReadModel>(plan, mapper.CreateContext());

        result.Id.Should().Be(plan.Id);
        result.Name.Should().Be(plan.Name);
        result.IsActive.Should().Be(plan.IsActive);
        result.CreatedAt.Should().Be(plan.CreatedAt);
    }

    [Test]
    public void AddTrainingPlanningModule_RegistersManagedPlanContractsExactlyOnceAndResolvesThem()
    {
        var services = CreateServices(
            Substitute.For<IPlanRepository>(),
            Substitute.For<IActivePlanPointerStore>(),
            Substitute.For<IAccountReadService>(),
            new FakeUnitOfWork());

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
        var planRepository = Substitute.For<IPlanRepository>();
        planRepository.GetByUserIdAsync(traineeId, cancellationToken).Returns([oldPlan, newPlan]);

        var useCase = Resolve<IGetManagedPlansUseCase>(planRepository);

        var result = await useCase.ExecuteAsync(new GetManagedPlansQuery(traineeId), cancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(plan => plan.Name).Should().Equal("New", "Old");
        await planRepository.Received(1).GetByUserIdAsync(traineeId, cancellationToken);
    }

    [Test]
    public async Task GetManagedPlansAsync_WhenRepositoryCancels_PropagatesCancellation()
    {
        var traineeId = Id<User>.New();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var planRepository = Substitute.For<IPlanRepository>();
        planRepository.GetByUserIdAsync(traineeId, cancellation.Token)
            .Returns(Task.FromCanceled<List<Plan>>(cancellation.Token));
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
        var planRepository = Substitute.For<IPlanRepository>();
        var unitOfWork = new FakeUnitOfWork();
        Plan? stagedPlan = null;
        planRepository.AddAsync(Arg.Do<Plan>(plan => stagedPlan = plan), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var useCase = Resolve<ICreateManagedPlanUseCase>(planRepository, unitOfWork: unitOfWork);

        var result = await useCase.ExecuteAsync(new CreateManagedPlanCommand(trainerId, traineeId, "  Template  "));

        result.IsSuccess.Should().BeTrue();
        stagedPlan.Should().NotBeNull();
        stagedPlan!.UserId.Should().Be(trainerId);
        stagedPlan.Name.Should().Be("Template");
        stagedPlan.IsActive.Should().BeFalse();
        stagedPlan.IsDeleted.Should().BeFalse();
        result.Value.Id.Should().Be(stagedPlan.Id);
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
        var planRepository = Substitute.For<IPlanRepository>();
        var unitOfWork = new FakeUnitOfWork();
        planRepository.FindByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        var useCase = Resolve<IUpdateManagedPlanUseCase>(planRepository, unitOfWork: unitOfWork);

        var trainerResult = await useCase.ExecuteAsync(new UpdateManagedPlanCommand(trainerId, traineeId, plan.Id, "  Trainer update  "));

        trainerResult.IsSuccess.Should().BeTrue();
        plan.Name.Should().Be("Trainer update");
        unitOfWork.SaveChangesCalls.Should().Be(1);

        var traineePlan = CreatePlan(traineeId, "Trainee old");
        planRepository.FindByIdAsync(traineePlan.Id, Arg.Any<CancellationToken>()).Returns(traineePlan);

        var traineeResult = await useCase.ExecuteAsync(new UpdateManagedPlanCommand(trainerId, traineeId, traineePlan.Id, "Trainee update"));

        traineeResult.IsSuccess.Should().BeTrue();
        traineePlan.Name.Should().Be("Trainee update");

        var foreignPlan = CreatePlan(Id<User>.New(), "Foreign");
        planRepository.FindByIdAsync(foreignPlan.Id, Arg.Any<CancellationToken>()).Returns(foreignPlan);

        var foreignResult = await useCase.ExecuteAsync(new UpdateManagedPlanCommand(trainerId, traineeId, foreignPlan.Id, "Nope"));

        foreignResult.IsFailure.Should().BeTrue();
        foreignResult.Error.Should().BeOfType<PlanNotFoundError>();
        await planRepository.DidNotReceive().UpdateAsync(foreignPlan, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateManagedPlanAsync_WhenPlanIsMissing_ReturnsNotFound()
    {
        var planRepository = Substitute.For<IPlanRepository>();
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
        var planRepository = Substitute.For<IPlanRepository>();
        var pointerStore = Substitute.For<IActivePlanPointerStore>();
        var unitOfWork = new FakeUnitOfWork();
        planRepository.FindByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        pointerStore.GetActivePlanIdAsync(traineeId, Arg.Any<CancellationToken>()).Returns(plan.Id);
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
        await pointerStore.Received(1).StageActivePlanIdAsync(traineeId, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteManagedPlanAsync_WhenTraineeIsMissing_ReturnsNotFoundWithoutStaging()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = CreatePlan(traineeId, "Assigned");
        var planRepository = Substitute.For<IPlanRepository>();
        planRepository.FindByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
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
        var planRepository = Substitute.For<IPlanRepository>();
        var pointerStore = Substitute.For<IActivePlanPointerStore>();
        var unitOfWork = new FakeUnitOfWork();
        planRepository.FindByIdAsync(template.Id, Arg.Any<CancellationToken>()).Returns(template);
        planRepository.ClonePlanAsync(template.Id, traineeId, true, Arg.Any<CancellationToken>()).Returns(clone);
        var useCase = Resolve<IAssignManagedPlanUseCase>(
            planRepository,
            pointerStore,
            ExistingAccount(traineeId),
            unitOfWork);

        var result = await useCase.ExecuteAsync(new AssignManagedPlanCommand(trainerId, traineeId, template.Id));

        result.IsSuccess.Should().BeTrue();
        await planRepository.Received(1).ClearActivePlansAsync(traineeId, Arg.Any<CancellationToken>());
        await pointerStore.Received(1).StageActivePlanIdAsync(traineeId, clone.Id, Arg.Any<CancellationToken>());
        unitOfWork.SaveChangesCalls.Should().Be(1);
        unitOfWork.Transaction!.CommitCalls.Should().Be(1);
    }

    [Test]
    public async Task AssignManagedPlanAsync_ActivatesTraineePlanAndRollsBackWhenSaveFails()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = CreatePlan(traineeId, "Trainee plan");
        var planRepository = Substitute.For<IPlanRepository>();
        var pointerStore = Substitute.For<IActivePlanPointerStore>();
        var unitOfWork = new FakeUnitOfWork { SaveChangesException = new InvalidOperationException("save failed") };
        planRepository.FindByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        var useCase = Resolve<IAssignManagedPlanUseCase>(
            planRepository,
            pointerStore,
            ExistingAccount(traineeId),
            unitOfWork);

        Func<Task> action = () => useCase.ExecuteAsync(new AssignManagedPlanCommand(trainerId, traineeId, plan.Id));

        await action.Should().ThrowAsync<InvalidOperationException>();
        await planRepository.Received(1).SetActivePlanAsync(traineeId, plan.Id, Arg.Any<CancellationToken>());
        await pointerStore.Received(1).StageActivePlanIdAsync(traineeId, plan.Id, Arg.Any<CancellationToken>());
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
        var planRepository = Substitute.For<IPlanRepository>();
        planRepository.FindByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        var useCase = Resolve<IAssignManagedPlanUseCase>(
            planRepository,
            Substitute.For<IActivePlanPointerStore>(),
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
        var planRepository = Substitute.For<IPlanRepository>();
        planRepository.FindByIdAsync(foreignPlan.Id, Arg.Any<CancellationToken>()).Returns(foreignPlan);
        var useCase = Resolve<IAssignManagedPlanUseCase>(planRepository);

        var foreignResult = await useCase.ExecuteAsync(new AssignManagedPlanCommand(trainerId, traineeId, foreignPlan.Id));

        foreignResult.IsFailure.Should().BeTrue();
        foreignResult.Error.Should().BeOfType<PlanNotFoundError>();

        var traineePlan = CreatePlan(traineeId, "Trainee");
        planRepository.FindByIdAsync(traineePlan.Id, Arg.Any<CancellationToken>()).Returns(traineePlan);

        var missingTraineeResult = await useCase.ExecuteAsync(new AssignManagedPlanCommand(trainerId, traineeId, traineePlan.Id));

        missingTraineeResult.IsFailure.Should().BeTrue();
        missingTraineeResult.Error.Should().BeOfType<PlanNotFoundError>();
    }

    [Test]
    public async Task UnassignManagedPlanAsync_ClearsActivePlansAndPointer()
    {
        var traineeId = Id<User>.New();
        var planRepository = Substitute.For<IPlanRepository>();
        var pointerStore = Substitute.For<IActivePlanPointerStore>();
        var unitOfWork = new FakeUnitOfWork();
        var useCase = Resolve<IUnassignManagedPlanUseCase>(
            planRepository,
            pointerStore,
            ExistingAccount(traineeId),
            unitOfWork);

        var result = await useCase.ExecuteAsync(new UnassignManagedPlanCommand(traineeId));

        result.IsSuccess.Should().BeTrue();
        await planRepository.Received(1).ClearActivePlansAsync(traineeId, Arg.Any<CancellationToken>());
        await pointerStore.Received(1).StageActivePlanIdAsync(traineeId, null, Arg.Any<CancellationToken>());
        unitOfWork.Transaction!.CommitCalls.Should().Be(1);
    }

    [Test]
    public async Task GetActiveAssignedPlanAsync_ReturnsActivePlanOrNotFound()
    {
        var traineeId = Id<User>.New();
        var activePlan = CreatePlan(traineeId, "Active", isActive: true);
        var planRepository = Substitute.For<IPlanRepository>();
        planRepository.FindActiveByUserIdAsync(traineeId, Arg.Any<CancellationToken>()).Returns(activePlan);
        var useCase = Resolve<IGetActiveAssignedPlanUseCase>(planRepository);

        var success = await useCase.ExecuteAsync(new GetActiveAssignedPlanQuery(traineeId));

        success.IsSuccess.Should().BeTrue();
        success.Value.Id.Should().Be(activePlan.Id);

        planRepository.FindActiveByUserIdAsync(traineeId, Arg.Any<CancellationToken>()).Returns((Plan?)null);

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
        IUnitOfWork unitOfWork)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => planRepository);
        services.AddScoped(_ => activePlanPointerStore);
        services.AddScoped(_ => accountReadService);
        services.AddScoped(_ => unitOfWork);
        services.AddApplicationMapping(typeof(IMappingProfile).Assembly);
        services.AddTrainingPlanningModule();
        return services;
    }

    private static TContract Resolve<TContract>(
        IPlanRepository? planRepository = null,
        IActivePlanPointerStore? activePlanPointerStore = null,
        IAccountReadService? accountReadService = null,
        IUnitOfWork? unitOfWork = null)
        where TContract : notnull
    {
        var services = CreateServices(
            planRepository ?? Substitute.For<IPlanRepository>(),
            activePlanPointerStore ?? Substitute.For<IActivePlanPointerStore>(),
            accountReadService ?? Substitute.For<IAccountReadService>(),
            unitOfWork ?? new FakeUnitOfWork());
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
}
