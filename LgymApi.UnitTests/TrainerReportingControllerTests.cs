using FluentAssertions;
using LgymApi.Api;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Features.Trainer.Controllers;
using LgymApi.Api.Middleware;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Reporting.Compatibility;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TrainerReportingControllerTests
{
    [Test]
    public async Task GetTemplate_WithInvalidId_ReturnsBadRequest()
    {
        var controller = CreateController(Substitute.For<IReportingService>());

        var result = await controller.GetTemplate("bad-id");

        result.Should().BeAssignableTo<ObjectResult>();
        ((ObjectResult)result).StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task CreateTemplate_WithValidRequest_ForwardsMappedFieldsAndReturnsCreated()
    {
        var service = Substitute.For<IReportingService>();
        CreateReportTemplateCommand? captured = null;
        var templateResult = CreateTemplateResult();
        service.CreateTemplateAsync(Arg.Any<AuthenticatedAccountContext>(), Arg.Do<CreateReportTemplateCommand>(cmd => captured = cmd), Arg.Any<CancellationToken>())
            .Returns(Result.Success<ReportTemplateResult, AppError>(templateResult));
        var controller = CreateController(service);

        var result = await controller.CreateTemplate(new UpsertReportTemplateRequest
        {
            Name = "Weekly",
            Description = "Desc",
            Fields =
            [
                new ReportTemplateFieldRequest { Key = "weight", Label = "Weight", Type = ReportFieldType.Number, IsRequired = true, Order = 1 }
            ]
        });

        result.Should().BeOfType<ObjectResult>();
        ((ObjectResult)result).StatusCode.Should().Be(StatusCodes.Status201Created);
        captured.Should().NotBeNull();
        captured!.Fields.Should().ContainSingle();
        captured.Fields[0].Key.Should().Be("weight");
    }

    [Test]
    public async Task GetTemplates_ReturnsMappedDtos()
    {
        var service = Substitute.For<IReportingService>();
        service.GetTrainerTemplatesAsync(Arg.Any<AuthenticatedAccountContext>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<List<ReportTemplateResult>, AppError>([CreateTemplateResult()]));
        var controller = CreateController(service);

        var result = await controller.GetTemplates();

        var payload = ((OkObjectResult)result).Value.Should().BeAssignableTo<IEnumerable<ReportTemplateDto>>().Subject;
        payload.Should().ContainSingle();
    }

    [Test]
    public async Task UpdateTemplate_WithValidRequest_ForwardsParsedId()
    {
        var service = Substitute.For<IReportingService>();
        var templateId = Id<ReportTemplate>.New();
        Id<ReportTemplate> capturedId = Id<ReportTemplate>.Empty;
        service.UpdateTemplateAsync(Arg.Any<AuthenticatedAccountContext>(), Arg.Do<Id<ReportTemplate>>(id => capturedId = id), Arg.Any<CreateReportTemplateCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<ReportTemplateResult, AppError>(CreateTemplateResult(templateId)));
        var controller = CreateController(service);

        var result = await controller.UpdateTemplate(templateId.ToString(), new UpsertReportTemplateRequest { Name = "Weekly", Fields = [] });

        result.Should().BeOfType<OkObjectResult>();
        capturedId.Should().Be(templateId);
    }

    [Test]
    public async Task DeleteTemplate_WithValidId_ReturnsDeletedMessage()
    {
        var service = Substitute.For<IReportingService>();
        service.DeleteTemplateAsync(Arg.Any<AuthenticatedAccountContext>(), Arg.Any<Id<ReportTemplate>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<Unit, AppError>(Unit.Value));
        var controller = CreateController(service);

        var result = await controller.DeleteTemplate(Id<ReportTemplate>.New().ToString());

        result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task CreateReportRequest_WithInvalidIds_ReturnsBadRequest()
    {
        var controller = CreateController(Substitute.For<IReportingService>());

        (await controller.CreateReportRequest("bad-user", new CreateReportRequestRequest { TemplateId = Id<ReportTemplate>.New().ToString() }))
            .Should().BeAssignableTo<ObjectResult>();
        (await controller.CreateReportRequest(Id<User>.New().ToString(), new CreateReportRequestRequest { TemplateId = "bad-template" }))
            .Should().BeAssignableTo<ObjectResult>();
    }

    [Test]
    public async Task CreateReportRequest_WithValidRequest_ForwardsParsedIds()
    {
        var service = Substitute.For<IReportingService>();
        var traineeId = Id<User>.New();
        var templateId = Id<ReportTemplate>.New();
        Id<AccountReference> capturedTraineeId = Id<AccountReference>.Empty;
        CreateReportRequestCommand? capturedCommand = null;
        service.CreateReportRequestAsync(Arg.Any<AuthenticatedAccountContext>(), Arg.Do<Id<AccountReference>>(id => capturedTraineeId = id), Arg.Do<CreateReportRequestCommand>(cmd => capturedCommand = cmd), Arg.Any<CancellationToken>())
            .Returns(Result.Success<ReportRequestResult, AppError>(CreateRequestResult(traineeId, templateId)));
        var controller = CreateController(service);

        var result = await controller.CreateReportRequest(traineeId.ToString(), new CreateReportRequestRequest
        {
            TemplateId = templateId.ToString(),
            Note = "note"
        });

        ((ObjectResult)result).StatusCode.Should().Be(StatusCodes.Status201Created);
        capturedTraineeId.Should().Be(traineeId.Rebind<AccountReference>());
        capturedCommand!.TemplateId.Should().Be(templateId);
    }

    [Test]
    public async Task GetTraineeSubmissions_WithInvalidTraineeId_ReturnsBadRequest()
    {
        var controller = CreateController(Substitute.For<IReportingService>());

        var result = await controller.GetTraineeSubmissions("bad-user");

        result.Should().BeAssignableTo<ObjectResult>();
        ((ObjectResult)result).StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task UpdateSubmissionFeedback_WithValidRequest_NormalizesFieldCommentsAndReturnsOk()
    {
        var service = Substitute.For<IReportingService>();
        var traineeId = Id<User>.New();
        var submissionId = Id<ReportSubmission>.New();
        UpdateReportSubmissionFeedbackCommand? captured = null;
        service.UpdateTrainerFeedbackAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), submissionId, Arg.Do<UpdateReportSubmissionFeedbackCommand>(cmd => captured = cmd), Arg.Any<CancellationToken>())
            .Returns(Result.Success<ReportSubmissionResult, AppError>(CreateSubmissionResult(traineeId)));
        var controller = CreateController(service);

        var result = await controller.UpdateSubmissionFeedback(traineeId.ToString(), submissionId.ToString(), new UpdateReportSubmissionFeedbackRequest
        {
            TrainerOverallComment = "overall",
            TrainerFieldComments = new Dictionary<string, string?> { ["Weight"] = "keep going" }
        });

        result.Should().BeOfType<OkObjectResult>();
        captured.Should().NotBeNull();
        captured!.FieldComments.Should().ContainKey("Weight");
    }

    [Test]
    public async Task UpdateSubmissionFeedback_WhenTrainerFieldCommentsIsNull_UsesEmptyDictionary()
    {
        var service = Substitute.For<IReportingService>();
        var traineeId = Id<User>.New();
        var submissionId = Id<ReportSubmission>.New();
        UpdateReportSubmissionFeedbackCommand? captured = null;
        service.UpdateTrainerFeedbackAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), submissionId, Arg.Do<UpdateReportSubmissionFeedbackCommand>(cmd => captured = cmd), Arg.Any<CancellationToken>())
            .Returns(Result.Success<ReportSubmissionResult, AppError>(CreateSubmissionResult(traineeId)));
        var controller = CreateController(service);

        var request = new UpdateReportSubmissionFeedbackRequest
        {
            TrainerOverallComment = "overall",
            TrainerFieldComments = null!
        };

        var result = await controller.UpdateSubmissionFeedback(traineeId.ToString(), submissionId.ToString(), request);

        result.Should().BeOfType<OkObjectResult>();
        captured.Should().NotBeNull();
        captured!.FieldComments.Should().BeEmpty();
    }

    [Test]
    public async Task CreateRecurringReportAssignment_WithInvalidIds_ReturnsBadRequest()
    {
        var controller = CreateController(Substitute.For<IReportingService>());

        (await controller.CreateRecurringReportAssignment("bad-user", new UpsertRecurringReportAssignmentRequest { TemplateId = Id<ReportTemplate>.New().ToString() }))
            .Should().BeAssignableTo<ObjectResult>();
        (await controller.CreateRecurringReportAssignment(Id<User>.New().ToString(), new UpsertRecurringReportAssignmentRequest { TemplateId = "bad-template" }))
            .Should().BeAssignableTo<ObjectResult>();
    }

    [Test]
    public async Task CreateRecurringReportAssignment_WithValidRequest_ForwardsParsedValues()
    {
        var reportingService = Substitute.For<IReportingService>();
        var recurringService = Substitute.For<IRecurringReportAssignmentService>();
        var traineeId = Id<User>.New();
        var templateId = Id<ReportTemplate>.New();
        UpsertRecurringReportAssignmentCommand? captured = null;
        recurringService.CreateAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), Arg.Do<UpsertRecurringReportAssignmentCommand>(cmd => captured = cmd), Arg.Any<CancellationToken>())
            .Returns(Result.Success<RecurringReportAssignmentResult, AppError>(CreateAssignmentResult(traineeId, templateId)));
        var controller = CreateController(reportingService, recurringService);
        var startsAt = DateTimeOffset.UtcNow;

        var result = await controller.CreateRecurringReportAssignment(traineeId.ToString(), new UpsertRecurringReportAssignmentRequest
        {
            TemplateId = templateId.ToString(),
            IntervalValue = 2,
            IntervalUnit = RecurringReportIntervalUnit.Month,
            StartsAt = startsAt,
            EndsAt = startsAt.AddDays(30),
            Note = "note"
        });

        result.Should().BeOfType<ObjectResult>();
        ((ObjectResult)result).StatusCode.Should().Be(StatusCodes.Status201Created);
        captured.Should().NotBeNull();
        captured!.TemplateId.Should().Be(templateId);
        captured.IntervalValue.Should().Be(2);
        captured.IntervalUnit.Should().Be(RecurringReportIntervalUnit.Month);
        captured.Note.Should().Be("note");
    }

    [Test]
    public async Task CreateRecurringReportAssignment_WhenServiceFails_ReturnsMappedErrorResult()
    {
        var recurringService = Substitute.For<IRecurringReportAssignmentService>();
        var traineeId = Id<User>.New();
        recurringService.CreateAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), Arg.Any<UpsertRecurringReportAssignmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<RecurringReportAssignmentResult, AppError>(new InvalidReportingError("bad request")));
        var controller = CreateController(Substitute.For<IReportingService>(), recurringService);

        var result = await controller.CreateRecurringReportAssignment(traineeId.ToString(), new UpsertRecurringReportAssignmentRequest
        {
            TemplateId = Id<ReportTemplate>.New().ToString(),
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = DateTimeOffset.UtcNow
        });

        result.Should().BeAssignableTo<ObjectResult>();
    }

    [Test]
    public async Task GetRecurringReportAssignments_WithInvalidTraineeId_ReturnsBadRequest()
    {
        var controller = CreateController(Substitute.For<IReportingService>());

        var result = await controller.GetRecurringReportAssignments("bad-user");

        result.Should().BeAssignableTo<ObjectResult>();
        ((ObjectResult)result).StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task GetRecurringReportAssignments_WhenServiceSucceeds_ReturnsMappedDtos()
    {
        var reportingService = Substitute.For<IReportingService>();
        var recurringService = Substitute.For<IRecurringReportAssignmentService>();
        var traineeId = Id<User>.New();
        recurringService.GetForTraineeAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<List<RecurringReportAssignmentResult>, AppError>([CreateAssignmentResult(traineeId, Id<ReportTemplate>.New())]));
        var controller = CreateController(reportingService, recurringService);

        var result = await controller.GetRecurringReportAssignments(traineeId.ToString());

        result.Should().BeOfType<OkObjectResult>();
        ((OkObjectResult)result).Value.Should().BeAssignableTo<IEnumerable<RecurringReportAssignmentDto>>();
    }

    [Test]
    public async Task GetRecurringReportAssignments_WhenServiceFails_ReturnsMappedErrorResult()
    {
        var recurringService = Substitute.For<IRecurringReportAssignmentService>();
        var traineeId = Id<User>.New();
        recurringService.GetForTraineeAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<List<RecurringReportAssignmentResult>, AppError>(new InvalidReportingError("bad request")));
        var controller = CreateController(Substitute.For<IReportingService>(), recurringService);

        var result = await controller.GetRecurringReportAssignments(traineeId.ToString());

        result.Should().BeAssignableTo<ObjectResult>();
    }

    [Test]
    public async Task UpdateRecurringReportAssignment_WithInvalidIds_ReturnsBadRequest()
    {
        var controller = CreateController(Substitute.For<IReportingService>());

        (await controller.UpdateRecurringReportAssignment("bad-user", Id<RecurringReportAssignment>.New().ToString(), new UpsertRecurringReportAssignmentRequest { TemplateId = Id<ReportTemplate>.New().ToString() }))
            .Should().BeAssignableTo<ObjectResult>();
        (await controller.UpdateRecurringReportAssignment(Id<User>.New().ToString(), "bad-assignment", new UpsertRecurringReportAssignmentRequest { TemplateId = Id<ReportTemplate>.New().ToString() }))
            .Should().BeAssignableTo<ObjectResult>();
    }

    [Test]
    public async Task UpdateRecurringReportAssignment_WithValidIds_ForwardsParsedValues()
    {
        var reportingService = Substitute.For<IReportingService>();
        var recurringService = Substitute.For<IRecurringReportAssignmentService>();
        var traineeId = Id<User>.New();
        var assignmentId = Id<RecurringReportAssignment>.New();
        var templateId = Id<ReportTemplate>.New();
        Id<RecurringReportAssignment> capturedAssignmentId = Id<RecurringReportAssignment>.Empty;
        recurringService.UpdateAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), Arg.Do<Id<RecurringReportAssignment>>(id => capturedAssignmentId = id), Arg.Any<UpsertRecurringReportAssignmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<RecurringReportAssignmentResult, AppError>(CreateAssignmentResult(traineeId, templateId)));
        var controller = CreateController(reportingService, recurringService);

        var result = await controller.UpdateRecurringReportAssignment(traineeId.ToString(), assignmentId.ToString(), new UpsertRecurringReportAssignmentRequest
        {
            TemplateId = templateId.ToString(),
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = DateTimeOffset.UtcNow
        });

        result.Should().BeOfType<OkObjectResult>();
        capturedAssignmentId.Should().Be(assignmentId);
    }

    [Test]
    public async Task UpdateRecurringReportAssignment_WhenServiceFails_ReturnsMappedErrorResult()
    {
        var recurringService = Substitute.For<IRecurringReportAssignmentService>();
        var traineeId = Id<User>.New();
        var assignmentId = Id<RecurringReportAssignment>.New();
        recurringService.UpdateAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), assignmentId, Arg.Any<UpsertRecurringReportAssignmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<RecurringReportAssignmentResult, AppError>(new InvalidReportingError("bad request")));
        var controller = CreateController(Substitute.For<IReportingService>(), recurringService);

        var result = await controller.UpdateRecurringReportAssignment(traineeId.ToString(), assignmentId.ToString(), new UpsertRecurringReportAssignmentRequest
        {
            TemplateId = Id<ReportTemplate>.New().ToString(),
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = DateTimeOffset.UtcNow
        });

        result.Should().BeAssignableTo<ObjectResult>();
    }

    [Test]
    public async Task PauseRecurringReportAssignment_WithInvalidIds_ReturnsBadRequest()
    {
        var controller = CreateController(Substitute.For<IReportingService>());

        (await controller.PauseRecurringReportAssignment("bad-user", Id<RecurringReportAssignment>.New().ToString())).Should().BeAssignableTo<ObjectResult>();
        (await controller.PauseRecurringReportAssignment(Id<User>.New().ToString(), "bad-assignment")).Should().BeAssignableTo<ObjectResult>();
    }

    [Test]
    public async Task PauseRecurringReportAssignment_WithValidIds_ForwardsToService()
    {
        var reportingService = Substitute.For<IReportingService>();
        var recurringService = Substitute.For<IRecurringReportAssignmentService>();
        var traineeId = Id<User>.New();
        var assignmentId = Id<RecurringReportAssignment>.New();
        recurringService.PauseAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), assignmentId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<RecurringReportAssignmentResult, AppError>(CreateAssignmentResult(traineeId, Id<ReportTemplate>.New())));
        var controller = CreateController(reportingService, recurringService);

        var result = await controller.PauseRecurringReportAssignment(traineeId.ToString(), assignmentId.ToString());

        result.Should().BeOfType<OkObjectResult>();
        await recurringService.Received(1).PauseAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), assignmentId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PauseRecurringReportAssignment_WhenServiceFails_ReturnsMappedErrorResult()
    {
        var recurringService = Substitute.For<IRecurringReportAssignmentService>();
        var traineeId = Id<User>.New();
        var assignmentId = Id<RecurringReportAssignment>.New();
        recurringService.PauseAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), assignmentId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<RecurringReportAssignmentResult, AppError>(new InvalidReportingError("bad request")));
        var controller = CreateController(Substitute.For<IReportingService>(), recurringService);

        var result = await controller.PauseRecurringReportAssignment(traineeId.ToString(), assignmentId.ToString());

        result.Should().BeAssignableTo<ObjectResult>();
    }

    [Test]
    public async Task ResumeRecurringReportAssignment_WithInvalidIds_ReturnsBadRequest()
    {
        var controller = CreateController(Substitute.For<IReportingService>());

        (await controller.ResumeRecurringReportAssignment("bad-user", Id<RecurringReportAssignment>.New().ToString())).Should().BeAssignableTo<ObjectResult>();
        (await controller.ResumeRecurringReportAssignment(Id<User>.New().ToString(), "bad-assignment")).Should().BeAssignableTo<ObjectResult>();
    }

    [Test]
    public async Task ResumeRecurringReportAssignment_WithValidIds_ForwardsToService()
    {
        var reportingService = Substitute.For<IReportingService>();
        var recurringService = Substitute.For<IRecurringReportAssignmentService>();
        var traineeId = Id<User>.New();
        var assignmentId = Id<RecurringReportAssignment>.New();
        recurringService.ResumeAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), assignmentId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<RecurringReportAssignmentResult, AppError>(CreateAssignmentResult(traineeId, Id<ReportTemplate>.New())));
        var controller = CreateController(reportingService, recurringService);

        var result = await controller.ResumeRecurringReportAssignment(traineeId.ToString(), assignmentId.ToString());

        result.Should().BeOfType<OkObjectResult>();
        await recurringService.Received(1).ResumeAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), assignmentId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResumeRecurringReportAssignment_WhenServiceFails_ReturnsMappedErrorResult()
    {
        var recurringService = Substitute.For<IRecurringReportAssignmentService>();
        var traineeId = Id<User>.New();
        var assignmentId = Id<RecurringReportAssignment>.New();
        recurringService.ResumeAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), assignmentId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<RecurringReportAssignmentResult, AppError>(new InvalidReportingError("bad request")));
        var controller = CreateController(Substitute.For<IReportingService>(), recurringService);

        var result = await controller.ResumeRecurringReportAssignment(traineeId.ToString(), assignmentId.ToString());

        result.Should().BeAssignableTo<ObjectResult>();
    }

    [Test]
    public async Task DeleteRecurringReportAssignment_WithInvalidIds_ReturnsBadRequest()
    {
        var controller = CreateController(Substitute.For<IReportingService>());

        (await controller.DeleteRecurringReportAssignment("bad-user", Id<RecurringReportAssignment>.New().ToString())).Should().BeAssignableTo<ObjectResult>();
        (await controller.DeleteRecurringReportAssignment(Id<User>.New().ToString(), "bad-assignment")).Should().BeAssignableTo<ObjectResult>();
    }

    [Test]
    public async Task DeleteRecurringReportAssignment_WithValidIds_ForwardsToService()
    {
        var reportingService = Substitute.For<IReportingService>();
        var recurringService = Substitute.For<IRecurringReportAssignmentService>();
        var traineeId = Id<User>.New();
        var assignmentId = Id<RecurringReportAssignment>.New();
        recurringService.DeleteAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), assignmentId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<Unit, AppError>(Unit.Value));
        var controller = CreateController(reportingService, recurringService);

        var result = await controller.DeleteRecurringReportAssignment(traineeId.ToString(), assignmentId.ToString());

        result.Should().BeOfType<OkObjectResult>();
        await recurringService.Received(1).DeleteAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), assignmentId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteRecurringReportAssignment_WhenServiceFails_ReturnsMappedErrorResult()
    {
        var recurringService = Substitute.For<IRecurringReportAssignmentService>();
        var traineeId = Id<User>.New();
        var assignmentId = Id<RecurringReportAssignment>.New();
        recurringService.DeleteAsync(Arg.Any<AuthenticatedAccountContext>(), traineeId.Rebind<AccountReference>(), assignmentId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Unit, AppError>(new InvalidReportingError("bad request")));
        var controller = CreateController(Substitute.For<IReportingService>(), recurringService);

        var result = await controller.DeleteRecurringReportAssignment(traineeId.ToString(), assignmentId.ToString());

        result.Should().BeAssignableTo<ObjectResult>();
    }

    private static TrainerReportingController CreateController(IReportingService reportingService, IRecurringReportAssignmentService? recurringService = null)
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        var controller = new TrainerReportingController(new TrainerReportTemplateApiAdapter(reportingService), new TrainerReportRequestApiAdapter(reportingService), new TrainerReportPhotoApiAdapter(reportingService, mapper), new RecurringReportAssignmentApiAdapter(recurringService ?? Substitute.For<IRecurringReportAssignmentService>()), mapper)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.Features.Set<IAuthenticatedAccountContextFeature>(new AuthenticatedAccountContextFeature(
            new AuthenticatedAccountContext(Id<AccountReference>.New(), null, [], [], false, false)));
        return controller;
    }

    private static ReportTemplateResult CreateTemplateResult(Id<ReportTemplate>? templateId = null)
        => new()
        {
            Id = templateId ?? Id<ReportTemplate>.New(),
            TrainerId = Id<AccountReference>.New(),
            Name = "Weekly",
            CreatedAt = DateTimeOffset.UtcNow,
            Fields = [new ReportTemplateFieldResult { Key = "weight", Label = "Weight", Type = ReportFieldType.Number, Order = 1 }]
        };

    private static ReportRequestResult CreateRequestResult(Id<User> traineeId, Id<ReportTemplate> templateId)
        => new()
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = Id<AccountReference>.New(),
            TraineeId = traineeId.Rebind<AccountReference>(),
            TemplateId = templateId,
            Status = ReportRequestStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            Template = CreateTemplateResult(templateId)
        };

    private static ReportSubmissionResult CreateSubmissionResult(Id<User> traineeId)
        => new()
        {
            Id = Id<ReportSubmission>.New(),
            ReportRequestId = Id<ReportRequest>.New(),
            TraineeId = traineeId.Rebind<AccountReference>(),
            SubmittedAt = DateTimeOffset.UtcNow,
            Request = CreateRequestResult(traineeId, Id<ReportTemplate>.New())
        };

    private static RecurringReportAssignmentResult CreateAssignmentResult(Id<User> traineeId, Id<ReportTemplate> templateId)
        => new()
        {
            Id = Id<RecurringReportAssignment>.New(),
            TrainerId = Id<AccountReference>.New(),
            TraineeId = traineeId.Rebind<AccountReference>(),
            TemplateId = templateId,
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = DateTimeOffset.UtcNow,
            Template = CreateTemplateResult(templateId)
        };
}
