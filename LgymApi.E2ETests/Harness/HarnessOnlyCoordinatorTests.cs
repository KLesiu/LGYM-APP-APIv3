using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Lifecycle")]
public sealed class HarnessOnlyCoordinatorTests
{
    private const string HeadSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [TestCase("happy", true)]
    [TestCase("zero", false)]
    [TestCase("skipped", false)]
    [TestCase("stale", false)]
    [TestCase("malformed", false)]
    [TestCase("child-failure", false)]
    public void HarnessOnlyCoordinator_validates_serial_child_evidence(string fixtureMode, bool expectedSuccess)
    {
        using var fixture = new CoordinatorFixture(fixtureMode);

        var result = fixture.Invoke();

        var invocationLog = File.Exists(fixture.InvocationLogPath) ? File.ReadAllText(fixture.InvocationLogPath) : "<no child invocation>";
        Assert.That(result.ExitCode == 0, Is.EqualTo(expectedSuccess), result.StandardError + invocationLog);
        Assert.That(result.StandardOutput + result.StandardError, Does.Not.Contain(fixture.Root));
        if (!expectedSuccess)
        {
            Assert.That(result.StandardError, Does.Contain("HarnessOnly coordinator failed."));
            return;
        }

        var invocations = File.ReadAllLines(fixture.InvocationLogPath);
        Assert.Multiple(() =>
        {
            Assert.That(invocations, Has.Length.EqualTo(2));
            Assert.That(invocations[0], Does.Contain("TestCategory=HarnessDocker"));
            Assert.That(invocations[1], Does.Contain("TestCategory=Lifecycle"));
            Assert.That(invocations.All(line => line.Contains("LgymApi.E2ETests.csproj", StringComparison.Ordinal)), Is.True);
            Assert.That(invocations.All(line => line.Contains("--no-build", StringComparison.Ordinal)), Is.True);
            Assert.That(File.Exists(fixture.ManifestPath), Is.True);
            Assert.That(File.ReadAllText(fixture.ManifestPath), Does.Not.Contain(fixture.Root));
            Assert.That(File.ReadAllText(fixture.ManifestPath), Does.Not.Contain("raw-private-path-canary"));
        });
    }

    [Test]
    public void HarnessOnlyCoordinator_rejects_successor_unknown_and_extra_arguments_before_preflight()
    {
        foreach (var arguments in new[]
                 {
                     new[] { "-Mode", "Full" },
                     new[] { "-Mode", "ArtifactDrill" },
                     new[] { "-Mode", "Unknown" },
                     new[] { "-Mode", "HarnessOnly", "unexpected" }
                 })
        {
            var result = InvokeScript(RepositoryRoot.Find(), arguments, new Dictionary<string, string>());
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
        }
    }

    [Test]
    public void HarnessOnlyCoordinator_rejects_missing_prerequisite_and_occupied_port_before_children()
    {
        using var missingChromium = new CoordinatorFixture("happy");
        File.Delete(missingChromium.ChromePath);
        var missingResult = missingChromium.Invoke();

        using var occupiedPort = new CoordinatorFixture("happy");
        using var listener = new TcpListener(IPAddress.Loopback, 8083);
        listener.Start();
        var occupiedResult = occupiedPort.Invoke();

        Assert.Multiple(() =>
        {
            Assert.That(missingResult.ExitCode, Is.Not.EqualTo(0));
            Assert.That(occupiedResult.ExitCode, Is.Not.EqualTo(0));
            Assert.That(File.Exists(missingChromium.InvocationLogPath), Is.False);
            Assert.That(File.Exists(occupiedPort.InvocationLogPath), Is.False);
        });
    }

