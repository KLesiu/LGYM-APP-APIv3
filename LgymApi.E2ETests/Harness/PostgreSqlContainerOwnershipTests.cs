using LgymApi.E2ETests.Lifecycle;
using System.Diagnostics;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class PostgreSqlContainerOwnershipTests
{
    [Test]
    public async Task PostgreSQL_cleanup_uses_one_shared_deadline_for_disposal_and_absence()
    {
        var operations = new ControlledPostgreSqlContainerOperations
        {
            DisposeDelay = TimeSpan.FromMilliseconds(70),
            AbsenceDelay = TimeSpan.FromMilliseconds(70)
        };
        var lease = await PostgreSqlContainerLease.StartAsync(
            operations,
            TimeSpan.FromMilliseconds(100));

        var stopwatch = Stopwatch.StartNew();
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await lease.DisposeAsync());
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Testcontainers PostgreSQL cleanup exceeded the configured shutdown timeout."));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(160)));
            Assert.That(operations.DisposeCount, Is.EqualTo(1));
            Assert.That(operations.AbsenceCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PostgreSQL_cleanup_failure_leaves_lease_retryable_and_serialized()
    {
        var operations = new ControlledPostgreSqlContainerOperations { DisposeFailuresRemaining = 1 };
        var lease = await PostgreSqlContainerLease.StartAsync(operations, TimeSpan.FromSeconds(1));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await lease.DisposeAsync());
        await lease.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Testcontainers PostgreSQL cleanup failed."));
            Assert.That(operations.DisposeCount, Is.EqualTo(2));
            Assert.That(operations.AbsenceCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void PostgreSQL_post_acquisition_start_failure_cleans_the_owned_container_within_one_deadline()
    {
        var operations = new ControlledPostgreSqlContainerOperations
        {
            StartFailureAfterAcquisition = true,
            CapturePrivateLocatorBeforeFailure = true
        };
        var stopwatch = Stopwatch.StartNew();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => PostgreSqlContainerLease.StartAsync(
            operations,
            TimeSpan.FromMilliseconds(100)));
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Injected startup failure after acquisition."));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(160)));
            Assert.That(operations.DisposeCount, Is.EqualTo(1));
            Assert.That(operations.AbsenceCount, Is.EqualTo(1));
            Assert.That(operations.IsAbsent, Is.True);
        });
    }

    [Test]
    public void PostgreSQL_post_acquisition_start_failure_preserves_the_primary_failure_only_after_a_private_locator_proves_absence()
    {
        var operations = new ControlledPostgreSqlContainerOperations
        {
            StartFailureAfterAcquisition = true,
            CapturePrivateLocatorBeforeFailure = true
        };

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => PostgreSqlContainerLease.StartAsync(
            operations,
            TimeSpan.FromMilliseconds(100)));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Injected startup failure after acquisition."));
            Assert.That(operations.DisposeCount, Is.EqualTo(1));
            Assert.That(operations.AbsenceCount, Is.EqualTo(1));
            Assert.That(operations.IsAbsent, Is.True);
        });
    }

    [Test]
    public void PostgreSQL_startup_failure_with_an_uncaptured_locator_fails_closed_without_exposing_the_raw_disposal_failure()
    {
        var operations = new ControlledPostgreSqlContainerOperations
        {
            StartFailureAfterAcquisition = true,
            CapturePrivateLocatorBeforeFailure = false,
            CanProveAbsence = false
        };
        operations.EnqueueDisposal(Task.FromException(new IOException("fixture disposal failure")));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => PostgreSqlContainerLease.StartAsync(
            operations,
            TimeSpan.FromMilliseconds(100)));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Testcontainers PostgreSQL cleanup failed."));
            Assert.That(exception.InnerException, Is.Null);
            Assert.That(operations.DisposeCount, Is.EqualTo(1));
            Assert.That(operations.AbsenceCount, Is.EqualTo(1));
            Assert.That(operations.IsAbsent, Is.False);
        });
    }

    [Test]
    public async Task PostgreSQL_cleanup_retains_one_raw_disposal_through_two_caller_timeouts_then_proves_absence_once()
    {
        var rawDisposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new ControlledPostgreSqlContainerOperations();
        operations.EnqueueDisposal(rawDisposal.Task);
        var lease = await PostgreSqlContainerLease.StartAsync(operations, TimeSpan.FromMilliseconds(50));
        var firstCaller = lease.DisposeAsync().AsTask();
        await operations.DisposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var secondCaller = lease.DisposeAsync().AsTask();

        try
        {
            var bothCallers = Task.WhenAll(IgnoreFailureAsync(firstCaller), IgnoreFailureAsync(secondCaller));
            var completed = await Task.WhenAny(bothCallers, Task.Delay(TimeSpan.FromMilliseconds(250)));

            Assert.That(completed, Is.SameAs(bothCallers));
            Assert.Multiple(() =>
            {
                Assert.That(operations.DisposeCount, Is.EqualTo(1));
                Assert.That(operations.MaximumConcurrentDisposals, Is.EqualTo(1));
                Assert.That(operations.AbsenceCount, Is.EqualTo(0));
            });
        }
        finally
        {
            rawDisposal.TrySetResult();
        }

        await IgnoreFailureAsync(firstCaller);
        await IgnoreFailureAsync(secondCaller);
        await lease.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(operations.DisposeCount, Is.EqualTo(1));
            Assert.That(operations.MaximumConcurrentDisposals, Is.EqualTo(1));
            Assert.That(operations.AbsenceCount, Is.EqualTo(1));
            Assert.That(lease.CleanupReceipt.ContainerAbsent, Is.True);
        });
    }

    [Test]
    public async Task PostgreSQL_late_raw_disposal_failure_is_terminal_before_a_safe_retry_starts()
    {
        var firstRawDisposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new ControlledPostgreSqlContainerOperations();
        operations.EnqueueDisposal(firstRawDisposal.Task);
        operations.EnqueueDisposal(Task.CompletedTask);
        var lease = await PostgreSqlContainerLease.StartAsync(operations, TimeSpan.FromMilliseconds(50));
        var timedOutCaller = lease.DisposeAsync().AsTask();
        await operations.DisposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        try
        {
            var completed = await Task.WhenAny(timedOutCaller, Task.Delay(TimeSpan.FromMilliseconds(250)));
            Assert.That(completed, Is.SameAs(timedOutCaller));
            Assert.That(operations.DisposeCount, Is.EqualTo(1));
        }
        finally
        {
            firstRawDisposal.TrySetException(new IOException("fixture disposal failure"));
        }

        await IgnoreFailureAsync(timedOutCaller);
        await WaitUntilAsync(() => operations.ActiveDisposals == 0);
        await lease.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(operations.DisposeCount, Is.EqualTo(2));
            Assert.That(operations.MaximumConcurrentDisposals, Is.EqualTo(1));
            Assert.That(operations.AbsenceCount, Is.EqualTo(2));
            Assert.That(lease.CleanupReceipt.ContainerAbsent, Is.True);
        });
    }

    [Test]
    public async Task PostgreSQL_cleanup_gate_acquisition_uses_the_callers_single_cleanup_budget()
    {
        var operations = new ControlledPostgreSqlContainerOperations();
        var lease = await PostgreSqlContainerLease.StartAsync(operations, TimeSpan.FromMilliseconds(50));
        var cleanupGate = typeof(PostgreSqlContainerLease)
            .GetField("_cleanupGate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(lease) as SemaphoreSlim;
        Assert.That(cleanupGate, Is.Not.Null);
        await cleanupGate!.WaitAsync();

        try
        {
            var caller = lease.DisposeAsync().AsTask();
            var completed = await Task.WhenAny(caller, Task.Delay(TimeSpan.FromMilliseconds(250)));

            Assert.That(completed, Is.SameAs(caller));
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await caller);
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo("Testcontainers PostgreSQL cleanup exceeded the configured shutdown timeout."));
                Assert.That(operations.DisposeCount, Is.EqualTo(0));
            });
        }
        finally
        {
            cleanupGate.Release();
        }

        await lease.DisposeAsync();
        Assert.That(lease.CleanupReceipt.ContainerAbsent, Is.True);
    }
    [Test]
    public async Task PostgreSQL_scenario_ownership_disposes_once_before_transfer()
    {
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var ownership = new ScenarioDatabaseOwnership(database, CreateObservation());

        await ownership.DisposeAsync();
        await ownership.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(database.DisposeCount, Is.EqualTo(1));
            Assert.That(cleanupOrder, Is.EqualTo(["postgresql"]));
        });
    }

    [Test]
    public async Task PostgreSQL_scenario_ownership_clears_its_disposable_slot_before_host_startup()
    {
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var ownership = new ScenarioDatabaseOwnership(database, CreateObservation());

        var transferred = ownership.TransferToApiHost();
        await ownership.DisposeAsync();
        await transferred.DisposeAsync();
        await ownership.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(database.DisposeCount, Is.EqualTo(1));
            Assert.That(cleanupOrder, Is.EqualTo(["postgresql"]));
        });
    }

    [Test]
    public async Task PostgreSQL_transferred_to_API_host_is_disposed_once_after_successful_start()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        await using var ownership = new ScenarioDatabaseOwnership(database, CreateObservation());
        var runtimeFactory = new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder);
        var processStarter = new FakeExternalApiProcessStarter([null], cleanupOrder);

        var hostDatabase = ownership.TransferToApiHost();
        var host = await ExternalApiHostLease.StartAsync(
            new ExternalApiHostCompositionRequest(
                fixture.Publication,
                hostDatabase,
                fixture.Options,
                fixture.RepositoryRoot),
            fixture.CreateInfrastructure(
                runtimeFactory,
                processStarter,
                new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]),
                new FakeLoopbackPortAllocator([46401])));

        await ownership.DisposeAsync();
        await host.DisposeAsync();
        await ownership.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(database.DisposeCount, Is.EqualTo(1));
            Assert.That(cleanupOrder, Is.EqualTo(["api-process", "runtime-configuration", "postgresql"]));
        });
    }

    [Test]
    public async Task PostgreSQL_transferred_to_API_host_is_disposed_once_when_startup_fails()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        await using var ownership = new ScenarioDatabaseOwnership(database, CreateObservation());
        var runtimeFactory = new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder);
        var processStarter = new FakeExternalApiProcessStarter([null], cleanupOrder);

        var hostDatabase = ownership.TransferToApiHost();
        var exception = Assert.ThrowsAsync<ExternalApiHostStartupException>(() =>
            ExternalApiHostLease.StartAsync(
                new ExternalApiHostCompositionRequest(
                    fixture.Publication,
                    hostDatabase,
                    fixture.Options,
                    fixture.RepositoryRoot),
                fixture.CreateInfrastructure(
                    runtimeFactory,
                    processStarter,
                    new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.HttpFailure]),
                    new FakeLoopbackPortAllocator([46402]))));

        await ownership.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ExternalApiHostLease.StartupFailureMessage));
            Assert.That(database.DisposeCount, Is.EqualTo(1));
            Assert.That(cleanupOrder, Is.EqualTo(["api-process", "runtime-configuration", "postgresql"]));
        });
    }

    [Test]
    public void Scenario_resource_identities_have_value_equality_without_raw_text()
    {
        var first = ScenarioResourceIdentity.Create();
        var second = ScenarioResourceIdentity.Create();

        Assert.Multiple(() =>
        {
            Assert.That(first.Equals(first), Is.True);
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(first.ToString(), Is.EqualTo("<scenario-resource-identity>"));
            Assert.That(second.ToString(), Is.EqualTo("<scenario-resource-identity>"));
        });
    }

    private static ScenarioResourceObservation CreateObservation() =>
        new(ScenarioResourceIdentity.Create(), () => Task.FromResult(true));

    private static async Task IgnoreFailureAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var completed = await Task.WhenAny(
            Task.Run(() => SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(1))),
            Task.Delay(TimeSpan.FromSeconds(1)));

        Assert.That(completed, Is.Not.Null);
        Assert.That(condition(), Is.True);
    }

    private sealed class ControlledPostgreSqlContainerOperations : IPostgreSqlContainerLeaseOperations
    {
        internal TimeSpan DisposeDelay { get; init; }

        internal TimeSpan AbsenceDelay { get; init; }

        internal int DisposeFailuresRemaining { get; set; }

        internal bool StartFailureAfterAcquisition { get; init; }

        internal bool CapturePrivateLocatorBeforeFailure { get; init; }

        internal bool CanProveAbsence { get; init; } = true;

        internal int DisposeCount { get; private set; }

        internal int AbsenceCount { get; private set; }

        internal bool IsAbsent { get; private set; }

        internal int MaximumConcurrentDisposals { get; private set; }

        internal int ActiveDisposals => Volatile.Read(ref _activeDisposals);

        internal TaskCompletionSource DisposalStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly Queue<Task> _disposals = new();
        private int _activeDisposals;
        private bool _hasPrivateLocator;

        public string ConnectionString => "redacted";

        public bool IsRunning => !IsAbsent;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _hasPrivateLocator = !StartFailureAfterAcquisition || CapturePrivateLocatorBeforeFailure;
            return StartFailureAfterAcquisition
                ? Task.FromException(new InvalidOperationException("Injected startup failure after acquisition."))
                : Task.CompletedTask;
        }

        internal void EnqueueDisposal(Task disposal)
        {
            _disposals.Enqueue(disposal);
        }

        public async Task DisposeAsync()
        {
            DisposeCount++;
            await Task.Delay(DisposeDelay);
            var activeDisposals = Interlocked.Increment(ref _activeDisposals);
            MaximumConcurrentDisposals = Math.Max(MaximumConcurrentDisposals, activeDisposals);
            DisposalStarted.TrySetResult();
            try
            {
                if (DisposeFailuresRemaining > 0)
                {
                    DisposeFailuresRemaining--;
                    throw new IOException("Injected cleanup failure.");
                }

                if (_disposals.Count > 0)
                {
                    await _disposals.Dequeue();
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeDisposals);
            }
        }

        public async Task<bool> WaitUntilAbsentAsync(CancellationToken cancellationToken)
        {
            AbsenceCount++;
            await Task.Delay(AbsenceDelay, cancellationToken);
            IsAbsent = CanProveAbsence && _hasPrivateLocator;
            return IsAbsent;
        }
    }
}
