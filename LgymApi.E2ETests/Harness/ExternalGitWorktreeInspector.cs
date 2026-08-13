using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LgymApi.E2ETests.Harness;

internal sealed class ExternalGitWorktreeInspector(IExternalGitCommandRunner git)
{
    internal const string SourceValidationMessage = "The configured web source is not the required Git worktree.";
    internal const string SourceChangedMessage = "The external web source changed during staging.";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex Sha1Pattern = new("\\A[0-9a-f]{40}\\z", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new("\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant);

    internal async Task<ExternalGitWorktree> InspectAsync(
        string sourcePath,
        string pin,
        ExternalGitCommandTimeouts timeouts,
        CancellationToken cancellationToken)
    {
        var normalizedSourcePath = NormalizeSourcePath(sourcePath);
        var topLevel = await ReadTextAsync(normalizedSourcePath, ["rev-parse", "--show-toplevel"], timeouts, cancellationToken);
        if (!string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(topLevel)),
                normalizedSourcePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(SourceValidationMessage);
        }

        var isBare = await ReadTextAsync(
            normalizedSourcePath,
            ["rev-parse", "--is-bare-repository"],
            timeouts,
            cancellationToken);
        if (!string.Equals(isBare, "false", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(SourceValidationMessage);
        }

        var objectFormat = ParseObjectFormat(await ReadTextAsync(
            normalizedSourcePath,
            ["rev-parse", "--show-object-format"],
            timeouts,
            cancellationToken));
        EnsureObjectId(pin, objectFormat);
        var resolvedPin = await ReadTextAsync(
            normalizedSourcePath,
            ["rev-parse", "--verify", $"{pin}^{{commit}}"],
            timeouts,
            cancellationToken);
        if (!string.Equals(resolvedPin, pin, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(SourceValidationMessage);
        }

        var state = await ReadStateAsync(normalizedSourcePath, objectFormat, timeouts, cancellationToken);
        return new ExternalGitWorktree(normalizedSourcePath, pin, objectFormat, state);
    }

    internal async Task EnsureUnchangedAsync(
        ExternalGitWorktree worktree,
        ExternalGitCommandTimeouts timeouts,
        CancellationToken cancellationToken)
    {
        var finalState = await ReadStateAsync(
            worktree.SourcePath,
            worktree.ObjectFormat,
            timeouts,
            cancellationToken);
        if (finalState != worktree.InitialState)
        {
            throw new InvalidOperationException(SourceChangedMessage);
        }
    }

    private async Task<ExternalGitWorktreeState> ReadStateAsync(
        string sourcePath,
        GitObjectFormat objectFormat,
        ExternalGitCommandTimeouts timeouts,
        CancellationToken cancellationToken)
    {
        var headSha = await ReadTextAsync(sourcePath, ["rev-parse", "HEAD"], timeouts, cancellationToken);
        EnsureObjectId(headSha, objectFormat);
        var statusResult = await git.RunAsync(
            sourcePath,
            ["status", "--porcelain=v1", "-z", "--untracked-files=all"],
            ReadStatusFingerprintAsync,
            timeouts,
            cancellationToken);
        if (statusResult.ExitCode != 0)
        {
            throw new InvalidOperationException(SourceValidationMessage);
        }

        return new ExternalGitWorktreeState(
            headSha,
            statusResult.Output.Sha256,
            statusResult.Output.RecordCount);
    }

    private async Task<string> ReadTextAsync(
        string sourcePath,
        IReadOnlyList<string> arguments,
        ExternalGitCommandTimeouts timeouts,
        CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(
            sourcePath,
            arguments,
            (stream, token) => ExternalGitCommandRunner.ReadBoundedBytesAsync(stream, 32 * 1024, token),
            timeouts,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(SourceValidationMessage);
        }

        try
        {
            var value = StrictUtf8.GetString(result.Output).TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(value) || value.Contains('\0'))
            {
                throw new InvalidOperationException(SourceValidationMessage);
            }

            return value;
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidOperationException(SourceValidationMessage);
        }
    }

    private static async Task<StatusFingerprint> ReadStatusFingerprintAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[16 * 1024];
        var recordCount = 0;
        var lastByteWasNull = true;
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            hash.AppendData(buffer, 0, bytesRead);
            for (var index = 0; index < bytesRead; index++)
            {
                if (buffer[index] == 0)
                {
                    recordCount++;
                }
            }

            lastByteWasNull = buffer[bytesRead - 1] == 0;
        }

        if (!lastByteWasNull)
        {
            throw new InvalidOperationException(SourceValidationMessage);
        }

        return new StatusFingerprint(
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
            recordCount);
    }

    private static string NormalizeSourcePath(string sourcePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !Path.IsPathFullyQualified(sourcePath) ||
                !Directory.Exists(sourcePath))
            {
                throw new InvalidOperationException(SourceValidationMessage);
            }
            var normalizedSourcePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
            var repositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RepositoryRoot.Find()));
            if (PrivateRunDirectoryLayout.IsDescendantOrSame(repositoryRoot, normalizedSourcePath))
            {
                throw new InvalidOperationException(SourceValidationMessage);
            }

            return normalizedSourcePath;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(SourceValidationMessage);
        }
    }

    private static GitObjectFormat ParseObjectFormat(string value) => value switch
    {
        "sha1" => GitObjectFormat.Sha1,
        "sha256" => GitObjectFormat.Sha256,
        _ => throw new InvalidOperationException(SourceValidationMessage)
    };

    private static void EnsureObjectId(string value, GitObjectFormat objectFormat)
    {
        var isValid = objectFormat switch
        {
            GitObjectFormat.Sha1 => Sha1Pattern.IsMatch(value),
            GitObjectFormat.Sha256 => Sha256Pattern.IsMatch(value),
            _ => false
        };
        if (!isValid)
        {
            throw new InvalidOperationException(SourceValidationMessage);
        }
    }

    private sealed record StatusFingerprint(string Sha256, int RecordCount);
}
