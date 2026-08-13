using System.Text.RegularExpressions;
using LgymApi.E2ETests.Configuration;

namespace LgymApi.E2ETests.Harness;

internal sealed class WebSourceRunLease : IAsyncDisposable
{
    internal const string NodePrerequisiteMessage = "E2E requires stable Node 22.18.0 or later.";
    internal const string InstallationMessage = "E2E private npm installation failed.";
    internal const string CleanupMessage = "E2E web source cleanup failed.";
    private static readonly Regex StableNodeVersion = new("\\Av(\\d+)\\.(\\d+)\\.(\\d+)\\r?\\n?\\z", RegexOptions.CultureInvariant);
    private readonly PrivateRunDirectoryLease _runLease;
    private readonly NodeNpmTools _tools;
    private readonly WebSourceRunDependencies _dependencies;
    private readonly IReadOnlyList<string> _secretCanaries;
    private readonly WebSourceRunEnvironment _environment;
    private readonly TimeSpan _sessionTimeout;
    private readonly TimeSpan _shutdownTimeout;
    private readonly DateTime _sessionDeadlineUtc;
    private readonly object _sync = new();
    private Task? _installation;
    private Task? _cleanup;

    private WebSourceRunLease(
        PrivateRunDirectoryLease runLease,
        PinnedWebSourceStage stage,
        NodeNpmTools tools,
        WebSourceRunDependencies dependencies,
        IReadOnlyList<string> secretCanaries,
        string gitExecutable,
        TimeSpan sessionTimeout,
        TimeSpan shutdownTimeout)
    {
        _runLease = runLease;
        SourceDirectory = stage.SourceDirectory;
        _tools = tools;
        _dependencies = dependencies;
        _secretCanaries = secretCanaries;
        _environment = new WebSourceRunEnvironment(tools, gitExecutable);
        _sessionTimeout = sessionTimeout;
        _shutdownTimeout = shutdownTimeout;
        _sessionDeadlineUtc = DateTime.UtcNow.Add(sessionTimeout);
        NpmCacheDirectory = runLease.ResolveCacheOwnedPath(".e2e-private/npm-cache");
    }

    internal string RunDirectory => _runLease.RunDirectory;

    internal string SourceDirectory { get; }

    internal string NpmCacheDirectory { get; }

    internal string NodeExecutable => _tools.NodeExecutable;

    internal string NpmCliScript => _tools.NpmCliScript;

    internal bool IsInstalled => _installation?.IsCompletedSuccessfully == true;

    internal PrivateRunCleanupStage CleanupStage { get; private set; }

    internal Dictionary<string, string?> CreateExpoEnvironment(Uri scenarioApiBaseUri)
    {
        var environment = _environment.Create(RunDirectory, NpmCacheDirectory);
        environment["EXPO_NO_TELEMETRY"] = "1";
        environment["BROWSER"] = "none";
        environment["REACT_APP_BACKEND"] = scenarioApiBaseUri.AbsoluteUri;
        return environment;
    }

