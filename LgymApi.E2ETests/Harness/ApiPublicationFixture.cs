using LgymApi.E2ETests.Configuration;

namespace LgymApi.E2ETests.Harness;

internal sealed class ApiPublicationFixture : IDisposable
{
    private readonly string _copiesRoot;

    private ApiPublicationFixture(
        string repositoryRoot,
        E2EOptions options,
        ApiPublication publication)
    {
        RepositoryRoot = repositoryRoot;
        Options = options;
        DotNetExecutable = DotNetExecutableResolver.Resolve();
        Publication = publication;
        StaleMarkerPath = Path.Combine(publication.PublicationDirectory, "stale.marker");
        _copiesRoot = Path.Combine(repositoryRoot, ".e2e-private", "task3-fixtures");
    }

    internal string RepositoryRoot { get; }

    internal E2EOptions Options { get; }

    internal string DotNetExecutable { get; }

    internal ApiPublication Publication { get; }

    internal string StaleMarkerPath { get; }

    internal static async Task<ApiPublicationFixture> CreateAsync()
    {
        var repositoryRoot = LgymApi.E2ETests.Harness.RepositoryRoot.Find();
        var options = E2EConfiguration.Load(TestContext.CurrentContext.TestDirectory, repositoryRoot);
        var layout = ApiPublicationLayout.Resolve(repositoryRoot, options.Api.PublishedDllPath);
        Directory.CreateDirectory(layout.PublicationDirectory);
        var staleMarkerPath = Path.Combine(layout.PublicationDirectory, "stale.marker");
        File.WriteAllText(staleMarkerPath, "stale");

        var publication = await new ApiPublisher().PublishAsync(options);
        return new ApiPublicationFixture(
            repositoryRoot,
            options,
            publication);
    }

    internal ApiPublication CopyPublication()
    {
        Directory.CreateDirectory(_copiesRoot);
        var copyDirectory = Path.Combine(_copiesRoot, Guid.NewGuid().ToString("N"));
        CopyDirectory(Publication.PublicationDirectory, copyDirectory);
        var relativeDllPath = Path.GetRelativePath(
            RepositoryRoot,
            Path.Combine(copyDirectory, ApiPublicationLayout.DllFileName));
        var layout = ApiPublicationLayout.Resolve(RepositoryRoot, relativeDllPath);
        return new ApiPublication(layout, Publication.Receipt);
    }

    internal ExternalProcessRequest CreateLaunchRequest(ApiPublication publication) =>
        new()
        {
            FileName = DotNetExecutable,
            Arguments = [publication.DllPath],
            WorkingDirectory = publication.PublicationDirectory,
            ExecutionTimeout = TimeSpan.FromSeconds(Options.Timeouts.ApiStartupSeconds),
            ShutdownTimeout = TimeSpan.FromSeconds(Options.Timeouts.ProcessShutdownSeconds)
        };

    public void Dispose()
    {
        if (Directory.Exists(_copiesRoot))
        {
            Directory.Delete(_copiesRoot, recursive: true);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var destinationFile = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile);
        }
    }
}
