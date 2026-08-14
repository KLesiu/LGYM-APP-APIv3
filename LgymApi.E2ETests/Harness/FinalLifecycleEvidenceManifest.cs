using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace LgymApi.E2ETests.Harness;

internal sealed record FinalLifecycleEvidenceCounters(
    int Total,
    int Executed,
    int Passed,
    int Failed,
    int Timeout,
    int NotExecuted);

internal sealed record FinalLifecycleDockerReceipt(
    int TestCount,
    int PassedCount,
    bool AllContainersAbsent,
    bool IdentitiesDistinct,
    bool RawIdentitiesExcluded);

internal sealed record FinalLifecycleScenarioReceipt(
    string CaseId,
    IReadOnlyList<string> AcquiredCategories,
    IReadOnlyList<string> CleanupCategories,
    int CleanupFailureCount,
    bool FreshPostgreSql,
    bool FreshApiHost,
    bool FreshExpo,
    bool FreshBrowserRun,
    bool FreshBrowserScenario,
    bool PreviousResourcesAbsent,
    bool BrowserStorageEmpty,
    bool DatabaseAbsent,
    bool ApiAbsent,
    bool ExpoAbsent,
    bool ScenarioPathsAbsent);

internal sealed record FinalLifecycleRunReceipt(
    string Schema,
    string ApiHeadSha,
    bool ApiRepositoryDirty,
    int CompletedScenarioCount,
    bool SourceStatePreserved,
    bool RuntimeRootAbsent,
    bool SuccessArtifactsAbsent,
    IReadOnlyList<FinalLifecycleScenarioReceipt> Scenarios);

internal static class FinalLifecycleEvidenceManifest
{
    internal const string Schema = "issue-435-lifecycle-evidence-v1";
    internal const string LifecycleReceiptSchema = "issue-435-lifecycle-run-receipt-v1";

    internal static readonly string[] RequiredHarnessDockerContracts =
    [
        "PostgreSQL_container_starts_with_module_readiness_and_is_removed_on_disposal",
        "PostgreSQL_container_is_removed_when_a_test_local_failure_occurs_after_start",
        "PostgreSQL_post_container_start_callback_failure_proves_private_locator_absence",
        "PostgreSQL_sequential_leases_have_distinct_redacted_observations_and_are_absent"
    ];

    internal static readonly string[] RequiredLifecycleContracts =
    [
        "Lifecycle_hooks_are_async_tag_scoped_and_explicitly_ordered",
        "Lifecycle_feature_declares_exactly_two_canonical_serial_probes",
        "Scenario_failure_after_the_ready_stack_preserves_the_primary_failure_writes_one_safe_artifact_and_starts_fresh",
        "Scenario_success_writes_no_failure_artifact_and_removes_the_completed_run",
        "Compiled_test_inventory_requires_nonempty_disjoint_serial_categories_without_parallel_markers"
    ];

    internal static readonly string[] RequiredCaseIds = ["lifecycle-probe-a", "lifecycle-probe-b"];

    private static readonly string[] RequiredAcquisitionCategories =
    [
        "scenario-paths",
        "postgresql",
        "external-api-host",
        "expo",
        "browser-run",
        "browser-scenario"
    ];

    private static readonly string[] RequiredCleanupCategories =
    ["browser-scenario", "browser-run", "expo", "external-api-host", "scenario-paths"];

    private static readonly string[] DockerReceiptProperties =
    ["testCount", "passedCount", "allContainersAbsent", "identitiesDistinct", "rawIdentitiesExcluded"];

    private static readonly string[] RunReceiptProperties =
    [
        "schema",
        "apiHeadSha",
        "apiRepositoryDirty",
        "completedScenarioCount",
        "sourceStatePreserved",
        "runtimeRootAbsent",
        "successArtifactsAbsent",
        "scenarios"
    ];

    private static readonly string[] ScenarioReceiptProperties =
    [
        "caseId",
        "acquiredCategories",
        "cleanupCategories",
        "cleanupFailureCount",
        "freshPostgreSql",
        "freshApiHost",
        "freshExpo",
        "freshBrowserRun",
        "freshBrowserScenario",
        "previousResourcesAbsent",
        "browserStorageEmpty",
        "databaseAbsent",
        "apiAbsent",
        "expoAbsent",
        "scenarioPathsAbsent"
    ];

