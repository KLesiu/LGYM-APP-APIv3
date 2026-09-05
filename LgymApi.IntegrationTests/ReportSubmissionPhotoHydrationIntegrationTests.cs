using System.Net;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class ReportSubmissionPhotoHydrationIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task GetOwnReportSubmissions_WhenPhotoUrlsAreExpired_ReturnsFreshCanonicalUrls()
    {
        var scenario = await SeedScenarioAsync();
        SetAuthorizationHeader(scenario.TraineeId);

        using var response = await Client.GetAsync("/api/trainee/report-submissions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertHydratedPhoto(document.RootElement[0], scenario.Photo.StorageKey);
    }

    [Test]
    public async Task MarkFeedbackRead_WhenHistoricalPhotoUrlsAreExpired_ReturnsFreshCanonicalUrls()
    {
        var scenario = await SeedScenarioAsync(feedbackAddedAt: DateTimeOffset.UtcNow);
        SetAuthorizationHeader(scenario.TraineeId);

        using var response = await Client.PostAsync(
            $"/api/trainee/report-submissions/{scenario.SubmissionId}/mark-feedback-read",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertHydratedPhoto(document.RootElement, scenario.Photo.StorageKey);
    }

    [Test]
    public async Task GetOwnReportSubmissions_WhenPhotoIdAliasesConflict_ClearsPersistedUrls()
    {
        var scenario = await SeedScenarioAsync(conflictingPhotoId: Id<Photo>.New().ToString());
        SetAuthorizationHeader(scenario.TraineeId);

        using var response = await Client.GetAsync("/api/trainee/report-submissions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var unresolved = document.RootElement[0].GetProperty("answers").GetProperty("photos")[0];
        unresolved.GetProperty("readUrl").ValueKind.Should().Be(JsonValueKind.Null);
        unresolved.GetProperty("thumbnailUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    public async Task GetOwnReportSubmissions_WhenExactPhotoIdIsDuplicated_DoesNotFailTheResponse()
    {
        var scenario = await SeedScenarioAsync(duplicateExactPhotoId: true);
        SetAuthorizationHeader(scenario.TraineeId);

        using var response = await Client.GetAsync("/api/trainee/report-submissions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertHydratedPhoto(document.RootElement[0], scenario.Photo.StorageKey);
    }

    [Test]
    public async Task GetOwnReportSubmissions_WhenPhotoArrayContainsMetadata_PreservesMetadataObject()
    {
        var scenario = await SeedScenarioAsync(includeMetadataObject: true);
        SetAuthorizationHeader(scenario.TraineeId);

        using var response = await Client.GetAsync("/api/trainee/report-submissions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var metadata = document.RootElement[0].GetProperty("answers").GetProperty("photos")[1];
        metadata.EnumerateObject().Select(property => property.Name).Should().Equal("caption");
        metadata.GetProperty("caption").GetString().Should().Be("front");
    }

    private async Task<Scenario> SeedScenarioAsync(
        DateTimeOffset? feedbackAddedAt = null,
        string? conflictingPhotoId = null,
        bool duplicateExactPhotoId = false,
        bool includeMetadataObject = false)
    {
        var trainer = await SeedUserAsync("photo-hydration-trainer", "photo-hydration-trainer@example.test");
        var trainee = await SeedUserAsync("photo-hydration-trainee", "photo-hydration-trainee@example.test");
        var now = DateTimeOffset.UtcNow;
        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = trainer.Id,
            Name = "Historical photo report",
            CreatedAt = now,
            Fields =
            [
                new ReportTemplateField
                {
                    Id = Id<ReportTemplateField>.New(),
                    Key = "photos",
                    Label = "Photos",
                    Type = ReportFieldType.Photos,
                    IsRequired = false,
                    Order = 0,
                    CreatedAt = now
                }
            ]
        };
        var request = new ReportRequest
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = trainer.Id,
            TraineeId = trainee.Id,
            TemplateId = template.Id,
            Template = template,
            Status = ReportRequestStatus.Submitted,
            SubmittedAt = now,
            CreatedAt = now
        };
        var photo = new Photo
        {
            Id = Id<Photo>.New(),
            ReportRequestId = request.Id,
            OwnerUserId = trainee.Id,
            UploaderUserId = trainee.Id,
            ViewType = PhotoViewType.Front.ToString(),
            StorageKey = $"photos/{trainee.Id}/{request.Id}/Front/canonical.jpg",
            MimeType = "image/jpeg",
            SizeBytes = 1024,
            Checksum = "etag",
            CreatedAt = now
        };
        var photoPayload = new Dictionary<string, object?>
        {
            ["photoId"] = photo.Id.ToString(),
            ["_id"] = photo.Id.ToString(),
            ["storageKey"] = "photos/attacker/foreign.jpg",
            ["readUrl"] = "https://expired.example/read",
            ["thumbnailUrl"] = "https://expired.example/thumb"
        };
        if (conflictingPhotoId != null)
        {
            photoPayload["PhotoId"] = conflictingPhotoId;
        }

        var photoAnswers = new List<object?> { photoPayload };
        if (includeMetadataObject)
        {
            photoAnswers.Add(new Dictionary<string, object?> { ["caption"] = "front" });
        }

        var payloadJson = duplicateExactPhotoId
            ? $"{{\"photos\":[{{\"photoId\":\"{photo.Id}\",\"photoId\":\"{photo.Id}\",\"storageKey\":\"{photo.StorageKey}\",\"readUrl\":\"https://expired.example/read\"}}]}}"
            : JsonSerializer.Serialize(new Dictionary<string, object?> { ["photos"] = photoAnswers });

        var submission = new ReportSubmission
        {
            Id = Id<ReportSubmission>.New(),
            ReportRequestId = request.Id,
            ReportRequest = request,
            TraineeId = trainee.Id,
            PayloadJson = payloadJson,
            TrainerFeedbackAddedAt = feedbackAddedAt,
            CreatedAt = now
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.ReportTemplates.Add(template);
            database.ReportRequests.Add(request);
            database.Photos.Add(photo);
            database.ReportSubmissions.Add(submission);
            await database.SaveChangesAsync();
        }

        return new Scenario(trainee.Id, photo, submission.Id);
    }

    private static void AssertHydratedPhoto(JsonElement submission, string canonicalStorageKey)
    {
        var hydrated = submission.GetProperty("answers").GetProperty("photos")[0];
        hydrated.GetProperty("storageKey").GetString().Should().Be(canonicalStorageKey);
        hydrated.GetProperty("readUrl").GetString().Should().NotBe("https://expired.example/read");
        hydrated.GetProperty("readUrl").GetString().Should().NotBeNullOrWhiteSpace();
        hydrated.GetProperty("thumbnailUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private sealed record Scenario(Id<User> TraineeId, Photo Photo, Id<ReportSubmission> SubmissionId);
}
