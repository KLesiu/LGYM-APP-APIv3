using LgymApi.E2ETests.Configuration;

namespace LgymApi.E2ETests.Harness;

internal interface IWebSourceCacheCleaner
{
    Task DeleteAsync(PrivateRunDirectoryLease runLease, TimeSpan timeout);
}

internal sealed class WebSourceCacheCleaner : IWebSourceCacheCleaner
{
    public async Task DeleteAsync(PrivateRunDirectoryLease runLease, TimeSpan timeout)
    {
        string cacheDirectory;
        try
        {
            cacheDirectory = runLease.ResolveCacheOwnedPath(".e2e-private/npm-cache");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PrivateRunCleanupException(PrivateRunCleanupStage.CachePathValidation);
        }
        if (!Directory.Exists(cacheDirectory))
        {
            return;
        }

        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await DeleteDirectoryAsync(cacheDirectory, cancellation.Token);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PrivateRunCleanupException(PrivateRunCleanupStage.CacheDelete);
        }
    }

    private static async Task DeleteDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0)
            {
                await DeleteDirectoryAsync(entry, cancellationToken);
            }
            else if ((attributes & FileAttributes.Directory) != 0)
            {
                Directory.Delete(entry);
            }
            else
            {
                File.Delete(entry);
            }

            await Task.Yield();
        }

        Directory.Delete(directory);
    }
}

internal sealed record WebSourceRunRequest(
    string RepositoryRoot,
    E2EOptions Options,
    string GitExecutable,
    IReadOnlyList<string> SecretCanaries);

internal sealed class WebSourceRunDependencies
{
    public required IWebSourceStager Stager { get; init; }

    public required INodeNpmToolResolver ToolResolver { get; init; }

    public required INodeNpmCommandRunner CommandRunner { get; init; }

    public IWebSourceCacheCleaner CacheCleaner { get; init; } = new WebSourceCacheCleaner();

    public IRunDirectoryCleaner RunDirectoryCleaner { get; init; } = new FileSystemRunDirectoryCleaner();
}
