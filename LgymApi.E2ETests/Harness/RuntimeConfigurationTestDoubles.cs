namespace LgymApi.E2ETests.Harness;

internal sealed class FailingFileWriter : IRuntimeConfigurationFileWriter
{
    internal string? RunDirectory { get; private set; }

    public Task WriteAsync(RuntimeConfigurationFileWriteRequest request, CancellationToken cancellationToken)
    {
        RunDirectory = Path.GetDirectoryName(Path.GetDirectoryName(request.Path)!);
        File.WriteAllBytes(request.Path, request.Content);
        throw new IOException("Injected runtime configuration write failure.");
    }
}

internal sealed class CancellingFileWriter(CancellationTokenSource cancellation) : IRuntimeConfigurationFileWriter
{
    internal string? RunDirectory { get; private set; }

    public Task WriteAsync(RuntimeConfigurationFileWriteRequest request, CancellationToken cancellationToken)
    {
        RunDirectory = Path.GetDirectoryName(Path.GetDirectoryName(request.Path)!);
        File.WriteAllBytes(request.Path, request.Content);
        cancellation.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

internal sealed class FailOnceCleaner : IRunDirectoryCleaner
{
    private int _attempts;

    public Task DeleteAsync(string runDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _attempts) == 1)
        {
            throw new IOException("Injected cleanup failure.");
        }

        return new FileSystemRunDirectoryCleaner().DeleteAsync(runDirectory, cancellationToken);
    }
}

internal sealed class ApiReparseFileWriter(string foreignDirectory) : IRuntimeConfigurationFileWriter
{
    private readonly AtomicRuntimeConfigurationFileWriter _writer = new();

    internal string ForeignConfigurationPath => Path.Combine(foreignDirectory, "appsettings.e2e.json");

    public async Task WriteAsync(RuntimeConfigurationFileWriteRequest request, CancellationToken cancellationToken)
    {
        var apiDirectory = Path.GetDirectoryName(request.Path)!;
        Directory.Delete(apiDirectory);
        Directory.CreateSymbolicLink(apiDirectory, foreignDirectory);
        try
        {
            await _writer.WriteAsync(request, cancellationToken);
        }
        finally
        {
            if (Directory.Exists(apiDirectory))
            {
                Directory.Delete(apiDirectory);
            }

            Directory.CreateDirectory(apiDirectory);
        }
    }
}

internal sealed class NeverCompletingCleaner : IRunDirectoryCleaner
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task DeleteAsync(string runDirectory, CancellationToken cancellationToken)
    {
        await _completion.Task.WaitAsync(cancellationToken);
        await new FileSystemRunDirectoryCleaner().DeleteAsync(runDirectory, cancellationToken);
    }

    internal void Complete() => _completion.SetResult();
}
