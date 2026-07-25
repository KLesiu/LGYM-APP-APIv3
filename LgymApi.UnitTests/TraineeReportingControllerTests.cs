using System.Text.Json;
using FluentAssertions;
using LgymApi.Api;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Features.Trainer.Controllers;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TraineeReportingControllerTests
{
    [Test]
    public async Task SubmitRequest_WithInvalidRequestId_ReturnsBadRequest()
    {
        var controller = CreateController(Substitute.For<IReportingService>());

        var result = await controller.SubmitRequest("bad-id", new SubmitReportRequestRequest());

        result.Should().BeAssignableTo<ObjectResult>();
        ((ObjectResult)result).StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task SubmitRequest_WithValidRequest_ForwardsParsedIdAndAnswers()
    {
        var reportingService = Substitute.For<IReportingService>();
        Id<ReportRequest> capturedRequestId = Id<ReportRequest>.Empty;
        SubmitReportRequestCommand? capturedCommand = null;
        var requestId = Id<ReportRequest>.New();
        var submissionId = Id<ReportSubmission>.New();
        reportingService
            .SubmitReportRequestAsync(Arg.Any<User>(), Arg.Do<Id<ReportRequest>>(id => capturedRequestId = id), Arg.Do<SubmitReportRequestCommand>(cmd => capturedCommand = cmd), Arg.Any<CancellationToken>())
            .Returns(Result.Success<ReportSubmissionResult, AppError>(CreateSubmissionResult(submissionId, requestId)));

        var controller = CreateController(reportingService);
        var answer = JsonDocument.Parse("{\"value\":42}").RootElement;

        var result = await controller.SubmitRequest(requestId.ToString(), new SubmitReportRequestRequest
        {
            Answers = new Dictionary<string, JsonElement> { ["weight"] = answer }
        });

        result.Should().BeOfType<OkObjectResult>();
        capturedRequestId.Should().Be(requestId);
        capturedCommand.Should().NotBeNull();
        capturedCommand!.Answers.Should().ContainKey("weight");
    }

    [Test]
    public async Task InitiatePhotoUpload_WithInvalidReportRequestId_ReturnsBadRequest()
    {
        var controller = CreateController(Substitute.For<IReportingService>());

        var result = await controller.InitiatePhotoUpload(new InitiatePhotoUploadRequest { ReportRequestId = "bad-id" });

        result.Should().BeAssignableTo<ObjectResult>();
        ((ObjectResult)result).StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task CompletePhotoUpload_WithInvalidReportRequestId_ReturnsBadRequest()
    {
        var controller = CreateController(Substitute.For<IReportingService>());

        var result = await controller.CompletePhotoUpload(new CompletePhotoUploadRequest { ReportRequestId = "bad-id" });

        result.Should().BeAssignableTo<ObjectResult>();
        ((ObjectResult)result).StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task GetPhotoHistory_WithInvalidRequestId_ReturnsBadRequest()
    {
        var controller = CreateController(Substitute.For<IReportingService>());

        var result = await controller.GetPhotoHistory("bad-id");

        result.Should().BeAssignableTo<ObjectResult>();
        ((ObjectResult)result).StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task GetPhotoHistory_WithValidRequest_ForwardsCurrentUserAsTrainee()
    {
        var reportingService = Substitute.For<IReportingService>();
        GetPhotoHistoryCommand? capturedCommand = null;
        var currentUser = CreateUser();
        var requestId = Id<ReportRequest>.New();
        var photoId = Id<Photo>.New();

        reportingService
            .GetPhotoHistoryAsync(Arg.Any<User>(), Arg.Do<GetPhotoHistoryCommand>(cmd => capturedCommand = cmd), Arg.Any<CancellationToken>())
            .Returns(Result.Success<List<PhotoHistoryItemResult>, AppError>(
            [
                new PhotoHistoryItemResult
                {
                    Id = photoId,
                    ViewType = "Front",
                    SizeBytes = 100,
                    ReadUrl = "https://read.example.com",
                    ReportRequestId = requestId,
                    UploadedAt = DateTimeOffset.UtcNow
                }
            ]));

        var controller = CreateController(reportingService, currentUser);

        var result = await controller.GetPhotoHistory(requestId.ToString());

        result.Should().BeOfType<OkObjectResult>();
        capturedCommand.Should().NotBeNull();
        capturedCommand!.TraineeId.Should().Be(currentUser.Id);
        capturedCommand.RequestId.Should().Be(requestId);
    }

    [Test]
    public async Task GetPendingRequests_ReturnsMappedDtos()
    {
        var reportingService = Substitute.For<IReportingService>();
        var currentUser = CreateUser();
        var requestId = Id<ReportRequest>.New();
        reportingService
            .GetPendingRequestsForTraineeAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<List<ReportRequestResult>, AppError>(
            [
                new ReportRequestResult
                {
                    Id = requestId,
                    TrainerId = Id<User>.New(),
                    TraineeId = currentUser.Id,
                    TemplateId = Id<ReportTemplate>.New(),
                    Status = ReportRequestStatus.Pending,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Template = new ReportTemplateResult
                    {
                        Id = Id<ReportTemplate>.New(),
                        TrainerId = Id<User>.New(),
                        Name = "Weekly",
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                }
            ]));

        var controller = CreateController(reportingService, currentUser);

        var result = await controller.GetPendingRequests();

        result.Should().BeOfType<OkObjectResult>();
        var payload = ((OkObjectResult)result).Value.Should().BeAssignableTo<IEnumerable<ReportRequestDto>>().Subject;
        payload.Should().ContainSingle();
    }

    [Test]
    public async Task GetPendingRequests_WhenServiceFails_ReturnsMappedErrorResult()
    {
        var reportingService = Substitute.For<IReportingService>();
        reportingService.GetPendingRequestsForTraineeAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<List<ReportRequestResult>, AppError>(new InvalidReportingError("bad request")));
        var controller = CreateController(reportingService);

        var result = await controller.GetPendingRequests();

        result.Should().BeAssignableTo<ObjectResult>();
    }

    [Test]
    public async Task SubmitRequest_WhenServiceFails_ReturnsMappedErrorResult()
    {
        var reportingService = Substitute.For<IReportingService>();
        var requestId = Id<ReportRequest>.New();
        reportingService.SubmitReportRequestAsync(Arg.Any<User>(), requestId, Arg.Any<SubmitReportRequestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ReportSubmissionResult, AppError>(new InvalidReportingError("bad request")));
        var controller = CreateController(reportingService);

        var result = await controller.SubmitRequest(requestId.ToString(), new SubmitReportRequestRequest { Answers = [] });

        result.Should().BeAssignableTo<ObjectResult>();
    }

    [Test]
    public async Task GetOwnSubmissions_ReturnsMappedDtos()
    {
        var reportingService = Substitute.For<IReportingService>();
        var requestId = Id<ReportRequest>.New();
        var submissionId = Id<ReportSubmission>.New();
        reportingService.GetOwnSubmissionsAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<List<ReportSubmissionResult>, AppError>([CreateSubmissionResult(submissionId, requestId)]));
        var controller = CreateController(reportingService);

        var result = await controller.GetOwnSubmissions();

        result.Should().BeOfType<OkObjectResult>();
        ((OkObjectResult)result).Value.Should().BeAssignableTo<IEnumerable<ReportSubmissionDto>>();
    }

    [Test]
    public async Task MarkFeedbackRead_WithValidId_ForwardsParsedIdAndReturnsOk()
    {
        var reportingService = Substitute.For<IReportingService>();
        var requestId = Id<ReportRequest>.New();
        var submissionId = Id<ReportSubmission>.New();
        Id<ReportSubmission> capturedSubmissionId = Id<ReportSubmission>.Empty;
        reportingService.MarkTrainerFeedbackAsReadAsync(Arg.Any<User>(), Arg.Do<Id<ReportSubmission>>(id => capturedSubmissionId = id), Arg.Any<CancellationToken>())
            .Returns(Result.Success<ReportSubmissionResult, AppError>(CreateSubmissionResult(submissionId, requestId)));
        var controller = CreateController(reportingService);

        var result = await controller.MarkFeedbackRead(submissionId.ToString());

        result.Should().BeOfType<OkObjectResult>();
        capturedSubmissionId.Should().Be(submissionId);
    }

    [Test]
    public async Task MarkFeedbackRead_WhenServiceFails_ReturnsMappedErrorResult()
    {
        var reportingService = Substitute.For<IReportingService>();
        var submissionId = Id<ReportSubmission>.New();
        reportingService.MarkTrainerFeedbackAsReadAsync(Arg.Any<User>(), submissionId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ReportSubmissionResult, AppError>(new InvalidReportingError("bad request")));
        var controller = CreateController(reportingService);

        var result = await controller.MarkFeedbackRead(submissionId.ToString());

        result.Should().BeAssignableTo<ObjectResult>();
    }

    [Test]
    public async Task InitiatePhotoUpload_WithValidRequest_ReturnsMappedResponse()
    {
        var reportingService = Substitute.For<IReportingService>();
        var requestId = Id<ReportRequest>.New();
        reportingService.InitiatePhotoUploadAsync(Arg.Any<User>(), Arg.Is<InitiatePhotoUploadCommand>(cmd => cmd.ReportRequestId == requestId), Arg.Any<CancellationToken>())
            .Returns(Result.Success<InitiatePhotoUploadResult, AppError>(new InitiatePhotoUploadResult
            {
                StorageKey = "photos/key.jpg",
                UploadUrl = "https://upload.example.com",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
            }));
        var controller = CreateController(reportingService);

        var result = await controller.InitiatePhotoUpload(new InitiatePhotoUploadRequest
        {
            ReportRequestId = requestId.ToString(),
            ViewType = "Front",
            MimeType = "image/jpeg",
            SizeBytes = 1234
        });

        result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task CompletePhotoUpload_WithValidRequest_ReturnsMappedResponse()
    {
        var reportingService = Substitute.For<IReportingService>();
        var requestId = Id<ReportRequest>.New();
        reportingService.CompletePhotoUploadAsync(Arg.Any<User>(), Arg.Is<CompletePhotoUploadCommand>(cmd => cmd.ReportRequestId == requestId), Arg.Any<CancellationToken>())
            .Returns(Result.Success<CompletePhotoUploadResult, AppError>(new CompletePhotoUploadResult
            {
                PhotoId = Id<Photo>.New(),
                UploadedAt = DateTimeOffset.UtcNow
            }));
        var controller = CreateController(reportingService);

        var result = await controller.CompletePhotoUpload(new CompletePhotoUploadRequest
        {
            ReportRequestId = requestId.ToString(),
            StorageKey = "photos/key.jpg",
            MimeType = "image/jpeg",
            SizeBytes = 1234,
            Checksum = "etag",
            ViewType = "Front"
        });

        result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task GetPhotoHistory_WhenServiceFails_ReturnsMappedErrorResult()
    {
        var reportingService = Substitute.For<IReportingService>();
        reportingService.GetPhotoHistoryAsync(Arg.Any<User>(), Arg.Any<GetPhotoHistoryCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<List<PhotoHistoryItemResult>, AppError>(new InvalidReportingError("bad request")));
        var controller = CreateController(reportingService);

        var result = await controller.GetPhotoHistory(null);

        result.Should().BeAssignableTo<ObjectResult>();
    }

    private static ReportSubmissionResult CreateSubmissionResult(Id<ReportSubmission> submissionId, Id<ReportRequest> requestId)
        => new()
        {
            Id = submissionId,
            ReportRequestId = requestId,
            TraineeId = Id<User>.New(),
            SubmittedAt = DateTimeOffset.UtcNow,
            Request = new ReportRequestResult
            {
                Id = requestId,
                TrainerId = Id<User>.New(),
                TraineeId = Id<User>.New(),
                TemplateId = Id<ReportTemplate>.New(),
                Status = ReportRequestStatus.Submitted,
                CreatedAt = DateTimeOffset.UtcNow,
                Template = new ReportTemplateResult
                {
                    Id = Id<ReportTemplate>.New(),
                    TrainerId = Id<User>.New(),
                    Name = "Weekly",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            }
        };

    private static User CreateUser()
        => new()
        {
            Id = Id<User>.New(),
            Name = "Trainee",
            Email = "trainee@example.com",
            ProfileRank = "Rookie"
        };

    private static TraineeReportingController CreateController(IReportingService reportingService, User? user = null)
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(typeof(Program).Assembly, typeof(IMappingProfile).Assembly);
        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        var controller = new TraineeReportingController(reportingService, mapper)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.HttpContext.Items["User"] = user ?? CreateUser();
        return controller;
    }
}
