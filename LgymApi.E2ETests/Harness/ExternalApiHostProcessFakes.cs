using System.Diagnostics;

namespace LgymApi.E2ETests.Harness;

internal sealed class FakeExternalApiProcessStarter(
    IEnumerable<ExternalApiProcessExitKind?> exits,
    ICollection<string> cleanupOrder,
    IEnumerable<bool>? cleanupFailures = null) : IExternalApiProcessStarter
{
    private readonly Queue<ExternalApiProcessExitKind?> _exits = new(exits);
    private readonly Queue<bool> _cleanupFailures = new(cleanupFailures ?? []);

    internal List<ExternalProcessRequest> Requests { get; } = [];

    internal List<ProcessStartInfo> StartInfos { get; } = [];

    internal List<FakeExternalApiProcess> Processes { get; } = [];

    public IExternalApiProcess Start(ExternalProcessRequest request)
    {
        Requests.Add(request);
        StartInfos.Add(ExternalProcessRunner.CreateStartInfo(request));
        var process = new FakeExternalApiProcess(
            _exits.Count == 0 ? null : _exits.Dequeue(),
            cleanupOrder,
            _cleanupFailures.Count != 0 && _cleanupFailures.Dequeue(),
            request.ShutdownTimeout);
        Processes.Add(process);
        return process;
    }
}

internal sealed class FakeExternalApiProcess : IExternalApiProcess
{
    private readonly TaskCompletionSource<ExternalApiProcessExit> _exit =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ICollection<string> _cleanupOrder;
    private int _cleanupFailuresRemaining;
    private int _disposed;

    internal FakeExternalApiProcess(
        ExternalApiProcessExitKind? exit,
        ICollection<string> cleanupOrder,
        bool cleanupFails,
        TimeSpan exitObservationTimeout)
    {
        _cleanupOrder = cleanupOrder;
        _cleanupFailuresRemaining = cleanupFails ? 1 : 0;
        ExitObservationTimeout = exitObservationTimeout;
        if (exit is not null)
        {
            _exit.SetResult(new ExternalApiProcessExit(exit.Value));
        }
    }

    public Task<ExternalApiProcessExit> Exit => _exit.Task;

    public TimeSpan ExitObservationTimeout { get; }

    internal int DisposeCount { get; private set; }

    public bool ProcessTreeAbsent { get; private set; }

    public ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return ValueTask.CompletedTask;
        }

        DisposeCount++;
        _cleanupOrder.Add("api-process");
        _exit.TrySetResult(new ExternalApiProcessExit(ExternalApiProcessExitKind.Failed));
        if (Interlocked.Exchange(ref _cleanupFailuresRemaining, 0) != 0)
        {
            ProcessTreeAbsent = false;
            return ValueTask.FromException(new IOException("Injected private process cleanup failure."));
        }

        ProcessTreeAbsent = true;
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }
}
