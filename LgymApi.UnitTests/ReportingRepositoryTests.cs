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
public sealed class ReportingRepositoryTests
{
    [Test]
    public async Task RequestPersistence_FiltersPendingAndExpiredAndPreservesOrdering()
    {
        await using var db = CreateDbContext("requests");
        var traineeId = Id<User>.New();
        var template = CreateTemplate(Id<User>.New());
        db.ReportTemplates.Add(template);
        db.ReportRequests.AddRange(
            CreateRequest(template, traineeId, ReportRequestStatus.Pending, DateTimeOffset.UtcNow.AddDays(-1)),
            CreateRequest(template, traineeId, ReportRequestStatus.Expired, DateTimeOffset.UtcNow),
            CreateRequest(template, traineeId, ReportRequestStatus.Submitted, DateTimeOffset.UtcNow.AddDays(1)));
        await db.SaveChangesAsync();
        var persistence = new ReportRequestSubmissionPersistenceRepository(db);

        var results = await persistence.ListPendingOrExpiredByTraineeAsync(traineeId.Rebind<AccountReference>());

        results.Select(result => result.Status).Should().Equal(ReportRequestStatus.Expired, ReportRequestStatus.Pending);
        results.Should().OnlyContain(result => result.Template.Fields.Select(field => field.Key).SequenceEqual(new[] { "a", "b" }));
    }

    [Test]
    public async Task SubmissionPersistence_FiltersByTrainerAndTrainee()
    {
        await using var db = CreateDbContext("submissions");
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var template = CreateTemplate(trainerId);
        var request = CreateRequest(template, traineeId, ReportRequestStatus.Submitted, DateTimeOffset.UtcNow);
        var submission = new ReportSubmission
        {
            Id = Id<ReportSubmission>.New(),
            ReportRequestId = request.Id,
            ReportRequest = request,
            TraineeId = traineeId,
            PayloadJson = "{}"
        };
        db.AddRange(template, request, submission);
        await db.SaveChangesAsync();
        var persistence = new ReportRequestSubmissionPersistenceRepository(db);

        var byTrainer = await persistence.FindSubmissionForTrainerAsync(
            submission.Id,
            trainerId.Rebind<AccountReference>(),
            traineeId.Rebind<AccountReference>());
        var byTrainee = await persistence.ListSubmissionsByTraineeAsync(traineeId.Rebind<AccountReference>());

        byTrainer.Should().NotBeNull();
        byTrainee.Should().ContainSingle();
    }

    [Test]
    public async Task PhotoPersistence_AggregatesRespectSoftDeleteAndCreatedAtFilters()
    {
        await using var db = CreateDbContext("photo-aggregates");
        var now = DateTimeOffset.UtcNow;
        db.Photos.AddRange(
            CreatePhoto(100, false, now.AddMinutes(-10)),
            CreatePhoto(50, true, now.AddMinutes(-5)),
            CreatePhoto(25, false, now.AddDays(-2)));
        await db.SaveChangesAsync();
        var persistence = new ReportPhotoPersistenceRepository(db);

        var totalBytes = await persistence.GetActiveStorageBytesAsync();
        var recentCount = await persistence.CountCreatedSinceAsync(now.AddHours(-1));

        totalBytes.Should().Be(125);
        recentCount.Should().Be(2);
    }

    private static AppDbContext CreateDbContext(string name)
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"reporting-{name}-{Id<ReportingRepositoryTests>.New():N}")
            .Options);

    private static ReportTemplate CreateTemplate(Id<User> trainerId)
    {
        var templateId = Id<ReportTemplate>.New();
        return new ReportTemplate
        {
            Id = templateId,
            TrainerId = trainerId,
            Name = "Weekly",
            Fields =
            [
                new ReportTemplateField { Id = Id<ReportTemplateField>.New(), TemplateId = templateId, Key = "b", Order = 2 },
                new ReportTemplateField { Id = Id<ReportTemplateField>.New(), TemplateId = templateId, Key = "a", Order = 1 }
            ]
        };
    }

    private static ReportRequest CreateRequest(
        ReportTemplate template,
        Id<User> traineeId,
        ReportRequestStatus status,
        DateTimeOffset createdAt)
        => new()
        {
            Id = Id<ReportRequest>.New(),
            TraineeId = traineeId,
            TrainerId = template.TrainerId,
            TemplateId = template.Id,
            Template = template,
            Status = status,
            CreatedAt = createdAt
        };

    private static Photo CreatePhoto(long sizeBytes, bool isDeleted, DateTimeOffset createdAt)
        => new()
        {
            Id = Id<Photo>.New(),
            ReportRequestId = Id<ReportRequest>.New(),
            OwnerUserId = Id<User>.New(),
            UploaderUserId = Id<User>.New(),
            ViewType = PhotoViewType.Front.ToString(),
            StorageKey = $"photos/{sizeBytes}.jpg",
            MimeType = "image/jpeg",
            SizeBytes = sizeBytes,
            Checksum = "etag",
            IsDeleted = isDeleted,
            CreatedAt = createdAt
        };
}
