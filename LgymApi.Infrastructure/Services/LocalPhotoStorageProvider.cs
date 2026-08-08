using LgymApi.Application.Abstractions.Storage;
using Microsoft.AspNetCore.Http;

namespace LgymApi.Infrastructure.Services;

/// <summary>
/// Local development implementation of IPhotoStorageProvider.
/// Returns expiring bearer-capability URLs for dev/test environments.
/// NOT FOR PRODUCTION USE - implement CloudflareR2PhotoStorageProvider or SupabasePhotoStorageProvider for production.
/// </summary>
public sealed class LocalPhotoStorageProvider : IPhotoStorageProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly LocalPhotoDevelopmentStore _store;
    private readonly LocalPhotoDevelopmentUrlSigner _urlSigner;

    public LocalPhotoStorageProvider(
        IHttpContextAccessor httpContextAccessor,
        LocalPhotoDevelopmentStore store,
        LocalPhotoDevelopmentUrlSigner urlSigner)
    {
        _httpContextAccessor = httpContextAccessor;
        _store = store;
        _urlSigner = urlSigner;
    }

    public Task<string> GenerateSignedUploadUrlAsync(
        string storageKey,
        string contentType,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var capability = _urlSigner.CreateCapability(HttpMethods.Put, storageKey, expiration);
        return Task.FromResult(
            $"{GetBaseUrl()}/dev/photos/upload/{capability.EncodedStorageKey}" +
            $"?v={LocalPhotoDevelopmentUrlSigner.Version}&expires={capability.ExpiresAt}&sig={capability.Signature}");
    }

    public Task<string> GenerateSignedReadUrlAsync(
        string storageKey,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var capability = _urlSigner.CreateCapability(HttpMethods.Get, storageKey, expiration);
        return Task.FromResult(
            $"{GetBaseUrl()}/dev/photos/read/{capability.EncodedStorageKey}" +
            $"?v={LocalPhotoDevelopmentUrlSigner.Version}&expires={capability.ExpiresAt}&sig={capability.Signature}");
    }

    public Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        return _store.DeleteAsync(storageKey, cancellationToken);
    }

    public Task<PhotoMetadata?> GetMetadataAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        return _store.GetMetadataAsync(storageKey, cancellationToken);
    }

    private string GetBaseUrl()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request != null && request.Host.HasValue)
        {
            return $"{request.Scheme}://{request.Host.Value}";
        }

        return "https://localhost:7025";
    }
}
