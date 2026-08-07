using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace LgymApi.Infrastructure.Services;

public sealed class LocalPhotoDevelopmentUrlSigner
{
    public const string Version = "1";

    private readonly byte[]? _signingKey;
    private readonly TimeProvider _timeProvider;

    public LocalPhotoDevelopmentUrlSigner(string? signingKey, TimeProvider timeProvider)
    {
        _signingKey = signingKey is null ? null : Encoding.UTF8.GetBytes(signingKey);
        _timeProvider = timeProvider;
    }

    public LocalPhotoDevelopmentCapability CreateCapability(
        string method,
        string storageKey,
        TimeSpan expiration)
    {
        if (_signingKey is null)
        {
            throw new InvalidOperationException("Local photo development capabilities are disabled.");
        }

        var normalizedStorageKey = NormalizeStorageKey(storageKey);
        var expiresAt = _timeProvider.GetUtcNow().Add(expiration).ToUnixTimeSeconds();
        var signature = Sign(method, normalizedStorageKey, expiresAt);

        return new LocalPhotoDevelopmentCapability(
            Uri.EscapeDataString(normalizedStorageKey),
            expiresAt,
            signature);
    }

    public bool TryValidate(
        HttpRequest request,
        string expectedMethod,
        string expectedPathPrefix,
        out string normalizedStorageKey)
    {
        normalizedStorageKey = string.Empty;
        if (_signingKey is null || !string.Equals(request.Method, expectedMethod, StringComparison.Ordinal))
        {
            return false;
        }

        var rawPath = GetRawPath(request);
        if (!rawPath.StartsWith(expectedPathPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var encodedStorageKey = rawPath[expectedPathPrefix.Length..];
        if (!TryDecodeCanonicalStorageKey(encodedStorageKey, out normalizedStorageKey) ||
            !TryReadCanonicalQuery(request.QueryString.Value, out var expiresText, out var signature))
        {
            normalizedStorageKey = string.Empty;
            return false;
        }

        if (!long.TryParse(expiresText, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt) ||
            !string.Equals(expiresText, expiresAt.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) ||
            expiresAt <= _timeProvider.GetUtcNow().ToUnixTimeSeconds())
        {
            normalizedStorageKey = string.Empty;
            return false;
        }

        var expectedSignature = Sign(expectedMethod, normalizedStorageKey, expiresAt);
        if (!TryDecodeBase64Url(signature, out var suppliedSignature) ||
            !TryDecodeBase64Url(expectedSignature, out var expectedSignatureBytes) ||
            !CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignatureBytes))
        {
            normalizedStorageKey = string.Empty;
            return false;
        }

        return true;
    }

    private string Sign(string method, string normalizedStorageKey, long expiresAt)
    {
        var canonicalTuple = string.Join(
            '\n',
            Version,
            method.ToUpperInvariant(),
            normalizedStorageKey,
            expiresAt.ToString(CultureInfo.InvariantCulture));
        using var hmac = new HMACSHA256(_signingKey!);
        return EncodeBase64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalTuple)));
    }

    private static string NormalizeStorageKey(string storageKey)
    {
        if (!TryNormalizeStorageKey(storageKey, out var normalizedStorageKey))
        {
            throw new ArgumentException("Storage key is invalid.", nameof(storageKey));
        }

        return normalizedStorageKey;
    }

    private static bool TryNormalizeStorageKey(string storageKey, out string normalizedStorageKey)
    {
        normalizedStorageKey = string.Empty;
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return false;
        }

        var segments = storageKey
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            return false;
        }

        normalizedStorageKey = string.Join('/', segments);
        return true;
    }

    private static bool TryDecodeCanonicalStorageKey(string encodedStorageKey, out string normalizedStorageKey)
    {
        normalizedStorageKey = string.Empty;
        if (string.IsNullOrEmpty(encodedStorageKey))
        {
            return false;
        }

        string decodedStorageKey;
        try
        {
            decodedStorageKey = Uri.UnescapeDataString(encodedStorageKey);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (!TryNormalizeStorageKey(decodedStorageKey, out normalizedStorageKey) ||
            !string.Equals(decodedStorageKey, normalizedStorageKey, StringComparison.Ordinal) ||
            !string.Equals(encodedStorageKey, Uri.EscapeDataString(normalizedStorageKey), StringComparison.Ordinal))
        {
            normalizedStorageKey = string.Empty;
            return false;
        }

        return true;
    }

    private static bool TryReadCanonicalQuery(
        string? queryString,
        out string expires,
        out string signature)
    {
        expires = string.Empty;
        signature = string.Empty;
        if (string.IsNullOrEmpty(queryString) || queryString[0] != '?')
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in queryString[1..].Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2 ||
                parts[1].Contains('%') ||
                parts[1].Contains('+') ||
                !values.TryAdd(parts[0], parts[1]))
            {
                return false;
            }
        }

        if (values.Count != 3 ||
            !values.TryGetValue("v", out var version) ||
            !string.Equals(version, Version, StringComparison.Ordinal) ||
            !values.TryGetValue("expires", out var expiresValue) ||
            !values.TryGetValue("sig", out var signatureValue))
        {
            return false;
        }

        expires = expiresValue;
        signature = signatureValue;
        return true;
    }

    private static string GetRawPath(HttpRequest request)
    {
        var rawTarget = request.HttpContext.Features.Get<IHttpRequestFeature>()?.RawTarget;
        if (!string.IsNullOrEmpty(rawTarget) &&
            !(string.Equals(rawTarget, "/", StringComparison.Ordinal) && request.Path != "/"))
        {
            var queryStart = rawTarget.IndexOf('?');
            return queryStart < 0 ? rawTarget : rawTarget[..queryStart];
        }

        return request.Path.Value ?? string.Empty;
    }

    private static string EncodeBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool TryDecodeBase64Url(string value, out byte[] bytes)
    {
        bytes = [];
        if (value.Length != 43 || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + "=");
            return bytes.Length == 32 && string.Equals(EncodeBase64Url(bytes), value, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}

public readonly record struct LocalPhotoDevelopmentCapability(
    string EncodedStorageKey,
    long ExpiresAt,
    string Signature);
