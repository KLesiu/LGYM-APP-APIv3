namespace LgymApi.E2ETests.Harness;

using static StandaloneBoundaryPolicy;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class Task7ApiHostProofTests
{
    [Test]
    public async Task Production_and_unknown_environments_reject_fresh_pending_migrations()
    {
        using var deadline = RealApiHostProofTests.CreateDeadline();
        var context = await RealApiHostProofContext.CreateAsync(deadline.Token);
        var failures = new Task7RealHostFailureContext(context);
        var receipts = new[]
        {
            await failures.StartWithPendingMigrationsAsync("Production", deadline.Token),
            await failures.StartWithPendingMigrationsAsync("PrOdUcTiOnLike", deadline.Token)
        };

        Assert.Multiple(() =>
        {
            Assert.That(receipts.Select(receipt => receipt.Category), Is.EqualTo(
                new[] { ExternalApiHostLease.PendingMigrationsFailureMessage, ExternalApiHostLease.PendingMigrationsFailureMessage }));
            Assert.That(receipts.All(receipt => !receipt.Ready && receipt.ProcessTreeAbsent && receipt.PrivateRunAbsent &&
                receipt.ConfigurationAbsent && receipt.ContainerAbsent), Is.True);
        });
        TestContext.Out.WriteLine("receipt category=pending-migrations productionRejected=true unknownRejected=true cleanup=true");
    }

    [Test]
    public async Task E2E_rejects_broadened_CORS_configuration_before_readiness()
    {
        using var deadline = RealApiHostProofTests.CreateDeadline();
        var context = await RealApiHostProofContext.CreateAsync(deadline.Token);

        var receipt = await context.StartWithInvalidCorsAsync(deadline.Token);

        Assert.That(receipt.Category, Is.EqualTo(ExternalApiHostLease.CorsPolicyFailureMessage));
        Assert.That(receipt.Ready, Is.False);
        Assert.That(receipt.ProcessTreeAbsent, Is.True);
        Assert.That(receipt.PrivateRunAbsent, Is.True);
        Assert.That(receipt.ContainerAbsent, Is.True);
        TestContext.Out.WriteLine("receipt category=cors-policy ready=false containerAbsent=true");
    }

    [Test]
    public async Task Startup_timeout_reaps_exact_API_process_tree_and_disposes_database()
    {
        using var deadline = RealApiHostProofTests.CreateDeadline();
        var context = await RealApiHostProofContext.CreateAsync(deadline.Token);
        var receipt = await new Task7RealHostFailureContext(context)
            .StartWithUnreachableDatabaseAsync(deadline.Token);

        Assert.Multiple(() =>
        {
            Assert.That(receipt.Category, Is.EqualTo(ExternalApiHostLease.StartupTimeoutMessage));
            Assert.That(receipt.Ready, Is.False);
            Assert.That(receipt.ProcessTreeAbsent, Is.True);
            Assert.That(receipt.PrivateRunAbsent, Is.True);
            Assert.That(receipt.ConfigurationAbsent, Is.True);
            Assert.That(receipt.ContainerAbsent, Is.True);
        });
        TestContext.Out.WriteLine("receipt category=startup-timeout ready=false exactTreeAbsent=true privateRunAbsent=true containerAbsent=true");
    }

    [Test]
    public async Task Api_host_output_and_receipts_redact_secret_canaries_and_raw_identifiers()
    {
        const string parentCanaryName = "LGYM_TASK7_PARENT_CANARY";
        var parentCanary = $"task7-{Guid.NewGuid():N}";
        var originalValue = Environment.GetEnvironmentVariable(parentCanaryName);
        using var deadline = RealApiHostProofTests.CreateDeadline();

        try
        {
            Environment.SetEnvironmentVariable(parentCanaryName, parentCanary);
            var context = await RealApiHostProofContext.CreateAsync(deadline.Token);
            var receipt = await new Task7RealHostFailureContext(context)
                .StartWithPendingMigrationsAsync("Production", deadline.Token);

            Assert.Multiple(() =>
            {
                Assert.That(receipt.Category, Is.EqualTo(ExternalApiHostLease.PendingMigrationsFailureMessage));
                Assert.That(receipt.ToString(), Does.Not.Contain(parentCanary));
                Assert.That(receipt.ToString(), Does.Not.Contain("ProcessId"));
                Assert.That(receipt.ToString(), Does.Not.Contain("ContainerId"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(parentCanaryName, originalValue);
        }

        TestContext.Out.WriteLine("receipt category=redaction parentCanaryPresent=false rawIdentifiersPresent=false");
    }

    [Test]
    public void API_child_receives_only_the_reviewed_environment_allowlist()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var runtime = new FakeApiHostRuntimeLease(fixture.RepositoryRoot, cleanupOrder, false);
        const string parentCanaryName = "LGYM_TASK7_PARENT_CANARY";
        var parentCanary = $"task7-{Guid.NewGuid():N}";
        var originalValue = Environment.GetEnvironmentVariable(parentCanaryName);

        try
        {
            Environment.SetEnvironmentVariable(parentCanaryName, parentCanary);
            var request = ExternalApiHostLaunchRequestFactory.Create(new ExternalApiHostLaunchRequest(
                fixture.Publication,
                fixture.Options,
                Path.Combine(Environment.SystemDirectory, "dotnet.exe"),
                runtime,
                new Uri("http://127.0.0.1:43127")));
            var expected = new[]
            {
                "ASPNETCORE_ENVIRONMENT", "ASPNETCORE_URLS", "DOTNET_CLI_TELEMETRY_OPTOUT",
                "DOTNET_ENVIRONMENT", "DOTNET_NOLOGO", "LGYM_APP_CONFIG_PATH", "SystemRoot", "TEMP", "TMP", "WINDIR"
            };

            Assert.Multiple(() =>
            {
                Assert.That(request.ClearEnvironment, Is.True);
                Assert.That(request.EnvironmentVariables.Keys.Order(), Is.EqualTo(expected.Order()));
                Assert.That(request.EnvironmentVariables.ContainsKey(parentCanaryName), Is.False);
                Assert.That(request.EnvironmentVariables.Values, Does.Not.Contain(parentCanary));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(parentCanaryName, originalValue);
        }

        TestContext.Out.WriteLine("receipt category=child-environment allowlistOnly=true parentCanaryPresent=false");
    }

    [Test]
    public void API_launch_forwards_scenario_secret_canaries_without_widening_the_child_environment()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var runtime = new FakeApiHostRuntimeLease(fixture.RepositoryRoot, cleanupOrder, false);
        var canaries = new[] { "api-scenario-secret-canary-a", "api-scenario-secret-canary-b" };

        var request = ExternalApiHostLaunchRequestFactory.Create(new ExternalApiHostLaunchRequest(
            fixture.Publication,
            fixture.Options,
            Path.Combine(Environment.SystemDirectory, "dotnet.exe"),
            runtime,
            new Uri("http://127.0.0.1:43127"))
        {
            SecretCanaries = canaries
        });

        Assert.Multiple(() =>
        {
            Assert.That(request.SecretCanaries, Is.EqualTo(canaries));
            Assert.That(request.EnvironmentVariables.Values, Does.Not.Contain(canaries[0]));
            Assert.That(request.EnvironmentVariables.Values, Does.Not.Contain(canaries[1]));
        });
    }

    [Test]
    public void ApiHostProof_remains_package_only_and_outside_main_solution()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var projectPath = Path.Combine(repositoryRoot, ToHostPath(E2ETestsProjectPath));
        var project = ParseProject(projectPath);
        var evaluatedReferences = ParseEvaluatedProjectReferences(
            Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "project.assets.json"));
        var standaloneProjects = ParseSolutionProjects(Path.Combine(repositoryRoot, "LgymApi.E2ETests.sln"));
        var mainProjects = ParseSolutionProjects(Path.Combine(repositoryRoot, "LgymApi.sln"));
        using var fixture = new TemporaryFixture();
        var invalidProject = fixture.Write(
            "invalid.csproj",
            "<Project><ItemGroup><ProjectReference Include=\"../Product/Product.csproj\" /></ItemGroup></Project>");
        var invalidAssets = fixture.Write(
            "project.assets.json",
            "{\"libraries\":{\"Product/1.0\":{\"type\":\"project\"}}}");

        Assert.Multiple(() =>
        {
            Assert.That(project.ProjectReferences, Is.Empty);
            Assert.That(evaluatedReferences, Is.Empty);
            Assert.That(standaloneProjects, Has.Count.EqualTo(1));
            Assert.That(NormalizePath(Path.GetRelativePath(repositoryRoot, standaloneProjects.Single())), Is.EqualTo(E2ETestsProjectPath));
            Assert.That(mainProjects.Select(path => NormalizePath(Path.GetRelativePath(repositoryRoot, path))), Does.Not.Contain(E2ETestsProjectPath));
            Assert.That(ParseProject(invalidProject).ProjectReferences, Is.Not.Empty);
            Assert.That(ParseEvaluatedProjectReferences(invalidAssets), Is.Not.Empty);
        });
        TestContext.Out.WriteLine("receipt category=standalone-boundary directReferences=false evaluatedReferences=false mainMembership=false injectedFixtureRejected=true");
    }
}
