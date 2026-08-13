using System.Security.Cryptography;
using System.Text;

namespace LgymApi.E2ETests.Harness;

internal sealed class GitTreeManifestReader(IExternalGitCommandRunner git)
{
    internal const string TreeValidationMessage = "The pinned Git tree is unsafe or malformed.";
    private const int MaximumRecordBytes = 32 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal async Task<GitTreeManifest> ReadAsync(
        ExternalGitWorktree worktree,
        ExternalGitCommandTimeouts timeouts,
        CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(
            worktree.SourcePath,
            ["ls-tree", "-rz", "--full-tree", worktree.PinnedCommitSha],
            (stream, token) => ReadManifestAsync(stream, worktree.ObjectFormat, token),
            timeouts,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(TreeValidationMessage);
        }

        return result.Output;
    }

    private static async Task<GitTreeManifest> ReadManifestAsync(
        Stream stream,
        GitObjectFormat objectFormat,
        CancellationToken cancellationToken)
    {
        using var manifestHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var record = new MemoryStream();
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var caseInsensitivePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buffer = new byte[16 * 1024];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            manifestHash.AppendData(buffer, 0, bytesRead);
            for (var index = 0; index < bytesRead; index++)
            {
                if (buffer[index] == 0)
                {
                    AddRecord(record, entries, caseInsensitivePaths, objectFormat);
                    record.SetLength(0);
                }
                else
                {
                    if (record.Length == MaximumRecordBytes)
                    {
                        throw new InvalidOperationException(TreeValidationMessage);
                    }

                    record.WriteByte(buffer[index]);
                }
            }
        }

        if (record.Length != 0 || entries.Count == 0)
        {
            throw new InvalidOperationException(TreeValidationMessage);
        }


        PinnedWebSourcePathPolicy.EnsureNoWindowsCollisions(entries.Keys, TreeValidationMessage);

        return new GitTreeManifest(
            entries,
            Convert.ToHexString(manifestHash.GetHashAndReset()).ToLowerInvariant());
    }

    private static void AddRecord(
        MemoryStream record,
        IDictionary<string, string> entries,
        ISet<string> caseInsensitivePaths,
        GitObjectFormat objectFormat)
    {
        string value;
        try
        {
            value = StrictUtf8.GetString(record.GetBuffer(), 0, checked((int)record.Length));
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidOperationException(TreeValidationMessage);
        }

        var tabIndex = value.IndexOf('\t');
        if (tabIndex <= 0 || value.IndexOf('\t', tabIndex + 1) >= 0)
        {
            throw new InvalidOperationException(TreeValidationMessage);
        }

        var metadata = value[..tabIndex].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var path = value[(tabIndex + 1)..];
        if (metadata.Length != 3 ||
            (metadata[0] != "100644" && metadata[0] != "100755") ||
            metadata[1] != "blob" ||
            !PinnedWebSourcePathPolicy.IsSafeRelativePath(path) ||
            !IsObjectId(metadata[2], objectFormat) ||
            !entries.TryAdd(path, metadata[2]) ||
            !caseInsensitivePaths.Add(path))
        {
            throw new InvalidOperationException(TreeValidationMessage);
        }
    }

    private static bool IsObjectId(string value, GitObjectFormat objectFormat) =>
        value.Length == (objectFormat == GitObjectFormat.Sha1 ? 40 : 64) &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
