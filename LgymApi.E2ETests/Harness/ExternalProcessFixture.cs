namespace LgymApi.E2ETests.Harness;

internal sealed class ExternalProcessFixture : IDisposable
{
    private const string ReadyFileEnvironmentVariable = "LGYM_E2E_READY_FILE";
    private const string WholeSecretEnvironmentVariable = "LGYM_E2E_WHOLE_CANARY";
    private const string SplitSecretEnvironmentVariable = "LGYM_E2E_SPLIT_CANARY";

    private const string OutputScript = """
        $whole = $env:LGYM_E2E_WHOLE_CANARY
        $split = $env:LGYM_E2E_SPLIT_CANARY
        [Console]::Out.Write(('o' * 70000))
        [Console]::Error.Write(('e' * 70000))
        [Console]::Out.Write($whole)
        [Console]::Out.Write($split.Substring(0, 7))
        [Console]::Out.Flush()
        [Console]::Out.Write($split.Substring(7))
        [Console]::Out.Write('::stdout-end::')
        [Console]::Error.Write($whole)
        [Console]::Error.Write($split.Substring(0, 7))
        [Console]::Error.Flush()
        [Console]::Error.Write($split.Substring(7))
        [Console]::Error.Write('::stderr-end::')
        """;

    private const string BlockingTreeScript = """
        $whole = $env:LGYM_E2E_WHOLE_CANARY
        $split = $env:LGYM_E2E_SPLIT_CANARY
        [Console]::Out.Write(('o' * 70000))
        [Console]::Error.Write(('e' * 70000))
        [Console]::Out.Write($whole)
        [Console]::Out.Write($split.Substring(0, 7))
        [Console]::Out.Flush()
        [Console]::Out.Write($split.Substring(7))
        [Console]::Error.Write($whole)
        [Console]::Error.Write($split.Substring(0, 7))
        [Console]::Error.Flush()
        [Console]::Error.Write($split.Substring(7))
        $childScript = '$gate=[System.Threading.ManualResetEventSlim]::new($false);$gate.Wait()'
        $null = Start-Process -FilePath 'pwsh' -ArgumentList @('-NoProfile','-NonInteractive','-Command',$childScript) -NoNewWindow
        Set-Content -LiteralPath $env:LGYM_E2E_READY_FILE -Value 'ready'
        $gate = [System.Threading.ManualResetEventSlim]::new($false)
        $gate.Wait()
        """;

    private const string QuietBlockingTreeScript = """
        $childScript = '$gate=[System.Threading.ManualResetEventSlim]::new($false);$gate.Wait()'
        $null = Start-Process -FilePath 'pwsh' -ArgumentList @('-NoProfile','-NonInteractive','-Command',$childScript) -NoNewWindow
        Set-Content -LiteralPath $env:LGYM_E2E_READY_FILE -Value 'ready'
        $gate = [System.Threading.ManualResetEventSlim]::new($false)
        $gate.Wait()
        """;

    private readonly string _directory;
    private readonly string _readyFile;
    private readonly FileSystemWatcher _watcher;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ExternalProcessFixture()
    {
        _directory = Directory.CreateTempSubdirectory("lgym-e2e-process-").FullName;
        _readyFile = Path.Combine(_directory, "ready.signal");
        _watcher = new FileSystemWatcher(_directory, Path.GetFileName(_readyFile));
        _watcher.Created += OnReady;
        _watcher.Changed += OnReady;
        _watcher.EnableRaisingEvents = true;
    }

    public ExternalProcessRequest CreateOutputRequest(
        string wholeSecret,
        string splitSecret) =>
        CreateRequest(OutputScript, wholeSecret, splitSecret, TimeSpan.FromSeconds(10));

    public ExternalProcessRequest CreateBlockingTreeRequest(
        string wholeSecret,
        string splitSecret,
        TimeSpan executionTimeout) =>
        CreateRequest(BlockingTreeScript, wholeSecret, splitSecret, executionTimeout);

    public ExternalProcessRequest CreateQuietBlockingTreeRequest(
        string wholeSecret,
        string splitSecret,
        TimeSpan executionTimeout) =>
        CreateRequest(QuietBlockingTreeScript, wholeSecret, splitSecret, executionTimeout);

    public bool HasSignaledReady => File.Exists(_readyFile);

    public Task WaitUntilReadyAsync(TimeSpan timeout) => File.Exists(_readyFile)
        ? Task.CompletedTask
        : _ready.Task.WaitAsync(timeout);

    public async Task WaitUntilReadyOrFailedAsync(Task processTask, TimeSpan timeout)
    {
        if (File.Exists(_readyFile))
        {
            return;
        }

        var readyTask = _ready.Task.WaitAsync(timeout);
        if (await Task.WhenAny(processTask, readyTask) == processTask)
        {
            await processTask;
        }

        await readyTask;
    }

    public void Dispose()
    {
        _watcher.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private ExternalProcessRequest CreateRequest(
        string script,
        string wholeSecret,
        string splitSecret,
        TimeSpan executionTimeout) =>
        new()
        {
            FileName = "pwsh",
            Arguments = ["-NoProfile", "-NonInteractive", "-Command", script],
            WorkingDirectory = _directory,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                [ReadyFileEnvironmentVariable] = _readyFile,
                [WholeSecretEnvironmentVariable] = wholeSecret,
                [SplitSecretEnvironmentVariable] = splitSecret
            },
            SecretCanaries = [wholeSecret, splitSecret],
            ExecutionTimeout = executionTimeout,
            ShutdownTimeout = TimeSpan.FromSeconds(10)
        };

    private void OnReady(object sender, FileSystemEventArgs args) => _ready.TrySetResult();
}
