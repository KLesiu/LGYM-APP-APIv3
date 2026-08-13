namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class WebSourceRunLeaseTests
{
    [TestCase("v22.17.9\n")]
    [TestCase("v22.18\n")]
    [TestCase("v22.18.0-rc.1\n")]
    [TestCase("22.18.0\n")]
    public async Task WebSourceRun_rejects_unsupported_Node_output_before_npm_ci(string versionOutput)
    {
        // Given
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        var runner = new Task3NodeNpmCommandRunner { VersionOutput = versionOutput };
        var lease = await CreateLeaseAsync(fixture, runner);

        try
        {
            // When
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await lease.EnsureInstalledAsync());

            // Then
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(WebSourceRunLease.NodePrerequisiteMessage));
                Assert.That(runner.NpmInvocationCount, Is.Zero);
                Assert.That(Directory.Exists(lease.RunDirectory), Is.False);
                Assert.That(Directory.Exists(lease.NpmCacheDirectory), Is.False);
            });
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    [TestCase("v22.18.0\n")]
    [TestCase("v22.18.1\n")]
    [TestCase("v23.0.0\n")]
    public async Task WebSourceRun_accepts_supported_stable_Node_output_and_invokes_exact_private_npm_ci(
        string versionOutput)
    {
        // Given
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        var parentCanary = "task-3-parent-credential";
        var originalCanary = Environment.GetEnvironmentVariable("TASK3_PARENT_CANARY");
        var runner = new Task3NodeNpmCommandRunner { VersionOutput = versionOutput };

        try
        {
            Environment.SetEnvironmentVariable("TASK3_PARENT_CANARY", parentCanary);
            await using var lease = await CreateLeaseAsync(fixture, runner, [parentCanary]);

            // When
            await lease.EnsureInstalledAsync();

            // Then
            var requests = runner.Requests;
            var versionRequest = requests.Single(request => request.Arguments.SequenceEqual(["--version"]));
            var installRequest = requests.Single(request => request.Arguments.SequenceEqual([fixture.NpmCliScript, "ci"]));
            Assert.Multiple(() =>
            {
                Assert.That(versionRequest.FileName, Is.EqualTo(fixture.NodeExecutable));
                Assert.That(installRequest.FileName, Is.EqualTo(fixture.NodeExecutable));
                Assert.That(installRequest.WorkingDirectory, Is.EqualTo(lease.SourceDirectory));
                Assert.That(installRequest.ClearEnvironment, Is.True);
                Assert.That(installRequest.EnvironmentVariables.Keys, Is.EquivalentTo(new[]
                {
                    "SystemRoot", "WINDIR", "ComSpec", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "TEMP", "TMP", "PATH", "CI", "NO_COLOR",
                    "npm_config_cache", "npm_config_userconfig", "npm_config_audit", "npm_config_fund", "npm_config_update_notifier",
                    "npm_config_progress", "npm_config_loglevel"
                }));
                Assert.That(installRequest.EnvironmentVariables, Does.Not.ContainKey("TASK3_PARENT_CANARY"));
                Assert.That(installRequest.EnvironmentVariables["CI"], Is.EqualTo("1"));
                Assert.That(installRequest.EnvironmentVariables["NO_COLOR"], Is.EqualTo("1"));
                Assert.That(installRequest.EnvironmentVariables["npm_config_cache"], Is.EqualTo(lease.NpmCacheDirectory));
                Assert.That(installRequest.SecretCanaries, Does.Contain(parentCanary));
                Assert.That(runner.NpmInvocationCount, Is.EqualTo(1));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("TASK3_PARENT_CANARY", originalCanary);
        }
    }

    [Test]
    public async Task WebSourceRun_supplies_private_Windows_application_data_paths_to_the_closed_Expo_environment()
    {
        // Given
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        var lease = await CreateLeaseAsync(fixture, new Task3NodeNpmCommandRunner());

        try
        {
            // When
            var environment = lease.CreateExpoEnvironment(new Uri("http://127.0.0.1:48123/"));

            // Then
            Assert.Multiple(() =>
            {
                Assert.That(environment, Does.ContainKey("APPDATA"));
                Assert.That(environment, Does.ContainKey("LOCALAPPDATA"));
                Assert.That(environment, Does.Not.ContainKey("EXPO_ROUTER_APP_ROOT"));
                Assert.That(PrivateRunDirectoryLayout.IsDescendantOrSame(lease.RunDirectory, environment["APPDATA"]!), Is.True);
                Assert.That(PrivateRunDirectoryLayout.IsDescendantOrSame(lease.RunDirectory, environment["LOCALAPPDATA"]!), Is.True);
            });
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    [Test]
    public async Task WebSourceRun_shares_one_completed_installation_per_run_and_installs_again_for_a_new_run()
    {
        // Given
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        var stager = new Task3WebSourceStager();
        var runner = new Task3NodeNpmCommandRunner();
        var dependencies = new WebSourceRunDependencies
        {
            Stager = stager,
            ToolResolver = fixture.CreateToolResolver(),
            CommandRunner = runner
        };

        await using (var firstRun = await WebSourceRunLease.CreateAsync(fixture.CreateRequest(), dependencies))
        {
            // When
            await Task.WhenAll(firstRun.EnsureInstalledAsync(), firstRun.EnsureInstalledAsync());

            // Then
            Assert.That(runner.NpmInvocationCount, Is.EqualTo(1));
        }

        await using var secondRun = await WebSourceRunLease.CreateAsync(fixture.CreateRequest(), dependencies);

        // When
        await secondRun.EnsureInstalledAsync();

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(runner.NpmInvocationCount, Is.EqualTo(2));
            Assert.That(stager.StageCount, Is.EqualTo(2));
        });
    }

    private static Task<WebSourceRunLease> CreateLeaseAsync(
        Task3WebSourceRunFixture fixture,
        Task3NodeNpmCommandRunner runner,
        IReadOnlyList<string>? secretCanaries = null) =>
        WebSourceRunLease.CreateAsync(
            fixture.CreateRequest(secretCanaries),
            new WebSourceRunDependencies
            {
                Stager = new Task3WebSourceStager(),
                ToolResolver = fixture.CreateToolResolver(),
                CommandRunner = runner
            });
}
