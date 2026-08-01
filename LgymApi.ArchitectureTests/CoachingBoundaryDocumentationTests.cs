using FluentAssertions;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class CoachingBoundaryDocumentationTests
{
    private static readonly string[] OwnerIds = SplitIds("coaching.owner.trainer-invitation coaching.owner.trainer-trainee-link coaching.owner.trainee-note coaching.owner.trainee-note-history");

    private static readonly string[] ActionIds = SplitIds("""
        coaching.action.create-trainer-invitation coaching.action.create-trainer-invitation-by-email coaching.action.get-trainer-invitations coaching.action.get-trainer-invitations-paginated coaching.action.get-public-invitation-status coaching.action.accept-trainer-invitation coaching.action.reject-trainer-invitation coaching.action.revoke-trainer-invitation
        coaching.action.unlink-trainee coaching.action.detach-from-trainer coaching.action.get-current-trainer coaching.action.get-trainer-dashboard coaching.action.get-training-dates coaching.action.get-training-by-date coaching.action.get-exercise-scores-chart coaching.action.get-elo-chart coaching.action.get-main-records-history coaching.action.list-managed-plans
        coaching.action.create-managed-plan coaching.action.update-managed-plan coaching.action.delete-managed-plan coaching.action.assign-managed-plan coaching.action.unassign-managed-plan coaching.action.get-active-managed-plan coaching.action.list-trainer-notes coaching.action.create-trainee-note coaching.action.update-trainee-note coaching.action.delete-trainee-note
        coaching.action.get-trainee-note-history coaching.action.list-visible-trainee-notes coaching.action.get-visible-trainee-note
        """);

    private static readonly string[] ContractIds = SplitIds("coaching.contract.relationship-access coaching.contract.invitation-facts coaching.contract.notification-facts coaching.contract.training-planning-authorization coaching.contract.workout-progress-authorization");

    private static readonly string[] DependencyIds = SplitIds("""
        coaching.dependency.api-to-coaching coaching.dependency.worker-to-coaching coaching.dependency.reporting-to-coaching coaching.dependency.nutrition-to-coaching coaching.dependency.coaching-to-identity
        coaching.dependency.coaching-to-training-planning coaching.dependency.coaching-to-workout-progress coaching.dependency.training-planning-authorization-port coaching.dependency.workout-progress-authorization-port
        """);

    private static readonly string[] AdapterIds = SplitIds("""
        coaching.adapter.api-invitations coaching.adapter.api-dashboard-progress coaching.adapter.api-managed-plans coaching.adapter.api-relationships coaching.adapter.api-trainee-notes coaching.adapter.api-public-invitation-status coaching.adapter.legacy-command.invitation-created-email
        coaching.adapter.legacy-command.invitation-created-in-app coaching.adapter.legacy-command.invitation-accepted-email coaching.adapter.legacy-command.invitation-accepted-in-app coaching.adapter.legacy-command.invitation-revoked-email coaching.adapter.legacy-command.invitation-rejected-in-app
        coaching.adapter.legacy-command.relationship-ended-in-app coaching.adapter.legacy-command.trainee-note-updated-in-app
        """);

    private static readonly string[] IntentIds = SplitIds("coaching.intent.invitation-created coaching.intent.invitation-accepted coaching.intent.invitation-rejected coaching.intent.invitation-revoked coaching.intent.relationship-ended coaching.intent.trainee-note-updated");

    private static readonly string[] GuardIds = SplitIds("coaching.guard.persisted-ownership coaching.guard.action-ledger coaching.guard.public-contracts coaching.guard.dependency-dag coaching.guard.persistence-topology coaching.guard.compatibility-adapters coaching.guard.scope");

    [Test]
    public void Boundary_Should_Publish_The_Approved_Stable_Surface()
    {
        var rows = ParseBoundaryRows();
        var ownerRows = ValidateStableIds(rows, "coaching.owner.", OwnerIds);
        var actionRows = ValidateStableIds(rows, "coaching.action.", ActionIds);
        var contractRows = ValidateStableIds(rows, "coaching.contract.", ContractIds);
        ValidateStableIds(rows, "coaching.dependency.", DependencyIds);
        ValidateStableIds(rows, "coaching.adapter.", AdapterIds);
        ValidateStableIds(rows, "coaching.intent.", IntentIds);
        ValidateStableIds(rows, "coaching.guard.", GuardIds);

        ValidateOwners(ownerRows);
        ValidateActions(actionRows);
        contractRows.Values.Should().OnlyContain(row => row.GetField("Status").StartsWith("Implemented", StringComparison.Ordinal));
        ValidateSharedPersistenceTopology(ValidateStableIds(
            rows,
            "coaching.persistence.",
            ["coaching.persistence.shared-topology"]));
    }

    [Test]
    public void Stable_Owner_Rows_Should_Reject_A_Duplicate_Row()
    {
        var action = () => ValidateStableIds(
            ParseStableRows(CreateOwnerFixture(OwnerIds.Append(OwnerIds[0]))),
            "coaching.owner.",
            OwnerIds);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate stable row IDs*coaching.owner.trainer-invitation*");
    }

    [Test]
    public void Stable_Owner_Rows_Should_Reject_A_Missing_Row()
    {
        var action = () => ValidateStableIds(
            ParseStableRows(CreateOwnerFixture(OwnerIds.Skip(1))),
            "coaching.owner.",
            OwnerIds);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Missing stable row IDs*coaching.owner.trainer-invitation*");
    }

    [Test]
    public void Persistence_Row_Should_Reject_A_Second_AppDbContext()
    {
        var action = () => ValidateSharedPersistenceTopology(ValidateStableIds(
            ParseStableRows("""
                | Persistence ID | AppDbContext count | Database count | Migration stream count | Physical split |
                | --- | --- | --- | --- | --- |
                | `coaching.persistence.shared-topology` | `2` | `1` | `1` | `None` |
                """),
            "coaching.persistence.",
            ["coaching.persistence.shared-topology"]));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*exactly one AppDbContext*");
    }

    private static IReadOnlyList<DocumentationRow> ParseBoundaryRows()
    {
        var markdown = File.ReadAllText(Path.Combine(
            ArchitectureTestHelpers.ResolveRepositoryRoot(),
            "docs",
            "modular-monolith",
            "issue-389-coaching-boundary.md"));
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
                    throw new InvalidOperationException($"Stable documentation table '{headers[0]}' has a row with {cells.Count} cells; expected {headers.Count}.");
                }

                var id = UnwrapCode(cells[0]);
                if (id.StartsWith("coaching.", StringComparison.Ordinal))
                {
                    rows.Add(new DocumentationRow(id, headers.Zip(cells.Select(UnwrapCode))
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
            .Where(entry => entry.Owner == PersistedEntityOwnershipCatalog.CoachingModuleName)
            .ToList();
        var documentedNames = ownerRows.Values.Select(row => row.GetField("Entity name")).ToHashSet(StringComparer.Ordinal);

        catalogEntries.Should().HaveCount(4);
        documentedNames.Should().BeEquivalentTo(catalogEntries.Select(entry => entry.EntityType.Name));
        foreach (var row in ownerRows.Values)
        {
            row.GetField("Owner").Should().Be(PersistedEntityOwnershipCatalog.CoachingModuleName);
        }
    }

    private static void ValidateActions(IReadOnlyDictionary<string, DocumentationRow> actionRows)
    {
        actionRows.Values.Count(row => row.GetField("Surface") == "HTTP").Should().Be(30);
        actionRows["coaching.action.get-trainer-invitations"].GetField("Surface").Should().Be("Application only");
        actionRows["coaching.action.get-trainer-invitations"].GetField("Application action").Should().Be("GetTrainerInvitationsAsync");
    }

    private static void ValidateSharedPersistenceTopology(IReadOnlyDictionary<string, DocumentationRow> rows)
    {
        var topology = rows["coaching.persistence.shared-topology"];
        if (topology.GetField("AppDbContext count") != "1")
        {
            throw new InvalidOperationException("Coaching boundary must declare exactly one AppDbContext.");
        }

        if (topology.GetField("Database count") != "1" || topology.GetField("Migration stream count") != "1" ||
            topology.GetField("Physical split") != "None")
        {
            throw new InvalidOperationException("Coaching boundary must declare one database, one migration stream, and no physical split.");
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
        ["| Owner ID | Entity name | Owner |", "| --- | --- | --- |", .. ids.Select(id => $"| `{id}` | Fixture | Coaching |")]);

    private sealed record DocumentationRow(string Id, IReadOnlyDictionary<string, string> Fields)
    {
        public string GetField(string fieldName) => Fields.TryGetValue(fieldName, out var value)
            ? value
            : throw new InvalidOperationException($"Stable row '{Id}' is missing expected field '{fieldName}'.");
    }
}
