using System.Diagnostics;
using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.Lifecycle;

[TestFixture]
[Category("Lifecycle")]
public sealed class DatabaseBackedApiReadinessHungCleanupTests
{
    [TestCase("api-process", 1, 0, 0)]
    [TestCase("runtime-configuration", 1, 1, 0)]
    [TestCase("postgresql", 1, 1, 1)]
    public async Task DatabaseBacked_hung_cleanup_stops_at_the_aggregate_coordinator_deadline(
        string hungCategory,
        int expectedProcessAttempts,
        int expectedRuntimeAttempts,
        int expectedDatabaseAttempts)
    {
        using var fixture = new ExternalApiHostTestFixture();
        fixture.Options.Timeouts.ProcessShutdownSeconds = 1;
        var state = new HungCleanupState(hungCategory);
        var stopwatch = Stopwatch.StartNew();

        var exception = Assert.ThrowsAsync<ExternalApiHostStartupException>(() => ExternalApiHostLease.StartAsync(
            new ExternalApiHostCompositionRequest(
                fixture.Publication,
                new HungDatabaseLease(state),
                fixture.Options,
                fixture.RepositoryRoot),
            new ExternalApiHostInfrastructure(
                new HungRuntimeFactory(fixture.RepositoryRoot, state),
                new HungProcessStarter(state),
                new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]),
                new FakeLoopbackPortAllocator([47117]),
                new ScriptedDatabaseBackedApiReadinessProbe(
                    [DatabaseBackedApiReadinessOutcome.UnexpectedStatus]))));
        var startupElapsed = stopwatch.Elapsed;
        var completion = await Task.WhenAny(
            exception!.CleanupCompletion,
            Task.Delay(TimeSpan.FromSeconds(4)));
        stopwatch.Stop();

        Assert.That(
            completion,
            Is.SameAs(exception.CleanupCompletion),
            "The failed-start cleanup coordinator outlived its aggregate deadline.");
        var terminalReceipt = await exception.CleanupCompletion;
        var attemptsAtTerminal = state.TotalAttempts;
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo(ExternalApiHostLease.StartupFailureMessage));
            Assert.That(startupElapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
            Assert.That(state.ProcessAttempts, Is.EqualTo(expectedProcessAttempts));
            Assert.That(state.RuntimeAttempts, Is.EqualTo(expectedRuntimeAttempts));
            Assert.That(state.DatabaseAttempts, Is.EqualTo(expectedDatabaseAttempts));
            Assert.That(state.TotalAttempts, Is.EqualTo(attemptsAtTerminal));
            Assert.That(state.MaximumConcurrency, Is.EqualTo(1));
            Assert.That(terminalReceipt.ProcessTreeAbsent, Is.False);
            Assert.That(terminalReceipt.RuntimeDirectoryAbsent, Is.False);
            Assert.That(terminalReceipt.DatabaseAbsent, Is.False);
            Assert.That(terminalReceipt.AttemptedCategories, Is.Empty);
            Assert.That(terminalReceipt.FailureCount, Is.EqualTo(1));
            Assert.That(terminalReceipt.AttemptHistoryTruncated, Is.False);
            Assert.That(terminalReceipt.FailureCountSaturated, Is.False);
        });
    }

    private sealed class HungCleanupState(string hungCategory)
    {
        private int _concurrency;

        internal int DatabaseAttempts { get; private set; }

        internal bool DatabaseAbsent { get; set; }

        internal string HungCategory { get; } = hungCategory;

        internal int MaximumConcurrency { get; private set; }

        internal int ProcessAttempts { get; private set; }

        internal bool ProcessAbsent { get; set; }

        internal int RuntimeAttempts { get; private set; }

        internal bool RuntimeAbsent { get; set; }

        internal int TotalAttempts => ProcessAttempts + RuntimeAttempts + DatabaseAttempts;

        internal void Begin(string category)
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

    private sealed class HungProcessStarter(HungCleanupState state) : IExternalApiProcessStarter
    {
        public IExternalApiProcess Start(ExternalProcessRequest request) => new HungProcess(state);
    }

    private sealed class HungProcess(HungCleanupState state) : IExternalApiProcess
    {
        private readonly TaskCompletionSource _never = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ExternalApiProcessExit> Exit { get; } =
            Task.FromResult(new ExternalApiProcessExit(ExternalApiProcessExitKind.Failed));

        public TimeSpan ExitObservationTimeout => TimeSpan.FromMilliseconds(25);

        public bool ProcessTreeAbsent => state.ProcessAbsent;

        public ValueTask DisposeAsync()
        {
            state.Begin(ExternalApiHostCleanup.ProcessCategory);
            if (state.HungCategory == ExternalApiHostCleanup.ProcessCategory)
            {
                return new ValueTask(_never.Task);
            }

            state.ProcessAbsent = true;
            state.Complete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HungRuntimeFactory(
        string fixtureRoot,
        HungCleanupState state) : IApiHostRuntimeLeaseFactory
    {
        public Task<IApiHostRuntimeLease> CreateAsync(
            RuntimeConfigurationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult<IApiHostRuntimeLease>(new HungRuntimeLease(fixtureRoot, state));
    }

    private sealed class HungRuntimeLease(
        string fixtureRoot,
        HungCleanupState state) : IApiHostRuntimeLease
    {
        private readonly TaskCompletionSource _never = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ConfigurationPath { get; } = Path.Combine(fixtureRoot, "api", "appsettings.e2e.json");

        public string PrivateTempDirectory { get; } = Path.Combine(fixtureRoot, "api", "temp");

        public bool RuntimeDirectoryAbsent => state.RuntimeAbsent;

        public ValueTask DisposeAsync()
        {
            state.Begin(ExternalApiHostCleanup.RuntimeCategory);
            if (state.HungCategory == ExternalApiHostCleanup.RuntimeCategory)
            {
                return new ValueTask(_never.Task);
            }

            state.RuntimeAbsent = true;
            state.Complete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HungDatabaseLease(HungCleanupState state)
        : IApiHostDatabaseLease, IApiHostDatabaseAbsenceObservation
    {
        private readonly TaskCompletionSource _never = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ConnectionString => "in-memory-hung-cleanup";

        public ValueTask DisposeAsync()
        {
            state.Begin(ExternalApiHostCleanup.DatabaseCategory);
            if (state.HungCategory == ExternalApiHostCleanup.DatabaseCategory)
            {
                return new ValueTask(_never.Task);
            }

            state.DatabaseAbsent = true;
            state.Complete();
            return ValueTask.CompletedTask;
        }

        public Task<bool> ConfirmAbsentAsync() => Task.FromResult(state.DatabaseAbsent);
    }
}