    internal static async Task<WebSourceRunLease> CreateAsync(
        WebSourceRunRequest request,
        WebSourceRunDependencies dependencies,
        CancellationToken cancellationToken = default)
    {
        var sessionTimeout = TimeSpan.FromSeconds(request.Options.Timeouts.TestSessionSeconds);
        var shutdownTimeout = TimeSpan.FromSeconds(request.Options.Timeouts.ProcessShutdownSeconds);
        if (sessionTimeout <= TimeSpan.Zero || shutdownTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(InstallationMessage);
        }

        var runLease = PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            request.RepositoryRoot,
            request.Options.Runtime.PrivateRunRoot,
            shutdownTimeout), dependencies.RunDirectoryCleaner);
        try
        {
            var stage = await dependencies.Stager.StageAsync(request.Options, runLease, cancellationToken);
            return new WebSourceRunLease(
                runLease,
                stage,
                dependencies.ToolResolver.Resolve(),
                dependencies,
                request.SecretCanaries,
                request.GitExecutable,
                sessionTimeout,
                shutdownTimeout);
        }
        catch
        {
            await runLease.DisposeAsync();
            throw;
        }
    }

    internal Task EnsureInstalledAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return _installation ??= InstallAsync(cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _cleanup ??= CleanupAsync();
            return new ValueTask(_cleanup);
        }
    }

    private async Task InstallAsync(CancellationToken cancellationToken)
    {
        try
        {
            var remainingSession = _sessionDeadlineUtc - DateTime.UtcNow;
            if (remainingSession <= TimeSpan.Zero)
            {
                throw new InvalidOperationException(InstallationMessage);
            }

            using var session = new CancellationTokenSource(remainingSession);
            using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Token);
            var version = await _dependencies.CommandRunner.RunAsync(CreateRequest(["--version"]), execution.Token);
            if (version.ExitCode != 0 || !IsSupported(version.StandardOutput.Tail))
            {
                throw new InvalidOperationException(NodePrerequisiteMessage);
            }

            Directory.CreateDirectory(NpmCacheDirectory);
            var install = await _dependencies.CommandRunner.RunAsync(CreateRequest([_tools.NpmCliScript, "ci"]), execution.Token);
            if (install.ExitCode != 0)
            {
                throw new InvalidOperationException(InstallationMessage);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DisposeAsync();
            throw;
        }
        catch (OperationCanceledException)
        {
            await DisposeAsync();
            throw new InvalidOperationException(InstallationMessage);
        }
        catch (InvalidOperationException exception) when (exception.Message is NodePrerequisiteMessage or InstallationMessage)
        {
            await DisposeAsync();
            throw;
        }
        catch
        {
            await DisposeAsync();
            throw new InvalidOperationException(InstallationMessage);
        }
    }

    private ExternalProcessRequest CreateRequest(IReadOnlyList<string> arguments) =>
        new()
        {
            FileName = _tools.NodeExecutable,
            Arguments = arguments,
            WorkingDirectory = SourceDirectory,
            EnvironmentVariables = _environment.Create(RunDirectory, NpmCacheDirectory),
            ClearEnvironment = true,
            SecretCanaries = _secretCanaries,
            ExecutionTimeout = _sessionTimeout,
            ShutdownTimeout = _shutdownTimeout
        };

    private async Task CleanupAsync()
    {
        Exception? failure = null;
        try
        {
            await _dependencies.CacheCleaner.DeleteAsync(_runLease, _shutdownTimeout);
        }
        catch (PrivateRunCleanupException exception)
        {
            CleanupStage = exception.Stage;
            failure = exception;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            CleanupStage = PrivateRunCleanupStage.CacheDelete;
            failure = exception;
        }

        try
        {
            await _runLease.DisposeAsync();
        }
        catch (PrivateRunCleanupException exception)
        {
            CleanupStage = CleanupStage == PrivateRunCleanupStage.Unknown ? exception.Stage : CleanupStage;
            failure ??= new IOException();
        }
        catch (InvalidOperationException)
        {
            CleanupStage = CleanupStage == PrivateRunCleanupStage.Unknown
                ? PrivateRunCleanupStage.Unknown
                : CleanupStage;
            failure ??= new IOException();
        }

        if (failure is not null)
        {
            throw new WebSourceRunCleanupException(CleanupStage);
        }
    }

    private static bool IsSupported(string output)
    {
        var match = StableNodeVersion.Match(output);
        return match.Success &&
               (int.Parse(match.Groups[1].Value) > 22 ||
                int.Parse(match.Groups[1].Value) == 22 &&
                (int.Parse(match.Groups[2].Value) > 18 ||
                 int.Parse(match.Groups[2].Value) == 18 && int.Parse(match.Groups[3].Value) >= 0));
    }
}

internal sealed class WebSourceRunCleanupException(PrivateRunCleanupStage stage) : InvalidOperationException(WebSourceRunLease.CleanupMessage)
{
    internal PrivateRunCleanupStage Stage { get; } = stage;
}