    internal static string Create(
        string harnessDockerTrx,
        string lifecycleTrx,
        string serializedDockerReceipt,
        string serializedLifecycleReceipt,
        ApiPublicationReceipt publication)
    {
        ValidatePublication(publication);
        var harnessDocker = ParseTrx(harnessDockerTrx, RequiredHarnessDockerContracts, "HarnessDocker");
        var lifecycle = ParseTrx(lifecycleTrx, RequiredLifecycleContracts, "Lifecycle");
        var dockerReceipt = ParseDockerReceipt(serializedDockerReceipt);
        var lifecycleReceipt = ParseLifecycleReceipt(serializedLifecycleReceipt, publication);

        return JsonSerializer.Serialize(new
        {
            schema = Schema,
            api = new { headSha = publication.ApiRepositoryHeadSha, repositoryDirty = publication.RepositoryIsDirty },
            harnessDocker = new
            {
                counters = harnessDocker,
                contractNames = RequiredHarnessDockerContracts.Order(StringComparer.Ordinal),
                receipt = dockerReceipt
            },
            lifecycle = new
            {
                counters = lifecycle,
                contractNames = RequiredLifecycleContracts.Order(StringComparer.Ordinal),
                run = new
                {
                    lifecycleReceipt.CompletedScenarioCount,
                    lifecycleReceipt.SourceStatePreserved,
                    lifecycleReceipt.RuntimeRootAbsent,
                    lifecycleReceipt.SuccessArtifactsAbsent,
                    scenarios = lifecycleReceipt.Scenarios
                        .OrderBy(scenario => scenario.CaseId, StringComparer.Ordinal)
                }
            }
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    internal static FinalLifecycleDockerReceipt ParseDockerReceipt(string serialized)
    {
        using var document = ParseJson(serialized, "HarnessDocker receipt");
        var root = document.RootElement;
        RequireExactObject(root, DockerReceiptProperties, "HarnessDocker receipt");
        var receipt = new FinalLifecycleDockerReceipt(
            ReadNonNegativeInt(root, "testCount", "HarnessDocker receipt"),
            ReadNonNegativeInt(root, "passedCount", "HarnessDocker receipt"),
            ReadTrue(root, "allContainersAbsent", "HarnessDocker receipt"),
            ReadTrue(root, "identitiesDistinct", "HarnessDocker receipt"),
            ReadTrue(root, "rawIdentitiesExcluded", "HarnessDocker receipt"));

        if (receipt.TestCount <= 0 || receipt.PassedCount != receipt.TestCount)
        {
            throw InvalidEvidence("HarnessDocker receipt");
        }

        return receipt;
    }

    internal static FinalLifecycleRunReceipt ParseLifecycleReceipt(
        string serialized,
        ApiPublicationReceipt publication)
    {
        ValidatePublication(publication);
        using var document = ParseJson(serialized, "Lifecycle receipt");
        var root = document.RootElement;
        RequireExactObject(root, RunReceiptProperties, "Lifecycle receipt");

        var schema = ReadString(root, "schema", "Lifecycle receipt");
        var apiHeadSha = ReadString(root, "apiHeadSha", "Lifecycle receipt");
        if (!string.Equals(schema, LifecycleReceiptSchema, StringComparison.Ordinal) ||
            !string.Equals(apiHeadSha, publication.ApiRepositoryHeadSha, StringComparison.Ordinal))
        {
            throw InvalidEvidence("Lifecycle receipt");
        }

        var scenarios = ReadScenarios(root, "Lifecycle receipt");
        var receipt = new FinalLifecycleRunReceipt(
            schema,
            apiHeadSha,
            ReadBoolean(root, "apiRepositoryDirty", "Lifecycle receipt"),
            ReadNonNegativeInt(root, "completedScenarioCount", "Lifecycle receipt"),
            ReadTrue(root, "sourceStatePreserved", "Lifecycle receipt"),
            ReadTrue(root, "runtimeRootAbsent", "Lifecycle receipt"),
            ReadTrue(root, "successArtifactsAbsent", "Lifecycle receipt"),
            scenarios);

        if (receipt.ApiRepositoryDirty != publication.RepositoryIsDirty ||
            receipt.CompletedScenarioCount != RequiredCaseIds.Length ||
            receipt.Scenarios.Count != RequiredCaseIds.Length ||
            !receipt.Scenarios.Select(scenario => scenario.CaseId)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(RequiredCaseIds.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw InvalidEvidence("Lifecycle receipt");
        }

        return receipt;
    }

    private static FinalLifecycleEvidenceCounters ParseTrx(
        string rawTrx,
        IReadOnlyList<string> requiredContracts,
        string category)
    {
        if (string.IsNullOrWhiteSpace(rawTrx))
        {
            throw InvalidEvidence($"{category} TRX");
        }

        try
        {
            var document = XDocument.Parse(rawTrx, LoadOptions.None);
            var counters = document.Descendants().Where(element => element.Name.LocalName == "Counters").ToArray();
            if (counters.Length != 1)
            {
                throw InvalidEvidence($"{category} TRX");
            }

            var resultElements = document.Descendants()
                .Where(element => element.Name.LocalName == "UnitTestResult")
                .ToArray();
            var results = resultElements.Select(result => new
            {
                Name = (string?)result.Attribute("testName"),
                Outcome = (string?)result.Attribute("outcome")
            }).ToArray();
            var parsedCounters = new FinalLifecycleEvidenceCounters(
                ReadCounter(counters[0], "total", category),
                ReadCounter(counters[0], "executed", category),
                ReadCounter(counters[0], "passed", category),
                ReadCounter(counters[0], "failed", category),
                ReadCounter(counters[0], "timeout", category),
                ReadCounter(counters[0], "notExecuted", category));

            if (parsedCounters.Total <= 0 ||
                parsedCounters.Executed != parsedCounters.Total ||
                parsedCounters.Passed != parsedCounters.Total ||
                parsedCounters.Failed != 0 ||
                parsedCounters.Timeout != 0 ||
                parsedCounters.NotExecuted != 0 ||
                results.Length != parsedCounters.Total ||
                results.Any(result => string.IsNullOrWhiteSpace(result.Name) || result.Outcome != "Passed") ||
                results.Any(result => IsPredecessorEvidenceName(result.Name!)) ||
                results.Select(result => result.Name!).Distinct(StringComparer.Ordinal).Count() != results.Length ||
                requiredContracts.Any(contract => results.Count(result => result.Name == contract) != 1))
            {
                throw InvalidEvidence($"{category} TRX");
            }

            return parsedCounters;
        }
        catch (Exception exception) when (exception is XmlException or FormatException or OverflowException)
        {
            throw InvalidEvidence($"{category} TRX");
        }
    }

    private static IReadOnlyList<FinalLifecycleScenarioReceipt> ReadScenarios(JsonElement root, string subject)
    {
        if (root.GetProperty("scenarios").ValueKind != JsonValueKind.Array)
        {
            throw InvalidEvidence(subject);
        }

        var scenarios = new List<FinalLifecycleScenarioReceipt>();
        foreach (var scenario in root.GetProperty("scenarios").EnumerateArray())
        {
            RequireExactObject(scenario, ScenarioReceiptProperties, subject);
            var receipt = new FinalLifecycleScenarioReceipt(
                ReadString(scenario, "caseId", subject),
                ReadExactNames(scenario, "acquiredCategories", RequiredAcquisitionCategories, subject),
                ReadExactNames(scenario, "cleanupCategories", RequiredCleanupCategories, subject),
                ReadNonNegativeInt(scenario, "cleanupFailureCount", subject),
                ReadTrue(scenario, "freshPostgreSql", subject),
                ReadTrue(scenario, "freshApiHost", subject),
                ReadTrue(scenario, "freshExpo", subject),
                ReadTrue(scenario, "freshBrowserRun", subject),
                ReadTrue(scenario, "freshBrowserScenario", subject),
                ReadTrue(scenario, "previousResourcesAbsent", subject),
                ReadTrue(scenario, "browserStorageEmpty", subject),
                ReadTrue(scenario, "databaseAbsent", subject),
                ReadTrue(scenario, "apiAbsent", subject),
                ReadTrue(scenario, "expoAbsent", subject),
                ReadTrue(scenario, "scenarioPathsAbsent", subject));

            if (receipt.CleanupFailureCount != 0 || !RequiredCaseIds.Contains(receipt.CaseId, StringComparer.Ordinal))
            {
                throw InvalidEvidence(subject);
            }

            scenarios.Add(receipt);
        }

        if (scenarios.Select(scenario => scenario.CaseId).Distinct(StringComparer.Ordinal).Count() != scenarios.Count)
        {
            throw InvalidEvidence(subject);
        }

        return scenarios;
    }

    private static IReadOnlyList<string> ReadExactNames(
        JsonElement element,
        string propertyName,
        IReadOnlyList<string> expected,
        string subject)
    {
        var value = element.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw InvalidEvidence(subject);
        }

        var names = value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null).ToArray();
        if (names.Any(string.IsNullOrWhiteSpace) || !names.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw InvalidEvidence(subject);
        }

        return names!;
    }

    private static void ValidatePublication(ApiPublicationReceipt publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (publication.CommandName != "publish" ||
            !IsHex(publication.DllSha256, 64) ||
            !IsHex(publication.ApiRepositoryHeadSha, 40) ||
            publication.Process.ExitCode != 0)
        {
            throw InvalidEvidence("API publication");
        }
    }

    private static JsonDocument ParseJson(string serialized, string subject)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            throw InvalidEvidence(subject);
        }

        try
        {
            return JsonDocument.Parse(serialized);
        }
        catch (JsonException)
        {
            throw InvalidEvidence(subject);
        }
    }

