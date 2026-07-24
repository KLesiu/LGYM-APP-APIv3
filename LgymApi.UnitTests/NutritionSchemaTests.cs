using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class NutritionSchemaTests
{
    private sealed record NutritionRosterEntry(Type EntityType, string DbSetName, string TableName, string ConfigurationName);

    private static readonly NutritionRosterEntry[] ExpectedRoster =
    [
        new(typeof(DietPlan), nameof(AppDbContext.DietPlans), "DietPlans", "DietPlanEntityTypeConfiguration"),
        new(typeof(DietMeal), nameof(AppDbContext.DietMeals), "DietMeals", "DietMealEntityTypeConfiguration"),
        new(typeof(DietPlanHistory), nameof(AppDbContext.DietPlanHistories), "DietPlanHistories", "DietPlanHistoryEntityTypeConfiguration"),
        new(typeof(SupplementPlan), nameof(AppDbContext.SupplementPlans), "SupplementPlans", "SupplementPlanEntityTypeConfiguration"),
        new(typeof(SupplementPlanItem), nameof(AppDbContext.SupplementPlanItems), "SupplementPlanItems", "SupplementPlanItemEntityTypeConfiguration"),
        new(typeof(SupplementIntakeLog), nameof(AppDbContext.SupplementIntakeLogs), "SupplementIntakeLogs", "SupplementIntakeLogEntityTypeConfiguration")
    ];

    [Test]
    public void Nutrition_Model_Should_Keep_Exactly_Six_Entities_DbSets_Configurations_And_Registrar_Entries()
    {
        using var dbContext = CreateContext();
        var root = ResolveRepositoryRoot();
        var configurationDirectory = Path.Combine(root, "LgymApi.Infrastructure/Data/Configurations/Nutrition");
        var configurationFiles = Directory.GetFiles(configurationDirectory, "*.cs")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var registrar = File.ReadAllText(Path.Combine(root, "LgymApi.Infrastructure/Data/Configurations/AppDbContextEntityTypeConfigurationRegistrar.cs"));
        var rosterViolations = ValidateRoster(ExpectedRoster);

        Assert.Multiple(() =>
        {
            rosterViolations.Should().BeEmpty();
            ExpectedRoster.Should().OnlyContain(entry => dbContext.Model.FindEntityType(entry.EntityType)!.GetTableName() == entry.TableName);
            typeof(AppDbContext).GetProperties().Where(property => property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                .Where(property => ExpectedRoster.Select(entry => entry.EntityType).Contains(property.PropertyType.GenericTypeArguments[0]))
                .Select(property => property.Name).Should().BeEquivalentTo(ExpectedRoster.Select(entry => entry.DbSetName));
            configurationFiles.Should().BeEquivalentTo(ExpectedRoster.Select(entry => $"{entry.ConfigurationName}.cs").Append("NutritionConfigurationFilters.cs"));
            ExpectedRoster.Should().OnlyContain(entry => registrar.Contains($"Register(new {entry.ConfigurationName}())", StringComparison.Ordinal));
        });
    }

    [Test]
    public void Nutrition_Model_Roster_Guard_Should_Reject_A_Seventh_Entity_With_A_Targeted_Diagnostic()
    {
        var violations = ValidateRoster(ExpectedRoster.Append(new(typeof(SeventhNutritionEntity), "SeventhNutritionEntities", "SeventhNutritionEntities", "SeventhNutritionEntityTypeConfiguration")));

        violations.Should().ContainSingle().Which.Should().Be("Nutrition roster must contain exactly six entities; found 7 including SeventhNutritionEntity.");
    }

    [Test]
    public void Nutrition_Model_Should_Keep_Exact_Tables_Indexes_ForeignKeys_Filters_And_Conversions()
    {
        using var dbContext = CreateContext();
        var entities = ExpectedRoster.Select(entry => GetEntity(dbContext, entry.EntityType)).ToArray();

        AssertTables(entities);
        AssertIndexes(entities);
        AssertForeignKeys(entities);
        AssertFiltersAndConversions(entities);
    }

    private static IReadOnlyList<string> ValidateRoster(IEnumerable<NutritionRosterEntry> roster)
    {
        var entries = roster.ToArray();
        if (entries.Length == ExpectedRoster.Length)
        {
            return [];
        }

        return [$"Nutrition roster must contain exactly six entities; found {entries.Length} including {entries.Last().EntityType.Name}."];
    }

    private static void AssertTables(IEnumerable<IEntityType> entities)
    {
        foreach (var entry in ExpectedRoster)
        {
            entities.Single(entity => entity.ClrType == entry.EntityType).GetTableName().Should().Be(entry.TableName);
        }
    }

    private static void AssertIndexes(IEnumerable<IEntityType> entities)
    {
        var indexes = entities.SelectMany(entity => entity.GetIndexes()).ToArray();
        indexes.Should().HaveCount(8);
        AssertIndex(GetEntity(entities, typeof(DietPlan)), [nameof(DietPlan.TrainerId), nameof(DietPlan.TraineeId), nameof(DietPlan.CreatedAt)], false, "IX_DietPlans_TrainerId_TraineeId_CreatedAt");
        AssertIndex(GetEntity(entities, typeof(DietPlan)), [nameof(DietPlan.TraineeId), nameof(DietPlan.IsActive)], false, "IX_DietPlans_TraineeId_IsActive");
        AssertIndex(GetEntity(entities, typeof(DietMeal)), [nameof(DietMeal.DietPlanId), nameof(DietMeal.Order)], false, "IX_DietMeals_DietPlanId_Order");
        AssertIndex(GetEntity(entities, typeof(DietPlanHistory)), [nameof(DietPlanHistory.DietPlanId), nameof(DietPlanHistory.ChangeDate)], false, "IX_DietPlanHistories_DietPlanId_ChangeDate");
        AssertIndex(GetEntity(entities, typeof(SupplementPlan)), [nameof(SupplementPlan.TrainerId), nameof(SupplementPlan.TraineeId), nameof(SupplementPlan.CreatedAt)], false, "IX_SupplementPlans_TrainerId_TraineeId_CreatedAt");
        AssertIndex(GetEntity(entities, typeof(SupplementPlan)), [nameof(SupplementPlan.TraineeId), nameof(SupplementPlan.IsActive)], false, "IX_SupplementPlans_TraineeId_IsActive");
        AssertIndex(GetEntity(entities, typeof(SupplementPlanItem)), [nameof(SupplementPlanItem.PlanId), nameof(SupplementPlanItem.Order), nameof(SupplementPlanItem.TimeOfDay)], false, "IX_SupplementPlanItems_PlanId_Order_TimeOfDay");
        AssertIndex(GetEntity(entities, typeof(SupplementIntakeLog)), [nameof(SupplementIntakeLog.TraineeId), nameof(SupplementIntakeLog.PlanItemId), nameof(SupplementIntakeLog.IntakeDate)], true, "IX_SupplementIntakeLogs_TraineeId_PlanItemId_IntakeDate", "\"IsDeleted\" = FALSE");
    }

    private static void AssertForeignKeys(IEnumerable<IEntityType> entities)
    {
        var foreignKeys = entities.SelectMany(entity => entity.GetForeignKeys()).ToArray();
        foreignKeys.Should().HaveCount(10);
        AssertForeignKey(GetEntity(entities, typeof(DietPlan)), nameof(DietPlan.TrainerId), typeof(User), DeleteBehavior.Cascade);
        AssertForeignKey(GetEntity(entities, typeof(DietPlan)), nameof(DietPlan.TraineeId), typeof(User), DeleteBehavior.Cascade);
        AssertForeignKey(GetEntity(entities, typeof(DietMeal)), nameof(DietMeal.DietPlanId), typeof(DietPlan), DeleteBehavior.Cascade);
        AssertForeignKey(GetEntity(entities, typeof(DietPlanHistory)), nameof(DietPlanHistory.DietPlanId), typeof(DietPlan), DeleteBehavior.Cascade);
        AssertForeignKey(GetEntity(entities, typeof(DietPlanHistory)), nameof(DietPlanHistory.ChangedByUserId), typeof(User), DeleteBehavior.Restrict);
        AssertForeignKey(GetEntity(entities, typeof(SupplementPlan)), nameof(SupplementPlan.TrainerId), typeof(User), DeleteBehavior.Cascade);
        AssertForeignKey(GetEntity(entities, typeof(SupplementPlan)), nameof(SupplementPlan.TraineeId), typeof(User), DeleteBehavior.Cascade);
        AssertForeignKey(GetEntity(entities, typeof(SupplementPlanItem)), nameof(SupplementPlanItem.PlanId), typeof(SupplementPlan), DeleteBehavior.Cascade);
        AssertForeignKey(GetEntity(entities, typeof(SupplementIntakeLog)), nameof(SupplementIntakeLog.TraineeId), typeof(User), DeleteBehavior.Cascade);
        AssertForeignKey(GetEntity(entities, typeof(SupplementIntakeLog)), nameof(SupplementIntakeLog.PlanItemId), typeof(SupplementPlanItem), DeleteBehavior.Cascade);
    }

    private static void AssertFiltersAndConversions(IEnumerable<IEntityType> entities)
    {
        foreach (var entity in entities)
        {
            entity.GetDeclaredQueryFilters().Should().NotBeEmpty($"{entity.ClrType.Name} must keep the global soft-delete filter");
            foreach (var property in entity.GetProperties().Where(property => property.ClrType.IsGenericType && property.ClrType.GetGenericTypeDefinition() == typeof(Id<>)))
            {
                property.GetValueConverter().Should().NotBeNull($"{entity.ClrType.Name}.{property.Name} must retain its typed-ID converter");
                property.GetValueConverter()!.ModelClrType.Should().Be(property.ClrType);
                property.GetValueConverter()!.ProviderClrType.FullName.Should().Be("System.Guid");
            }
        }

        var daysOfWeekMask = GetEntity(entities, typeof(SupplementPlanItem)).FindProperty(nameof(SupplementPlanItem.DaysOfWeekMask))!;
        daysOfWeekMask.GetValueConverter()!.ModelClrType.Should().Be(typeof(DaysOfWeekSet));
        daysOfWeekMask.GetValueConverter()!.ProviderClrType.Should().Be(typeof(int));
    }

    private static void AssertIndex(IEntityType entity, string[] properties, bool unique, string name, string? filter = null)
    {
        var index = entity.GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual(properties));
        index.IsUnique.Should().Be(unique);
        index.GetDatabaseName().Should().Be(name);
        index.GetFilter().Should().Be(filter);
    }

    private static void AssertForeignKey(IEntityType entity, string property, Type principalType, DeleteBehavior deleteBehavior)
    {
        var foreignKey = entity.GetForeignKeys().Single(key => key.Properties.Select(candidate => candidate.Name).SequenceEqual([property]));
        foreignKey.PrincipalEntityType.ClrType.Should().Be(principalType);
        foreignKey.DeleteBehavior.Should().Be(deleteBehavior);
    }

    private static IEntityType GetEntity(AppDbContext dbContext, Type type) => dbContext.Model.FindEntityType(type)!;
    private static IEntityType GetEntity(IEnumerable<IEntityType> entities, Type type) => entities.Single(entity => entity.ClrType == type);
    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"nutrition-schema-{Id<NutritionSchemaTests>.New():N}").Options);

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LgymApi.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Unable to locate repository root.");
    }

    private sealed class SeventhNutritionEntity;
}