    [Test]
    public void HarnessOnlyCoordinator_does_not_inherit_unapproved_parent_environment()
    {
        using var fixture = new CoordinatorFixture("happy");
        var environment = new Dictionary<string, string>(fixture.Environment, StringComparer.Ordinal)
        {
            ["HARNESS_ONLY_PARENT_CANARY"] = "private-parent-canary"
        };

        var result = InvokeScript(fixture.Root, ["-Mode", "HarnessOnly"], environment);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(File.ReadAllText(fixture.InvocationLogPath), Does.Not.Contain("private-parent-canary"));
            Assert.That(result.StandardOutput + result.StandardError, Does.Not.Contain("private-parent-canary"));
        });
    }

    [Test]
    public void HarnessOnlyCoordinator_propagates_only_the_Docker_allowlist()
    {
        using var fixture = new CoordinatorFixture("happy");
        var environment = new Dictionary<string, string>(fixture.Environment, StringComparer.Ordinal)
        {
            ["DOCKER_HOST"] = "docker-host-canary",
            ["DOCKER_CONTEXT"] = "docker-context-canary"
        };

        var result = InvokeScript(fixture.Root, ["-Mode", "HarnessOnly"], environment);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
            Assert.That(File.ReadAllText(fixture.InvocationLogPath), Does.Contain("docker-host-canary").And.Contain("docker-context-canary"));
        });
    }

    [Test]
    public void HarnessOnlyCoordinator_uses_capped_exact_Chromium_discovery()
    {
        using var fixture = new CoordinatorFixture("happy");
        fixture.CreateAdditionalChromiumInstallation();

        var result = fixture.Invoke();

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(File.Exists(fixture.InvocationLogPath), Is.False);
        });
    }

    [Test]
    public async Task HarnessOnlyCoordinator_bounds_a_hung_child_before_evidence_validation()
    {
        using var fixture = new CoordinatorFixture("hang");

        var result = await fixture.InvokeAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(result.TimedOut, Is.False, "The coordinator must bound its own child wait.");
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(File.Exists(fixture.ManifestPath), Is.False);
        });
    }

    private static ProcessResult InvokeScript(string workingDirectory, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        var scriptPath = Path.Combine(workingDirectory, "LgymApi.E2ETests", "scripts", "invoke-e2e-coordinator.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Coordinator process did not start.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, standardOutput, standardError, false);
    }

    private static async Task<ProcessResult> InvokeScriptAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout)
    {
        var scriptPath = Path.Combine(workingDirectory, "LgymApi.E2ETests", "scripts", "invoke-e2e-coordinator.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Coordinator process did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout);
            return new ProcessResult(process.ExitCode, await standardOutput, await standardError, false);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            return new ProcessResult(process.ExitCode, await standardOutput, await standardError, true);
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

    private sealed class CoordinatorFixture : IDisposable
    {
        private readonly string _toolsDirectory;

        public CoordinatorFixture(string mode)
        {
            var testSessionSeconds = mode == "hang" ? 1 : 10;
            Root = Path.Combine(Path.GetTempPath(), $"lgym e2e coordinator {Guid.NewGuid():N}");
            _toolsDirectory = Path.Combine(Root, "tools");
            Directory.CreateDirectory(_toolsDirectory);
            Directory.CreateDirectory(Path.Combine(Root, "external source"));
            Directory.CreateDirectory(Path.Combine(Root, ".e2e-private", "browsers", "chromium-1234", "chrome-win"));
            Directory.CreateDirectory(Path.Combine(Root, ".e2e-private", "published-api"));
            Directory.CreateDirectory(Path.Combine(Root, "LgymApi.E2ETests", "scripts"));
            Directory.CreateDirectory(Path.Combine(Root, "LgymApi.E2ETests", "bin", "Release", "net10.0"));
            File.Copy(Path.Combine(RepositoryRoot.Find(), "LgymApi.E2ETests", "scripts", "invoke-e2e-coordinator.ps1"), ScriptPath);
            File.WriteAllText(Path.Combine(Root, "LgymApi.E2ETests", "LgymApi.E2ETests.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(Root, "LgymApi.E2ETests", "LgymApi.E2ETests.runsettings"), "<RunSettings />");
            File.WriteAllText(Path.Combine(Root, "LgymApi.E2ETests", "bin", "Release", "net10.0", "LgymApi.E2ETests.dll"), string.Empty);
            File.WriteAllText(Path.Combine(Root, ".e2e-private", "published-api", "LgymApi.Api.dll"), "fixture");
            File.WriteAllText(Path.Combine(Root, ".e2e-private", "published-api", "LgymApi.Api.deps.json"), "{}");
            File.WriteAllText(Path.Combine(Root, ".e2e-private", "published-api", "LgymApi.Api.runtimeconfig.json"), "{}");
            File.WriteAllText(ChromePath, string.Empty);
            File.WriteAllText(Path.Combine(Root, "LgymApi.E2ETests", "appsettings.E2E.json"), $$"""
                { "E2E": { "WebSource": { "CommitSha": "{{HeadSha}}" }, "Api": { "PublishedDllPath": ".e2e-private/published-api/LgymApi.Api.dll" }, "Web": { "Port": 8083 }, "Timeouts": { "TestSessionSeconds": {{testSessionSeconds}} } } }
                """);
            WriteCommand("git", $"@echo off\r\necho %* | findstr /C:\"rev-parse\" >nul\r\nif not errorlevel 1 echo {HeadSha}\r\n");
            WriteCommand("node", "@echo off\r\necho v22.18.0\r\n");
            WriteCommand("npm", "@echo off\r\necho 10.0.0\r\n");
            WriteCommand("docker", "@echo off\r\necho 27.0.0\r\n");
            File.WriteAllText(Path.Combine(_toolsDirectory, "fake-dotnet.ps1"), FakeDotNetScript);
            File.WriteAllText(Path.Combine(_toolsDirectory, "fixture-mode.txt"), mode);
            WriteCommand("dotnet", "@echo off\r\npwsh -NoProfile -File \"%~dp0fake-dotnet.ps1\" %*\r\n");
            InvocationLogPath = Path.Combine(_toolsDirectory, "invocations.log");
            ManifestPath = Path.Combine(Root, "LgymApi.E2ETests", "TestResults", "issue-435-harness-only", "issue-435-lifecycle-evidence.json");
            Environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PATH"] = _toolsDirectory + Path.PathSeparator + EnvironmentVariable("PATH"),
                ["LGYM_E2E__WebSource__SourcePath"] = Path.Combine(Root, "external source")
            };
        }

        public string Root { get; }
        public string ScriptPath => Path.Combine(Root, "LgymApi.E2ETests", "scripts", "invoke-e2e-coordinator.ps1");
        public string ChromePath => Path.Combine(Root, ".e2e-private", "browsers", "chromium-1234", "chrome-win", "chrome.exe");
        public string InvocationLogPath { get; }
        public string ManifestPath { get; }
        public IReadOnlyDictionary<string, string> Environment { get; }

        public ProcessResult Invoke() => InvokeScript(Root, ["-Mode", "HarnessOnly"], Environment);

        public Task<ProcessResult> InvokeAsync(TimeSpan timeout) =>
            InvokeScriptAsync(Root, ["-Mode", "HarnessOnly"], Environment, timeout);

        public void CreateAdditionalChromiumInstallation()
        {
            var executable = Path.Combine(Root, ".e2e-private", "browsers", "chromium-5678", "chrome-win", "chrome.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllText(executable, string.Empty);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        private void WriteCommand(string name, string content) => File.WriteAllText(Path.Combine(_toolsDirectory, $"{name}.cmd"), content);

        private static string EnvironmentVariable(string name) => System.Environment.GetEnvironmentVariable(name) ?? string.Empty;

        private const string FakeDotNetScript = """
            $fixtureMode = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixture-mode.txt')
            $invocationLog = Join-Path $PSScriptRoot 'invocations.log'
            $resultsIndex = [array]::IndexOf($args, '--results-directory')
            $filterIndex = [array]::IndexOf($args, '--filter')
            $resultsDirectory = $args[$resultsIndex + 1]
            $filter = $args[$filterIndex + 1]
            Add-Content -LiteralPath $invocationLog -Value ($filter + '|' + ($args -join '|') + '|' + $env:HARNESS_ONLY_HARNESS_DOCKER_RECEIPT_PATH + '|' + $env:HARNESS_ONLY_LIFECYCLE_RECEIPT_PATH + '|' + $env:HARNESS_ONLY_PARENT_CANARY + '|' + $env:DOCKER_HOST + '|' + $env:DOCKER_CONTEXT)
            if ($fixtureMode -eq 'hang') { Start-Sleep -Seconds 5; exit 0 }
            New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
            $harnessNames = @('PostgreSQL_container_starts_with_module_readiness_and_is_removed_on_disposal', 'PostgreSQL_container_is_removed_when_a_test_local_failure_occurs_after_start', 'PostgreSQL_post_container_start_callback_failure_proves_private_locator_absence', 'PostgreSQL_sequential_leases_have_distinct_redacted_observations_and_are_absent')
            $lifecycleNames = @('Lifecycle_hooks_are_async_tag_scoped_and_explicitly_ordered', 'Lifecycle_feature_declares_exactly_two_canonical_serial_probes', 'Scenario_failure_after_the_ready_stack_preserves_the_primary_failure_writes_one_safe_artifact_and_starts_fresh', 'Scenario_success_writes_no_failure_artifact_and_removes_the_completed_run', 'Compiled_test_inventory_requires_nonempty_disjoint_serial_categories_without_parallel_markers')
            $isHarness = $filter -eq 'TestCategory=HarnessDocker'
            $names = if ($isHarness) { $harnessNames } else { $lifecycleNames }
            $outcome = if ($fixtureMode -eq 'skipped' -and -not $isHarness) { 'NotExecuted' } else { 'Passed' }
            $total = if ($fixtureMode -eq 'zero' -and $isHarness) { 0 } else { $names.Count }
            $executed = if ($outcome -eq 'Passed') { $total } else { 0 }
            $passed = if ($outcome -eq 'Passed') { $total } else { 0 }
            $notExecuted = if ($outcome -eq 'Passed') { 0 } else { $total }
            $results = if ($total -eq 0) { '' } else { ($names | ForEach-Object { '<UnitTestResult testName="' + $_ + '" outcome="' + $outcome + '" />' }) -join '' }
            $fileName = if ($isHarness) { 'issue-435-harness-docker.trx' } else { 'issue-435-lifecycle.trx' }
            Set-Content -LiteralPath (Join-Path $resultsDirectory $fileName) -Value ('<TestRun><ResultSummary><Counters total="' + $total + '" executed="' + $executed + '" passed="' + $passed + '" failed="0" timeout="0" notExecuted="' + $notExecuted + '" /></ResultSummary><Results>' + $results + '</Results></TestRun>')
            if ($isHarness) {
              Set-Content -LiteralPath $env:HARNESS_ONLY_HARNESS_DOCKER_RECEIPT_PATH -Value '{"testCount":1,"passedCount":1,"allContainersAbsent":true,"identitiesDistinct":true,"rawIdentitiesExcluded":true}'
              if ($fixtureMode -eq 'child-failure') { exit 7 }
              exit 0
            }
            if ($fixtureMode -eq 'malformed') { Set-Content -LiteralPath $env:HARNESS_ONLY_LIFECYCLE_RECEIPT_PATH -Value '{'; exit 0 }
            $head = if ($fixtureMode -eq 'stale') { 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' } else { 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }
            $scenario = @{ acquiredCategories=@('scenario-paths','postgresql','external-api-host','expo','browser-run','browser-scenario'); cleanupCategories=@('browser-scenario','browser-run','expo','external-api-host','scenario-paths'); cleanupFailureCount=0; freshPostgreSql=$true; freshApiHost=$true; freshExpo=$true; freshBrowserRun=$true; freshBrowserScenario=$true; previousResourcesAbsent=$true; browserStorageEmpty=$true; databaseAbsent=$true; apiAbsent=$true; expoAbsent=$true; scenarioPathsAbsent=$true }
            $receipt = @{ schema='issue-435-lifecycle-run-receipt-v1'; apiHeadSha=$head; apiRepositoryDirty=$false; completedScenarioCount=2; sourceStatePreserved=$true; runtimeRootAbsent=$true; successArtifactsAbsent=$true; scenarios=@(($scenario + @{caseId='lifecycle-probe-a'}),($scenario + @{caseId='lifecycle-probe-b'})) }
            $receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $env:HARNESS_ONLY_LIFECYCLE_RECEIPT_PATH
            """;
    }
}
