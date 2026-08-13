using System.Diagnostics;

namespace LgymApi.E2ETests.Harness;

internal sealed class OwnedExternalProcessFixture : IDisposable
{
    private const string ReadyFileVariable = "LGYM_OWNED_READY_FILE";
    private const string WholeSecretVariable = "LGYM_OWNED_WHOLE_CANARY";
    private const string SplitSecretVariable = "LGYM_OWNED_SPLIT_CANARY";

    private const string BlockingTreeScript = """
        $whole = $env:LGYM_OWNED_WHOLE_CANARY
        $split = $env:LGYM_OWNED_SPLIT_CANARY
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
        Set-Content -LiteralPath $env:LGYM_OWNED_READY_FILE -Value 'ready'
        [Console]::Out.Write('::owned-ready::')
        [Console]::Out.Flush()
        $gate = [System.Threading.ManualResetEventSlim]::new($false)
        $gate.Wait()
        """;

    private const string EarlyExitScript = """
        [Console]::Out.Write('::early-stdout::')
        [Console]::Error.Write('::early-stderr::')
        exit 23
        """;

    private const string RootExitWithChildScript = """
        $childScript = '$gate=[System.Threading.ManualResetEventSlim]::new($false);$gate.Wait()'
        $null = Start-Process -FilePath 'pwsh' -ArgumentList @('-NoProfile','-NonInteractive','-Command',$childScript) -NoNewWindow
        Set-Content -LiteralPath $env:LGYM_OWNED_READY_FILE -Value 'ready'
        Start-Sleep -Milliseconds 300
        exit 7
        """;

    private const string ForeignBlockingScript = """
        $gate = [System.Threading.ManualResetEventSlim]::new($false)
        $gate.Wait()
        """;

    private readonly string _directory;
    private readonly string _readyFile;

    internal OwnedExternalProcessFixture()
    {
        _directory = Directory.CreateTempSubdirectory("lgym-owned-process-").FullName;
        _readyFile = Path.Combine(_directory, "ready.signal");
    }

    internal ExternalProcessRequest CreateBlockingTreeRequest(
        string wholeSecret,
        string splitSecret,
        TimeSpan? shutdownTimeout = null) =>
        CreateRequest(BlockingTreeScript, wholeSecret, splitSecret, shutdownTimeout ?? TimeSpan.FromSeconds(5));

    internal ExternalProcessRequest CreateEarlyExitRequest() =>
        CreateRequest(EarlyExitScript, string.Empty, string.Empty, TimeSpan.FromSeconds(5));

    internal ExternalProcessRequest CreateRootExitWithChildRequest() =>
        CreateRequest(RootExitWithChildScript, string.Empty, string.Empty, TimeSpan.FromMilliseconds(400));

    internal async Task WaitUntilReadyAsync(Task processExit, CancellationToken cancellationToken)
    {
        while (!File.Exists(_readyFile))
        {
            if (processExit.IsCompleted)
            {
                await processExit;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
    }

    internal Process StartForeignProcess()
    {
        var request = CreateRequest(ForeignBlockingScript, string.Empty, string.Empty, TimeSpan.FromSeconds(5));
        return Process.Start(ExternalProcessRunner.CreateStartInfo(request))
            ?? throw new InvalidOperationException("Could not start the owned foreign-process fixture.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private ExternalProcessRequest CreateRequest(
        string script,
        string wholeSecret,
        string splitSecret,
        TimeSpan shutdownTimeout) =>
        new()
        {
            FileName = "pwsh",
            Arguments = ["-NoProfile", "-NonInteractive", "-Command", script],
            WorkingDirectory = _directory,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                [ReadyFileVariable] = _readyFile,
                [WholeSecretVariable] = wholeSecret,
                [SplitSecretVariable] = splitSecret
            },
            SecretCanaries = [wholeSecret, splitSecret],
            ExecutionTimeout = TimeSpan.FromMilliseconds(100),
            ShutdownTimeout = shutdownTimeout
        };
}
