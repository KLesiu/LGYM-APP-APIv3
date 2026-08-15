using System.Diagnostics;
using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.Lifecycle;

[TestFixture]
[Category("Lifecycle")]
public sealed class DatabaseBackedApiReadinessPermanentCleanupTests
{
    [Test]
    public void Cleanup_receipt_merge_caps_history_and_saturates_failure_count()
    {
        var receipt = new ExternalApiHostCleanupReceipt(true, true, true, [], 0);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            receipt = ExternalApiHostCleanup.Merge(
                receipt,
                new ExternalApiHostCleanupReceipt(
                    false,
                    false,
                    false,
                    ["api-process", "runtime-configuration", "postgresql"],
                    100));
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                receipt.AttemptedCategories,
                Has.Count.EqualTo(ExternalApiHostCleanup.MaximumRetainedAttemptedCategories));
            Assert.That(receipt.FailureCount, Is.EqualTo(ExternalApiHostCleanup.MaximumRetainedFailureCount));
            Assert.That(receipt.AttemptHistoryTruncated, Is.True);
            Assert.That(receipt.FailureCountSaturated, Is.True);
        });
    }

    [Test]
    public async Task DatabaseBacked_permanent_cleanup_failure_stops_with_a_bounded_terminal_receipt()
    {
        using var fixture = new ExternalApiHostTestFixture();
        fixture.Options.Timeouts.ProcessShutdownSeconds = 1;
        var state = new PermanentCleanupState();
        var database = new PermanentlyFailingDatabaseLease(state);
        var stopwatch = Stopwatch.StartNew();

        var exception = Assert.ThrowsAsync<ExternalApiHostStartupException>(() => ExternalApiHostLease.StartAsync(
            new ExternalApiHostCompositionRequest(
                fixture.Publication,
                database,
                fixture.Options,
                fixture.RepositoryRoot),
            new ExternalApiHostInfrastructure(
                new PermanentlyFailingRuntimeFactory(fixture.RepositoryRoot, state),
                new PermanentlyFailingProcessStarter(state),
                new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]),
                new FakeLoopbackPortAllocator([47116]),
                new ScriptedDatabaseBackedApiReadinessProbe(
                    [DatabaseBackedApiReadinessOutcome.UnexpectedStatus]))));
        stopwatch.Stop();
        var attemptsAtReturn = state.TotalAttempts;

        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ExternalApiHostLease.StartupFailureMessage));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(4)));
            Assert.That(state.ProcessAttempts, Is.EqualTo(ExternalApiHostLease.MaximumFailedStartupCleanupAttempts));
            Assert.That(state.RuntimeAttempts, Is.EqualTo(ExternalApiHostLease.MaximumFailedStartupCleanupAttempts));
            Assert.That(state.DatabaseAttempts, Is.EqualTo(ExternalApiHostLease.MaximumFailedStartupCleanupAttempts));
            Assert.That(state.TotalAttempts, Is.EqualTo(attemptsAtReturn));
            Assert.That(state.MaximumConcurrency, Is.EqualTo(1));
            Assert.That(exception.CleanupReceipt.ProcessTreeAbsent, Is.False);
            Assert.That(exception.CleanupReceipt.RuntimeDirectoryAbsent, Is.False);
            Assert.That(exception.CleanupReceipt.DatabaseAbsent, Is.False);
            Assert.That(exception.CleanupReceipt.AttemptedCategories, Is.EqualTo(new[]
            {
                "api-process", "runtime-configuration", "postgresql",
                "api-process", "runtime-configuration", "postgresql",
                "api-process", "runtime-configuration", "postgresql"
            }));
            Assert.That(exception.CleanupReceipt.FailureCount, Is.EqualTo(18));
            Assert.That(exception.CleanupReceipt.AttemptHistoryTruncated, Is.False);
            Assert.That(exception.CleanupReceipt.FailureCountSaturated, Is.False);
        });
    }

    private sealed class PermanentCleanupState
    {
        private int _concurrency;

        internal int DatabaseAttempts { get; private set; }

        internal int MaximumConcurrency { get; private set; }

        internal int ProcessAttempts { get; private set; }

        internal int RuntimeAttempts { get; private set; }

        internal int TotalAttempts => ProcessAttempts + RuntimeAttempts + DatabaseAttempts;

        internal void Record(string category)
        {
            switch (category)
            {
                case ExternalApiHostCleanup.ProcessCategory:
                    ProcessAttempts++;
                    break;
                case ExternalApiHostCleanup.RuntimeCategory:
                    RuntimeAttempts++;
                    break;
                case ExternalApiHostCleanup.DatabaseCategory:
                    DatabaseAttempts++;
                    break;
            }

            var concurrency = Interlocked.Increment(ref _concurrency);
            MaximumConcurrency = Math.Max(MaximumConcurrency, concurrency);
        }

        internal void Complete() => Interlocked.Decrement(ref _concurrency);
    }

    private sealed class PermanentlyFailingProcessStarter(PermanentCleanupState state) : IExternalApiProcessStarter
    {
        public IExternalApiProcess Start(ExternalProcessRequest request) => new PermanentlyFailingProcess(state);
    }

    private sealed class PermanentlyFailingProcess(PermanentCleanupState state) : IExternalApiProcess
    {
        public Task<ExternalApiProcessExit> Exit { get; } =
            Task.FromResult(new ExternalApiProcessExit(ExternalApiProcessExitKind.Failed));

        public TimeSpan ExitObservationTimeout => TimeSpan.FromMilliseconds(25);

        public bool ProcessTreeAbsent => false;

        public ValueTask DisposeAsync()
        {
            state.Record(ExternalApiHostCleanup.ProcessCategory);
            state.Complete();
            return ValueTask.FromException(new IOException("Injected permanent process cleanup failure."));
        }
    }

    private sealed class PermanentlyFailingRuntimeFactory(
        string fixtureRoot,
        PermanentCleanupState state) : IApiHostRuntimeLeaseFactory
    {
        public Task<IApiHostRuntimeLease> CreateAsync(
            RuntimeConfigurationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult<IApiHostRuntimeLease>(new PermanentlyFailingRuntimeLease(fixtureRoot, state));
    }

    private sealed class PermanentlyFailingRuntimeLease(
        string fixtureRoot,
        PermanentCleanupState state) : IApiHostRuntimeLease
    {
        public string ConfigurationPath { get; } = Path.Combine(fixtureRoot, "api", "appsettings.e2e.json");

        public string PrivateTempDirectory { get; } = Path.Combine(fixtureRoot, "api", "temp");

        public bool RuntimeDirectoryAbsent => false;

        public ValueTask DisposeAsync()
        {
            state.Record(ExternalApiHostCleanup.RuntimeCategory);
            state.Complete();
            return ValueTask.FromException(new IOException("Injected permanent runtime cleanup failure."));
        }
    }

    private sealed class PermanentlyFailingDatabaseLease(PermanentCleanupState state)
        : IApiHostDatabaseLease, IApiHostDatabaseAbsenceObservation
    {
        public string ConnectionString => "in-memory-permanent-cleanup-failure";

        public ValueTask DisposeAsync()
        {
            state.Record(ExternalApiHostCleanup.DatabaseCategory);
            state.Complete();
            return ValueTask.FromException(new IOException("Injected permanent database cleanup failure."));
        }

        public Task<bool> ConfirmAbsentAsync() => Task.FromResult(false);
    }
}
