using System.Reflection;
using LgymApi.Domain.Entities;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ArchitectureTestHelpersCatalogOwnershipTests
{
    [Test]
    public void Persisted_Entity_Owners_Should_Resolve_From_The_Canonical_Catalog()
    {
        var ownerResolver = typeof(ArchitectureTestHelpers).GetMethod(
            "TryGetPersistedEntityOwner",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(Type), typeof(string).MakeByRefType()],
            modifiers: null);

        Assert.That(
            ownerResolver,
            Is.Not.Null,
            "Persisted entity ownership must be resolved through the canonical catalog.");

        foreach (var entry in PersistedEntityOwnershipCatalog.Entries)
        {
            var arguments = new object?[] { entry.EntityType, null };
            var resolved = (bool)ownerResolver!.Invoke(null, arguments)!;

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.True, $"Expected catalog owner for '{entry.EntityType.FullName}'.");
                Assert.That(arguments[1], Is.EqualTo(entry.Owner), $"Unexpected owner for '{entry.EntityType.FullName}'.");
            });
        }

        var unknownArguments = new object?[] { typeof(ArchitectureTestHelpersCatalogOwnershipTests), null };
        var unknownResolved = (bool)ownerResolver!.Invoke(null, unknownArguments)!;

        Assert.Multiple(() =>
        {
            Assert.That(unknownResolved, Is.False);
            Assert.That(unknownArguments[1], Is.EqualTo(string.Empty));
        });
    }
}
