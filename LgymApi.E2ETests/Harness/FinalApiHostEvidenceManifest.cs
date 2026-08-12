using System.Text.Json;
using System.Xml.Linq;

namespace LgymApi.E2ETests.Harness;

internal sealed record FinalEvidenceCommandReceipt(string Category, bool Passed);

internal sealed record FinalEvidenceRepositoryReceipt(string HeadSha, bool IsDirty);

internal static class SanitizedApiHostEvidenceManifest
{
    internal const string ManifestFileName = "issue-433-api-host-final-manifest.json";
    internal static readonly string[] RequiredProofNames =
    [
        "Published_API_starts_as_exact_dotnet_DLL_process",
        "E2E_fresh_PostgreSQL_is_migrated_before_database_backed_readiness",
        "E2E_allows_only_configured_credentialed_browser_origin",
        "E2E_enables_password_recovery_rate_limit",
        "E2E_suppresses_Hangfire_dashboard_and_recurring_runtime",
        "Testing_behavior_remains_test_safe_without_migration_or_rate_limit",
        "Production_and_unknown_environments_reject_fresh_pending_migrations",
        "E2E_rejects_broadened_CORS_configuration_before_readiness",
        "Startup_timeout_reaps_exact_API_process_tree_and_disposes_database",
        "Api_host_output_and_receipts_redact_secret_canaries_and_raw_identifiers",
        "API_child_receives_only_the_reviewed_environment_allowlist",
        "ApiHostProof_remains_package_only_and_outside_main_solution"
    ];

    internal static string Create(
        string rawTrx,
        FinalEvidenceRepositoryReceipt repository,
        IReadOnlyList<FinalEvidenceCommandReceipt> commands)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawTrx);
        ValidateRepository(repository);
        ValidateCommands(commands);

        var document = XDocument.Parse(rawTrx, LoadOptions.None);
        var counters = document.Descendants().Single(element => element.Name.LocalName == "Counters");
        var results = document.Descendants()
            .Where(element => element.Name.LocalName == "UnitTestResult")
            .Select(element => new
            {
                Name = (string?)element.Attribute("testName"),
                Outcome = (string?)element.Attribute("outcome")
            })
            .Where(result => result.Name is not null && RequiredProofNames.Contains(result.Name, StringComparer.Ordinal))
            .GroupBy(result => result.Name!, StringComparer.Ordinal)
            .Where(group => group.All(result => result.Outcome == "Passed"))
            .Select(group => new { Name = group.Key, Outcome = "Passed" })
            .OrderBy(result => result.Name, StringComparer.Ordinal)
            .ToArray();

        if (results.Length != RequiredProofNames.Length || results.Any(result => result.Outcome != "Passed"))
        {
            throw new InvalidOperationException("Final ApiHostProof evidence is incomplete.");
        }

        var manifest = new
        {
            schema = "issue-433-final-evidence-v1",
            repository = new { repository.HeadSha, repository.IsDirty },
            commands = commands.Select(command => new { command.Category, command.Passed }),
            counters = new
            {
                total = ReadCounter(counters, "total"),
                executed = ReadCounter(counters, "executed"),
                passed = ReadCounter(counters, "passed"),
                failed = ReadCounter(counters, "failed"),
                notExecuted = ReadCounter(counters, "notExecuted")
            },
            proofs = results.Select(result => new { name = result.Name, outcome = result.Outcome }),
            receipts = new
            {
                output = new { retainedUtf8ByteLimit = ExternalProcessOutput.MaximumTailBytes, truncationRecorded = true },
                negativeHost = new { ready = false, processTreeAbsent = true, privateRunAbsent = true, containerAbsent = true }
            }
        };
        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
    }

    internal static void Write(string path, string manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, manifest);
    }

    internal static async Task WriteForCurrentRunAsync(
        string rawTrxPath,
        IReadOnlyList<FinalEvidenceCommandReceipt> commands,
        CancellationToken cancellationToken = default)
    {
        var repositoryRoot = RepositoryRoot.Find();
        var options = Configuration.E2EConfiguration.Load(TestContext.CurrentContext.TestDirectory, repositoryRoot);
        var state = await new ApiRepositoryStateReader(
                new ExternalProcessRunner(),
                ApiRepositoryStateReader.ResolveGitExecutable())
            .ReadAsync(
                repositoryRoot,
                new ApiRepositoryStateTimeouts(
                    TimeSpan.FromSeconds(Math.Min(options.Timeouts.ApiPublishSeconds, 30)),
                    TimeSpan.FromSeconds(options.Timeouts.ProcessShutdownSeconds)),
                cancellationToken);
        var manifest = Create(
            File.ReadAllText(rawTrxPath),
            new FinalEvidenceRepositoryReceipt(state.HeadSha, state.IsDirty),
            commands);
        Write(Path.Combine(Path.GetDirectoryName(rawTrxPath)!, ManifestFileName), manifest);
    }

    private static int ReadCounter(XElement counters, string name) =>
        int.Parse((string?)counters.Attribute(name) ?? throw new InvalidOperationException("Final TRX counters are incomplete."));

    private static void ValidateRepository(FinalEvidenceRepositoryReceipt repository)
    {
        if (repository.HeadSha.Length != 40 || !repository.HeadSha.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("Final repository evidence is invalid.");
        }
    }

    private static void ValidateCommands(IReadOnlyList<FinalEvidenceCommandReceipt> commands)
    {
        var required = new[] { "restore", "build", "publish", "test" };
        if (!commands.Select(command => command.Category).Order(StringComparer.Ordinal).SequenceEqual(required.Order(StringComparer.Ordinal)) ||
            commands.Any(command => !command.Passed))
        {
            throw new InvalidOperationException("Final command evidence is incomplete.");
        }
    }
}
