using FluentAssertions;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class NutritionBoundaryDocumentationTests
{
    private const string OwnerPrefix = "nutrition.owner.";
    private const string ActionPrefix = "nutrition.action.";
    private const string AdapterPrefix = "nutrition.adapter.api.";
    private const string PersistencePrefix = "nutrition.persistence.";

    private static readonly string[] OwnerIds = SplitIds("""
        nutrition.owner.diet-plan nutrition.owner.diet-meal nutrition.owner.diet-plan-history
        nutrition.owner.supplement-plan nutrition.owner.supplement-plan-item nutrition.owner.supplement-intake-log
        """);

    private static readonly string[] ActionIds = SplitIds("""
        nutrition.action.d1.list-trainee-diet-plans nutrition.action.d2.get-trainee-diet-plan nutrition.action.d3.create-trainee-diet-plan
        nutrition.action.d4.update-trainee-diet-plan nutrition.action.d5.activate-trainee-diet-plan nutrition.action.d6.delete-trainee-diet-plan
        nutrition.action.d7.get-trainee-diet-plan-history nutrition.action.d8.list-current-diet-plans nutrition.action.d9.get-current-diet-plan
        nutrition.action.s1.list-trainee-supplement-plans nutrition.action.s2.create-trainee-supplement-plan nutrition.action.s3.update-trainee-supplement-plan
        nutrition.action.s4.delete-trainee-supplement-plan nutrition.action.s5.assign-trainee-supplement-plan nutrition.action.s6.unassign-trainee-supplement-plan
        nutrition.action.s7.get-supplement-compliance-summary nutrition.action.s8.get-supplement-schedule nutrition.action.s9.check-off-supplement-intake
        """);

    private static readonly string[] AdapterIds = SplitIds("""
        nutrition.adapter.api.trainer-diet-plans nutrition.adapter.api.trainee-diet-plans
        nutrition.adapter.api.trainer-supplementation nutrition.adapter.api.trainee-supplementation
        """);

    [Test]
    public void Boundary_Should_Publish_The_Approved_Nutrition_Surface()
    {
        var rows = ParseBoundaryRows();
        var ownerRows = ValidateStableIds(rows, OwnerPrefix, OwnerIds);
        var actionRows = ValidateStableIds(rows, ActionPrefix, ActionIds);
        var adapterRows = ValidateStableIds(rows, AdapterPrefix, AdapterIds);
        var topologyRows = ValidateStableIds(
            rows,
            PersistencePrefix,
            ["nutrition.persistence.shared-topology"]);

        ValidateOwners(ownerRows);
        actionRows.Values.Should().OnlyContain(row => row.GetField("Surface") == "HTTP");
        adapterRows.Should().HaveCount(4);
        ValidateSharedPersistenceTopology(topologyRows);
    }

    [Test]
    public void Stable_Action_Rows_Should_Reject_A_Missing_Row()
    {
        var action = () => ValidateStableIds(
            ParseStableRows(CreateActionFixture(ActionIds.Skip(1))),
            ActionPrefix,
            ActionIds);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Missing stable row IDs*nutrition.action.d1.list-trainee-diet-plans*");
    }

    [Test]
    public void Stable_Owner_Rows_Should_Reject_A_Duplicate_Row()
    {
        var action = () => ValidateStableIds(
            ParseStableRows(CreateOwnerFixture(OwnerIds.Append(OwnerIds[0]))),
            OwnerPrefix,
            OwnerIds);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate stable row IDs*nutrition.owner.diet-plan*");
    }

    [Test]
    public void Persistence_Row_Should_Reject_A_Second_AppDbContext()
    {
        var action = () => ValidateSharedPersistenceTopology(ValidateStableIds(
            ParseStableRows("""
                | Persistence ID | AppDbContext count | Database count | Migration stream count | Physical split |
                | --- | --- | --- | --- | --- |
                | `nutrition.persistence.shared-topology` | `2` | `1` | `1` | `None` |
                """),
            PersistencePrefix,
            ["nutrition.persistence.shared-topology"]));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*exactly one AppDbContext*");
    }

    private static IReadOnlyList<DocumentationRow> ParseBoundaryRows()
    {
        var markdown = File.ReadAllText(Path.Combine(
            ArchitectureTestHelpers.ResolveRepositoryRoot(),
            "docs",
            "modular-monolith",
            "issue-390-nutrition-boundary.md"));
        return ParseStableRows(markdown);
    }

    private static List<DocumentationRow> ParseStableRows(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var rows = new List<DocumentationRow>();

        for (var lineIndex = 0; lineIndex < lines.Length - 1; lineIndex++)
        {
            var headers = ParseTableCells(lines[lineIndex]);
            if (headers.Count == 0 || !headers[0].EndsWith(" ID", StringComparison.Ordinal) ||
                !IsTableSeparator(lines[lineIndex + 1], headers.Count))
            {
                continue;
            }

            for (lineIndex += 2; lineIndex < lines.Length; lineIndex++)
            {
                var cells = ParseTableCells(lines[lineIndex]);
                if (cells.Count == 0)
                {
                    lineIndex--;
                    break;
                }

                if (cells.Count != headers.Count)
                {
                    throw new InvalidOperationException(
                        $"Stable documentation table '{headers[0]}' has a row with {cells.Count} cells; expected {headers.Count}.");
                }

                var id = UnwrapCode(cells[0]);
                if (id.StartsWith("nutrition.", StringComparison.Ordinal))
                {
                    rows.Add(new DocumentationRow(
                        id,
                        headers.Zip(cells.Select(UnwrapCode))
                            .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal)));
                }
            }
        }

        return rows;
    }

    private static IReadOnlyDictionary<string, DocumentationRow> ValidateStableIds(
        IEnumerable<DocumentationRow> rows,
        string prefix,
        IEnumerable<string> expectedIds)
    {
        var scopedRows = rows.Where(row => row.Id.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        var duplicateIds = scopedRows.GroupBy(row => row.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (duplicateIds.Count > 0)
        {
            throw new InvalidOperationException($"Duplicate stable row IDs for prefix '{prefix}': {string.Join(", ", duplicateIds)}.");
        }

        var expectedIdSet = expectedIds.ToHashSet(StringComparer.Ordinal);
        var actualIdSet = scopedRows.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        var missingIds = expectedIdSet.Except(actualIdSet).OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (missingIds.Count > 0)
        {
            throw new InvalidOperationException($"Missing stable row IDs for prefix '{prefix}': {string.Join(", ", missingIds)}.");
        }

        var unknownIds = actualIdSet.Except(expectedIdSet).OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (unknownIds.Count > 0)
        {
            throw new InvalidOperationException($"Unknown stable row IDs for prefix '{prefix}': {string.Join(", ", unknownIds)}.");
        }

        return scopedRows.ToDictionary(row => row.Id, StringComparer.Ordinal);
    }

    private static void ValidateOwners(IReadOnlyDictionary<string, DocumentationRow> ownerRows)
    {
        var catalogEntries = PersistedEntityOwnershipCatalog.Entries
            .Where(entry => entry.Owner == PersistedEntityOwnershipCatalog.NutritionModuleName)
            .ToList();

        catalogEntries.Should().HaveCount(6);
        ownerRows.Values.Select(row => row.GetField("Entity name"))
            .Should().BeEquivalentTo(catalogEntries.Select(entry => entry.EntityType.Name));
        ownerRows.Values.Should().OnlyContain(row => row.GetField("Owner") == PersistedEntityOwnershipCatalog.NutritionModuleName);
    }

    private static void ValidateSharedPersistenceTopology(IReadOnlyDictionary<string, DocumentationRow> rows)
    {
        var topology = rows["nutrition.persistence.shared-topology"];
        if (topology.GetField("AppDbContext count") != "1")
        {
            throw new InvalidOperationException("Nutrition boundary must declare exactly one AppDbContext.");
        }

        if (topology.GetField("Database count") != "1" || topology.GetField("Migration stream count") != "1" ||
            topology.GetField("Physical split") != "None")
        {
            throw new InvalidOperationException("Nutrition boundary must declare one database, one migration stream, and no physical split.");
        }
    }

    private static List<string> ParseTableCells(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length < 2 || trimmed[0] != '|' || trimmed[^1] != '|'
            ? []
            : trimmed[1..^1].Split('|').Select(cell => cell.Trim()).ToList();
    }

    private static bool IsTableSeparator(string line, int expectedCellCount)
    {
        var cells = ParseTableCells(line);
        return cells.Count == expectedCellCount && cells.All(cell => cell.Length >= 3 && cell.All(character => character == '-'));
    }

    private static string UnwrapCode(string value) => value.Length >= 2 && value[0] == '`' && value[^1] == '`' ? value[1..^1] : value;

    private static string[] SplitIds(string ids) => ids.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string CreateOwnerFixture(IEnumerable<string> ids) => string.Join('\n',
        ["| Owner ID | Entity name | Owner |", "| --- | --- | --- |", .. ids.Select(id => $"| `{id}` | Fixture | Nutrition |")]);

    private static string CreateActionFixture(IEnumerable<string> ids) => string.Join('\n',
        ["| Action ID | Application action | Surface | Current adapter family |", "| --- | --- | --- | --- |", .. ids.Select(id => $"| `{id}` | Fixture | HTTP | Fixture |")]);

    private sealed record DocumentationRow(string Id, IReadOnlyDictionary<string, string> Fields)
    {
        public string GetField(string fieldName) => Fields.TryGetValue(fieldName, out var value)
            ? value
            : throw new InvalidOperationException($"Stable row '{Id}' is missing expected field '{fieldName}'.");
    }
}
