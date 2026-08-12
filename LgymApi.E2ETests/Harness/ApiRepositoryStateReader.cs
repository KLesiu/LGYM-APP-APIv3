using System.Text.RegularExpressions;

namespace LgymApi.E2ETests.Harness;

internal sealed record ApiRepositoryState(string HeadSha, bool IsDirty);

internal sealed record ApiRepositoryStateTimeouts(TimeSpan Execution, TimeSpan Shutdown);

internal sealed class ApiRepositoryStateReader(
    ExternalProcessRunner processRunner,
    string gitExecutable)
{
    private const string MetadataFailureMessage = "API publication repository evidence could not be captured.";
    private static readonly Regex HeadPattern = new("\\A[0-9a-f]{40}\\z", RegexOptions.CultureInvariant);

    internal async Task<ApiRepositoryState> ReadAsync(
        string repositoryRoot,
        ApiRepositoryStateTimeouts timeouts,
        CancellationToken cancellationToken = default)
    {
        var headResult = await processRunner.RunAsync(
            CreateRequest(repositoryRoot, ["--no-optional-locks", "rev-parse", "HEAD"], timeouts),
            cancellationToken);
        if (headResult.ExitCode != 0 || headResult.StandardOutput.WasTruncated)
        {
            throw new InvalidOperationException(MetadataFailureMessage);
        }

        var headSha = headResult.StandardOutput.Tail.Trim();
        if (!HeadPattern.IsMatch(headSha))
        {
            throw new InvalidOperationException(MetadataFailureMessage);
        }

        var statusResult = await processRunner.RunAsync(
            CreateRequest(
                repositoryRoot,
                ["--no-optional-locks", "status", "--porcelain=v1", "--untracked-files=all"],
                timeouts),
            cancellationToken);
        if (statusResult.ExitCode != 0)
        {
            throw new InvalidOperationException(MetadataFailureMessage);
        }

        return new ApiRepositoryState(
            headSha,
            statusResult.StandardOutput.WasTruncated ||
            !string.IsNullOrWhiteSpace(statusResult.StandardOutput.Tail));
    }

    internal static string ResolveGitExecutable(
        Func<string, string?>? getEnvironmentVariable = null,
        Func<string, bool>? fileExists = null)
    {
        var getValue = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        var exists = fileExists ?? File.Exists;
        var candidates = (getValue("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => Path.Combine(path.Trim('"'), "git.exe"))
            .Append(Path.Combine(getValue("ProgramFiles") ?? string.Empty, "Git", "cmd", "git.exe"));

        foreach (var candidate in candidates)
        {
            if (Path.IsPathFullyQualified(candidate) && exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new InvalidOperationException(MetadataFailureMessage);
    }

    private ExternalProcessRequest CreateRequest(
        string repositoryRoot,
        IReadOnlyList<string> arguments,
        ApiRepositoryStateTimeouts timeouts) =>
        new()
        {
            FileName = gitExecutable,
            Arguments = arguments,
            WorkingDirectory = repositoryRoot,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["GIT_OPTIONAL_LOCKS"] = "0",
                ["GIT_TERMINAL_PROMPT"] = "0"
            },
            SecretCanaries = [repositoryRoot],
            ExecutionTimeout = timeouts.Execution,
            ShutdownTimeout = timeouts.Shutdown
        };
}
