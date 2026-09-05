using System.Text.Json;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.Reporting;

public sealed partial class ReportingService
{
    private async Task HydrateSubmissionPhotosAsync(
        IReadOnlyList<ReportSubmissionResult> submissions,
        CancellationToken cancellationToken)
    {
        var hydratableSubmissions = submissions
            .Where(submission => submission.Answers.Values.Any(IsPhotoAnswer))
            .ToList();
        if (hydratableSubmissions.Count == 0)
        {
            return;
        }

        var requestIds = hydratableSubmissions
            .Select(submission => submission.ReportRequestId)
            .Distinct()
            .ToArray();
        var canonicalPhotos = await _photoPersistence.ListByRequestsAsync(requestIds, cancellationToken);
        var photosByRequest = canonicalPhotos
            .GroupBy(photo => photo.ReportRequestId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var signedUrls = new Dictionary<Id<Photo>, SignedPhotoUrls>();

        foreach (var submission in hydratableSubmissions)
        {
            photosByRequest.TryGetValue(submission.ReportRequestId, out var requestPhotos);
            requestPhotos = (requestPhotos ?? [])
                .Where(photo => photo.OwnerAccountId == submission.TraineeId)
                .ToList();
            foreach (var answer in submission.Answers.Where(pair => IsPhotoAnswer(pair.Value)).ToList())
            {
                submission.Answers[answer.Key] = answer.Value.ValueKind == JsonValueKind.Array
                    ? await HydratePhotoArrayAsync(answer.Value, requestPhotos, signedUrls, cancellationToken)
                    : await HydratePhotoObjectAsync(answer.Value, requestPhotos, signedUrls, cancellationToken);
            }
        }
    }

    private async Task<JsonElement> HydratePhotoArrayAsync(
        JsonElement answer,
        IReadOnlyList<ReportPhotoPersistenceModel> requestPhotos,
        Dictionary<Id<Photo>, SignedPhotoUrls> signedUrls,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var item in answer.EnumerateArray())
            {
                if (IsPhotoObject(item))
                {
                    await WriteHydratedPhotoObjectAsync(writer, item, requestPhotos, signedUrls, cancellationToken);
                }
                else
                {
                    item.WriteTo(writer);
                }
            }

            writer.WriteEndArray();
            writer.Flush();
        }

