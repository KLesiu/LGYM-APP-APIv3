using LgymApi.E2ETests.Configuration;

namespace LgymApi.E2ETests.Harness;

internal sealed record NodeNpmTools(string NodeExecutable, string NpmCliScript)
{
    public override string ToString() => "<node-npm-tools>";
}

internal interface INodeNpmToolResolver
{
    NodeNpmTools Resolve();
}

internal sealed class NodeNpmToolResolver(Func<string?>? nodeDirectory = null, Func<string, bool>? fileExists = null)
    : INodeNpmToolResolver
{
    internal const string PrerequisiteMessage = "E2E Node and npm prerequisites are unavailable.";

    private readonly Func<string?> _nodeDirectory = nodeDirectory ?? ResolveNodeDirectory;
    private readonly Func<string, bool> _fileExists = fileExists ?? File.Exists;

    public NodeNpmTools Resolve()
    {
        var directory = _nodeDirectory();
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
        {
            throw new InvalidOperationException(PrerequisiteMessage);
        }

        var nodeExecutable = Path.GetFullPath(Path.Combine(directory, "node.exe"));
        var npmCliScript = Path.GetFullPath(Path.Combine(directory, "node_modules", "npm", "bin", "npm-cli.js"));
        if (!_fileExists(nodeExecutable) || !_fileExists(npmCliScript))
        {
            throw new InvalidOperationException(PrerequisiteMessage);
        }

        return new NodeNpmTools(nodeExecutable, npmCliScript);
    }

    private static string? ResolveNodeDirectory()
    {
        var nodeFromPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => path.Trim('"'))
            .FirstOrDefault(path => File.Exists(Path.Combine(path, "node.exe")));
        return nodeFromPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs");
    }
}

internal interface INodeNpmCommandRunner
{
    Task<ExternalProcessResult> RunAsync(ExternalProcessRequest request, CancellationToken cancellationToken);
}

internal sealed class NodeNpmCommandRunner : INodeNpmCommandRunner
{
    private readonly ExternalProcessRunner _runner = new();

    public Task<ExternalProcessResult> RunAsync(ExternalProcessRequest request, CancellationToken cancellationToken) =>
        _runner.RunAsync(request, cancellationToken);
}

internal interface IWebSourceStager
{
    Task<PinnedWebSourceStage> StageAsync(
        E2EOptions options,
        PrivateRunDirectoryLease runLease,
        CancellationToken cancellationToken);

    Task<PinnedWebSourceStage> StageForLifecycleAsync(
        E2EOptions options,
        PrivateRunDirectoryLease runLease,
        CancellationToken cancellationToken) =>
        StageAsync(options, runLease, cancellationToken);
}

internal sealed class WebSourceStager(string gitExecutable) : IWebSourceStager
{
    private readonly PinnedWebSourceStager _inner = new(gitExecutable);

    public Task<PinnedWebSourceStage> StageAsync(
        E2EOptions options,
        PrivateRunDirectoryLease runLease,
        CancellationToken cancellationToken) =>
        _inner.StageAsync(options, runLease, cancellationToken);

    public Task<PinnedWebSourceStage> StageForLifecycleAsync(
        E2EOptions options,
        PrivateRunDirectoryLease runLease,
        CancellationToken cancellationToken) =>
        _inner.StageAsync(new PinnedWebSourceRequest(
            options.WebSource.SourcePath ?? string.Empty,
            options.WebSource.CommitSha,
            runLease,
            TimeSpan.FromSeconds(options.Timeouts.WebStartupSeconds),
            TimeSpan.FromSeconds(options.Timeouts.ProcessShutdownSeconds),
            DisposeRunLeaseOnFailure: false), cancellationToken);
}
