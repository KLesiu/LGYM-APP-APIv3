using System.Reflection;
using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.Lifecycle;

[TestFixture]
[Category("Lifecycle")]
public sealed class DatabaseBackedApiReadinessNegativePathTests
{
    [Test]
    public async Task DatabaseBacked_failed_startup_retains_authoritative_cleanup_after_initial_wait_expires()
    {
        using var fixture = new ExternalApiHostTestFixture();
        fixture.Options.Timeouts.ProcessShutdownSeconds = 1;
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var runtimeFactory = new SharingLockedApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder);
        var processStarter = new FakeExternalApiProcessStarter([null], cleanupOrder);

        Exception? exception = null;
        try
        {
            await ExternalApiHostLease.StartAsync(
                fixture.CreateRequest(database),
                new ExternalApiHostInfrastructure(
                    runtimeFactory,
                    processStarter,
                    new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]),
                    new FakeLoopbackPortAllocator([47115]),
                    new ScriptedDatabaseBackedApiReadinessProbe(
                        [DatabaseBackedApiReadinessOutcome.UnexpectedStatus])));
        }
        catch (Exception startupException)
        {
            exception = startupException;
        }

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf<ExternalApiHostStartupException>());
            Assert.That(exception?.Message, Is.EqualTo(ExternalApiHostLease.StartupFailureMessage));
            Assert.That(runtimeFactory.Lease!.DisposeCount, Is.EqualTo(1));
            Assert.That(database.DisposeCount, Is.Zero);
            Assert.That(cleanupOrder, Is.EqualTo(["api-process", "runtime-configuration"]));
        });

        runtimeFactory.Lease!.ReleaseSharingLock();
        var completed = await Task.WhenAny(
            runtimeFactory.Lease.FinalAbsence,
            Task.Delay(TimeSpan.FromSeconds(3)));

        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.SameAs(runtimeFactory.Lease.FinalAbsence));
            Assert.That(processStarter.Processes.Single().ProcessTreeAbsent, Is.True);
            Assert.That(runtimeFactory.Lease.RuntimeDirectoryAbsent, Is.True);
            Assert.That(database.IsAbsent, Is.True);
            Assert.That(processStarter.Processes.Single().DisposeCount, Is.EqualTo(1));
            Assert.That(runtimeFactory.Lease.DisposeCount, Is.EqualTo(2));
            Assert.That(runtimeFactory.Lease.MaximumConcurrentDisposals, Is.EqualTo(1));
            Assert.That(database.DisposeCount, Is.EqualTo(1));
            Assert.That(cleanupOrder, Is.EqualTo(new[]
            {
                "api-process",
                "runtime-configuration",
                "postgresql",
                "runtime-configuration"
            }));
        });
    }

    [TestCase("readiness")]
    [TestCase("caller-cancellation")]
    public void DatabaseBacked_failed_startup_retries_all_unresolved_cleanup_before_preserving_primary_failure(
        string failureKind)
    {
        using var fixture = new ExternalApiHostTestFixture();
        using var callerCancellation = new CancellationTokenSource();
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder, cleanupFails: true);
        var runtimeFactory = new FakeApiHostRuntimeFactory(
            fixture.RepositoryRoot,
            cleanupOrder,
            cleanupFails: true);
        var processStarter = new FakeExternalApiProcessStarter(
            [null],
            cleanupOrder,
            cleanupFailures: [true]);
        IDatabaseBackedApiReadinessProbe databaseProbe = failureKind == "caller-cancellation"
            ? new CancelingDatabaseReadinessProbe(callerCancellation)
            : new ScriptedDatabaseBackedApiReadinessProbe(
                [DatabaseBackedApiReadinessOutcome.UnexpectedStatus]);

        var exception = Assert.CatchAsync<Exception>(() => ExternalApiHostLease.StartAsync(
            fixture.CreateRequest(database),
            new ExternalApiHostInfrastructure(
                runtimeFactory,
                processStarter,
                new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]),
                new FakeLoopbackPortAllocator([47110]),
                databaseProbe),
            failureKind == "caller-cancellation" ? callerCancellation.Token : CancellationToken.None));
        var receipt = ReadCleanupReceipt(exception!);

        Assert.Multiple(() =>
        {
            Assert.That(
                exception,
                failureKind == "caller-cancellation"
                    ? Is.TypeOf<OperationCanceledException>()
                    : Is.TypeOf<ExternalApiHostStartupException>());
            Assert.That(
                exception!.Message,
                Is.EqualTo(failureKind == "caller-cancellation"
                    ? ExternalApiHostLease.CallerCancellationMessage
                    : ExternalApiHostLease.StartupFailureMessage));
            Assert.That(receipt, Is.Not.Null);
            Assert.That(receipt?.ProcessTreeAbsent, Is.True);
            Assert.That(receipt?.RuntimeDirectoryAbsent, Is.True);
            Assert.That(receipt?.DatabaseAbsent, Is.True);
            Assert.That(receipt?.FailureCount, Is.EqualTo(6));
            Assert.That(receipt?.AttemptedCategories, Is.EqualTo(new[]
            {
                "api-process",
                "runtime-configuration",
                "postgresql",
                "api-process",
                "runtime-configuration",
                "postgresql"
            }));
            Assert.That(processStarter.Processes.Single().DisposeCount, Is.EqualTo(2));
            Assert.That(runtimeFactory.Lease!.DisposeCount, Is.EqualTo(2));
            Assert.That(database.DisposeCount, Is.EqualTo(2));
        });
    }

    [TestCase("unexpected-status")]
    [TestCase("http-failure")]
    [TestCase("http-timeout")]
    public void DatabaseBacked_readiness_failure_cleans_every_resource_before_later_acquisition(
        string outcomeName)
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var laterAcquisitionCount = 0;
        var outcome = outcomeName switch
        {
            "unexpected-status" => DatabaseBackedApiReadinessOutcome.UnexpectedStatus,
            "http-failure" => DatabaseBackedApiReadinessOutcome.HttpFailure,
            "http-timeout" => DatabaseBackedApiReadinessOutcome.HttpTimeout,
            _ => throw new InvalidOperationException("Unknown database-readiness fixture.")
        };

        var exception = Assert.ThrowsAsync<ExternalApiHostStartupException>(async () =>
        {
            await ExternalApiHostLease.StartAsync(
                fixture.CreateRequest(new FakeApiHostDatabaseLease(cleanupOrder)),
                new ExternalApiHostInfrastructure(
                    new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder),
                    new FakeExternalApiProcessStarter([null], cleanupOrder),
                    new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]),
                    new FakeLoopbackPortAllocator([47111]),
                    new ScriptedDatabaseBackedApiReadinessProbe([outcome])));
            laterAcquisitionCount++;
        });
        var receipt = ReadCleanupReceipt(exception!);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ExternalApiHostLease.StartupFailureMessage));
            Assert.That(receipt, Is.Not.Null);
            Assert.That(receipt?.ProcessTreeAbsent, Is.True);
            Assert.That(receipt?.RuntimeDirectoryAbsent, Is.True);
            Assert.That(receipt?.DatabaseAbsent, Is.True);
            Assert.That(receipt?.AttemptedCategories, Is.EqualTo(new[]
            {
                "api-process",
                "runtime-configuration",
                "postgresql"
            }));
            Assert.That(laterAcquisitionCount, Is.Zero);
        });
    }

    [Test]
    public void DatabaseBacked_primary_readiness_failure_survives_a_safe_cleanup_fault()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var runtimeFactory = new AbsentButFaultingRuntimeFactory(fixture.RepositoryRoot, cleanupOrder);

        var exception = Assert.CatchAsync<Exception>(() => ExternalApiHostLease.StartAsync(
            fixture.CreateRequest(new FakeApiHostDatabaseLease(cleanupOrder)),
            new ExternalApiHostInfrastructure(
                runtimeFactory,
                new FakeExternalApiProcessStarter([null], cleanupOrder),
                new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]),
                new FakeLoopbackPortAllocator([47112]),
                new ScriptedDatabaseBackedApiReadinessProbe(
                    [DatabaseBackedApiReadinessOutcome.UnexpectedStatus]))));
        var receipt = ReadCleanupReceipt(exception!);

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf<ExternalApiHostStartupException>());
            Assert.That(exception!.Message, Is.EqualTo(ExternalApiHostLease.StartupFailureMessage));
            Assert.That(receipt, Is.Not.Null);
            Assert.That(receipt?.ProcessTreeAbsent, Is.True);
            Assert.That(receipt?.RuntimeDirectoryAbsent, Is.True);
            Assert.That(receipt?.DatabaseAbsent, Is.True);
            Assert.That(receipt?.FailureCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(cleanupOrder, Is.EqualTo(new[]
            {
                "api-process",
                "runtime-configuration",
                "postgresql"
            }));
        });
    }

    [Test]
    public void DatabaseBacked_caller_cancellation_preserves_its_token_and_safe_cleanup_facts()
    {
        using var fixture = new ExternalApiHostTestFixture();
        using var callerCancellation = new CancellationTokenSource();
        var cleanupOrder = new List<string>();
        var laterAcquisitionCount = 0;

        var exception = Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await ExternalApiHostLease.StartAsync(
                fixture.CreateRequest(new FakeApiHostDatabaseLease(cleanupOrder)),
                new ExternalApiHostInfrastructure(
                    new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder),
                    new FakeExternalApiProcessStarter([null], cleanupOrder),
                    new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]),
                    new FakeLoopbackPortAllocator([47113]),
                    new CancelingDatabaseReadinessProbe(callerCancellation)),
                callerCancellation.Token);
            laterAcquisitionCount++;
        });
        var receipt = ReadCleanupReceipt(exception!);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.CancellationToken, Is.EqualTo(callerCancellation.Token));
            Assert.That(exception.Message, Is.EqualTo(ExternalApiHostLease.CallerCancellationMessage));
            Assert.That(receipt, Is.Not.Null);
            Assert.That(receipt?.ProcessTreeAbsent, Is.True);
            Assert.That(receipt?.RuntimeDirectoryAbsent, Is.True);
            Assert.That(receipt?.DatabaseAbsent, Is.True);
            Assert.That(cleanupOrder, Is.EqualTo(new[]
            {
                "api-process",
                "runtime-configuration",
                "postgresql"
            }));
            Assert.That(laterAcquisitionCount, Is.Zero);
        });
    }

    [Test]
    public void ApiHostObservation_process_only_failure_merges_later_runtime_and_database_absence()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var processStarter = new FakeExternalApiProcessStarter(
            [ExternalApiProcessExitKind.AddressInUse],
            cleanupOrder,
            cleanupFailures: [true]);

        var exception = Assert.ThrowsAsync<ExternalApiHostCleanupException>(() => ExternalApiHostLease.StartAsync(
            fixture.CreateRequest(new FakeApiHostDatabaseLease(cleanupOrder)),
            new ExternalApiHostInfrastructure(
                new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder),
                processStarter,
                new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.AddressInUse]),
                new FakeLoopbackPortAllocator([47114]),
                new ScriptedDatabaseBackedApiReadinessProbe())));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Receipt.ProcessTreeAbsent, Is.True);
            Assert.That(exception.Receipt.RuntimeDirectoryAbsent, Is.True);
            Assert.That(exception.Receipt.DatabaseAbsent, Is.True);
            Assert.That(exception.Receipt.FailureCount, Is.EqualTo(2));
            Assert.That(exception.Receipt.AttemptedCategories, Is.EqualTo(new[]
            {
                "api-process",
                "api-process",
                "runtime-configuration",
                "postgresql"
            }));
            Assert.That(processStarter.Processes.Single().DisposeCount, Is.EqualTo(2));
        });
    }

    private static ExternalApiHostCleanupReceipt? ReadCleanupReceipt(Exception exception) =>
        exception.GetType()
            .GetProperty(
                "CleanupReceipt",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(exception) as ExternalApiHostCleanupReceipt ??
        exception.Data[nameof(ExternalApiHostCleanupReceipt)] as ExternalApiHostCleanupReceipt;

    private sealed class CancelingDatabaseReadinessProbe(CancellationTokenSource callerCancellation)
        : IDatabaseBackedApiReadinessProbe
    {
        public Task<DatabaseBackedApiReadinessOutcome> WaitUntilReadyAsync(
            Uri baseAddress,
            ApiHostReadinessBounds bounds,
            CancellationToken cancellationToken)
        {
            callerCancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation was not observed.");
        }
    }

    private sealed class AbsentButFaultingRuntimeFactory(
        string fixtureRoot,
        ICollection<string> cleanupOrder) : IApiHostRuntimeLeaseFactory
    {
        public Task<IApiHostRuntimeLease> CreateAsync(
            RuntimeConfigurationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult<IApiHostRuntimeLease>(new AbsentButFaultingRuntimeLease(fixtureRoot, cleanupOrder));
    }

    private sealed class SharingLockedApiHostRuntimeFactory(
        string fixtureRoot,
        ICollection<string> cleanupOrder) : IApiHostRuntimeLeaseFactory
    {
        internal SharingLockedApiHostRuntimeLease? Lease { get; private set; }

        public Task<IApiHostRuntimeLease> CreateAsync(
            RuntimeConfigurationRequest request,
            CancellationToken cancellationToken)
        {
            Lease = new SharingLockedApiHostRuntimeLease(fixtureRoot, cleanupOrder);
            return Task.FromResult<IApiHostRuntimeLease>(Lease);
        }
    }

    private sealed class SharingLockedApiHostRuntimeLease(
        string fixtureRoot,
        ICollection<string> cleanupOrder) : IApiHostRuntimeLease
    {
        private readonly TaskCompletionSource _sharingLockRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _finalAbsence =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _cleanupFailuresRemaining = 1;
        private int _concurrentDisposals;

        internal int DisposeCount { get; private set; }

        internal Task FinalAbsence => _finalAbsence.Task;

        internal int MaximumConcurrentDisposals { get; private set; }

        public string ConfigurationPath { get; } = Path.Combine(fixtureRoot, "api", "appsettings.e2e.json");

        public string PrivateTempDirectory { get; } = Path.Combine(fixtureRoot, "api", "temp");

        public bool RuntimeDirectoryAbsent { get; private set; }

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            cleanupOrder.Add("runtime-configuration");
            var concurrentDisposals = Interlocked.Increment(ref _concurrentDisposals);
            MaximumConcurrentDisposals = Math.Max(MaximumConcurrentDisposals, concurrentDisposals);
            try
            {
                await _sharingLockRelease.Task;
                if (Interlocked.Exchange(ref _cleanupFailuresRemaining, 0) != 0)
                {
                    throw new IOException("Injected sharing-locked runtime cleanup failure.");
                }

                RuntimeDirectoryAbsent = true;
                _finalAbsence.TrySetResult();
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentDisposals);
            }
        }

        internal void ReleaseSharingLock() => _sharingLockRelease.TrySetResult();
    }

    private sealed class AbsentButFaultingRuntimeLease(
        string fixtureRoot,
        ICollection<string> cleanupOrder) : IApiHostRuntimeLease
    {
        public string ConfigurationPath { get; } = Path.Combine(fixtureRoot, "api", "appsettings.e2e.json");

        public string PrivateTempDirectory { get; } = Path.Combine(fixtureRoot, "api", "temp");

        public bool RuntimeDirectoryAbsent => true;

        public ValueTask DisposeAsync()
        {
            cleanupOrder.Add("runtime-configuration");
            return ValueTask.FromException(new IOException("Injected private runtime cleanup failure."));
        }
    }
}
