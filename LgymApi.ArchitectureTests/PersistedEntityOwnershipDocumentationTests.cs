using System.Text.RegularExpressions;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class PersistedEntityOwnershipDocumentationTests
{
    private const string CatalogHeading = "## Persisted Entity Ownership Catalog";

    [Test]
    public void Ownership_Map_Should_Match_The_Compiled_Catalog()
    {
        var markdown = File.ReadAllText(Path.Combine(
            ArchitectureTestHelpers.ResolveRepositoryRoot(),
            "docs",
            "modular-monolith",
            "issue-376-ownership-map.md"));

        AssertRowsMatchCatalog(ParseCatalogRows(markdown));
    }

    [TestCase("IEloRegistryRepository", "EloRegistry")]
    [TestCase("EloRegistries", "EloRegistry")]
    [TestCase("MainRecordRepository / IMainRecordRepository", "MainRecord")]
    public void Legacy_Owner_Matrix_Persistence_Rows_Should_Match_The_Compiled_Catalog(string artifactName, string entityName)
    {
        var markdown = File.ReadAllText(Path.Combine(
            ArchitectureTestHelpers.ResolveRepositoryRoot(),
            "docs",
            "modular-monolith",
            "issue-376-ownership-map.md"));
        var expectedOwner = PersistedEntityOwnershipCatalog.Entries
            .Single(entry => entry.EntityType.Name == entityName)
            .Owner;

        Assert.That(markdown, Does.Contain($"| `{artifactName}` | `{expectedOwner}` |"));
    }

    [Test]
    public void Readme_Should_Describe_The_Typed_Id_Transport_Boundary_Without_A_Blanket_Guid_Claim()
    {
        var readme = File.ReadAllText(Path.Combine(ArchitectureTestHelpers.ResolveRepositoryRoot(), "README.md"));

        Assert.Multiple(() =>
        {
            Assert.That(readme, Does.Contain("Known internal entity IDs use `Id<T>`"));
            Assert.That(readme, Does.Contain("PostgreSQL `uuid`"));
            Assert.That(readme, Does.Contain("API responses transport UUID values such as `_id` as strings"));
            Assert.That(readme, Does.Not.Contain("all IDs are GUIDs"));
        });
    }

    [Test]
    public void Parser_Should_Reject_A_Duplicate_Documented_Row()
    {
        var rows = GetCatalogRows();
        rows.Add(rows[0]);

        Assert.That(() => AssertRowsMatchCatalog(ParseCatalogRows(CreateCatalogMarkdown(rows))), Throws.InvalidOperationException
            .With.Message.Contains("Duplicate documented persisted entity rows"));
    }

    [Test]
    public void Parser_Should_Reject_A_Missing_Documented_Row()
    {
        var rows = GetCatalogRows();
        rows.RemoveAt(0);

        Assert.That(() => AssertRowsMatchCatalog(ParseCatalogRows(CreateCatalogMarkdown(rows))), Throws.InvalidOperationException
            .With.Message.Contains("Persisted entity catalog rows missing from documentation"));
    }

    [Test]
    public void Parser_Should_Reject_A_Nonexistent_Documented_Reference()
    {
        var rows = GetCatalogRows();
        rows.Add(("NonexistentEntity", PersistedEntityOwnershipCatalog.IdentityModuleName));

        Assert.That(() => AssertRowsMatchCatalog(ParseCatalogRows(CreateCatalogMarkdown(rows))), Throws.InvalidOperationException
            .With.Message.Contains("Documented persisted entity rows absent from the catalog"));
    }

    [Test]
    public void Parser_Should_Accept_Windows_Line_Endings()
    {
        var markdown = CreateCatalogMarkdown(GetCatalogRows())
            .Replace("\n", "\r\n", StringComparison.Ordinal);

        Assert.That(() => AssertRowsMatchCatalog(ParseCatalogRows(markdown)), Throws.Nothing);
    }

    private static List<(string EntityName, string Owner)> GetCatalogRows()
    {
        return PersistedEntityOwnershipCatalog.Entries
            .Select(entry => (entry.EntityType.Name, entry.Owner))
            .ToList();
    }

    private static string CreateCatalogMarkdown(IEnumerable<(string EntityName, string Owner)> rows)
    {
        var tableRows = rows.Select(row => $"| `{row.EntityName}` | `{row.Owner}` |");
        return string.Join('\n', [CatalogHeading, "", "| Persisted entity | Owner module |", "| --- | --- |", .. tableRows]);
    }

    private static List<(string EntityName, string Owner)> ParseCatalogRows(string markdown)
    {
        var headingIndex = markdown.IndexOf(CatalogHeading, StringComparison.Ordinal);
        if (headingIndex < 0)
        {
            throw new InvalidOperationException($"Missing documentation heading '{CatalogHeading}'.");
        }

        var catalogSection = markdown[headingIndex..];
        var nextHeadingIndex = catalogSection.IndexOf("\n## ", CatalogHeading.Length, StringComparison.Ordinal);
        if (nextHeadingIndex >= 0)
        {
            catalogSection = catalogSection[..nextHeadingIndex];
        }

        var rows = new List<(string EntityName, string Owner)>();
        foreach (var rawLine in catalogSection.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd('\r');
            var match = Regex.Match(line, "^\\| `(?<entity>[^`]+)` \\| `(?<owner>[^`]+)` \\|$");
            if (match.Success)
            {
                rows.Add((match.Groups["entity"].Value, match.Groups["owner"].Value));
            }
        }

        return rows;
    }

    private static void AssertRowsMatchCatalog(IReadOnlyCollection<(string EntityName, string Owner)> documentedRows)
    {
        var expectedRows = GetCatalogRows();
        var duplicateNames = documentedRows
            .GroupBy(row => row.EntityName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateNames.Count > 0)
        {
            throw new InvalidOperationException($"Duplicate documented persisted entity rows: {string.Join(", ", duplicateNames)}.");
        }

        var documentedByName = documentedRows.ToDictionary(row => row.EntityName, row => row.Owner, StringComparer.Ordinal);
        var expectedByName = expectedRows.ToDictionary(row => row.EntityName, row => row.Owner, StringComparer.Ordinal);
        var missingNames = expectedByName.Keys.Except(documentedByName.Keys, StringComparer.Ordinal).ToList();
        if (missingNames.Count > 0)
        {
            throw new InvalidOperationException($"Persisted entity catalog rows missing from documentation: {string.Join(", ", missingNames)}.");
        }

        var nonexistentNames = documentedByName.Keys.Except(expectedByName.Keys, StringComparer.Ordinal).ToList();
        if (nonexistentNames.Count > 0)
        {
            throw new InvalidOperationException($"Documented persisted entity rows absent from the catalog: {string.Join(", ", nonexistentNames)}.");
        }

        var ownerMismatches = expectedRows
            .Where(expected => !string.Equals(documentedByName[expected.EntityName], expected.Owner, StringComparison.Ordinal))
            .Select(expected => expected.EntityName)
            .ToList();
        if (ownerMismatches.Count > 0)
        {
            throw new InvalidOperationException($"Documented persisted entity owners do not match the catalog: {string.Join(", ", ownerMismatches)}.");
        }
    }
}