    private static void RequireExactObject(JsonElement element, IReadOnlyList<string> expectedNames, string subject)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw InvalidEvidence(subject);
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name) || !expectedNames.Contains(property.Name, StringComparer.Ordinal))
            {
                throw InvalidEvidence(subject);
            }
        }

        if (names.Count != expectedNames.Count)
        {
            throw InvalidEvidence(subject);
        }
    }

    private static string ReadString(JsonElement element, string propertyName, string subject)
    {
        var value = element.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw InvalidEvidence(subject);
        }

        return value.GetString()!;
    }

    private static bool ReadTrue(JsonElement element, string propertyName, string subject)
    {
        if (element.GetProperty(propertyName).ValueKind != JsonValueKind.True)
        {
            throw InvalidEvidence(subject);
        }

        return true;
    }

    private static bool ReadBoolean(JsonElement element, string propertyName, string subject)
    {
        var value = element.GetProperty(propertyName).ValueKind;
        return value switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw InvalidEvidence(subject)
        };
    }

    private static int ReadNonNegativeInt(JsonElement element, string propertyName, string subject)
    {
        var value = element.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < 0)
        {
            throw InvalidEvidence(subject);
        }

        return number;
    }

    private static int ReadCounter(XElement counters, string name, string category)
    {
        var value = (string?)counters.Attribute(name);
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var counter) || counter < 0)
        {
            throw InvalidEvidence($"{category} TRX");
        }

        return counter;
    }

    private static bool IsPredecessorEvidenceName(string name) =>
        name.Contains("issue-433", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("issue-434", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("FinalWebHarnessEvidence", StringComparison.Ordinal) ||
        name.Contains("SanitizedApiHostEvidence", StringComparison.Ordinal) ||
        name.Contains("FinalTrxManifest", StringComparison.Ordinal) ||
        name.Contains("Pinned_source_is_exported_started_and_navigated_by_Chromium", StringComparison.Ordinal);

    private static bool IsHex(string value, int length) =>
        value.Length == length && value.All(Uri.IsHexDigit);

    private static InvalidOperationException InvalidEvidence(string subject) =>
        new($"{subject} evidence is invalid.");
}
