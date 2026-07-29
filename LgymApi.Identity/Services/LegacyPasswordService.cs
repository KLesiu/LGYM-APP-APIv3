using System.Security.Cryptography;
using LgymApi.Application.Services;

namespace LgymApi.Infrastructure.Services;

internal sealed class LegacyPasswordService : ILegacyPasswordService
{
    public bool Verify(string password, string hash, string salt, int? iterations, int? keyLength, string? digest)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(salt))
        {
            return false;
        }

        var iterationCount = iterations ?? LegacyPasswordConstants.Iterations;
        var algorithm = ResolveHashAlgorithm(digest);

        var hashBytes = DecodeHash(hash);
        if (hashBytes == null)
        {
            return false;
        }

        var derivedKeyLength = ResolveDerivedKeyLength(hashBytes.Length, keyLength);
        var saltBytes = DecodeSalt(salt);
        var derived = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, iterationCount, algorithm, derivedKeyLength);

        return ConstantTimeEquals(hashBytes, derived);
    }

    public (string Hash, string Salt, int Iterations, int KeyLength, string Digest) Create(string password)
    {
        var iterations = LegacyPasswordConstants.Iterations;
        var keyLength = LegacyPasswordConstants.KeyLength;
        var digest = LegacyPasswordConstants.Digest;

        var saltBytes = RandomNumberGenerator.GetBytes(LegacyPasswordConstants.SaltLengthBytes);
        var saltHex = ConvertToHex(saltBytes);
        var saltForPbkdf2 = System.Text.Encoding.UTF8.GetBytes(saltHex);
        var derived = Rfc2898DeriveBytes.Pbkdf2(password, saltForPbkdf2, iterations, HashAlgorithmName.SHA256, keyLength);
        var hashHex = ConvertToHex(derived);

        return (hashHex, saltHex, iterations, keyLength, digest);
    }

    private static HashAlgorithmName ResolveHashAlgorithm(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return HashAlgorithmName.SHA256;
        }

        return digest.ToLowerInvariant() switch
        {
            "sha1" => HashAlgorithmName.SHA1,
            "sha256" => HashAlgorithmName.SHA256,
            "sha384" => HashAlgorithmName.SHA384,
            "sha512" => HashAlgorithmName.SHA512,
            _ => HashAlgorithmName.SHA256
        };
    }

    private static byte[] DecodeSalt(string salt)
    {
        // passport-local-mongoose stores salt as hex string but uses it as UTF-8 bytes directly
        // (not decoded from hex). The salt is passed to pbkdf2 as the literal hex string.
        return System.Text.Encoding.UTF8.GetBytes(salt);
    }

    private static byte[]? DecodeHash(string hash)
    {
        try
        {
            return ConvertFromHex(hash);
        }
        catch (FormatException)
        {
            try
            {
                return Convert.FromBase64String(hash);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }

    private static int ResolveDerivedKeyLength(int hashLengthBytes, int? storedKeyLength)
    {
        if (hashLengthBytes <= 0)
        {
            return storedKeyLength ?? LegacyPasswordConstants.KeyLength;
        }

        if (!storedKeyLength.HasValue || storedKeyLength.Value <= 0)
        {
            return hashLengthBytes;
        }

        if (storedKeyLength.Value == hashLengthBytes)
        {
            return storedKeyLength.Value;
        }

        if (storedKeyLength.Value == hashLengthBytes * 8)
        {
            return hashLengthBytes;
        }

        return hashLengthBytes;
    }

    private static byte[] ConvertFromHex(string hex)
    {
        if (hex.Length % 2 != 0)
        {
            throw new FormatException("Invalid hex string length.");
        }

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return bytes;
    }

    private static string ConvertToHex(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool ConstantTimeEquals(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        var diff = 0;
        for (var i = 0; i < left.Length; i++)
        {
            diff |= left[i] ^ right[i];
        }

        return diff == 0;
    }
}
