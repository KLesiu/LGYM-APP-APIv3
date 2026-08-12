namespace LgymApi.E2ETests.Harness;

internal sealed class ApiPublicationLayout
{
    internal const string PathValidationMessage = "API publication path validation failed.";
    internal const string DllFileName = "LgymApi.Api.dll";
    internal const string DependenciesFileName = "LgymApi.Api.deps.json";
    internal const string RuntimeConfigurationFileName = "LgymApi.Api.runtimeconfig.json";

    private ApiPublicationLayout(
        string repositoryRoot,
        string publicationDirectory,
        string dllPath)
    {
        RepositoryRoot = repositoryRoot;
        PublicationDirectory = publicationDirectory;
        DllPath = dllPath;
    }

    internal string RepositoryRoot { get; }

    internal string PublicationDirectory { get; }

    internal string DllPath { get; }

    internal string DependenciesPath => Path.Combine(PublicationDirectory, DependenciesFileName);

    internal string RuntimeConfigurationPath => Path.Combine(PublicationDirectory, RuntimeConfigurationFileName);

    internal string ApiProjectPath => Path.Combine(RepositoryRoot, "LgymApi.Api", "LgymApi.Api.csproj");

    internal static ApiPublicationLayout Resolve(string repositoryRoot, string configuredDllPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(configuredDllPath) ||
                Path.IsPathRooted(configuredDllPath) ||
                Path.IsPathFullyQualified(configuredDllPath))
            {
                throw new InvalidOperationException(PathValidationMessage);
            }

            var segments = configuredDllPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3 ||
                !string.Equals(segments[0], ".e2e-private", StringComparison.Ordinal) ||
                segments.Any(segment => segment is "." or "..") ||
                !string.Equals(segments[^1], DllFileName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(PathValidationMessage);
            }

            var normalizedRepositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
            var privateRoot = Path.Combine(normalizedRepositoryRoot, ".e2e-private");
            var dllPath = Path.GetFullPath(Path.Combine(normalizedRepositoryRoot, configuredDllPath));
            var publicationDirectory = Path.GetDirectoryName(dllPath);
            if (publicationDirectory is null || !IsStrictDescendant(privateRoot, publicationDirectory))
            {
                throw new InvalidOperationException(PathValidationMessage);
            }

            var layout = new ApiPublicationLayout(
                normalizedRepositoryRoot,
                publicationDirectory,
                dllPath);
            layout.EnsureNoReparsePoints();
            if (!File.Exists(layout.ApiProjectPath))
            {
                throw new InvalidOperationException(PathValidationMessage);
            }

            return layout;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(PathValidationMessage);
        }
    }

    internal void CleanAndCreatePublicationDirectory()
    {
        EnsureNoReparsePoints();
        if (Directory.Exists(PublicationDirectory))
        {
            Directory.Delete(PublicationDirectory, recursive: true);
        }

        Directory.CreateDirectory(PublicationDirectory);
        EnsureNoReparsePoints();
    }

    internal void EnsureRequiredArtifacts()
    {
        EnsureNoReparsePoints();
        if (!File.Exists(DllPath) ||
            !File.Exists(DependenciesPath) ||
            !File.Exists(RuntimeConfigurationPath))
        {
            throw new InvalidOperationException(ApiPublication.RequiredArtifactMessage);
        }
    }

    internal void EnsureNoReparsePoints()
    {
        var relativePath = Path.GetRelativePath(RepositoryRoot, PublicationDirectory);
        var currentPath = RepositoryRoot;
        EnsureNotReparsePoint(currentPath);
        foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            EnsureNotReparsePoint(currentPath);
        }

        EnsureNotReparsePoint(DllPath);
        EnsureNotReparsePoint(DependenciesPath);
        EnsureNotReparsePoint(RuntimeConfigurationPath);
    }

    private static bool IsStrictDescendant(string parentPath, string candidatePath)
    {
        var relativePath = Path.GetRelativePath(parentPath, candidatePath);
        return !Path.IsPathRooted(relativePath) &&
               relativePath is not "." &&
               relativePath != ".." &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static void EnsureNotReparsePoint(string path)
    {
        try
        {
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                return;
            }

            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(PathValidationMessage);
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(PathValidationMessage);
        }
    }
}
