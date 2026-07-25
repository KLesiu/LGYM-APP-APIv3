using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class PersistedEntityOwnershipCatalogTests
{
    [Test]
    public void Catalog_And_Public_DbSets_Should_Match_The_Exact_Persistence_Identity_Contract()
    {
        var dbSetProperties = GetPublicDbSetProperties();
        var dbSetEntityTypes = dbSetProperties
            .Select(property => property.PropertyType.GetGenericArguments()[0])
            .OrderBy(entityType => entityType.FullName, StringComparer.Ordinal)
            .ToList();
        var dbSetIdentities = dbSetProperties
            .Select(property => new PersistedDbSetIdentity(
                property.Name,
                property.PropertyType.GetGenericArguments()[0].FullName!))
            .OrderBy(identity => identity.PropertyName, StringComparer.Ordinal)
            .ToList();
        var expectedDbSetIdentities = PersistenceIdentityContract.DbSets
            .OrderBy(identity => identity.PropertyName, StringComparer.Ordinal)
            .ToList();
        var expectedEntityTypeNames = expectedDbSetIdentities
            .Select(identity => identity.EntityType)
            .OrderBy(entityType => entityType, StringComparer.Ordinal)
            .ToList();
        var catalogEntityTypeNames = PersistedEntityOwnershipCatalog.Entries
            .Select(entry => entry.EntityType.FullName)
            .OrderBy(entityType => entityType, StringComparer.Ordinal)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(PersistedEntityOwnershipCatalog.Entries, Has.Count.EqualTo(48));
            Assert.That(PersistenceIdentityContract.DbSets, Has.Count.EqualTo(48));
            Assert.That(expectedDbSetIdentities.Select(identity => identity.PropertyName), Is.Unique);
            Assert.That(expectedEntityTypeNames, Is.Unique);
            Assert.That(catalogEntityTypeNames, Is.EqualTo(expectedEntityTypeNames));
            Assert.That(dbSetIdentities, Is.EqualTo(expectedDbSetIdentities));
            Assert.That(PersistedEntityOwnershipCatalog.CanonicalOwners, Has.Count.EqualTo(8));
            Assert.That(PersistedEntityOwnershipCatalog.CanonicalOwners.Distinct().ToList(), Has.Count.EqualTo(8));
            Assert.That(
                PersistedEntityOwnershipCatalog.Entries.Select(entry => entry.Owner).Distinct(),
                Is.EquivalentTo(PersistedEntityOwnershipCatalog.CanonicalOwners));
            Assert.That(CountEntriesFor(PersistedEntityOwnershipCatalog.IdentityModuleName), Is.EqualTo(9));
            Assert.That(CountEntriesFor(PersistedEntityOwnershipCatalog.NotificationsModuleName), Is.EqualTo(5));
            Assert.That(CountEntriesFor(PersistedEntityOwnershipCatalog.ReportingModuleName), Is.EqualTo(7));
            Assert.That(CountEntriesFor(PersistedEntityOwnershipCatalog.TrainingPlanningModuleName), Is.EqualTo(3));
            Assert.That(CountEntriesFor(PersistedEntityOwnershipCatalog.WorkoutProgressModuleName), Is.EqualTo(10));
            Assert.That(CountEntriesFor(PersistedEntityOwnershipCatalog.CoachingModuleName), Is.EqualTo(4));
            Assert.That(CountEntriesFor(PersistedEntityOwnershipCatalog.NutritionModuleName), Is.EqualTo(6));
            Assert.That(CountEntriesFor(PersistedEntityOwnershipCatalog.PlatformModuleName), Is.EqualTo(4));
        });

        Assert.DoesNotThrow(() => PersistedEntityOwnershipCatalog.Validate(
            PersistedEntityOwnershipCatalog.Entries,
            dbSetEntityTypes));
    }

    [Test]
    public void Validate_Should_Reject_A_Duplicate_Entity_Row()
    {
        AssertInvalidCatalog(
            PersistedEntityOwnershipCatalog.Entries.Append(PersistedEntityOwnershipCatalog.Entries[0]),
            "Duplicate persisted entity catalog entries");
    }

    [Test]
    public void Validate_Should_Reject_A_Missing_Entity_Row()
    {
        AssertInvalidCatalog(
            PersistedEntityOwnershipCatalog.Entries.Skip(1),
            "Persisted DbSet entity types missing from catalog");
    }

    [Test]
    public void Validate_Should_Reject_An_Unknown_Entity_Row()
    {
        AssertInvalidCatalog(
            PersistedEntityOwnershipCatalog.Entries.Append(
                new PersistedEntityOwnership(typeof(string), PersistedEntityOwnershipCatalog.IdentityModuleName)),
            "Catalog entries that are not AppDbContext DbSet entity types");
    }

    [Test]
    public void Validate_Should_Reject_A_Ninth_Module()
    {
        var entriesWithNinthModule = PersistedEntityOwnershipCatalog.Entries
            .Select((entry, index) => index == 0
                ? new PersistedEntityOwnership(entry.EntityType, "Ninth Module")
                : entry);

        AssertInvalidCatalog(entriesWithNinthModule, "Unexpected catalog owner modules");
    }

    private static int CountEntriesFor(string owner)
    {
        return PersistedEntityOwnershipCatalog.Entries.Count(entry => entry.Owner == owner);
    }

    private static IReadOnlyList<System.Reflection.PropertyInfo> GetPublicDbSetProperties()
    {
        return typeof(AppDbContext)
            .GetProperties(System.Reflection.BindingFlags.Instance |
                           System.Reflection.BindingFlags.Public |
                           System.Reflection.BindingFlags.DeclaredOnly)
            .Where(property => property.PropertyType.IsGenericType &&
                               property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<Type> GetDbSetEntityTypes()
    {
        return GetPublicDbSetProperties()
            .Select(property => property.PropertyType.GetGenericArguments()[0])
            .OrderBy(entityType => entityType.FullName, StringComparer.Ordinal)
            .ToList();
    }

    private static void AssertInvalidCatalog(IEnumerable<PersistedEntityOwnership> entries, string expectedMessage)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PersistedEntityOwnershipCatalog.Validate(
            entries,
            GetDbSetEntityTypes()));

        Assert.That(exception!.Message, Does.Contain(expectedMessage));
    }
}
