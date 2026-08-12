namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class ExternalApiHostFailureTests
{
    [Test]
    public void ExternalApiHost_early_process_exit_cleans_every_owned_resource()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var runtimeFactory = new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder);
        var processStarter = new FakeExternalApiProcessStarter(
            [ExternalApiProcessExitKind.Exited],
            cleanupOrder);

        var exception = Assert.ThrowsAsync<ExternalApiHostStartupException>(() =>
            ExternalApiHostLease.StartAsync(
                fixture.CreateRequest(database),
                fixture.CreateInfrastructure(
                    runtimeFactory,
                    processStarter,
                    new ExitObservingApiHostReadinessMonitor(),
                    new FakeLoopbackPortAllocator([45101]))));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ExternalApiHostLease.StartupFailureMessage));
            Assert.That(processStarter.Requests, Has.Count.EqualTo(1));
            Assert.That(cleanupOrder, Is.EqualTo(new[]
            {
                "api-process",
                "runtime-configuration",
                "postgresql"
            }));
        });
    }

    [TestCase("http-failure", ExternalApiHostLease.StartupFailureMessage)]
    [TestCase("http-timeout", ExternalApiHostLease.StartupFailureMessage)]
    [TestCase("startup-timeout", ExternalApiHostLease.StartupTimeoutMessage)]
    public void ExternalApiHost_readiness_failure_is_terminal_and_sanitized(
        string outcomeName,
        string expectedMessage)
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var runtimeFactory = new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder);
        var processStarter = new FakeExternalApiProcessStarter([null], cleanupOrder);
        var outcome = outcomeName switch
        {
            "http-failure" => ApiHostReadinessOutcome.HttpFailure,
            "http-timeout" => ApiHostReadinessOutcome.HttpTimeout,
            "startup-timeout" => ApiHostReadinessOutcome.StartupTimeout,
            _ => throw new InvalidOperationException("Unknown readiness fixture.")
        };
        var readiness = new ScriptedApiHostReadinessMonitor([outcome]);

        var exception = Assert.ThrowsAsync<ExternalApiHostStartupException>(() =>
            ExternalApiHostLease.StartAsync(
                fixture.CreateRequest(database),
                fixture.CreateInfrastructure(
                    runtimeFactory,
                    processStarter,
                    readiness,
                    new FakeLoopbackPortAllocator([45201]))));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(expectedMessage));
            Assert.That(processStarter.Requests, Has.Count.EqualTo(1));
            Assert.That(readiness.HealthEndpoints.Single().AbsolutePath, Is.EqualTo("/health/live"));
            Assert.That(readiness.Bounds.Single().HttpRequestTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(cleanupOrder, Is.EqualTo(new[]
            {
                "api-process",
                "runtime-configuration",
                "postgresql"
            }));
        });
    }

    [Test]
    public async Task ExternalApiHost_caller_cancellation_is_terminal_and_next_start_is_fresh()
    {
        using var fixture = new ExternalApiHostTestFixture();
        using var callerCancellation = new CancellationTokenSource();
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var runtimeFactory = new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder);
        var processStarter = new FakeExternalApiProcessStarter([null], cleanupOrder);

        var exception = Assert.ThrowsAsync<OperationCanceledException>(() =>
            ExternalApiHostLease.StartAsync(
                fixture.CreateRequest(database),
                fixture.CreateInfrastructure(
                    runtimeFactory,
                    processStarter,
                    new CancelingApiHostReadinessMonitor(callerCancellation),
                    new FakeLoopbackPortAllocator([45301])),
                callerCancellation.Token));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.CancellationToken, Is.EqualTo(callerCancellation.Token));
            Assert.That(processStarter.Requests, Has.Count.EqualTo(1));
            Assert.That(cleanupOrder, Is.EqualTo(new[]
            {
                "api-process",
                "runtime-configuration",
                "postgresql"
            }));
        });

        var resumedCleanupOrder = new List<string>();
        var resumedDatabase = new FakeApiHostDatabaseLease(resumedCleanupOrder);
        var resumedRuntimeFactory = new FakeApiHostRuntimeFactory(
            fixture.RepositoryRoot,
            resumedCleanupOrder);
        var resumedProcessStarter = new FakeExternalApiProcessStarter([null], resumedCleanupOrder);
        var resumedLease = await ExternalApiHostLease.StartAsync(
            fixture.CreateRequest(resumedDatabase),
            fixture.CreateInfrastructure(
                resumedRuntimeFactory,
                resumedProcessStarter,
                new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]),
                new FakeLoopbackPortAllocator([45302])));

        Assert.Multiple(() =>
        {
            Assert.That(resumedRuntimeFactory.Lease, Is.Not.SameAs(runtimeFactory.Lease));
            Assert.That(
                resumedProcessStarter.StartInfos.Single().Environment["ASPNETCORE_URLS"],
                Is.EqualTo("http://127.0.0.1:45302"));
            Assert.That(resumedProcessStarter.StartInfos.Single().Environment.Keys, Does.Not.Contain("PATH"));
        });

        await resumedLease.DisposeAsync();
    }
}
