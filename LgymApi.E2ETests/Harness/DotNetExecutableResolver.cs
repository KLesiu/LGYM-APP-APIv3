namespace LgymApi.E2ETests.Harness;

internal static class DotNetExecutableResolver
{
    internal const string PrerequisiteMessage =
        "API publication prerequisite failed: an absolute dotnet.exe executable is unavailable.";

    internal static string Resolve(
        Func<string, string?>? getEnvironmentVariable = null,
        Func<string, bool>? fileExists = null)
    {
        var getValue = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        var exists = fileExists ?? File.Exists;
        var candidates = new[]
        {
            getValue("DOTNET_HOST_PATH"),
            CombineWithExecutable(getValue("DOTNET_ROOT")),
            CombineWithDotNetDirectory(getValue("ProgramFiles"))
        };

        foreach (var candidate in candidates)
        {
            if (IsAbsoluteDotNetExecutable(candidate, exists))
            {
                return Path.GetFullPath(candidate!);
            }
        }

        throw new InvalidOperationException(PrerequisiteMessage);
    }

    private static string? CombineWithExecutable(string? directory) =>
        string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)
            ? null
            : Path.Combine(directory, "dotnet.exe");

    private static string? CombineWithDotNetDirectory(string? programFiles) =>
        string.IsNullOrWhiteSpace(programFiles) || !Path.IsPathFullyQualified(programFiles)
            ? null
            : Path.Combine(programFiles, "dotnet", "dotnet.exe");

    private static bool IsAbsoluteDotNetExecutable(string? candidate, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(candidate) ||
            !Path.IsPathFullyQualified(candidate) ||
            !string.Equals(Path.GetFileName(candidate), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return fileExists(Path.GetFullPath(candidate));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
