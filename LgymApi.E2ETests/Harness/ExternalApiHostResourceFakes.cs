namespace LgymApi.E2ETests.Harness;

internal sealed class FakeApiHostDatabaseLease(
    ICollection<string> cleanupOrder,
    bool cleanupFails = false) : IApiHostDatabaseLease
{
    private int _cleanupFailuresRemaining = cleanupFails ? 1 : 0;

    internal int DisposeCount { get; private set; }

    internal bool IsAbsent { get; private set; }

    public string ConnectionString => "in-memory-task-5-connection";

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        cleanupOrder.Add("postgresql");
        if (Interlocked.Exchange(ref _cleanupFailuresRemaining, 0) != 0)
        {
            IsAbsent = false;
            return ValueTask.FromException(new IOException("Injected private database cleanup failure."));
        }

        IsAbsent = true;
        return ValueTask.CompletedTask;
    }

    public Task<bool> ConfirmAbsentAsync() => Task.FromResult(IsAbsent);
}

internal sealed class FakeApiHostRuntimeFactory(
    string fixtureRoot,
    ICollection<string> cleanupOrder,
    bool cleanupFails = false) : IApiHostRuntimeLeaseFactory
{
    internal RuntimeConfigurationRequest? Request { get; private set; }

    internal FakeApiHostRuntimeLease? Lease { get; private set; }

    public Task<IApiHostRuntimeLease> CreateAsync(
        RuntimeConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Request = request;
        Lease = new FakeApiHostRuntimeLease(fixtureRoot, cleanupOrder, cleanupFails);
        return Task.FromResult<IApiHostRuntimeLease>(Lease);
    }
}

internal sealed class FakeApiHostRuntimeLease(
    string fixtureRoot,
    ICollection<string> cleanupOrder,
    bool cleanupFails) : IApiHostRuntimeLease
{
    private int _cleanupFailuresRemaining = cleanupFails ? 1 : 0;

    internal int DisposeCount { get; private set; }

    public string ConfigurationPath { get; } = Path.Combine(fixtureRoot, "api", "appsettings.e2e.json");

    public string PrivateTempDirectory { get; } = Path.Combine(fixtureRoot, "api", "temp");

    public bool RuntimeDirectoryAbsent { get; private set; }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        cleanupOrder.Add("runtime-configuration");
        if (Interlocked.Exchange(ref _cleanupFailuresRemaining, 0) != 0)
        {
            RuntimeDirectoryAbsent = false;
            return ValueTask.FromException(new IOException("Injected private runtime cleanup failure."));
        }

        RuntimeDirectoryAbsent = true;
        return ValueTask.CompletedTask;
    }
}