        return ParseJsonElement(stream);
    }

    private async Task<JsonElement> HydratePhotoObjectAsync(
        JsonElement answer,
        IReadOnlyList<ReportPhotoPersistenceModel> requestPhotos,
        Dictionary<Id<Photo>, SignedPhotoUrls> signedUrls,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            await WriteHydratedPhotoObjectAsync(writer, answer, requestPhotos, signedUrls, cancellationToken);
            writer.Flush();
        }

        return ParseJsonElement(stream);
    }

    private async Task WriteHydratedPhotoObjectAsync(
        Utf8JsonWriter writer,
        JsonElement photoObject,
        IReadOnlyList<ReportPhotoPersistenceModel> requestPhotos,
        Dictionary<Id<Photo>, SignedPhotoUrls> signedUrls,
        CancellationToken cancellationToken)
    {
        var canonicalPhoto = ResolveCanonicalPhoto(photoObject, requestPhotos);
        SignedPhotoUrls? urls = null;
        if (canonicalPhoto != null && !signedUrls.TryGetValue(canonicalPhoto.Id, out urls))
        {
            var readUrl = await _photoStorageProvider.GenerateSignedReadUrlAsync(
                canonicalPhoto.StorageKey,
                GetSignedReadExpiration(),
                cancellationToken);
            string? thumbnailUrl = null;
            if (!string.IsNullOrWhiteSpace(canonicalPhoto.ThumbnailStorageKey))
            {
                thumbnailUrl = await _photoStorageProvider.GenerateSignedReadUrlAsync(
                    canonicalPhoto.ThumbnailStorageKey,
                    GetSignedReadExpiration(),
                    cancellationToken);
            }

            urls = new SignedPhotoUrls(readUrl, thumbnailUrl);
            signedUrls.Add(canonicalPhoto.Id, urls);
        }

        var overwrittenProperties = canonicalPhoto == null ? UrlOutputPropertyNames : CanonicalOutputPropertyNames;
        writer.WriteStartObject();
        foreach (var property in photoObject.EnumerateObject())
        {
            if (!overwrittenProperties.Contains(property.Name))
            {
                property.WriteTo(writer);
            }
        }

        if (canonicalPhoto == null || urls == null)
        {
            writer.WriteNull("readUrl");
            writer.WriteNull("thumbnailUrl");
        }
        else
        {
            writer.WriteString("storageKey", canonicalPhoto.StorageKey);
            writer.WriteString("readUrl", urls.ReadUrl);
            if (urls.ThumbnailUrl == null)
            {
                writer.WriteNull("thumbnailUrl");
            }
            else
            {
                writer.WriteString("thumbnailUrl", urls.ThumbnailUrl);
            }
        }

        writer.WriteEndObject();
    }

    private static ReportPhotoPersistenceModel? ResolveCanonicalPhoto(
        JsonElement photoObject,
        IReadOnlyList<ReportPhotoPersistenceModel> requestPhotos)
    {
        if (!TryGetUniqueStringProperty(photoObject, "photoId", out var hasPhotoId, out var photoIdValue)
            || !TryGetUniqueStringProperty(photoObject, "_id", out var hasLegacyId, out var legacyIdValue))
        {
            return null;
        }

        var photoId = ParsePhotoId(photoIdValue);
        var legacyId = ParsePhotoId(legacyIdValue);
        if ((hasPhotoId && !photoId.HasValue)
            || (hasLegacyId && !legacyId.HasValue)
            || (photoId.HasValue && legacyId.HasValue && photoId.Value != legacyId.Value))
        {
            return null;
        }

        var canonicalId = photoId ?? legacyId;
        if (canonicalId.HasValue)
        {
            return requestPhotos.FirstOrDefault(photo => photo.Id == canonicalId.Value);
        }

        if (!TryGetUniqueStringProperty(photoObject, "storageKey", out var hasStorageKey, out var storageKey))
        {
            return null;
        }

        return !hasStorageKey || string.IsNullOrWhiteSpace(storageKey)
            ? null
            : requestPhotos.FirstOrDefault(photo => string.Equals(photo.StorageKey, storageKey, StringComparison.Ordinal));
    }

    private static bool TryGetUniqueStringProperty(
        JsonElement photoObject,
        string propertyName,
        out bool isPresent,
        out string? propertyValue)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        isPresent = false;
        propertyValue = null;
        foreach (var property in photoObject.EnumerateObject()
                     .Where(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)))
        {
            isPresent = true;
            if (property.Value.ValueKind != JsonValueKind.String || property.Value.GetString() is not { } value)
            {
                return false;
            }

            values.Add(value);
        }

        if (!isPresent)
        {
            return true;
        }

        if (values.Count != 1)
        {
            return false;
        }

        propertyValue = values.Single();
        return true;
    }

    private static JsonElement ParseJsonElement(MemoryStream stream)
    {
        stream.Position = 0;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    private static bool IsPhotoAnswer(JsonElement answer)
        => answer.ValueKind switch
        {
            JsonValueKind.Object => IsPhotoObject(answer),
            JsonValueKind.Array => answer.EnumerateArray().Any(IsPhotoObject),
            _ => false
        };

    private static bool IsPhotoObject(JsonElement item)
        => item.ValueKind == JsonValueKind.Object
            && item.EnumerateObject().Any(property => PhotoReferencePropertyNames.Contains(property.Name));

    private static Id<Photo>? ParsePhotoId(string? value)
        => !string.IsNullOrWhiteSpace(value) && Id<Photo>.TryParse(value, out var photoId) ? photoId : null;

    private static readonly HashSet<string> PhotoReferencePropertyNames = new(
        ["photoId", "_id", "storageKey", "readUrl", "thumbnailUrl"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> UrlOutputPropertyNames = new(
        ["readUrl", "thumbnailUrl"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> CanonicalOutputPropertyNames = new(
        ["storageKey", "readUrl", "thumbnailUrl"],
        StringComparer.OrdinalIgnoreCase);

    private sealed record SignedPhotoUrls(string ReadUrl, string? ThumbnailUrl);
}
