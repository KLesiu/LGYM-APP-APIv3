using System.Text.Json;
using FluentAssertions;
using LgymApi.Application.Abstractions.Storage;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Options;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ReportSubmissionPhotoHydrationTests
{
    [Test]
    public async Task GetOwnSubmissionsAsync_WhenHistoricalPhotoHasCanonicalThumbnail_ReturnsFreshCanonicalUrls()
    {
        var fixture = CreateFixture();
        var photo = fixture.Photos[0];
        var submission = CreateSubmission(fixture.RequestId, fixture.TraineeId, new Dictionary<string, object?>
        {
            ["photoId"] = photo.Id.ToString(),
            ["_id"] = photo.Id.ToString(),
            ["StorageKey"] = photo.StorageKey,
            ["ReadUrl"] = "https://expired.example/read",
            ["ThumbnailUrl"] = "https://expired.example/thumb"
        });
        fixture.ReturnSubmissions(submission);

        var result = await fixture.Service.GetOwnSubmissionsAsync(ReportingTestData.Account(fixture.TraineeId));

        var hydrated = GetOnlyPhoto(result.Value.Single());
        hydrated.GetProperty("readUrl").GetString().Should().Be($"fresh:{photo.StorageKey}");
        hydrated.GetProperty("thumbnailUrl").GetString().Should().Be($"fresh:{photo.ThumbnailStorageKey}");
        hydrated.TryGetProperty("ReadUrl", out _).Should().BeFalse();
        hydrated.TryGetProperty("ThumbnailUrl", out _).Should().BeFalse();
    }

    [Test]
    public async Task GetOwnSubmissionsAsync_WhenCanonicalPhotoHasNoThumbnail_ClearsExpiredThumbnailUrl()
    {
        var fixture = CreateFixture(thumbnailStorageKey: null);
        var photo = fixture.Photos[0];
        fixture.ReturnSubmissions(CreateSubmission(fixture.RequestId, fixture.TraineeId, new Dictionary<string, object?>
        {
            ["photoId"] = photo.Id.ToString(),
            ["readUrl"] = "https://expired.example/read",
            ["thumbnailUrl"] = "https://expired.example/thumb"
        }));

        var result = await fixture.Service.GetOwnSubmissionsAsync(ReportingTestData.Account(fixture.TraineeId));

        var hydrated = GetOnlyPhoto(result.Value.Single());
        hydrated.GetProperty("readUrl").GetString().Should().Be($"fresh:{photo.StorageKey}");
        hydrated.GetProperty("thumbnailUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    public async Task GetOwnSubmissionsAsync_WhenPayloadStorageKeyIsTampered_SignsCanonicalStorageKey()
    {
        var fixture = CreateFixture();
        using var cancellation = new CancellationTokenSource();
        var photo = fixture.Photos[0];
        fixture.ReturnSubmissions(CreateSubmission(fixture.RequestId, fixture.TraineeId, new Dictionary<string, object?>
        {
            ["photoId"] = photo.Id.ToString(),
            ["storageKey"] = "photos/attacker/foreign.jpg",
            ["readUrl"] = "https://expired.example/read"
        }));

        await fixture.Service.GetOwnSubmissionsAsync(ReportingTestData.Account(fixture.TraineeId), cancellation.Token);

        await fixture.Storage.Received().GenerateSignedReadUrlAsync(photo.StorageKey, Arg.Any<TimeSpan>(), cancellation.Token);
        await fixture.Storage.DidNotReceive().GenerateSignedReadUrlAsync("photos/attacker/foreign.jpg", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetOwnSubmissionsAsync_WhenPayloadHasMalformedId_DoesNotFallbackToStorageKey()
    {
        var fixture = CreateFixture();
        fixture.ReturnSubmissions(CreateSubmission(fixture.RequestId, fixture.TraineeId, new Dictionary<string, object?>
        {
            ["photoId"] = "not-a-photo-id",
            ["storageKey"] = fixture.Photos[0].StorageKey,
            ["readUrl"] = "https://expired.example/read",
            ["thumbnailUrl"] = "https://expired.example/thumb"
        }));

        var result = await fixture.Service.GetOwnSubmissionsAsync(ReportingTestData.Account(fixture.TraineeId));

        var unresolved = GetOnlyPhoto(result.Value.Single());
        unresolved.GetProperty("readUrl").ValueKind.Should().Be(JsonValueKind.Null);
        unresolved.GetProperty("thumbnailUrl").ValueKind.Should().Be(JsonValueKind.Null);
        await fixture.Storage.DidNotReceive().GenerateSignedReadUrlAsync(fixture.Photos[0].StorageKey, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetOwnSubmissionsAsync_WhenCanonicalPhotoIsMissing_DegradesWithoutSigningPayloadValues()
    {
        var fixture = CreateFixture(photos: []);
        fixture.ReturnSubmissions(CreateSubmission(fixture.RequestId, fixture.TraineeId, new Dictionary<string, object?>
        {
            ["photoId"] = Id<Photo>.New().ToString(),
            ["storageKey"] = "photos/missing.jpg",
            ["readUrl"] = "https://expired.example/read"
        }));

        var result = await fixture.Service.GetOwnSubmissionsAsync(ReportingTestData.Account(fixture.TraineeId));

        result.IsSuccess.Should().BeTrue();
        GetOnlyPhoto(result.Value.Single()).GetProperty("readUrl").ValueKind.Should().Be(JsonValueKind.Null);
        await fixture.Storage.DidNotReceive().GenerateSignedReadUrlAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetOwnSubmissionsAsync_WhenLegacyPayloadHasMatchingStorageKey_ReturnsFreshCanonicalUrl()
    {
        var fixture = CreateFixture();
        var photo = fixture.Photos[0];
        fixture.ReturnSubmissions(CreateSubmission(fixture.RequestId, fixture.TraineeId, new Dictionary<string, object?>
        {
            ["storageKey"] = photo.StorageKey,
            ["readUrl"] = "https://expired.example/read"
        }, photoAsObject: true));

        var result = await fixture.Service.GetOwnSubmissionsAsync(ReportingTestData.Account(fixture.TraineeId));

        result.Value.Single().Answers["photos"].GetProperty("readUrl").GetString().Should().Be($"fresh:{photo.StorageKey}");
    }

    [Test]
    public async Task GetOwnSubmissionsAsync_WhenMultipleReportsContainPhotos_LoadsCanonicalPhotosOnce()
    {
        var traineeId = Id<User>.New();
        var firstRequestId = Id<ReportRequest>.New();
        var secondRequestId = Id<ReportRequest>.New();
        var photos = new[]
        {
            CreatePhoto(firstRequestId, traineeId, "photos/first.jpg"),
            CreatePhoto(secondRequestId, traineeId, "photos/second.jpg")
        };
        var fixture = new Fixture(traineeId, firstRequestId, photos);
        fixture.ReturnSubmissions(
            CreateSubmission(firstRequestId, traineeId, new Dictionary<string, object?> { ["photoId"] = photos[0].Id.ToString() }),
            CreateSubmission(secondRequestId, traineeId, new Dictionary<string, object?> { ["photoId"] = photos[1].Id.ToString() }));

        await fixture.Service.GetOwnSubmissionsAsync(ReportingTestData.Account(traineeId));

        await fixture.PhotoPersistence.Received(1).ListByRequestsAsync(
            Arg.Is<IReadOnlyCollection<Id<ReportRequest>>>(ids => ids.Count == 2 && ids.Contains(firstRequestId) && ids.Contains(secondRequestId)),
            Arg.Any<CancellationToken>());
    }

    private static Fixture CreateFixture(string? thumbnailStorageKey = "photos/thumb.jpg", IReadOnlyList<ReportPhotoPersistenceModel>? photos = null)
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        photos ??= [CreatePhoto(requestId, traineeId, "photos/canonical.jpg", thumbnailStorageKey)];
        return new Fixture(traineeId, requestId, photos);
    }

    private static ReportPhotoPersistenceModel CreatePhoto(
        Id<ReportRequest> requestId,
        Id<User> traineeId,
        string storageKey,
        string? thumbnailStorageKey = null)
        => new(
            Id<Photo>.New(),
            storageKey,
            "image/jpeg",
            1024,
            "etag",
            thumbnailStorageKey,
            PhotoViewType.Front.ToString(),
            requestId,
            ReportingTestData.AccountId(traineeId),
            ReportingTestData.AccountId(traineeId),
            DateTimeOffset.UtcNow,
            false);

    private static ReportSubmissionPersistenceModel CreateSubmission(
        Id<ReportRequest> requestId,
        Id<User> traineeId,
        Dictionary<string, object?> photo,
        bool photoAsObject = false)
    {
        var now = DateTimeOffset.UtcNow;
        var templateId = Id<ReportTemplate>.New();
        var template = new ReportTemplatePersistenceModel(
            templateId,
            Id<AccountReference>.New(),
            "Photo report",
            null,
            now,
            false,
            [new ReportTemplateFieldPersistenceModel(Id<ReportTemplateField>.New(), "photos", "Photos", ReportFieldType.Photos, false, 1, null, now)]);
        var request = new ReportRequestPersistenceModel(
            requestId,
            template.TrainerId,
            ReportingTestData.AccountId(traineeId),
            templateId,
            null,
            ReportRequestStatus.Submitted,
            null,
            now,
            null,
            now,
            false,
            template,
            null);
        return new ReportSubmissionPersistenceModel(
            Id<ReportSubmission>.New(),
            requestId,
            ReportingTestData.AccountId(traineeId),
            JsonSerializer.Serialize(new Dictionary<string, object?> { ["photos"] = photoAsObject ? photo : new[] { photo } }),
            null,
            null,
            null,
            null,
            now,
            request);
    }

    private static JsonElement GetOnlyPhoto(ReportSubmissionResult submission)
        => submission.Answers["photos"].EnumerateArray().Single();

    private sealed class Fixture
    {
        private readonly IReportRequestSubmissionPersistence _submissions;
        private readonly IReportPhotoPersistence _photos;

        public Fixture(Id<User> traineeId, Id<ReportRequest> requestId, IReadOnlyList<ReportPhotoPersistenceModel> photos)
        {
            TraineeId = traineeId;
            RequestId = requestId;
            Photos = photos;
            _submissions = Substitute.For<IReportRequestSubmissionPersistence>();
            _photos = Substitute.For<IReportPhotoPersistence>();
            Storage = Substitute.For<IPhotoStorageProvider>();
            _photos.ListByRequestAsync(requestId, Arg.Any<CancellationToken>()).Returns(photos);
            _photos.ListByRequestsAsync(Arg.Any<IReadOnlyCollection<Id<ReportRequest>>>(), Arg.Any<CancellationToken>())
                .Returns(call => Photos.Where(photo => call.ArgAt<IReadOnlyCollection<Id<ReportRequest>>>(0).Contains(photo.ReportRequestId)).ToList());
            Storage.GenerateSignedReadUrlAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
                .Returns(call => $"fresh:{call.ArgAt<string>(0)}");
            Service = new ReportingService(
                Substitute.For<IReportTemplatePersistence>(),
                _submissions,
                Substitute.For<IRecurringReportAssignmentPersistence>(),
                _photos,
                Substitute.For<IReportingRelationshipAccessPersistence>(),
                new ReportSubmissionAcceptedProgressCommandFactory(),
                Substitute.For<ICommandDispatcher>(),
                Substitute.For<ICommandOutboxWriter>(),
                Substitute.For<IUnitOfWork>(),
                Storage,
                ReportingTestData.Mapper(),
                Substitute.For<ILogger<ReportingService>>(),
                new PhotoStorageOptions());
        }

        public Id<User> TraineeId { get; }
        public Id<ReportRequest> RequestId { get; }
        public IReadOnlyList<ReportPhotoPersistenceModel> Photos { get; }
        public IReportPhotoPersistence PhotoPersistence => _photos;
        public IPhotoStorageProvider Storage { get; }
        public ReportingService Service { get; }

        public void ReturnSubmissions(params ReportSubmissionPersistenceModel[] submissions)
            => _submissions.ListSubmissionsByTraineeAsync(ReportingTestData.AccountId(TraineeId), Arg.Any<CancellationToken>())
                .Returns(submissions);
    }
}
