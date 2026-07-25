using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Features.PlanDay;
using LgymApi.Application.Features.PlanDay.Models;
using LgymApi.Application.Features.TrainerRelationships.Models;
using LgymApi.Application.Models;
using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.TrainingPlanning.Plan.ActivePlanPointer;
using LgymApi.Application.TrainingPlanning.Plan.CheckIsUserHavePlan;
using LgymApi.Application.TrainingPlanning.Plan.CopyPlan;
using LgymApi.Application.TrainingPlanning.Plan.CreatePlan;
using LgymApi.Application.TrainingPlanning.Plan.DeletePlan;
using LgymApi.Application.TrainingPlanning.Plan.GenerateShareCode;
using LgymApi.Application.TrainingPlanning.Plan.GetPlanConfig;
using LgymApi.Application.TrainingPlanning.Plan.GetPlansList;
using LgymApi.Application.TrainingPlanning.Plan.Models;
using LgymApi.Application.TrainingPlanning.Plan.SetActivePlan;
using LgymApi.Application.TrainingPlanning.Plan.UpdatePlan;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ServiceTransactionBehaviorTests
{
    [Test]
    public async Task SetActivePlanUseCase_WhenSuccessful_CommitsTransaction()
    {
        var userId = Id<User>.New();
        var planId = Id<Plan>.New();
        var unitOfWork = new RecordingUnitOfWork();
        var planRepository = new PlanRepositoryStub
        {
            PlanToReturn = new Plan { Id = planId, UserId = userId }
        };
        var activePlanPointerStore = new ActivePlanPointerStoreStub();

        var useCase = new SetActivePlanUseCase(planRepository, activePlanPointerStore, unitOfWork);

        await useCase.ExecuteAsync(new SetActivePlanCommand(userId, userId, planId), CancellationToken.None);

        unitOfWork.SaveChangesCalls.Should().Be(1);
        unitOfWork.Transaction.CommitCalls.Should().Be(1);
        unitOfWork.Transaction.RollbackCalls.Should().Be(0);
        activePlanPointerStore.StagedPlanId.Should().Be(planId);
        planRepository.SetActiveCalls.Should().Be(1);
    }

    [Test]
    public void SetActivePlanUseCase_WhenSetActiveFails_RollsBackTransaction()
    {
        var userId = Id<User>.New();
        var planId = Id<Plan>.New();
        var unitOfWork = new RecordingUnitOfWork();
        var planRepository = new PlanRepositoryStub
        {
            PlanToReturn = new Plan { Id = planId, UserId = userId },
            SetActiveException = new InvalidOperationException("boom")
        };
        var activePlanPointerStore = new ActivePlanPointerStoreStub();

        var useCase = new SetActivePlanUseCase(planRepository, activePlanPointerStore, unitOfWork);

        Func<Task> action = async () =>
            await useCase.ExecuteAsync(new SetActivePlanCommand(userId, userId, planId), CancellationToken.None);

        action.Should().ThrowAsync<InvalidOperationException>();

        unitOfWork.Transaction.CommitCalls.Should().Be(0);
        unitOfWork.Transaction.RollbackCalls.Should().Be(1);
    }

    [Test]
    public async Task DeletePlanUseCase_WhenPlanDayDeletionFails_RollsBackWithoutSavingOrCommitting()
    {
        var userId = Id<User>.New();
        var planId = Id<Plan>.New();
        var unitOfWork = new RecordingUnitOfWork();
        var planDayRepository = new PlanDayRepositoryStub
        {
            MarkDeletedException = new InvalidOperationException("plan day deletion failed")
        };
        var useCase = new DeletePlanUseCase(
            new PlanRepositoryStub
            {
                PlanToReturn = new Plan { Id = planId, UserId = userId }
            },
            planDayRepository,
            new ActivePlanPointerStoreStub(),
            unitOfWork);

        Func<Task> action = () => useCase.ExecuteAsync(new DeletePlanCommand(userId, planId), CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();

        planDayRepository.MarkDeletedCalls.Should().Be(1);
        unitOfWork.SaveChangesCalls.Should().Be(0);
        unitOfWork.Transaction.CommitCalls.Should().Be(0);
        unitOfWork.Transaction.RollbackCalls.Should().Be(1);
    }

    [Test]
    public async Task DeletePlanUseCase_WhenPersistedPointerTargetsDeletedPlan_ActivatesFallbackAndStagesPointer()
    {
        var userId = Id<User>.New();
        var deletedPlanId = Id<Plan>.New();
        var fallbackPlanId = Id<Plan>.New();
        var deletedPlan = new Plan { Id = deletedPlanId, UserId = userId, IsActive = true };
        var unitOfWork = new RecordingUnitOfWork();
        var planRepository = new PlanRepositoryStub
        {
            PlanToReturn = deletedPlan,
            LastActivePlanToReturn = new Plan { Id = fallbackPlanId, UserId = userId, IsActive = false }
        };
        var activePlanPointerStore = new ActivePlanPointerStoreStub { ActivePlanId = deletedPlanId };
        var useCase = new DeletePlanUseCase(
            planRepository,
            new PlanDayRepositoryStub(),
            activePlanPointerStore,
            unitOfWork);

        var result = await useCase.ExecuteAsync(new DeletePlanCommand(userId, deletedPlanId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        deletedPlan.IsActive.Should().BeFalse();
        deletedPlan.IsDeleted.Should().BeTrue();
        planRepository.UpdateCalls.Should().Be(1);
        planRepository.SetActiveCalls.Should().Be(1);
        activePlanPointerStore.StagedPlanId.Should().Be(fallbackPlanId);
        unitOfWork.SaveChangesCalls.Should().Be(1);
        unitOfWork.Transaction.CommitCalls.Should().Be(1);
    }

    [Test]
    public async Task UpdatePlanDayAsync_WhenSuccessful_CommitsTransaction()
    {
        var userId = Id<User>.New();
        var planId = Id<Plan>.New();
        var planDayId = Id<PlanDay>.New();
        var unitOfWork = new RecordingUnitOfWork();
        var exercisesRepository = new PlanDayExerciseRepositoryStub();

        var planRepository = new PlanRepositoryStub
        {
            PlanToReturn = new Plan { Id = planId, UserId = userId }
        };

        var planDayRepository = new PlanDayRepositoryStub
        {
            PlanDayToReturn = new PlanDay { Id = planDayId, PlanId = planId, Name = "old" }
        };

        var service = new PlanDayService(new PlanDayServiceDependenciesStub(
            planRepository,
            new PlanDayRelationshipAccessStub(),
            planDayRepository,
            exercisesRepository,
            new ExerciseRepositoryStub(),
           new TrainingRepositoryStub(),
           unitOfWork));

        await service.UpdatePlanDayAsync(
            new User { Id = userId },
            planDayId,
            "new",
            [new PlanDayExerciseInput { ExerciseId = Id<Exercise>.New(), Series = 3, Reps = "8" }],
            CancellationToken.None);

        planDayRepository.UpdateCalls.Should().Be(1);
        exercisesRepository.RemoveCalls.Should().Be(1);
        exercisesRepository.AddRangeCalls.Should().Be(1);
        unitOfWork.SaveChangesCalls.Should().Be(1);
        unitOfWork.Transaction.CommitCalls.Should().Be(1);
        unitOfWork.Transaction.RollbackCalls.Should().Be(0);
    }

    [Test]
    public void UpdatePlanDayAsync_WhenRemoveFails_RollsBackTransaction()
    {
        var userId = Id<User>.New();
        var planId = Id<Plan>.New();
        var planDayId = Id<PlanDay>.New();
        var unitOfWork = new RecordingUnitOfWork();
        var exercisesRepository = new PlanDayExerciseRepositoryStub
        {
            RemoveException = new InvalidOperationException("remove failed")
        };

        var service = new PlanDayService(new PlanDayServiceDependenciesStub(
             new PlanRepositoryStub
             {
                 PlanToReturn = new Plan { Id = planId, UserId = userId }
             },
             new PlanDayRelationshipAccessStub(),
             new PlanDayRepositoryStub
             {
                 PlanDayToReturn = new PlanDay { Id = planDayId, PlanId = planId, Name = "old" }
             },
            exercisesRepository,
            new ExerciseRepositoryStub(),
            new TrainingRepositoryStub(),
            unitOfWork));

        Func<Task> action = async () =>
            await service.UpdatePlanDayAsync(
                new User { Id = userId },
                planDayId,
                "new",
                [new PlanDayExerciseInput { ExerciseId = Id<Exercise>.New(), Series = 3, Reps = "8" }],
                CancellationToken.None);

        action.Should().ThrowAsync<InvalidOperationException>();

        unitOfWork.Transaction.CommitCalls.Should().Be(0);
        unitOfWork.Transaction.RollbackCalls.Should().Be(1);
    }

    // ===== VALIDATION BRANCH TESTS (Wave 3, Batch A) =====

    [Test]
    public async Task CreatePlanUseCase_WhenCurrentUserIdEmpty_ReturnsInvalidPlanError()
    {
        var useCase = new CreatePlanUseCase(
            new PlanRepositoryStub(),
            new ActivePlanPointerStoreStub(),
            new RecordingUnitOfWork());

        var result = await useCase.ExecuteAsync(new CreatePlanCommand(Id<User>.Empty, Id<User>.New(), "Test Plan"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidPlanError>();
    }

    [Test]
    public async Task CreatePlanUseCase_WhenRouteUserIdEmpty_ReturnsInvalidPlanError()
    {
        var useCase = new CreatePlanUseCase(
            new PlanRepositoryStub(),
            new ActivePlanPointerStoreStub(),
            new RecordingUnitOfWork());

        var result = await useCase.ExecuteAsync(new CreatePlanCommand(Id<User>.New(), Id<User>.Empty, "Test Plan"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidPlanError>();
    }

    [Test]
    public async Task UpdatePlanUseCase_WhenRouteUserIdEmpty_ReturnsInvalidPlanError()
    {
        var planId = Id<Plan>.New();
        var useCase = new UpdatePlanUseCase(new PlanRepositoryStub(), new RecordingUnitOfWork());

        var result = await useCase.ExecuteAsync(new UpdatePlanCommand(Id<User>.New(), Id<User>.Empty, planId, "Updated"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidPlanError>();
    }

    [Test]
    public async Task UpdatePlanUseCase_WhenPlanIdEmpty_ReturnsInvalidPlanError()
    {
        var userId = Id<User>.New();
        var useCase = new UpdatePlanUseCase(new PlanRepositoryStub(), new RecordingUnitOfWork());

        var result = await useCase.ExecuteAsync(new UpdatePlanCommand(userId, userId, Id<Plan>.Empty, "Updated"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidPlanError>();
    }

    [Test]
    public async Task UpdatePlanUseCase_WhenNameBlankAndPlanIdEmpty_ReturnsFieldRequiredError()
    {
        var userId = Id<User>.New();
        var useCase = new UpdatePlanUseCase(new PlanRepositoryStub(), new RecordingUnitOfWork());

        var result = await useCase.ExecuteAsync(new UpdatePlanCommand(userId, userId, Id<Plan>.Empty, " "), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidPlanError>();
        result.Error.Message.Should().Be(LgymApi.Resources.Messages.FieldRequired);
    }

    [Test]
    public async Task UpdatePlanUseCase_WhenSuccessful_UpdatesTrackedPlanAndSavesOnce()
    {
        var userId = Id<User>.New();
        var plan = new Plan { Id = Id<Plan>.New(), UserId = userId, Name = "Old" };
        var planRepository = new PlanRepositoryStub { PlanToReturn = plan };
        var unitOfWork = new RecordingUnitOfWork();
        var useCase = new UpdatePlanUseCase(planRepository, unitOfWork);

        var result = await useCase.ExecuteAsync(new UpdatePlanCommand(userId, userId, plan.Id, "Updated"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        plan.Name.Should().Be("Updated");
        planRepository.UpdateCalls.Should().Be(1);
        unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task GetPlanConfigAsync_WhenRouteUserIdEmpty_ReturnsInvalidPlanError()
    {
        var currentUser = new User { Id = Id<User>.New() };
        var useCase = new GetPlanConfigUseCase(new PlanRepositoryStub());

        var result = await useCase.ExecuteAsync(new GetPlanConfigQuery(currentUser.Id, Id<User>.Empty), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidPlanError>();
    }

    [Test]
    public void TrainingPlanningModule_ResolvesFocusedPlanUseCases()
    {
        var services = new ServiceCollection();
        services.AddTrainingPlanningModule();
        services.AddScoped<IPlanRepository>(_ => new PlanRepositoryStub());
        services.AddScoped<IPlanDayRepository>(_ => new PlanDayRepositoryStub());
        services.AddScoped<IActivePlanPointerStore>(_ => new ActivePlanPointerStoreStub());
        services.AddScoped<IUnitOfWork>(_ => new RecordingUnitOfWork());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        serviceProvider.GetRequiredService<ICreatePlanUseCase>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IUpdatePlanUseCase>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IDeletePlanUseCase>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IGetPlanConfigUseCase>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IGetPlansListUseCase>().Should().NotBeNull();
        serviceProvider.GetRequiredService<ISetActivePlanUseCase>().Should().NotBeNull();
        serviceProvider.GetRequiredService<ICopyPlanUseCase>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IGenerateShareCodeUseCase>().Should().NotBeNull();
        serviceProvider.GetRequiredService<ICheckIsUserHavePlanUseCase>().Should().NotBeNull();
    }

    [Test]
    public async Task CheckIsUserHavePlanAsync_WhenRouteUserIdEmpty_ReturnsPlanFlagBadRequestErrorWithFalsePayload()
    {
        var currentUser = new User { Id = Id<User>.New() };
        var useCase = new CheckIsUserHavePlanUseCase(new PlanRepositoryStub(), new PlanDayRepositoryStub());

        var result = await useCase.ExecuteAsync(new CheckIsUserHavePlanQuery(currentUser.Id, Id<User>.Empty), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeAssignableTo<BadRequestError>();
        result.Error.GetPayload().Should().Be(false);
    }

    [Test]
    public async Task CreatePlanDayAsync_WhenPlanIdEmpty_ReturnsInvalidPlanDayError()
    {
        var currentUser = new User { Id = Id<User>.New() };
        var service = new PlanDayService(new PlanDayServiceDependenciesStub(
            new PlanRepositoryStub(),
            new PlanDayRelationshipAccessStub(),
            new PlanDayRepositoryStub(),
            new PlanDayExerciseRepositoryStub(),
            new ExerciseRepositoryStub(),
            new TrainingRepositoryStub(),
            new RecordingUnitOfWork()));

        var result = await service.CreatePlanDayAsync(
            currentUser,
            Id<Plan>.Empty,
            "Test PlanDay",
            [new PlanDayExerciseInput { ExerciseId = Id<Exercise>.New(), Series = 3, Reps = "8" }],
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidPlanDayError>();
    }

    [Test]
    public async Task GetPlanDayAsync_WhenPlanDayIdEmpty_ReturnsInvalidPlanDayError()
    {
        var currentUser = new User { Id = Id<User>.New() };
        var service = new PlanDayService(new PlanDayServiceDependenciesStub(
            new PlanRepositoryStub(),
            new PlanDayRelationshipAccessStub(),
            new PlanDayRepositoryStub(),
            new PlanDayExerciseRepositoryStub(),
            new ExerciseRepositoryStub(),
            new TrainingRepositoryStub(),
            new RecordingUnitOfWork()));

        var result = await service.GetPlanDayAsync(currentUser, Id<PlanDay>.Empty, ["en"], CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidPlanDayError>();
    }

    private sealed class PlanDayServiceDependenciesStub : IPlanDayServiceDependencies
    {
        public PlanDayServiceDependenciesStub(
            IPlanRepository planRepository,
            IPlanDayRelationshipAccessPort relationshipAccess,
            IPlanDayRepository planDayRepository,
            IPlanDayExerciseRepository planDayExerciseRepository,
            IExerciseRepository exerciseRepository,
            ITrainingRepository trainingRepository,
            IUnitOfWork unitOfWork)
        {
            PlanRepository = planRepository;
            RelationshipAccess = relationshipAccess;
            PlanDayRepository = planDayRepository;
            PlanDayExerciseRepository = planDayExerciseRepository;
            ExerciseRepository = exerciseRepository;
            TrainingRepository = trainingRepository;
            UnitOfWork = unitOfWork;
        }

        public IPlanRepository PlanRepository { get; }
        public IPlanDayRelationshipAccessPort RelationshipAccess { get; }
        public IPlanDayRepository PlanDayRepository { get; }
        public IPlanDayExerciseRepository PlanDayExerciseRepository { get; }
        public IExerciseRepository ExerciseRepository { get; }
        public ITrainingRepository TrainingRepository { get; }
        public IUnitOfWork UnitOfWork { get; }
    }

    private sealed class PlanDayRelationshipAccessStub : IPlanDayRelationshipAccessPort
    {
        public Task<bool> HasActiveRelationshipAsync(Id<User> trainerId, Id<User> traineeId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public RecordingTransaction Transaction { get; } = new();
        public int SaveChangesCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            return Task.FromResult(1);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IUnitOfWorkTransaction>(Transaction);
        }

        public void DetachEntity<TEntity>(TEntity entity) where TEntity : class { }
    }

    private sealed class RecordingTransaction : IUnitOfWorkTransaction
    {
        public int CommitCalls { get; private set; }
        public int RollbackCalls { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCalls++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PlanRepositoryStub : IPlanRepository
    {
        public Plan? PlanToReturn { get; set; }
        public Plan? LastActivePlanToReturn { get; set; }
        public Exception? SetActiveException { get; set; }
        public int SetActiveCalls { get; private set; }
        public int UpdateCalls { get; private set; }

        public Task<Plan?> FindByIdAsync(Id<Plan> id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PlanToReturn);
        }

        public Task SetActivePlanAsync(Id<User> userId, Id<Plan> planId, CancellationToken cancellationToken = default)
        {
            SetActiveCalls++;
            if (SetActiveException != null)
            {
                throw SetActiveException;
            }

            return Task.CompletedTask;
        }

        public Task ClearActivePlansAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Plan> ClonePlanAsync(Id<Plan> sourcePlanId, Id<User> userId, bool isActive = true, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Plan?> FindActiveByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PlanReadModel?> FindActiveReadModelByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.FromResult<PlanReadModel?>(null);
        public Task<Plan?> FindLastActiveByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.FromResult(LastActivePlanToReturn);
        public Task<List<Plan>> GetByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<PlanReadModel>> GetReadModelsByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.FromResult(new List<PlanReadModel>());
        public Task AddAsync(Plan plan, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(Plan plan, CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            return Task.CompletedTask;
        }

        public Task<Plan> CopyPlanByShareCodeAsync(string shareCode, Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GenerateShareCodeAsync(Id<Plan> planId, Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ActivePlanPointerStoreStub : IActivePlanPointerStore
    {
        public Id<Plan>? ActivePlanId { get; set; }
        public Id<User>? StagedUserId { get; private set; }
        public Id<Plan>? StagedPlanId { get; private set; }

        public Task<Id<Plan>?> GetActivePlanIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ActivePlanId);
        }

        public Task StageActivePlanIdAsync(Id<User> userId, Id<Plan>? planId, CancellationToken cancellationToken = default)
        {
            StagedUserId = userId;
            StagedPlanId = planId;
            return Task.CompletedTask;
        }
    }

    private sealed class PlanDayRepositoryStub : IPlanDayRepository
    {
        public PlanDay? PlanDayToReturn { get; set; }
        public Exception? MarkDeletedException { get; set; }
        public int UpdateCalls { get; private set; }
        public int MarkDeletedCalls { get; private set; }

        public Task<PlanDay?> FindByIdAsync(Id<PlanDay> id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PlanDayToReturn);
        }

        public Task UpdateAsync(PlanDay planDay, CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            return Task.CompletedTask;
        }

        public Task<List<PlanDay>> GetByPlanIdAsync(Id<Plan> planId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(PlanDay planDay, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MarkDeletedAsync(Id<PlanDay> planDayId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MarkDeletedByPlanIdAsync(Id<Plan> planId, CancellationToken cancellationToken = default)
        {
            MarkDeletedCalls++;
            if (MarkDeletedException is not null)
            {
                throw MarkDeletedException;
            }

            return Task.CompletedTask;
        }
        public Task<bool> AnyByPlanIdAsync(Id<Plan> planId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PlanDayExerciseRepositoryStub : IPlanDayExerciseRepository
    {
        public Exception? RemoveException { get; set; }
        public int RemoveCalls { get; private set; }
        public int AddRangeCalls { get; private set; }

        public Task RemoveByPlanDayIdAsync(Id<PlanDay> planDayId, CancellationToken cancellationToken = default)
        {
            RemoveCalls++;
            if (RemoveException != null)
            {
                throw RemoveException;
            }

            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<PlanDayExercise> exercises, CancellationToken cancellationToken = default)
        {
            AddRangeCalls++;
            return Task.CompletedTask;
        }

        public Task<List<PlanDayExercise>> GetByPlanDayIdsAsync(List<Id<PlanDay>> planDayIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<PlanDayExercise>> GetByPlanDayIdAsync(Id<PlanDay> planDayId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UserRepositoryStub : IUserRepository
    {
        public Task UpdateAsync(User user, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<User?> FindByIdAsync(Id<LgymApi.Domain.Entities.User> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByIdIncludingDeletedAsync(Id<LgymApi.Domain.Entities.User> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByIdWithRolesAsync(Id<LgymApi.Domain.Entities.User> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByNameAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByNameOrEmailAsync(string name, string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<UserRankingEntry>> GetRankingAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Pagination<UserResult>> GetUsersPaginatedAsync(FilterInput filterInput, bool includeDeleted, CancellationToken cancellationToken = default) => Task.FromResult(new Pagination<UserResult>());
    }

    private sealed class ExerciseRepositoryStub : IExerciseRepository
    {
        public Task<Exercise?> FindByIdAsync(Id<Exercise> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetAllForUserAsync(Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetAllGlobalAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetUserExercisesAsync(Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetByBodyPartAsync(Id<User> userId, BodyParts bodyPart, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Exercise>> GetByIdsAsync(List<Id<Exercise>> ids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Dictionary<Id<Exercise>, string>> GetTranslationsAsync(IEnumerable<Id<Exercise>> exerciseIds, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertTranslationAsync(Id<Exercise> exerciseId, string culture, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(Exercise exercise, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(Exercise exercise, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TrainingRepositoryStub : ITrainingRepository
    {
        public Task AddAsync(Training training, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Training?> GetByIdAsync(Id<Training> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Training?> GetLastByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Training>> GetByUserIdAndDateAsync(Id<User> userId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<DateTimeOffset>> GetDatesByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Training>> GetByGymIdsAsync(List<Id<Gym>> gymIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Training>> GetByPlanDayIdsAsync(List<Id<PlanDay>> planDayIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
