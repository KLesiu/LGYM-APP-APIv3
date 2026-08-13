using System.Security.Cryptography;
using System.Text;

namespace LgymApi.E2ETests.Harness;

internal static class GitBlobHasher
{
    internal static async Task<string> ComputeObjectIdAsync(
        Stream stream,
        long length,
        GitObjectFormat objectFormat,
        CancellationToken cancellationToken)
    {
        if (length < 0)
        {
            throw new InvalidOperationException(PinnedWebSourceArchiveExtractor.ArchiveValidationMessage);
        }

        var algorithm = objectFormat switch
        {
            GitObjectFormat.Sha1 => HashAlgorithmName.SHA1,
            GitObjectFormat.Sha256 => HashAlgorithmName.SHA256,
            _ => throw new InvalidOperationException(PinnedWebSourceArchiveExtractor.ArchiveValidationMessage)
        };
        using var hash = IncrementalHash.CreateHash(algorithm);
        hash.AppendData(Encoding.ASCII.GetBytes($"blob {length}\0"));
        var buffer = new byte[16 * 1024];
        long bytesReadTotal = 0;
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            bytesReadTotal += bytesRead;
            if (bytesReadTotal > length)
            {
                throw new InvalidOperationException(PinnedWebSourceArchiveExtractor.ArchiveValidationMessage);
            }

            hash.AppendData(buffer, 0, bytesRead);
        }

        if (bytesReadTotal != length)
        {
            throw new InvalidOperationException(PinnedWebSourceArchiveExtractor.ArchiveValidationMessage);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
