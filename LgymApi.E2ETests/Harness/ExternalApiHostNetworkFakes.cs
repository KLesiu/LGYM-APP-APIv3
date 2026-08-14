namespace LgymApi.E2ETests.Harness;

internal sealed class ScriptedApiHostReadinessMonitor(IEnumerable<ApiHostReadinessOutcome> outcomes)
    : IApiHostReadinessMonitor
{
    private readonly Queue<ApiHostReadinessOutcome> _outcomes = new(outcomes);

    internal List<Uri> HealthEndpoints { get; } = [];

    internal List<CancellationToken> StartupTokens { get; } = [];

    internal List<ApiHostReadinessBounds> Bounds { get; } = [];

    public Task<ApiHostReadinessOutcome> WaitUntilReadyAsync(
        Uri healthEndpoint,
        Task<ExternalApiProcessExit> processExit,
        ApiHostReadinessBounds bounds,
        CancellationToken cancellationToken)
    {
        HealthEndpoints.Add(healthEndpoint);
        StartupTokens.Add(cancellationToken);
        Bounds.Add(bounds);
        return Task.FromResult(_outcomes.Dequeue());
    }
}

internal sealed class ExitObservingApiHostReadinessMonitor : IApiHostReadinessMonitor
{
    public async Task<ApiHostReadinessOutcome> WaitUntilReadyAsync(
        Uri healthEndpoint,
        Task<ExternalApiProcessExit> processExit,
        ApiHostReadinessBounds bounds,
        CancellationToken cancellationToken)
    {
        var exit = await processExit.WaitAsync(cancellationToken);
        return exit.Kind == ExternalApiProcessExitKind.AddressInUse
            ? ApiHostReadinessOutcome.AddressInUse
            : ApiHostReadinessOutcome.ProcessExited;
    }
}

internal sealed class CancelingApiHostReadinessMonitor(CancellationTokenSource callerCancellation)
    : IApiHostReadinessMonitor
{
    public Task<ApiHostReadinessOutcome> WaitUntilReadyAsync(
        Uri healthEndpoint,
        Task<ExternalApiProcessExit> processExit,
        ApiHostReadinessBounds bounds,
        CancellationToken cancellationToken)
    {
        callerCancellation.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("Cancellation was not observed.");
    }
}

internal sealed class ScriptedDatabaseBackedApiReadinessProbe(
    IEnumerable<DatabaseBackedApiReadinessOutcome>? outcomes = null) : IDatabaseBackedApiReadinessProbe
{
    private readonly Queue<DatabaseBackedApiReadinessOutcome> _outcomes = new(
        outcomes ?? [DatabaseBackedApiReadinessOutcome.Ready]);

    public Task<DatabaseBackedApiReadinessOutcome> WaitUntilReadyAsync(
        Uri baseAddress,
        ApiHostReadinessBounds bounds,
        CancellationToken cancellationToken) =>
        Task.FromResult(_outcomes.Dequeue());
}

internal sealed class FakeLoopbackPortAllocator(IEnumerable<int> ports) : ILoopbackPortAllocator
{
    private readonly Queue<int> _ports = new(ports);

    internal int AllocationCount { get; private set; }

    public int Allocate()
    {
        AllocationCount++;
        return _ports.Dequeue();
    }
}
