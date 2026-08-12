namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class ExternalApiHostCleanupTests
{
    [Test]
    public async Task ExternalApiHost_disposes_process_runtime_and_database_once_in_reverse_order()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var runtimeFactory = new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder);
        var processStarter = new FakeExternalApiProcessStarter([null], cleanupOrder);
        var lease = await ExternalApiHostLease.StartAsync(
            fixture.CreateRequest(database),
            fixture.CreateInfrastructure(
                runtimeFactory,
                processStarter,
                new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]),
                new FakeLoopbackPortAllocator([46101])));

        await lease.DisposeAsync();
        await lease.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(cleanupOrder, Is.EqualTo(new[]
            {
                "api-process",
                "runtime-configuration",
                "postgresql"
            }));
            Assert.That(processStarter.Processes.Single().DisposeCount, Is.EqualTo(1));
            Assert.That(runtimeFactory.Lease!.DisposeCount, Is.EqualTo(1));
            Assert.That(database.DisposeCount, Is.EqualTo(1));
            Assert.That(lease.CleanupReceipt.AttemptedCategories, Is.EqualTo(cleanupOrder));
            Assert.That(lease.CleanupReceipt.FailureCount, Is.Zero);
            Assert.That(lease.ToString(), Is.EqualTo("<external-api-host-lease>"));
        });
    }

    [Test]
    public async Task ExternalApiHost_aggregates_cleanup_failures_and_still_attempts_later_resources()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var runtimeFactory = new FakeApiHostRuntimeFactory(
            fixture.RepositoryRoot,
            cleanupOrder,
            cleanupFails: true);
        var processStarter = new FakeExternalApiProcessStarter(
            [null],
            cleanupOrder,
            cleanupFailures: [true]);
        var lease = await ExternalApiHostLease.StartAsync(
            fixture.CreateRequest(database),
            fixture.CreateInfrastructure(
                runtimeFactory,
                processStarter,
                new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]),
                new FakeLoopbackPortAllocator([46201])));

        var exception = Assert.ThrowsAsync<ExternalApiHostCleanupException>(async () =>
            await lease.DisposeAsync());
        await lease.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ExternalApiHostLease.CleanupFailureMessage));
            Assert.That(exception.Receipt.FailureCount, Is.EqualTo(2));
            Assert.That(exception.Receipt.AttemptedCategories, Is.EqualTo(new[]
            {
                "api-process",
                "runtime-configuration",
                "postgresql"
            }));
            Assert.That(exception.Receipt.ToString(), Is.EqualTo("<external-api-host-cleanup>"));
            Assert.That(exception.Message, Does.Not.Contain("Injected private"));
            Assert.That(cleanupOrder, Is.EqualTo(exception.Receipt.AttemptedCategories));
            Assert.That(database.DisposeCount, Is.EqualTo(1));
            Assert.That(runtimeFactory.Lease!.DisposeCount, Is.EqualTo(1));
            Assert.That(processStarter.Processes.Single().DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExternalApiHost_process_exit_fault_is_aggregated_before_later_cleanup()
    {
        var cleanupOrder = new List<string>();
        var process = new AdverseExitApiProcess(cleanupOrder, exitFaults: true);
        var runtime = new FakeApiHostRuntimeLease(string.Empty, cleanupOrder, false);
        var database = new FakeApiHostDatabaseLease(cleanupOrder);

        var result = await ExternalApiHostCleanup.DisposeAsync(process, runtime, database);

        Assert.Multiple(() =>
        {
            Assert.That(result.Receipt.AttemptedCategories, Is.EqualTo(new[]
            {
                "api-process",
                "runtime-configuration",
                "postgresql"
            }));
            Assert.That(result.Receipt.FailureCount, Is.EqualTo(1));
            Assert.That(result.HangfireServerStartObserved, Is.False);
            Assert.That(runtime.DisposeCount, Is.EqualTo(1));
            Assert.That(database.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExternalApiHost_process_exit_noncompletion_is_bounded_before_later_cleanup()
    {
        var cleanupOrder = new List<string>();
        var process = new AdverseExitApiProcess(cleanupOrder, exitFaults: false);
        var runtime = new FakeApiHostRuntimeLease(string.Empty, cleanupOrder, false);
        var database = new FakeApiHostDatabaseLease(cleanupOrder);

        var result = await ExternalApiHostCleanup.DisposeAsync(process, runtime, database)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(result.Receipt.AttemptedCategories, Is.EqualTo(new[]
            {
                "api-process",
                "runtime-configuration",
                "postgresql"
            }));
            Assert.That(result.Receipt.FailureCount, Is.EqualTo(1));
            Assert.That(result.HangfireServerStartObserved, Is.False);
            Assert.That(runtime.DisposeCount, Is.EqualTo(1));
            Assert.That(database.DisposeCount, Is.EqualTo(1));
        });
    }

    private sealed class AdverseExitApiProcess : IExternalApiProcess
    {
        private readonly ICollection<string> _cleanupOrder;

        internal AdverseExitApiProcess(ICollection<string> cleanupOrder, bool exitFaults)
        {
            _cleanupOrder = cleanupOrder;
            Exit = exitFaults
                ? Task.FromException<ExternalApiProcessExit>(new IOException("Injected private exit failure."))
                : new TaskCompletionSource<ExternalApiProcessExit>(
                    TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }

        public Task<ExternalApiProcessExit> Exit { get; }

        public TimeSpan ExitObservationTimeout => TimeSpan.FromMilliseconds(25);

        public ValueTask DisposeAsync()
        {
            _cleanupOrder.Add("api-process");
            return ValueTask.CompletedTask;
        }
    }
}
