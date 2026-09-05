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
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ReportSubmissionPhotoHydrationFailureTests
{
    [Test]
    public async Task SubmitReportRequestAsync_WhenPhotoHydrationFails_DoesNotCommitSubmission()
    {
        var traineeId = Id<User>.New();
        var request = CreatePhotoRequest(traineeId);
        var requestPersistence = Substitute.For<IReportRequestSubmissionPersistence>();
        requestPersistence.FindRequestByIdAsync(request.Id, Arg.Any<CancellationToken>())
            .Returns(ReportingTestData.Request(request));
        var photoPersistence = CreateFailingPhotoPersistence();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var service = CreateService(requestPersistence, photoPersistence, unitOfWork);
        var command = new SubmitReportRequestCommand
        {
            Answers = new Dictionary<string, JsonElement>
            {
                ["photos"] = JsonSerializer.SerializeToElement(new[] { new { photoId = Id<Photo>.New().ToString() } })
            }
        };

        var action = () => service.SubmitReportRequestAsync(ReportingTestData.Account(traineeId), request.Id, command);

        await action.Should().ThrowAsync<InvalidOperationException>();
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MarkTrainerFeedbackAsReadAsync_WhenPhotoHydrationFails_DoesNotCommitReadState()
    {
        var traineeId = Id<User>.New();
        var request = CreatePhotoRequest(traineeId);
        var submission = new ReportSubmission
        {
            Id = Id<ReportSubmission>.New(),
            ReportRequestId = request.Id,
            ReportRequest = request,
            TraineeId = traineeId,
            PayloadJson = JsonSerializer.Serialize(new
            {
                photos = new[] { new { photoId = Id<Photo>.New().ToString() } }
            }),
            TrainerFeedbackAddedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var requestPersistence = Substitute.For<IReportRequestSubmissionPersistence>();
        requestPersistence.FindSubmissionForTraineeAsync(
                submission.Id,
                ReportingTestData.AccountId(traineeId),
                Arg.Any<CancellationToken>())
            .Returns(ReportingTestData.Submission(submission));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var service = CreateService(requestPersistence, CreateFailingPhotoPersistence(), unitOfWork);

        var action = () => service.MarkTrainerFeedbackAsReadAsync(ReportingTestData.Account(traineeId), submission.Id);

        await action.Should().ThrowAsync<InvalidOperationException>();
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static ReportRequest CreatePhotoRequest(Id<User> traineeId)
    {
        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = Id<User>.New(),
            Name = "Photo report",
            Fields =
            [
                new ReportTemplateField
                {
                    Id = Id<ReportTemplateField>.New(),
                    Key = "photos",
                    Label = "Photos",
                    Type = ReportFieldType.Photos,
                    IsRequired = false,
                    Order = 0
                }
            ]
        };
        return new ReportRequest
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = template.TrainerId,
            TraineeId = traineeId,
            TemplateId = template.Id,
            Template = template,
            Status = ReportRequestStatus.Pending
        };
    }

    private static IReportPhotoPersistence CreateFailingPhotoPersistence()
    {
        var persistence = Substitute.For<IReportPhotoPersistence>();
        persistence.ListByRequestsAsync(
                Arg.Any<IReadOnlyCollection<Id<ReportRequest>>>(),
                Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ReportPhotoPersistenceModel>>(_ => throw new InvalidOperationException("Photo hydration failed"));
        return persistence;
    }

    private static ReportingService CreateService(
        IReportRequestSubmissionPersistence requestPersistence,
        IReportPhotoPersistence photoPersistence,
        IUnitOfWork unitOfWork)
        => new(
            Substitute.For<IReportTemplatePersistence>(),
            requestPersistence,
            Substitute.For<IRecurringReportAssignmentPersistence>(),
            photoPersistence,
            Substitute.For<IReportingRelationshipAccessPersistence>(),
            new ReportSubmissionAcceptedProgressCommandFactory(),
            Substitute.For<ICommandDispatcher>(),
            Substitute.For<ICommandOutboxWriter>(),
            unitOfWork,
            Substitute.For<IPhotoStorageProvider>(),
            ReportingTestData.Mapper(),
            Substitute.For<ILogger<ReportingService>>(),
            new PhotoStorageOptions());
}
