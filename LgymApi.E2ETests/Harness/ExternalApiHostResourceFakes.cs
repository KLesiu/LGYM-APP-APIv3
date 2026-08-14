namespace LgymApi.E2ETests.Harness;

internal sealed class FakeApiHostDatabaseLease(
    ICollection<string> cleanupOrder,
    bool cleanupFails = false) : IApiHostDatabaseLease
{
    internal int DisposeCount { get; private set; }

    internal bool IsAbsent { get; private set; }

    public string ConnectionString => "in-memory-task-5-connection";

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        cleanupOrder.Add("postgresql");
        IsAbsent = !cleanupFails;
        return cleanupFails
            ? ValueTask.FromException(new IOException("Injected private database cleanup failure."))
            : ValueTask.CompletedTask;
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
    internal int DisposeCount { get; private set; }

    public string ConfigurationPath { get; } = Path.Combine(fixtureRoot, "api", "appsettings.e2e.json");

    public string PrivateTempDirectory { get; } = Path.Combine(fixtureRoot, "api", "temp");

    public bool RuntimeDirectoryAbsent { get; private set; }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        cleanupOrder.Add("runtime-configuration");
        RuntimeDirectoryAbsent = !cleanupFails;
        return cleanupFails
            ? ValueTask.FromException(new IOException("Injected private runtime cleanup failure."))
            : ValueTask.CompletedTask;
    }
}
