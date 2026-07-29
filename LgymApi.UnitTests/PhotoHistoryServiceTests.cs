using FluentAssertions;
using LgymApi.Application.Abstractions.Storage;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Repositories;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PhotoHistoryServiceTests
{
    [Test]
    public async Task GetPhotoHistoryAsync_WhenNonOwnerAndNonTrainer_ReturnsForbidden()
    {
        var traineeId = Id<User>.New();
        var otherUserId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var otherUser = PhotoServiceTestFactory.CreateUser(otherUserId, "other@example.com");
        var request = PhotoServiceTestFactory.CreateReportRequest(requestId, traineeId);
        var storageProvider = Substitute.For<IPhotoStorageProvider>();
        var service = PhotoServiceTestFactory.CreateService(
            findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request),
            relationshipAccess: (_, _, _) => Task.FromResult(false),
            photoStorageProvider: storageProvider);

        var result = await service.GetPhotoHistoryAsync(otherUser, new GetPhotoHistoryCommand { TraineeId = ReportingTestData.AccountId(traineeId), RequestId = requestId });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingForbiddenError>();
        await storageProvider.DidNotReceive().GenerateSignedReadUrlAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetPhotoHistoryAsync_WhenNonAssignedTrainer_ReturnsForbidden()
    {
        var traineeId = Id<User>.New();
        var trainerId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var trainer = PhotoServiceTestFactory.CreateUser(trainerId, "non-assigned-trainer@example.com");
        var request = PhotoServiceTestFactory.CreateReportRequest(requestId, traineeId);
        var storageProvider = Substitute.For<IPhotoStorageProvider>();
        var service = PhotoServiceTestFactory.CreateService(
            findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request),
            relationshipAccess: (_, _, _) => Task.FromResult(false),
            photoStorageProvider: storageProvider);

        var result = await service.GetPhotoHistoryAsync(trainer, new GetPhotoHistoryCommand { TraineeId = ReportingTestData.AccountId(traineeId), RequestId = requestId });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingNotFoundError>();
        await storageProvider.DidNotReceive().GenerateSignedReadUrlAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetPhotoHistoryAsync_WhenOwner_ReturnsHistory()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var trainee = PhotoServiceTestFactory.CreateUser(traineeId, "trainee@example.com");
        var request = PhotoServiceTestFactory.CreateReportRequest(requestId, traineeId);
        var photos = new List<Photo> { new() { Id = Id<Photo>.New(), ReportRequestId = requestId, OwnerUserId = traineeId, UploaderUserId = traineeId, ViewType = PhotoViewType.Front.ToString(), StorageKey = "photos/front.jpg", MimeType = "image/jpeg", SizeBytes = 1024, Checksum = "abc123", IsDeleted = false } };

        var repo = Substitute.For<IReportPhotoPersistence>();
        repo.ListByRequestAsync(requestId, Arg.Any<CancellationToken>()).Returns(photos.Select(ReportingTestData.Photo).ToList());
        var storageProvider = Substitute.For<IPhotoStorageProvider>();
        storageProvider.GenerateSignedReadUrlAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns("https://storage.example.com/read-url");

        var service = PhotoServiceTestFactory.CreateService(findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request), reportingRepository: repo, photoStorageProvider: storageProvider);
        var result = await service.GetPhotoHistoryAsync(trainee, new GetPhotoHistoryCommand { TraineeId = ReportingTestData.AccountId(traineeId), RequestId = requestId });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].ViewType.Should().Be("Front");
    }

    [Test]
    public async Task GetPhotoHistoryAsync_WhenAssignedTrainer_ReturnsHistory()
    {
        var traineeId = Id<User>.New();
        var trainerId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var trainer = PhotoServiceTestFactory.CreateUser(trainerId, "trainer@example.com");
        var request = PhotoServiceTestFactory.CreateReportRequest(requestId, traineeId);
        var photos = new List<Photo> { new() { Id = Id<Photo>.New(), ReportRequestId = requestId, OwnerUserId = traineeId, UploaderUserId = traineeId, ViewType = PhotoViewType.Side.ToString(), StorageKey = "photos/side.jpg", MimeType = "image/jpeg", SizeBytes = 2048, Checksum = "def456", IsDeleted = false } };

        var repo = Substitute.For<IReportPhotoPersistence>();
        repo.ListByRequestAsync(requestId, Arg.Any<CancellationToken>()).Returns(photos.Select(ReportingTestData.Photo).ToList());
        var storageProvider = Substitute.For<IPhotoStorageProvider>();
        storageProvider.GenerateSignedReadUrlAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns("https://storage.example.com/read-url");

        var service = PhotoServiceTestFactory.CreateService(
            findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request),
            relationshipAccess: (currentTrainerId, currentTraineeId, _) => Task.FromResult(
                currentTrainerId == trainerId && currentTraineeId == traineeId),
            reportingRepository: repo,
            photoStorageProvider: storageProvider);
        var result = await service.GetPhotoHistoryAsync(trainer, new GetPhotoHistoryCommand { TraineeId = ReportingTestData.AccountId(traineeId), RequestId = requestId });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].ViewType.Should().Be("Side");
    }

    [Test]
    public async Task GetPhotoHistoryAsync_WhenNeitherRequestNorTraineeProvided_ReturnsInvalidError()
    {
        var currentUser = PhotoServiceTestFactory.CreateUser(Id<User>.New(), "user@example.com");
        var service = PhotoServiceTestFactory.CreateService();
        var result = await service.GetPhotoHistoryAsync(currentUser, new GetPhotoHistoryCommand());
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
    }

    [Test]
    public async Task GetPhotoHistoryAsync_WhenRequestDoesNotExist_ReturnsInvalidError()
    {
        var currentUser = PhotoServiceTestFactory.CreateUser(Id<User>.New(), "user@example.com");
        var requestId = Id<ReportRequest>.New();
        var service = PhotoServiceTestFactory.CreateService(findRequestById: (_, _) => Task.FromResult<ReportRequest?>(null));
        var result = await service.GetPhotoHistoryAsync(currentUser, new GetPhotoHistoryCommand { RequestId = requestId });
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
    }

    [Test]
    public async Task GetPhotoHistoryAsync_WhenPhotoHasThumbnail_ReturnsThumbnailSignedUrl()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var trainee = PhotoServiceTestFactory.CreateUser(traineeId, "trainee@example.com");
        var request = PhotoServiceTestFactory.CreateReportRequest(requestId, traineeId);
        var photos = new List<Photo> { new() { Id = Id<Photo>.New(), ReportRequestId = requestId, OwnerUserId = traineeId, UploaderUserId = traineeId, ViewType = PhotoViewType.Front.ToString(), StorageKey = "photos/front.jpg", ThumbnailStorageKey = "photos/front-thumb.jpg", MimeType = "image/jpeg", SizeBytes = 1024, Checksum = "abc123", IsDeleted = false } };

        var repo = Substitute.For<IReportPhotoPersistence>();
        repo.ListByRequestAsync(requestId, Arg.Any<CancellationToken>()).Returns(photos.Select(ReportingTestData.Photo).ToList());
        var storageProvider = Substitute.For<IPhotoStorageProvider>();
        storageProvider.GenerateSignedReadUrlAsync("photos/front.jpg", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns("https://storage.example.com/read-url");
        storageProvider.GenerateSignedReadUrlAsync("photos/front-thumb.jpg", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns("https://storage.example.com/thumb-url");

        var service = PhotoServiceTestFactory.CreateService(findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request), reportingRepository: repo, photoStorageProvider: storageProvider);
        var result = await service.GetPhotoHistoryAsync(trainee, new GetPhotoHistoryCommand { RequestId = requestId });

        result.IsSuccess.Should().BeTrue();
        result.Value[0].ThumbnailUrl.Should().Be("https://storage.example.com/thumb-url");
    }
}
