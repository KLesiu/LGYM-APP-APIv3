using FluentAssertions;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class NotificationsBoundaryDocumentationTests
{
    private const string ArtifactPrefix = "notifications.artifact.";
    private const string ContractPrefix = "notifications.contract.";
    private const string AdapterPrefix = "notifications.adapter.";
    private const string MigrationPrefix = "notifications.migration.";
    private const string GuardPrefix = "notifications.guard.";

    private static readonly string[] ArtifactIds =
    [
        "notifications.artifact.in-app-notification",
        "notifications.artifact.notification-message",
        "notifications.artifact.email-subscription",
        "notifications.artifact.push-installation",
        "notifications.artifact.push-message",
        "notifications.artifact.delivery-status-retry-policy",
        "notifications.artifact.delivery-jobs-cleanup",
        "notifications.artifact.provider-adapters",
        "notifications.artifact.event-bridge"
    ];

    private static readonly string[] PersistedArtifactIds = ArtifactIds[..5];

    private static readonly string[] ContractIds =
    [
        "notifications.contract.event-intent",
        "notifications.contract.channel-preference",
        "notifications.contract.delivery-outcome",
        "notifications.contract.push-payload",
        "notifications.contract.installation-registration"
    ];

    private static readonly string[] AdapterIds =
    [
        "notifications.adapter.push-payload",
        "notifications.adapter.legacy-command-ids",
        "notifications.adapter.password-email",
        "notifications.adapter.report-submission",
        "notifications.adapter.scheduler-runtime",
        "notifications.adapter.coaching-intents"
    ];

    private static readonly string[] MigrationIds =
    [
        "notifications.migration.381",
        "notifications.migration.382",
        "notifications.migration.383",
        "notifications.migration.adapter-removal"
    ];

    private static readonly string[] GuardIds =
    [
        "notifications.guard.persisted-ownership",
        "notifications.guard.public-contracts",
        "notifications.guard.worker-common-seam",
        "notifications.guard.compatibility",
        "notifications.guard.scope"
    ];

    [Test]
    public void Notifications_Persisted_Entities_Should_Appear_Exactly_Once_In_The_Ownership_Table()
    {
        var rows = ParseBoundaryRows();
        var artifactRows = ValidateStableIds(rows, ArtifactPrefix, ArtifactIds);
        var persistedRows = PersistedArtifactIds.Select(id => artifactRows[id]).ToList();
        var catalogEntries = PersistedEntityOwnershipCatalog.Entries
            .Where(entry => entry.Owner == PersistedEntityOwnershipCatalog.NotificationsModuleName)
            .ToList();

        catalogEntries.Should().HaveCount(5);
        ValidateEntityNames(
            persistedRows.Select(row => row.GetField("Artifact name")),
            catalogEntries.Select(entry => entry.EntityType.Name));

        foreach (var row in persistedRows)
        {
            var entityName = row.GetField("Artifact name");
            var expectedOwner = catalogEntries.Single(entry => entry.EntityType.Name == entityName).Owner;
            row.GetField("Owner").Should().Be(expectedOwner, $"stable row '{row.Id}' must use the catalog owner");
        }
    }

    [Test]
    public void Provider_And_Runtime_Concerns_Should_Remain_On_Their_Private_Sides_Of_The_Boundary()
    {
        var rows = ParseBoundaryRows();
        var artifactRows = ValidateStableIds(rows, ArtifactPrefix, ArtifactIds);
        var contractRows = ValidateStableIds(rows, ContractPrefix, ContractIds);
        var adapterRows = ValidateStableIds(rows, AdapterPrefix, AdapterIds);
        var guardRows = ValidateStableIds(rows, GuardPrefix, GuardIds);

        AssertFieldContains(
            artifactRows["notifications.artifact.provider-adapters"],
            "Responsibility and allowed access",
            "Notifications",
            "private FCM transport/configuration",
            "provider-private mapping",
            "without exposing");
        AssertFieldContains(
            contractRows["notifications.contract.push-payload"],
            "Explicit exclusion",
            "provider-specific",
            "credential",
            "raw token");
        AssertRowsExcludePrivateRuntimeNames(contractRows.Values);

        AssertFieldContains(
            adapterRows["notifications.adapter.scheduler-runtime"],
            "Boundary rule",
            "Worker",
            "scheduler selection",
            "execution",
            "Common");
        AssertFieldContains(
            adapterRows["notifications.adapter.password-email"],
            "Boundary rule",
            "Worker",
            "closed Common email wire seam");
        AssertFieldContains(
            guardRows["notifications.guard.worker-common-seam"],
            "Asserted invariant",
            "Worker owns runtime selection",
            "Common remains",
            "closed job and email wire seam");
    }

    [Test]
    public void Compatibility_Migration_Rows_Should_Preserve_Installation_And_Delivery_Sequencing()
    {
        var rows = ParseBoundaryRows();
        var adapterRows = ValidateStableIds(rows, AdapterPrefix, AdapterIds);
        var migrationRows = ValidateStableIds(rows, MigrationPrefix, MigrationIds);

        AssertFieldContains(
            adapterRows["notifications.adapter.push-payload"],
            "Boundary rule",
            "Preserve current meaning",
            "serialization");
        AssertFieldContains(
            migrationRows["notifications.migration.382"],
            "Change",
            "registration",
            "refresh",
            "disablement",
            "stale lifecycle");
        AssertFieldContains(
            migrationRows["notifications.migration.382"],
            "Constraint",
            "preserve current registration endpoints",
            "token privacy",
            "adapter",
            "consumers");
        AssertFieldContains(
            migrationRows["notifications.migration.383"],
            "Change",
            "enqueueing",
            "delivery claims",
            "retries",
            "cleanup");
        AssertFieldContains(
            migrationRows["notifications.migration.383"],
            "Constraint",
            "Preserve",
            "retry/failure status",
            "before adapter removal");
        AssertFieldContains(
            migrationRows["notifications.migration.adapter-removal"],
            "Constraint",
            "consumer inventory",
            "rollback or coexistence criteria");
    }

    [Test]
    public void Stable_Table_Validation_Should_Reject_A_Missing_Id()
    {
        var markdown = CreateAdapterFixture("notifications.adapter.push-payload");

        var action = () => ValidateStableIds(
            ParseStableRows(markdown),
            AdapterPrefix,
            ["notifications.adapter.push-payload", "notifications.adapter.scheduler-runtime"]);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Missing stable row IDs*notifications.adapter.scheduler-runtime*");
    }

    [Test]
    public void Stable_Table_Validation_Should_Reject_A_Duplicate_Id()
    {
        var markdown = CreateAdapterFixture(
            "notifications.adapter.push-payload",
            "notifications.adapter.push-payload");

        var action = () => ValidateStableIds(
            ParseStableRows(markdown),
            AdapterPrefix,
            ["notifications.adapter.push-payload"]);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate stable row IDs*notifications.adapter.push-payload*");
    }

    [Test]
    public void Stable_Table_Validation_Should_Reject_An_Unknown_Id()
    {
        var markdown = CreateAdapterFixture(
            "notifications.adapter.push-payload",
            "notifications.adapter.unknown-runtime");

        var action = () => ValidateStableIds(
            ParseStableRows(markdown),
            AdapterPrefix,
            ["notifications.adapter.push-payload"]);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown stable row IDs*notifications.adapter.unknown-runtime*");
    }

    private static IReadOnlyList<DocumentationRow> ParseBoundaryRows()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var markdown = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "docs",
            "modular-monolith",
            "issue-381-notifications-boundary.md"));
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
                if (!id.StartsWith("notifications.", StringComparison.Ordinal))
                {
                    continue;
                }

                rows.Add(new DocumentationRow(
                    id,
                    headers.Zip(cells.Select(UnwrapCode))
                        .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal)));
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
        var duplicateIds = scopedRows
            .GroupBy(row => row.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
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

    private static void ValidateEntityNames(IEnumerable<string> documentedNames, IEnumerable<string> expectedNames)
    {
        var documented = documentedNames.ToList();
        var expected = expectedNames.ToHashSet(StringComparer.Ordinal);
        var duplicates = documented.GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        var missing = expected.Except(documented, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList();
        var unknown = documented.Except(expected, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList();

        if (duplicates.Count > 0 || missing.Count > 0 || unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"Notifications persisted entity rows do not match the catalog. " +
                $"Duplicate entity names: {FormatNames(duplicates)}; missing entity names: {FormatNames(missing)}; " +
                $"unknown entity names: {FormatNames(unknown)}.");
        }
    }

    private static void AssertFieldContains(DocumentationRow row, string fieldName, params string[] requiredTokens)
    {
        var fieldValue = row.GetField(fieldName);
        foreach (var token in requiredTokens)
        {
            fieldValue.Should().ContainEquivalentOf(token, $"stable row '{row.Id}' field '{fieldName}' must document '{token}'");
        }
    }

    private static void AssertRowsExcludePrivateRuntimeNames(IEnumerable<DocumentationRow> rows)
    {
        string[] privateRuntimeNames = ["FCM", "Hangfire", "Worker", "Common"];
        foreach (var row in rows)
        {
            var structuredValues = string.Join(" ", row.Fields.Values);
            foreach (var privateRuntimeName in privateRuntimeNames)
            {
                structuredValues.Should().NotContainEquivalentOf(
                    privateRuntimeName,
                    $"stable public contract row '{row.Id}' must remain provider and runtime neutral");
            }
        }
    }

    private static List<string> ParseTableCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '|' || trimmed[^1] != '|')
        {
            return [];
        }

        return trimmed[1..^1]
            .Split('|')
            .Select(cell => cell.Trim())
            .ToList();
    }

    private static bool IsTableSeparator(string line, int expectedCellCount)
    {
        var cells = ParseTableCells(line);
        return cells.Count == expectedCellCount && cells.All(cell => cell.Length >= 3 && cell.All(character => character == '-'));
    }

    private static string UnwrapCode(string value)
    {
        return value.Length >= 2 && value[0] == '`' && value[^1] == '`' ? value[1..^1] : value;
    }

    private static string CreateAdapterFixture(params string[] ids)
    {
        var rows = ids.Select(id => $"| `{id}` | Fixture surface | Fixture rule |");
        return string.Join('\n',
        [
            "| Adapter ID | Current compatibility surface | Boundary rule |",
            "| --- | --- | --- |",
            .. rows
        ]);
    }

    private static string FormatNames(IReadOnlyCollection<string> names)
    {
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    private sealed record DocumentationRow(string Id, IReadOnlyDictionary<string, string> Fields)
    {
        public string GetField(string fieldName)
        {
            if (!Fields.TryGetValue(fieldName, out var value))
            {
                throw new InvalidOperationException($"Stable row '{Id}' is missing expected field '{fieldName}'.");
            }

            return value;
        }
    }
}
