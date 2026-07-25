using FluentAssertions;
using LgymApi.Application.Abstractions.Storage;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PhotoCompleteUploadServiceTests
{
    [Test]
    public async Task CompletePhotoUploadAsync_WhenDuplicateFinalize_ShouldSoftDeleteOldPhoto()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var currentUser = PhotoServiceTestFactory.CreateUser(traineeId, "trainee@example.com");
        var request = PhotoServiceTestFactory.CreateReportRequest(requestId, traineeId);

        var oldPhoto = new Photo { Id = Id<Photo>.New(), ReportRequestId = requestId, OwnerUserId = traineeId, UploaderUserId = traineeId, ViewType = PhotoViewType.Front.ToString(), StorageKey = "photos/old-front.jpg", MimeType = "image/jpeg", SizeBytes = 1024, Checksum = "oldchecksum", IsDeleted = false };
        var existingPhotos = new List<Photo> { oldPhoto };
        Photo? savedPhoto = null;

        var repo = Substitute.For<IReportingRepository>();
        repo.FindRequestByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(request);
        repo.GetPhotosByRequestIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(existingPhotos);
        repo.FindActivePhotoByRequestAndViewAsync(requestId, PhotoViewType.Front.ToString(), Arg.Any<CancellationToken>()).Returns(oldPhoto);
        repo.SavePhotoAsync(Arg.Do<Photo>(p => savedPhoto = p), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var storageProvider = Substitute.For<IPhotoStorageProvider>();
        storageProvider.GetMetadataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new PhotoMetadata { ContentType = "image/jpeg", SizeBytes = 2048, ETag = "newchecksum", UploadedAt = DateTimeOffset.UtcNow });
        var pendingUpload = new PendingPhotoUpload { StorageKey = $"photos/{traineeId}/{requestId}/Front/new-photo.jpg", InitiatedByUserId = traineeId, OwnerUserId = traineeId, ReportRequestId = requestId, ViewType = PhotoViewType.Front.ToString(), DeclaredContentType = "image/jpeg", DeclaredSizeBytes = 2048, CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };

        var service = PhotoServiceTestFactory.CreateService(reportingRepository: repo, photoStorageProvider: storageProvider, pendingUpload: pendingUpload);
        var result = await service.CompletePhotoUploadAsync(currentUser, new CompletePhotoUploadCommand { ReportRequestId = requestId, ViewType = "Front", StorageKey = $"photos/{traineeId}/{requestId}/Front/new-photo.jpg", MimeType = "image/jpeg", SizeBytes = 2048, Checksum = "newchecksum" });

        result.IsSuccess.Should().BeTrue();
        savedPhoto.Should().NotBeNull();
        savedPhoto!.ViewType.Should().Be(PhotoViewType.Front.ToString());
        savedPhoto.Checksum.Should().Be("newchecksum");
    }

    [Test]
    public async Task CompletePhotoUploadAsync_WhenMetadataDoesNotMatchClientSize_ReturnsInvalidError()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var currentUser = PhotoServiceTestFactory.CreateUser(traineeId, "trainee@example.com");
        var request = PhotoServiceTestFactory.CreateReportRequest(requestId, traineeId);
        var storageProvider = Substitute.For<IPhotoStorageProvider>();
        storageProvider.GetMetadataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new PhotoMetadata { ContentType = "image/jpeg", SizeBytes = 4096, ETag = "etag", UploadedAt = DateTimeOffset.UtcNow });
        var pendingUpload = new PendingPhotoUpload { StorageKey = $"photos/{traineeId}/{requestId}/Front/test.jpg", InitiatedByUserId = traineeId, OwnerUserId = traineeId, ReportRequestId = requestId, ViewType = PhotoViewType.Front.ToString(), DeclaredContentType = "image/jpeg", DeclaredSizeBytes = 2048, CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        var tracker = Substitute.For<IPhotoUploadInitTracker>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var repo = Substitute.For<IReportingRepository>();
        repo.FindRequestByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(request);

        var service = PhotoServiceTestFactory.CreateService(
            reportingRepository: repo,
            photoStorageProvider: storageProvider,
            pendingUpload: pendingUpload,
            photoUploadInitTracker: tracker,
            unitOfWork: unitOfWork);
        var result = await service.CompletePhotoUploadAsync(currentUser, new CompletePhotoUploadCommand { ReportRequestId = requestId, ViewType = "Front", StorageKey = $"photos/{traineeId}/{requestId}/Front/test.jpg", MimeType = "image/jpeg", SizeBytes = 2048, Checksum = "etag" });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
        result.Error.Message.Should().Contain("size");
        await storageProvider.Received(1).GetMetadataAsync(pendingUpload.StorageKey, Arg.Any<CancellationToken>());
        await storageProvider.Received(1).DeleteAsync(pendingUpload.StorageKey, Arg.Any<CancellationToken>());
        await tracker.Received(1).MarkFailedAsync(pendingUpload.StorageKey, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await repo.DidNotReceive().SavePhotoAsync(Arg.Any<Photo>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CompletePhotoUploadAsync_WhenPendingUploadMissing_ReturnsInvalidError()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var currentUser = PhotoServiceTestFactory.CreateUser(traineeId, "trainee@example.com");
        var request = PhotoServiceTestFactory.CreateReportRequest(requestId, traineeId);
        var service = PhotoServiceTestFactory.CreateService(findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request));

        var result = await service.CompletePhotoUploadAsync(currentUser, new CompletePhotoUploadCommand { ReportRequestId = requestId, ViewType = "Front", StorageKey = $"photos/{traineeId}/{requestId}/Front/test.jpg", MimeType = "image/jpeg", SizeBytes = 2048, Checksum = "etag" });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
        result.Error.Message.Should().Contain("Upload session");
    }

    [Test]
    public async Task CompletePhotoUploadAsync_WhenNumericViewTypeIsUndefined_ReturnsInvalidError()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var currentUser = PhotoServiceTestFactory.CreateUser(traineeId, "trainee@example.com");
        var request = PhotoServiceTestFactory.CreateReportRequest(requestId, traineeId);
        var service = PhotoServiceTestFactory.CreateService(findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request));

        var result = await service.CompletePhotoUploadAsync(currentUser, new CompletePhotoUploadCommand { ReportRequestId = requestId, ViewType = "999", StorageKey = $"photos/{traineeId}/{requestId}/Front/test.jpg", MimeType = "image/jpeg", SizeBytes = 2048, Checksum = "etag" });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
        result.Error.Message.Should().Contain("Invalid view type");
    }

    [Test]
    public async Task CompletePhotoUploadAsync_WhenMetadataSizeIsSmallerThanInitiated_ReturnsInvalidError()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var currentUser = PhotoServiceTestFactory.CreateUser(traineeId, "trainee@example.com");
        var request = PhotoServiceTestFactory.CreateReportRequest(requestId, traineeId);
        var storageProvider = Substitute.For<IPhotoStorageProvider>();
        storageProvider.GetMetadataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new PhotoMetadata { ContentType = "image/jpeg", SizeBytes = 1024, ETag = "etag", UploadedAt = DateTimeOffset.UtcNow });
        var pendingUpload = new PendingPhotoUpload { StorageKey = $"photos/{traineeId}/{requestId}/Front/test.jpg", InitiatedByUserId = traineeId, OwnerUserId = traineeId, ReportRequestId = requestId, ViewType = PhotoViewType.Front.ToString(), DeclaredContentType = "image/jpeg", DeclaredSizeBytes = 2048, CreatedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };

        var service = PhotoServiceTestFactory.CreateService(findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request), photoStorageProvider: storageProvider, pendingUpload: pendingUpload);
        var result = await service.CompletePhotoUploadAsync(currentUser, new CompletePhotoUploadCommand { ReportRequestId = requestId, ViewType = "Front", StorageKey = $"photos/{traineeId}/{requestId}/Front/test.jpg", MimeType = "image/jpeg", SizeBytes = 2048, Checksum = "etag" });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
        result.Error.Message.Should().Contain("size");
    }

    [Test]
    public async Task CompletePhotoUploadAsync_WhenRequestAlreadySubmitted_ReturnsInvalidError()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var currentUser = PhotoServiceTestFactory.CreateUser(traineeId, "trainee@example.com");
        var request = PhotoServiceTestFactory.CreateReportRequest(requestId, traineeId);
        request.Status = ReportRequestStatus.Submitted;
        var service = PhotoServiceTestFactory.CreateService(findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request));

        var result = await service.CompletePhotoUploadAsync(currentUser, new CompletePhotoUploadCommand { ReportRequestId = requestId, ViewType = "Front", StorageKey = $"photos/{traineeId}/{requestId}/Front/test.jpg", MimeType = "image/jpeg", SizeBytes = 2048, Checksum = "etag" });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
        result.Error.Message.Should().Be(LgymApi.Resources.Messages.ReportRequestNotPending);
    }
}
