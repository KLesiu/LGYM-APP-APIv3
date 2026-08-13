using LgymApi.E2ETests.Configuration;

namespace LgymApi.E2ETests.Harness;

internal sealed class Task3WebSourceRunFixture : IAsyncDisposable
{
    private Task3WebSourceRunFixture(string root)
    {
        Root = root;
        OwnerRoot = Path.Combine(root, "owner");
        NodeDirectory = Path.Combine(root, "tools", "node");
        NodeExecutable = Path.Combine(NodeDirectory, "node.exe");
        NpmCliScript = Path.Combine(NodeDirectory, "node_modules", "npm", "bin", "npm-cli.js");
        GitExecutable = Path.Combine(root, "tools", "git", "git.exe");
    }

    internal string Root { get; }

    internal string OwnerRoot { get; }

    internal string NodeDirectory { get; }

    internal string NodeExecutable { get; }

    internal string NpmCliScript { get; }

    internal string GitExecutable { get; }

    internal static Task<Task3WebSourceRunFixture> CreateAsync()
    {
        var fixture = new Task3WebSourceRunFixture(
            Directory.CreateTempSubdirectory("lgym-e2e-task3-").FullName);
        Directory.CreateDirectory(fixture.OwnerRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.NpmCliScript)!);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.GitExecutable)!);
        File.WriteAllText(fixture.NodeExecutable, string.Empty);
        File.WriteAllText(fixture.NpmCliScript, string.Empty);
        File.WriteAllText(fixture.GitExecutable, string.Empty);
        return Task.FromResult(fixture);
    }

    internal WebSourceRunRequest CreateRequest(IReadOnlyList<string>? secretCanaries = null) =>
        new(
            OwnerRoot,
            new E2EOptions
            {
                WebSource = new E2EWebSourceOptions
                {
                    SourcePath = Path.Combine(Root, "external-source"),
                    CommitSha = "1111111111111111111111111111111111111111"
                },
                Runtime = new E2ERuntimeOptions { PrivateRunRoot = ".e2e-private/runs" },
                Timeouts = new E2ETimeoutsOptions
                {
                    ProcessShutdownSeconds = 2,
                    TestSessionSeconds = 30,
                    WebStartupSeconds = 1
                }
            },
            GitExecutable,
            secretCanaries ?? []);

    internal INodeNpmToolResolver CreateToolResolver() =>
        new NodeNpmToolResolver(() => NodeDirectory, File.Exists);

    public ValueTask DisposeAsync()
    {
        Directory.Delete(Root, recursive: true);
        return ValueTask.CompletedTask;
    }
}

internal sealed class Task3WebSourceStager : IWebSourceStager
{
    internal int StageCount { get; private set; }

    public Task<PinnedWebSourceStage> StageAsync(
        E2EOptions options,
        PrivateRunDirectoryLease runLease,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StageCount++;
        var sourceDirectory = runLease.ResolveWebOwnedPath("web-source");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "package-lock.json"), "{}\n");
        return Task.FromResult(new PinnedWebSourceStage(
            sourceDirectory,
            new PinnedWebSourceReceipt("1111111111111111111111111111111111111111", true, 1, "digest", true, true)));
    }
}

internal sealed class Task3NodeNpmCommandRunner : INodeNpmCommandRunner
{
    private readonly object _sync = new();
    private readonly List<ExternalProcessRequest> _requests = [];

    internal string VersionOutput { get; init; } = "v22.18.0\n";

    internal int NpmExitCode { get; init; }

    internal bool WaitForNpmCancellation { get; init; }

    internal string NpmOutput { get; init; } = string.Empty;

    internal TaskCompletionSource NpmStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal int NpmInvocationCount { get; private set; }

    internal IReadOnlyList<ExternalProcessRequest> Requests
    {
        get
        {
            lock (_sync)
            {
                return _requests.ToArray();
            }
        }
    }

    public async Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _requests.Add(request);
        }

        if (request.Arguments.SequenceEqual(["--version"]))
        {
            return Result(0, VersionOutput);
        }

        NpmInvocationCount++;
        NpmStarted.TrySetResult();
        if (WaitForNpmCancellation)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return Result(NpmExitCode, NpmOutput);
    }

    private static ExternalProcessResult Result(int exitCode, string output) =>
        new(exitCode, new ExternalProcessOutput(output, false), new ExternalProcessOutput(string.Empty, false));
}

internal sealed class Task3FailingCacheCleaner : IWebSourceCacheCleaner
{
    internal TimeSpan? ObservedTimeout { get; private set; }

    public Task DeleteAsync(PrivateRunDirectoryLease runLease, TimeSpan timeout)
    {
        ObservedTimeout = timeout;
        throw new IOException("task-3-test-cleanup-failure");
    }
}
