using System.Reflection;
using FluentAssertions;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories.Nutrition;
using LgymApi.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class DietPlanPersistenceRepositoryTests
{
    [Test]
    public async Task PlanLoads_UseTrackedMutationLoadAndNoTrackingReadLoad()
    {
        await using var database = CreateDbContext();
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = CreatePlan(trainerId, traineeId, "plan", true, new DateOnly(2026, 7, 20), DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1));
        database.DietPlans.Add(plan);
        database.DietMeals.AddRange(
            CreateMeal(plan.Id, "second", 1, DateTimeOffset.UtcNow.AddDays(-2)),
            CreateMeal(plan.Id, "first", 0, DateTimeOffset.UtcNow.AddDays(-1)));
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var repository = new DietPlanPersistenceRepository(database);

        var tracked = await repository.FindTrackedPlanByIdAsync(plan.Id);

        tracked.Should().NotBeNull();
        tracked!.Meals.Select(meal => meal.Name).Should().Equal("first", "second");
        database.ChangeTracker.Entries().Should().NotBeEmpty();

        database.ChangeTracker.Clear();
        var noTracking = await repository.GetPlanByIdAsync(plan.Id);

        noTracking.Should().NotBeNull();
        noTracking!.Meals.Select(meal => meal.Name).Should().Equal("first", "second");
        database.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Test]
    public async Task ReadQueries_PreserveLegacyFiltersIncludesAndOrderingWithoutTracking()
    {
        await using var database = CreateDbContext();
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var activeLatest = CreatePlan(trainerId, traineeId, "active-latest", true, new DateOnly(2026, 7, 20), DateTimeOffset.UtcNow.AddDays(-5), DateTimeOffset.UtcNow.AddDays(-1));
        var activeEarlier = CreatePlan(trainerId, traineeId, "active-earlier", true, new DateOnly(2026, 7, 10), DateTimeOffset.UtcNow.AddDays(-4), DateTimeOffset.UtcNow.AddDays(-2));
        var inactive = CreatePlan(trainerId, traineeId, "inactive", false, new DateOnly(2026, 7, 30), DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow);
        var deleted = CreatePlan(trainerId, traineeId, "deleted", true, new DateOnly(2026, 8, 1), DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddHours(-1), isDeleted: true);
        var otherTrainer = CreatePlan(Id<User>.New(), traineeId, "other-trainer", true, new DateOnly(2026, 7, 25), DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow);
        var otherTrainee = CreatePlan(trainerId, Id<User>.New(), "other-trainee", true, new DateOnly(2026, 7, 25), DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow);
        database.DietPlans.AddRange(activeLatest, activeEarlier, inactive, deleted, otherTrainer, otherTrainee);
        database.DietMeals.AddRange(
            CreateMeal(activeLatest.Id, "dinner", 1, DateTimeOffset.UtcNow.AddDays(-2)),
            CreateMeal(activeLatest.Id, "breakfast", 0, DateTimeOffset.UtcNow.AddDays(-1)));
        database.DietPlanHistories.AddRange(
            CreateHistory(activeLatest.Id, trainerId, "latest", DateTimeOffset.UtcNow.AddHours(-1)),
            CreateHistory(activeLatest.Id, trainerId, "older", DateTimeOffset.UtcNow.AddDays(-1)),
            CreateHistory(activeLatest.Id, trainerId, "deleted", DateTimeOffset.UtcNow, isDeleted: true));
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var repository = new DietPlanPersistenceRepository(database);

        var trainerPlans = await repository.ListPlansByTrainerAndTraineeAsync(trainerId, traineeId);
        var activePlans = await repository.ListActivePlansForTraineeAsync(traineeId);
        var activePlan = await repository.GetActivePlanForTraineeAsync(traineeId);
        var history = await repository.ListPlanHistoryAsync(activeLatest.Id);

        trainerPlans.Select(plan => plan.Id).Should().Equal(activeLatest.Id, activeEarlier.Id, inactive.Id);
        trainerPlans.Single(plan => plan.Id == activeLatest.Id).Meals.Select(meal => meal.Name).Should().Equal("breakfast", "dinner");
        activePlans.Select(plan => plan.Id).Should().Equal(otherTrainer.Id, activeLatest.Id, activeEarlier.Id);
        activePlan!.Id.Should().Be(otherTrainer.Id);
        history.Select(entry => entry.ChangeType).Should().Equal("latest", "older");
        database.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Test]
    public async Task StagedWrites_AreInvisibleToFreshContextUntilExternalUnitOfWorkSaves()
    {
        var databaseName = $"diet-plan-persistence-stage-{Id<DietPlanPersistenceRepositoryTests>.New():N}";
        var options = CreateOptions(databaseName);
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var plan = CreatePlan(trainerId, traineeId, "staged", true, new DateOnly(2026, 7, 20), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var history = CreateHistory(plan.Id, trainerId, "Created", DateTimeOffset.UtcNow);

        await using (var writeContext = new AppDbContext(options))
        {
            var repository = new DietPlanPersistenceRepository(writeContext);
            await repository.AddPlanAsync(plan);
            await repository.AddHistoryEntryAsync(history);

            await using (var beforeCommitContext = new AppDbContext(options))
            {
                (await beforeCommitContext.DietPlans.AsNoTracking().AnyAsync()).Should().BeFalse();
                (await beforeCommitContext.DietPlanHistories.AsNoTracking().AnyAsync()).Should().BeFalse();
            }

            var unitOfWork = new EfUnitOfWork(writeContext);
            await unitOfWork.SaveChangesAsync();
        }

        await using var afterCommitContext = new AppDbContext(options);
        (await afterCommitContext.DietPlans.AsNoTracking().SingleAsync()).Id.Should().Be(plan.Id);
        (await afterCommitContext.DietPlanHistories.AsNoTracking().SingleAsync()).Id.Should().Be(history.Id);
    }

    [Test]
    public void Repository_DoesNotExposeOrCallSaveOrTransactionOperations()
    {
        var declaredMethodNames = typeof(DietPlanPersistenceRepository)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Select(method => method.Name);
        var sourcePath = Path.Combine(
            GetRepositoryRoot(),
            "LgymApi.Infrastructure",
            "Repositories",
            "Nutrition",
            "DietPlanPersistenceRepository.cs");
        var source = File.ReadAllText(sourcePath);

        declaredMethodNames.Should().NotContain(new[] { nameof(AppDbContext.SaveChangesAsync), nameof(EfUnitOfWork.BeginTransactionAsync) });
        source.Should().NotContain("SaveChangesAsync(");
        source.Should().NotContain("BeginTransactionAsync(");
    }

    [Test]
    public void NutritionInfrastructure_RegistersDietPersistencePortOnce()
    {
        var services = new ServiceCollection();
        services.AddNutritionInfrastructure();

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(IDietPlanPersistence))
            .ToList();

        registrations.Should().ContainSingle();
        registrations[0].ImplementationType.Should().Be(typeof(DietPlanPersistenceRepository));
    }

    private static AppDbContext CreateDbContext()
        => new(CreateOptions($"diet-plan-persistence-{Id<DietPlanPersistenceRepositoryTests>.New():N}"));

    private static DbContextOptions<AppDbContext> CreateOptions(string databaseName)
        => new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

    private static DietPlan CreatePlan(
        Id<User> trainerId,
        Id<User> traineeId,
        string name,
        bool isActive,
        DateOnly startDate,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isDeleted = false)
        => new()
        {
            Id = Id<DietPlan>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            Name = name,
            IsActive = isActive,
            StartDate = startDate,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            IsDeleted = isDeleted
        };

    private static DietMeal CreateMeal(Id<DietPlan> planId, string name, int order, DateTimeOffset createdAt)
        => new()
        {
            Id = Id<DietMeal>.New(),
            DietPlanId = planId,
            Name = name,
            Order = order,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            IsDeleted = false
        };

    private static DietPlanHistory CreateHistory(
        Id<DietPlan> planId,
        Id<User> changedByUserId,
        string changeType,
        DateTimeOffset changeDate,
        bool isDeleted = false)
        => new()
        {
            Id = Id<DietPlanHistory>.New(),
            DietPlanId = planId,
            ChangedByUserId = changedByUserId,
            ChangeType = changeType,
            ChangeDate = changeDate,
            SnapshotJson = "{}",
            CreatedAt = changeDate,
            UpdatedAt = changeDate,
            IsDeleted = isDeleted
        };

    private static string GetRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LgymApi.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
