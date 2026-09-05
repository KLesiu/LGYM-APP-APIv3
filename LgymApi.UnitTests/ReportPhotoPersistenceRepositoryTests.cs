using FluentAssertions;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories.Reporting;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ReportPhotoPersistenceRepositoryTests
{
    [Test]
    public async Task RecordAndFindUploadSession_PreservesMarkerAccountIds()
    {
        await using var db = CreateDbContext("record");
        var persistence = new ReportPhotoPersistenceRepository(db);
        var pending = CreatePendingUpload("photos/persisted.jpg", DateTimeOffset.UtcNow.AddMinutes(10));

        await persistence.RecordUploadInitAsync(pending);
        await db.SaveChangesAsync();
        var reloaded = await persistence.FindUploadSessionAsync(pending.StorageKey);

        reloaded.Should().NotBeNull();
        reloaded!.InitiatedByAccountId.Should().Be(pending.InitiatedByAccountId);
        reloaded.OwnerAccountId.Should().Be(pending.OwnerAccountId);
        reloaded.ReportRequestId.Should().Be(pending.ReportRequestId);
    }

    [Test]
    public async Task MarkUploadCompleted_StagesCompletionAndClearsFailure()
    {
        await using var db = CreateDbContext("complete");
        var persistence = new ReportPhotoPersistenceRepository(db);
        var pending = CreatePendingUpload("photos/complete.jpg", DateTimeOffset.UtcNow.AddMinutes(10)) with
        {
            Status = PhotoUploadSessionStatus.Failed,
            FailureReason = "temporary"
        };
        await persistence.RecordUploadInitAsync(pending);
        await db.SaveChangesAsync();

        var photoId = Id<Photo>.New();
        var completedAt = DateTimeOffset.UtcNow;
        await persistence.MarkUploadCompletedAsync(pending.StorageKey, photoId, completedAt);

        var entity = await db.PhotoUploadSessions.SingleAsync();
        entity.Status.Should().Be(PhotoUploadSessionStatus.Completed);
        entity.CompletedPhotoId.Should().Be(photoId);
        entity.CompletedAtUtc.Should().Be(completedAt);
        entity.FailureReason.Should().BeNull();
    }

    [Test]
    public async Task CleanupCandidates_ReturnsExpiredPendingAndFailedInExpirationOrder()
    {
        await using var db = CreateDbContext("cleanup");
        var persistence = new ReportPhotoPersistenceRepository(db);
        var now = DateTimeOffset.UtcNow;
        await persistence.RecordUploadInitAsync(CreatePendingUpload("photos/pending.jpg", now.AddMinutes(-30)));
        await persistence.RecordUploadInitAsync(CreatePendingUpload("photos/failed.jpg", now.AddMinutes(-10)) with { Status = PhotoUploadSessionStatus.Failed });
        await persistence.RecordUploadInitAsync(CreatePendingUpload("photos/completed.jpg", now.AddMinutes(-20)) with { Status = PhotoUploadSessionStatus.Completed });
        await persistence.RecordUploadInitAsync(CreatePendingUpload("photos/future.jpg", now.AddMinutes(5)));
        await db.SaveChangesAsync();

        var candidates = await persistence.ListCleanupCandidatesAsync(now);

        candidates.Select(candidate => candidate.StorageKey).Should().Equal("photos/pending.jpg", "photos/failed.jpg");
    }

    [Test]
    public async Task SavePhoto_SoftDeletesExistingActivePhotoAndStagesReplacement()
    {
        await using var db = CreateDbContext("replace");
        var persistence = new ReportPhotoPersistenceRepository(db);
        var requestId = Id<ReportRequest>.New();
        var ownerId = Id<AccountReference>.New();
        var first = NewPhoto(requestId, ownerId, "photos/old.jpg");
        var replacement = NewPhoto(requestId, ownerId, "photos/new.jpg");
        await persistence.SaveAsync(first);
        await db.SaveChangesAsync();

        await persistence.SaveAsync(replacement);
        await db.SaveChangesAsync();

        var rows = await db.Photos.IgnoreQueryFilters().OrderBy(photo => photo.CreatedAt).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Single(photo => photo.Id == first.Id).IsDeleted.Should().BeTrue();
        rows.Single(photo => photo.Id == replacement.Id).IsDeleted.Should().BeFalse();
    }

    [Test]
    public async Task ListByRequests_ReturnsOnlyActivePhotosFromRequestedReports()
    {
        await using var db = CreateDbContext("requests");
        var persistence = new ReportPhotoPersistenceRepository(db);
        var ownerId = Id<AccountReference>.New();
        var firstRequestId = Id<ReportRequest>.New();
        var secondRequestId = Id<ReportRequest>.New();
        var excludedRequestId = Id<ReportRequest>.New();
        await persistence.SaveAsync(NewPhoto(firstRequestId, ownerId, "photos/first.jpg"));
        await persistence.SaveAsync(NewPhoto(secondRequestId, ownerId, "photos/second.jpg"));
        await persistence.SaveAsync(NewPhoto(excludedRequestId, ownerId, "photos/excluded.jpg"));
        await db.SaveChangesAsync();
        db.Photos.Single(photo => photo.ReportRequestId == secondRequestId).IsDeleted = true;
        await db.SaveChangesAsync();

        var photos = await persistence.ListByRequestsAsync([firstRequestId, secondRequestId]);

        photos.Select(photo => photo.StorageKey).Should().Equal("photos/first.jpg");
    }

    private static AppDbContext CreateDbContext(string name)
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"report-photo-{name}-{Id<ReportPhotoPersistenceRepositoryTests>.New():N}")
            .Options);

    private static PendingPhotoUpload CreatePendingUpload(string storageKey, DateTimeOffset expiresAt)
        => new(
            Id<PhotoUploadSession>.New(),
            storageKey,
            Id<AccountReference>.New(),
            Id<AccountReference>.New(),
            Id<ReportRequest>.New(),
            PhotoViewType.Front.ToString(),
            "image/jpeg",
            1024,
            DateTimeOffset.UtcNow.AddMinutes(-30),
            expiresAt,
            null,
            null,
            PhotoUploadSessionStatus.Pending,
            null);

    private static NewReportPhotoPersistenceModel NewPhoto(
        Id<ReportRequest> requestId,
        Id<AccountReference> ownerId,
        string storageKey)
        => new(
            Id<Photo>.New(),
            storageKey,
            "image/jpeg",
            1024,
            "etag",
            null,
            PhotoViewType.Front.ToString(),
            requestId,
            ownerId,
            ownerId,
            DateTimeOffset.UtcNow);
}
