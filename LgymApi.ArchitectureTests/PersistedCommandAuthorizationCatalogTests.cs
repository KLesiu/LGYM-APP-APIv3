using FluentAssertions;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class PersistedCommandAuthorizationCatalogTests
{
    [Test]
    public void Catalog_IsClosedAndClassifiesEveryPersistedCommandAsACommittedSystemIntent()
    {
        var entries = PersistedCommandAuthorizationCatalog.Entries;

        entries.Should().HaveCount(15);
        entries.Select(entry => entry.CanonicalId).Should().OnlyHaveUniqueItems();
        entries.Select(entry => entry.RuntimeCommandType).Should().OnlyHaveUniqueItems();
        PersistedCommandAuthorizationCatalog.AuthorizationClass.Should().Be(PersistedCommandAuthorizationClass.CommittedSystemIntent);
        entries.Should().OnlyContain(entry =>
            !string.IsNullOrWhiteSpace(entry.Owner)
            && entry.SubjectIds.Count != 0
            && entry.RecipientIds.Count != 0
            && entry.ScheduleSites.Count != 0);
    }

    [Test]
    public void Catalog_CoversTheClosedRegistryAndEveryDeclaredScheduleSite()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var defaultContractsSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "LgymApi.BackgroundWorker",
            "Runtime",
            "CommandContractRegistry.DefaultContracts.cs"));
        var registrySource = defaultContractsSource + File.ReadAllText(Path.Combine(
            repositoryRoot,
            "LgymApi.BackgroundWorker",
            "Runtime",
            "CommandContractRegistry.cs"));

        foreach (var entry in PersistedCommandAuthorizationCatalog.Entries)
        {
            registrySource.Should().Contain(entry.CanonicalId);
            defaultContractsSource.Should().Contain(entry.RuntimeCommandType);

            foreach (var scheduleSite in entry.ScheduleSites)
            {
                var sourcePath = Path.Combine(repositoryRoot, scheduleSite);
                File.Exists(sourcePath).Should().BeTrue($"{entry.RuntimeCommandType} must have its declared schedule site");
                File.ReadAllText(sourcePath).Should().Contain(
                    entry.RuntimeCommandType,
                    $"{scheduleSite} must schedule {entry.RuntimeCommandType}");
            }
        }
    }
}
