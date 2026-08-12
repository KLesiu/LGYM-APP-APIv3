using System.Security.Cryptography;

namespace LgymApi.E2ETests.Harness;

internal sealed record ApiPublicationProcessReceipt(
    int ExitCode,
    bool StandardOutputWasTruncated,
    bool StandardErrorWasTruncated);

internal sealed record ApiPublicationReceipt(
    string CommandName,
    string DllSha256,
    DateTimeOffset CompletedAtUtc,
    string ApiRepositoryHeadSha,
    bool RepositoryIsDirty,
    ApiPublicationProcessReceipt Process);

internal sealed class ApiPublication
{
    internal const string RequiredArtifactMessage =
        "API publication artifact validation failed: a required artifact is missing.";
    internal const string IntegrityMessage = "API publication integrity validation failed.";
    internal const string LaunchCommandMessage = "API publication launch command validation failed.";

    private readonly ApiPublicationLayout _layout;

    internal ApiPublication(ApiPublicationLayout layout, ApiPublicationReceipt receipt)
    {
        _layout = layout;
        Receipt = receipt;
    }

    internal ApiPublicationReceipt Receipt { get; }

    internal string PublicationDirectory => _layout.PublicationDirectory;

    internal string DllPath => _layout.DllPath;

    internal string DependenciesPath => _layout.DependenciesPath;

    internal string RuntimeConfigurationPath => _layout.RuntimeConfigurationPath;

    internal void ValidateBeforeLaunch(ExternalProcessRequest request)
    {
        if (!IsValidLaunchCommand(request))
        {
            throw new InvalidOperationException(LaunchCommandMessage);
        }

        _layout.EnsureRequiredArtifacts();
        var currentHash = ComputeDllHash(DllPath);
        if (!string.Equals(currentHash, Receipt.DllSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(IntegrityMessage);
        }
    }

    internal static string ComputeDllHash(string dllPath)
    {
        try
        {
            using var dll = File.OpenRead(dllPath);
            return Convert.ToHexString(SHA256.HashData(dll)).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(IntegrityMessage);
        }
    }

    public override string ToString() => "<verified-api-publication>";

    private static bool IsForbiddenArgument(string argument) =>
        string.Equals(argument, "run", StringComparison.OrdinalIgnoreCase) ||
        argument.StartsWith("--launch-profile", StringComparison.OrdinalIgnoreCase);

    private bool IsValidLaunchCommand(ExternalProcessRequest request)
    {
        try
        {
            return Path.IsPathFullyQualified(request.FileName) &&
                   string.Equals(Path.GetFileName(request.FileName), "dotnet.exe", StringComparison.OrdinalIgnoreCase) &&
                   !request.Arguments.Any(IsForbiddenArgument) &&
                   request.Arguments.Count == 1 &&
                   Path.IsPathFullyQualified(request.Arguments[0]) &&
                   PathsEqual(request.Arguments[0], DllPath) &&
                   Path.IsPathFullyQualified(request.WorkingDirectory) &&
                   PathsEqual(request.WorkingDirectory, PublicationDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
