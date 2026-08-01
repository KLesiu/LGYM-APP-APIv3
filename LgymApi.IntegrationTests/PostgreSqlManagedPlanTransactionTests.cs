using FluentAssertions;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.TrainingPlanning.Contracts;
using LgymApi.Application.TrainingPlanning.Plan.ActivePlanPointer;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LgymApi.TrainingPlanning;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.IntegrationTests;

[TestFixture]
[Category("PostgreSql")]
internal sealed class PostgreSqlManagedPlanTransactionTests : PostgreSqlIntegrationTestBase
{
    [Test]
    public async Task AssignManagedPlanAsync_WhenSaveFlushesThenFails_RollsBackCloneAndActivePointer()
    {
        var trainer = await SeedUserAsync("managed-plan-trainer", "managed-plan-trainer@example.com");
        var trainee = await SeedUserAsync("managed-plan-trainee", "managed-plan-trainee@example.com");
        var cloneId = Id<Plan>.New();

        await using (var serviceScope = Factory.Services.CreateAsyncScope())
        {
            var database = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var template = new Plan
            {
                Id = Id<Plan>.New(),
                UserId = trainer.Id,
                Name = "Trainer template",
                IsActive = false
            };
            database.Plans.Add(template);
            await database.SaveChangesAsync();

            var clonedPlan = new Plan
            {
                Id = cloneId,
                UserId = trainee.Id,
                Name = "Cloned template",
                IsActive = true
            };
            var planRepository = new PlanRepositoryFake
            {
                FoundPlan = template,
                ExerciseIds = [],
                CloneHandler = () =>
                {
                    database.Plans.Add(clonedPlan);
                    return clonedPlan;
                }
            };
            var exerciseClone = Substitute.For<IPlanExerciseClonePort>();
            var exerciseIdMap = new Dictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>>();
            exerciseClone.StageClonesAsync(trainee.Id.Rebind<LgymApi.Identity.Contracts.AccountReference>(), [], Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyDictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>>>(exerciseIdMap));

            var accountReadService = Substitute.For<IAccountReadService>();
            accountReadService.GetByIdAsync(trainee.Id, Arg.Any<CancellationToken>())
                .Returns(new AccountReadModel(trainee.Id, trainee.Name, trainee.Email.Value, null, "en", "UTC"));

            var services = new ServiceCollection();
            services.AddTrainingPlanningModule();
            services.AddScoped<IPlanRepository>(_ => planRepository);
            services.AddScoped<IPlanExerciseClonePort>(_ => exerciseClone);
            services.AddScoped<IActivePlanPointerStore>(_ => new ActivePlanPointerStore(database));
            services.AddScoped<IAccountReadService>(_ => accountReadService);
            services.AddScoped<IUnitOfWork>(_ => new FlushThenThrowUnitOfWork(database));

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var useCase = scope.ServiceProvider.GetRequiredService<IAssignManagedPlanUseCase>();

            Func<Task> action = () => useCase.ExecuteAsync(new AssignManagedPlanCommand(trainer.Id, trainee.Id, template.Id));

            await action.Should().ThrowAsync<InvalidOperationException>();
        }

        await using var verificationScope = Factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedClone = await verificationDatabase.Plans.SingleOrDefaultAsync(plan => plan.Id == cloneId);
        persistedClone.Should().BeNull();
        (await verificationDatabase.Plans.AnyAsync(plan => plan.UserId == trainee.Id && plan.IsActive)).Should().BeFalse();
    }

    private sealed class FlushThenThrowUnitOfWork : IUnitOfWork
    {
        private readonly EfUnitOfWork _inner;

        public FlushThenThrowUnitOfWork(AppDbContext database)
        {
            _inner = new EfUnitOfWork(database);
        }

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await _inner.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("Forced post-flush failure.");
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return _inner.BeginTransactionAsync(cancellationToken);
        }
    }

    private sealed class PlanRepositoryFake : IPlanRepository
    {
        public Plan? FoundPlan { get; init; }
        public IReadOnlyCollection<Id<PlanExerciseReference>> ExerciseIds { get; init; } = [];
        public Func<Plan>? CloneHandler { get; init; }

        public Task<Plan?> FindByIdAsync(Id<Plan> id, CancellationToken cancellationToken = default) => Task.FromResult(FoundPlan);
        public Task<Plan?> FindActiveByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.FromResult<Plan?>(null);
        public Task<LgymApi.Application.TrainingPlanning.Plan.Models.PlanReadModel?> FindActiveReadModelByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.FromResult<LgymApi.Application.TrainingPlanning.Plan.Models.PlanReadModel?>(null);
        public Task<Plan?> FindLastActiveByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.FromResult<Plan?>(null);
        public Task<List<Plan>> GetByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.FromResult<List<Plan>>([]);
        public Task<List<LgymApi.Application.TrainingPlanning.Plan.Models.PlanReadModel>> GetReadModelsByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.FromResult<List<LgymApi.Application.TrainingPlanning.Plan.Models.PlanReadModel>>([]);
        public Task AddAsync(Plan plan, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(Plan plan, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetActivePlanAsync(Id<User> userId, Id<Plan> planId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearActivePlansAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Plan?> FindByShareCodeAsync(string shareCode, CancellationToken cancellationToken = default) => Task.FromResult<Plan?>(null);
        public Task<IReadOnlyCollection<Id<PlanExerciseReference>>> GetPlanExerciseIdsAsync(Id<Plan> planId, CancellationToken cancellationToken = default) => Task.FromResult(ExerciseIds);
        public Task<Plan> ClonePlanAsync(Id<Plan> sourcePlanId, Id<User> userId, IReadOnlyDictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>> exerciseIdMap, bool isActive = true, CancellationToken cancellationToken = default) => CloneHandler is null ? Task.FromException<Plan>(new InvalidOperationException("A clone handler must be configured.")) : Task.FromResult(CloneHandler());
        public Task<string> GenerateShareCodeAsync(Id<Plan> planId, Id<User> userId, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
    }
}
