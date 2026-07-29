using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning;
using LgymApi.Application.TrainingPlanning.Plan.ActivePlanPointer;
using LgymApi.Application.TrainingPlanning.Plan.CopyPlan;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LgymApi.TrainingPlanning;
using LgymApi.TrainingPlanning.Contracts;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.IntegrationTests;

[TestFixture]
[Category("PostgreSql")]
internal sealed class PostgreSqlTransactionTests : PostgreSqlIntegrationTestBase
{
    [Test]
    public async Task EfUnitOfWorkTransaction_CommitAsync_PersistsFlushedRecordAfterCommit()
    {
        var idempotencyKey = CreateIdempotencyKey("commit");

        await using (var writeScope = Factory.Services.CreateAsyncScope())
        {
            var database = writeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unitOfWork = new EfUnitOfWork(database);

            await using (var transaction = await unitOfWork.BeginTransactionAsync())
            {
                await database.ApiIdempotencyRecords.AddAsync(CreateProbe(idempotencyKey));
                await unitOfWork.SaveChangesAsync();

                (await ExistsInFreshContextAsync(idempotencyKey)).Should().BeFalse();

                await transaction.CommitAsync();
            }
        }

        (await ExistsInFreshContextAsync(idempotencyKey)).Should().BeTrue();
    }

    [Test]
    public async Task CopyPlanAsync_WhenCloneAdapterFlushesThenThrows_RollsBackProbe()
    {
        var idempotencyKey = CreateIdempotencyKey("rollback");
        var currentUser = new User { Id = Id<User>.New() };

        await using (var serviceScope = Factory.Services.CreateAsyncScope())
        {
            var database = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sourcePlan = new Plan { Id = Id<Plan>.New(), UserId = currentUser.Id, Name = "source" };
            var planRepository = new PlanRepositoryFake { PlanByShareCode = sourcePlan };
            var exerciseClone = new FlushThenThrowExerciseClonePort(database, idempotencyKey);

            var facadeServices = new ServiceCollection();
            facadeServices.AddTrainingPlanningModule();
            facadeServices.AddScoped<IPlanRepository>(_ => planRepository);
            facadeServices.AddScoped<IPlanExerciseClonePort>(_ => exerciseClone);
            facadeServices.AddScoped<IPlanDayRepository>(_ => Substitute.For<IPlanDayRepository>());
            facadeServices.AddScoped<IActivePlanPointerStore>(_ => new ActivePlanPointerStoreFake());
            facadeServices.AddScoped<IUnitOfWork>(_ => new EfUnitOfWork(database));

            using var facadeProvider = facadeServices.BuildServiceProvider();
            using var facadeScope = facadeProvider.CreateScope();
            var copyPlanUseCase = facadeScope.ServiceProvider.GetRequiredService<ICopyPlanUseCase>();

            var action = () => copyPlanUseCase.ExecuteAsync(new CopyPlanCommand(currentUser.Id, "missing-share-code"));

            await action.Should().ThrowAsync<InvalidOperationException>();
            exerciseClone.Calls.Should().Be(1);
        }

        (await ExistsInFreshContextAsync(idempotencyKey)).Should().BeFalse();
    }

    private async Task<bool> ExistsInFreshContextAsync(string idempotencyKey)
    {
        await using var verificationScope = Factory.Services.CreateAsyncScope();
        var database = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await database.ApiIdempotencyRecords
            .AnyAsync(record => record.IdempotencyKey == idempotencyKey);
    }

    private static ApiIdempotencyRecord CreateProbe(string idempotencyKey)
    {
        return new ApiIdempotencyRecord
        {
            Id = Id<ApiIdempotencyRecord>.New(),
            IdempotencyKey = idempotencyKey,
            ScopeTuple = $"POST|/postgresql-transaction-tests|{idempotencyKey}",
            RequestFingerprint = new string('f', 64),
            ResponseStatusCode = 200,
            ResponseBodyJson = "{}",
            ProcessedAt = DateTimeOffset.UtcNow
        };
    }

    private static string CreateIdempotencyKey(string scenario)
    {
        return $"postgresql-transaction-{scenario}-{Id<ApiIdempotencyRecord>.New()}";
    }

    private sealed class PlanRepositoryFake : IPlanRepository
    {
        public Plan? PlanByShareCode { get; init; }

        public Task<Plan?> FindByIdAsync(Id<Plan> id, CancellationToken cancellationToken = default) => Task.FromResult<Plan?>(null);
        public Task<Plan?> FindActiveByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.FromResult<Plan?>(null);
        public Task<LgymApi.Application.TrainingPlanning.Plan.Models.PlanReadModel?> FindActiveReadModelByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.FromResult<LgymApi.Application.TrainingPlanning.Plan.Models.PlanReadModel?>(null);
        public Task<Plan?> FindLastActiveByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.FromResult<Plan?>(null);
        public Task<List<Plan>> GetByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.FromResult<List<Plan>>([]);
        public Task<List<LgymApi.Application.TrainingPlanning.Plan.Models.PlanReadModel>> GetReadModelsByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.FromResult<List<LgymApi.Application.TrainingPlanning.Plan.Models.PlanReadModel>>([]);
        public Task AddAsync(Plan plan, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(Plan plan, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetActivePlanAsync(Id<User> userId, Id<Plan> planId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearActivePlansAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Plan?> FindByShareCodeAsync(string shareCode, CancellationToken cancellationToken = default) => Task.FromResult(PlanByShareCode);
        public Task<IReadOnlyCollection<Id<PlanExerciseReference>>> GetPlanExerciseIdsAsync(Id<Plan> planId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<Id<PlanExerciseReference>>>([]);
        public Task<Plan> ClonePlanAsync(Id<Plan> sourcePlanId, Id<User> userId, IReadOnlyDictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>> exerciseIdMap, bool isActive = true, CancellationToken cancellationToken = default) => Task.FromException<Plan>(new NotSupportedException());
        public Task<string> GenerateShareCodeAsync(Id<Plan> planId, Id<User> userId, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
    }

    private sealed class ActivePlanPointerStoreFake : IActivePlanPointerStore
    {
        public Task<Id<Plan>?> GetActivePlanIdAsync(Id<User> userId, CancellationToken cancellationToken = default) => Task.FromResult<Id<Plan>?>(null);
        public Task StageActivePlanIdAsync(Id<User> userId, Id<Plan>? planId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FlushThenThrowExerciseClonePort(
        AppDbContext database,
        string idempotencyKey) : IPlanExerciseClonePort
    {
        public int Calls { get; private set; }

        public async Task<IReadOnlyDictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>>> StageClonesAsync(
            Id<LgymApi.Identity.Contracts.AccountReference> targetAccountId,
            IReadOnlyCollection<Id<PlanExerciseReference>> exerciseIds,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            await database.ApiIdempotencyRecords.AddAsync(CreateProbe(idempotencyKey), cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("Forced post-save failure.");
        }
    }
}
