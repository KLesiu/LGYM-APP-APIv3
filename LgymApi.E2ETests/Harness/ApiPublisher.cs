using LgymApi.E2ETests.Configuration;

namespace LgymApi.E2ETests.Harness;

internal sealed class ApiPublisher
{
    internal const string PublicationFailureMessage = "API publication command failed.";
    private readonly ExternalProcessRunner _processRunner;
    private readonly Func<ExternalProcessRequest, CancellationToken, Task<ExternalProcessResult>> _runPublication;

    internal ApiPublisher(
        ExternalProcessRunner? processRunner = null,
        Func<ExternalProcessRequest, CancellationToken, Task<ExternalProcessResult>>? runPublication = null)
    {
        _processRunner = processRunner ?? new ExternalProcessRunner();
        _runPublication = runPublication ?? _processRunner.RunAsync;
    }

    internal async Task<ApiPublication> PublishAsync(
        E2EOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var repositoryRoot = RepositoryRoot.Find();
        var dotnetExecutable = DotNetExecutableResolver.Resolve();
        var layout = ApiPublicationLayout.Resolve(repositoryRoot, options.Api.PublishedDllPath);
        var gitExecutable = ApiRepositoryStateReader.ResolveGitExecutable();
        layout.CleanAndCreatePublicationDirectory();

        try
        {
            var request = CreatePublishRequest(dotnetExecutable, layout, options);
            var result = await _runPublication(request, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(PublicationFailureMessage);
            }

            layout.EnsureRequiredArtifacts();
            var dllHash = ApiPublication.ComputeDllHash(layout.DllPath);
            var gitTimeout = TimeSpan.FromSeconds(Math.Min(options.Timeouts.ApiPublishSeconds, 30));
            var shutdownTimeout = TimeSpan.FromSeconds(options.Timeouts.ProcessShutdownSeconds);
            var repositoryState = await new ApiRepositoryStateReader(_processRunner, gitExecutable).ReadAsync(
                repositoryRoot,
                new ApiRepositoryStateTimeouts(gitTimeout, shutdownTimeout),
                cancellationToken);
            var receipt = new ApiPublicationReceipt(
                "publish",
                dllHash,
                DateTimeOffset.UtcNow,
                repositoryState.HeadSha,
                repositoryState.IsDirty,
                new ApiPublicationProcessReceipt(
                    result.ExitCode,
                    result.StandardOutput.WasTruncated,
                    result.StandardError.WasTruncated));
            return new ApiPublication(layout, receipt);
        }
        catch
        {
            CleanupFailedPublication(layout);
            throw;
        }
    }

    internal static ExternalProcessRequest CreatePublishRequest(
        string dotnetExecutable,
        ApiPublicationLayout layout,
        E2EOptions options)
    {
        if (!Path.IsPathFullyQualified(dotnetExecutable) ||
            !string.Equals(Path.GetFileName(dotnetExecutable), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(DotNetExecutableResolver.PrerequisiteMessage);
        }

        var relativeProjectPath = Path.GetRelativePath(layout.RepositoryRoot, layout.ApiProjectPath);
        var relativeOutputPath = Path.GetRelativePath(layout.RepositoryRoot, layout.PublicationDirectory);
        return new ExternalProcessRequest
        {
            FileName = dotnetExecutable,
            Arguments =
            [
                "publish",
                relativeProjectPath,
                "--configuration",
                "Release",
                "--output",
                relativeOutputPath,
                "--disable-build-servers"
            ],
            WorkingDirectory = layout.RepositoryRoot,
            SecretCanaries =
            [
                layout.RepositoryRoot,
                layout.PublicationDirectory,
                layout.DllPath
            ],
            ExecutionTimeout = TimeSpan.FromSeconds(options.Timeouts.ApiPublishSeconds),
            ShutdownTimeout = TimeSpan.FromSeconds(options.Timeouts.ProcessShutdownSeconds)
        };
    }

    private static void CleanupFailedPublication(ApiPublicationLayout layout)
    {
        layout.EnsureNoReparsePoints();
        if (Directory.Exists(layout.PublicationDirectory))
        {
            Directory.Delete(layout.PublicationDirectory, recursive: true);
        }
    }
}
