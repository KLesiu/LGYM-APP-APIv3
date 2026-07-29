using System.Text.Json;
using FluentAssertions;
using FluentValidation.TestHelper;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Features.Trainer.Validation;
using LgymApi.Application.Abstractions.Storage;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Options;
using LgymApi.Application.Repositories;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ReportingServiceTests
{
    private readonly UpsertReportTemplateRequestValidator _validator = new();

    [Test]
    public void ValidMeasurementsModuleTemplate_ShouldNotHaveErrors()
    {
        var request = new UpsertReportTemplateRequest
        {
            Name = "Monthly Progress Report",
            Fields =
            [
                new ReportTemplateFieldRequest
                {
                    Key = "body_measurements",
                    Label = "Body Measurements",
                    Type = ReportFieldType.Measurements,
                    IsRequired = true,
                    Order = 1,
                    ModuleConfig = JsonDocument.Parse("""
                        {
                            "measurementTypes": ["weight", "bodyFat", "chest", "waist"]
                        }
                        """).RootElement
                }
            ]
        };

        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void MeasurementsField_WithMissingModuleConfig_ShouldHaveError()
    {
        var request = new UpsertReportTemplateRequest
        {
            Name = "Monthly Progress Report",
            Fields =
            [
                new ReportTemplateFieldRequest
                {
                    Key = "body_measurements",
                    Label = "Body Measurements",
                    Type = ReportFieldType.Measurements,
                    IsRequired = true,
                    Order = 1,
                    ModuleConfig = null
                }
            ]
        };

        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor("Fields[0].ModuleConfig");
    }

    [Test]
    public void MeasurementsField_WithEmptyMeasurementTypes_ShouldHaveError()
    {
        var request = new UpsertReportTemplateRequest
        {
            Name = "Monthly Progress Report",
            Fields =
            [
                new ReportTemplateFieldRequest
                {
                    Key = "body_measurements",
                    Label = "Body Measurements",
                    Type = ReportFieldType.Measurements,
                    IsRequired = true,
                    Order = 1,
                    ModuleConfig = JsonDocument.Parse("""
                        {
                            "measurementTypes": []
                        }
                        """).RootElement
                }
            ]
        };

        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor("Fields[0].ModuleConfig");
    }

    [Test]
    public void MeasurementsField_WithMissingMeasurementTypesProperty_ShouldHaveError()
    {
        var request = new UpsertReportTemplateRequest
        {
            Name = "Monthly Progress Report",
            Fields =
            [
                new ReportTemplateFieldRequest
                {
                    Key = "body_measurements",
                    Label = "Body Measurements",
                    Type = ReportFieldType.Measurements,
                    IsRequired = true,
                    Order = 1,
                    ModuleConfig = JsonDocument.Parse("""
                        {
                            "wrongProperty": ["weight", "chest"]
                        }
                        """).RootElement
                }
            ]
        };

        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor("Fields[0].ModuleConfig");
    }

    [Test]
    public void MeasurementsField_WithAllValidMeasurementTypes_ShouldNotHaveErrors()
    {
        var request = new UpsertReportTemplateRequest
        {
            Name = "Comprehensive Body Measurements",
            Fields =
            [
                new ReportTemplateFieldRequest
                {
                    Key = "full_body_measurements",
                    Label = "Full Body Measurements",
                    Type = ReportFieldType.Measurements,
                    IsRequired = true,
                    Order = 1,
                    ModuleConfig = JsonDocument.Parse("""
                        {
                            "measurementTypes": ["weight", "bodyFat", "chest", "waist", "hips", "thighs", "biceps", "calves"]
                        }
                        """).RootElement
                }
            ]
        };

        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void MixedFieldTypesTemplate_WithValidFields_ShouldNotHaveErrors()
    {
        var request = new UpsertReportTemplateRequest
        {
            Name = "Complete Progress Report",
            Fields =
            [
                new ReportTemplateFieldRequest
                {
                    Key = "overall_feedback",
                    Label = "Overall Feedback",
                    Type = ReportFieldType.Text,
                    IsRequired = true,
                    Order = 1,
                    ModuleConfig = null
                },
                new ReportTemplateFieldRequest
                {
                    Key = "progress_photos",
                    Label = "Progress Photos",
                    Type = ReportFieldType.Photos,
                    IsRequired = true,
                    Order = 2,
                    ModuleConfig = JsonDocument.Parse("""
                        {
                            "requiredViews": ["front", "sideLeft", "sideRight", "back"]
                        }
                        """).RootElement
                },
                new ReportTemplateFieldRequest
                {
                    Key = "body_measurements",
                    Label = "Body Measurements",
                    Type = ReportFieldType.Measurements,
                    IsRequired = true,
                    Order = 3,
                    ModuleConfig = JsonDocument.Parse("""
                        {
                            "measurementTypes": ["weight", "bodyFat", "chest", "waist"]
                        }
                        """).RootElement
                },
                new ReportTemplateFieldRequest
                {
                    Key = "current_weight",
                    Label = "Current Weight (kg)",
                    Type = ReportFieldType.Number,
                    IsRequired = true,
                    Order = 4,
                    ModuleConfig = null
                },
                new ReportTemplateFieldRequest
                {
                    Key = "training_completed",
                    Label = "Did you complete all training sessions?",
                    Type = ReportFieldType.Boolean,
                    IsRequired = true,
                    Order = 5,
                    ModuleConfig = null
                }
            ]
        };

        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void PhotosField_WithValidRequiredViews_ShouldNotHaveErrors()
    {
        var request = new UpsertReportTemplateRequest
        {
            Name = "Photo Progress Report",
            Fields =
            [
                new ReportTemplateFieldRequest
                {
                    Key = "progress_photos",
                    Label = "Progress Photos",
                    Type = ReportFieldType.Photos,
                    IsRequired = true,
                    Order = 1,
                    ModuleConfig = JsonDocument.Parse("""
                        {
                            "requiredViews": ["front", "sideLeft", "sideRight", "back"]
                        }
                        """).RootElement
                }
            ]
        };

        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Test]
    public void PhotosField_WithMissingModuleConfig_ShouldHaveError()
    {
        var request = new UpsertReportTemplateRequest
        {
            Name = "Photo Progress Report",
            Fields =
            [
                new ReportTemplateFieldRequest
                {
                    Key = "progress_photos",
                    Label = "Progress Photos",
                    Type = ReportFieldType.Photos,
                    IsRequired = true,
                    Order = 1,
                    ModuleConfig = null
                }
            ]
        };

        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor("Fields[0].ModuleConfig");
    }

    [Test]
    public void PhotosField_WithEmptyRequiredViews_ShouldHaveError()
    {
        var request = new UpsertReportTemplateRequest
        {
            Name = "Photo Progress Report",
            Fields =
            [
                new ReportTemplateFieldRequest
                {
                    Key = "progress_photos",
                    Label = "Progress Photos",
                    Type = ReportFieldType.Photos,
                    IsRequired = true,
                    Order = 1,
                    ModuleConfig = JsonDocument.Parse("""
                        {
                            "requiredViews": []
                        }
                        """).RootElement
                }
            ]
        };

        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor("Fields[0].ModuleConfig");
    }

    [Test]
    public void PhotosField_WithMissingRequiredViewsProperty_ShouldHaveError()
    {
        var request = new UpsertReportTemplateRequest
        {
            Name = "Photo Progress Report",
            Fields =
            [
                new ReportTemplateFieldRequest
                {
                    Key = "progress_photos",
                    Label = "Progress Photos",
                    Type = ReportFieldType.Photos,
                    IsRequired = true,
                    Order = 1,
                    ModuleConfig = JsonDocument.Parse("""
                        {
                            "wrongProperty": ["front", "side"]
                        }
                        """).RootElement
                }
            ]
        };

        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor("Fields[0].ModuleConfig");
    }

    [Test]
    public void PhotosField_WithInvalidRequiredView_ShouldHaveError()
    {
        var request = new UpsertReportTemplateRequest
        {
            Name = "Photo Progress Report",
            Fields =
            [
                new ReportTemplateFieldRequest
                {
                    Key = "progress_photos",
                    Label = "Progress Photos",
                    Type = ReportFieldType.Photos,
                    IsRequired = true,
                    Order = 1,
                    ModuleConfig = JsonDocument.Parse("""
                        {
                            "requiredViews": ["frontt"]
                        }
                        """).RootElement
                }
            ]
        };

        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor("Fields[0].ModuleConfig");
    }

    [Test]
    public async Task SubmitReportRequest_WithInvalidPhotoModuleConfig_ShouldReturnValidationError()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var templateId = Id<ReportTemplate>.New();
        var trainee = CreateUser(traineeId);
        var template = CreateTemplateWithPhotos(templateId, new[] { "Frontt" });
        var request = CreateReportRequest(requestId, traineeId, templateId, template);

        var service = CreateReportingService(findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request));

        var command = new SubmitReportRequestCommand
        {
            Answers = new Dictionary<string, JsonElement>
            {
                ["photos"] = JsonDocument.Parse("[]").RootElement
            }
        };

        var result = await service.SubmitReportRequestAsync(trainee, requestId, command);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
        result.Error.Message.Should().Be(Messages.ReportFieldValidationFailed);
    }

    [Test]
    public void ScalarField_WithModuleConfig_ShouldHaveError()
    {
        var request = new UpsertReportTemplateRequest
        {
            Name = "Invalid Report",
            Fields =
            [
                new ReportTemplateFieldRequest
                {
                    Key = "feedback",
                    Label = "Feedback",
                    Type = ReportFieldType.Text,
                    IsRequired = true,
                    Order = 1,
                    ModuleConfig = JsonDocument.Parse("""
                        {
                            "someConfig": "value"
                        }
                        """).RootElement
                }
            ]
        };

        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor("Fields[0].ModuleConfig");
    }

    [Test]
    public void MixedTemplate_PhotosAndTextAndNumber_ValidatesCorrectly()
    {
        var request = new UpsertReportTemplateRequest
        {
            Name = "Complete Mixed Progress Report",
            Fields =
            [
                new ReportTemplateFieldRequest
                {
                    Key = "progress_photos",
                    Label = "Progress Photos",
                    Type = ReportFieldType.Photos,
                    IsRequired = true,
                    Order = 1,
                    ModuleConfig = JsonDocument.Parse("""
                        {
                            "requiredViews": ["front", "side", "back"]
                        }
                        """).RootElement
                },
                new ReportTemplateFieldRequest
                {
                    Key = "feedback_text",
                    Label = "Overall Feedback",
                    Type = ReportFieldType.Text,
                    IsRequired = false,
                    Order = 2,
                    ModuleConfig = null
                },
                new ReportTemplateFieldRequest
                {
                    Key = "current_weight",
                    Label = "Current Weight (kg)",
                    Type = ReportFieldType.Number,
                    IsRequired = true,
                    Order = 3,
                    ModuleConfig = null
                },
                new ReportTemplateFieldRequest
                {
                    Key = "training_completed",
                    Label = "Did you complete all sessions?",
                    Type = ReportFieldType.Boolean,
                    IsRequired = false,
                    Order = 4,
                    ModuleConfig = null
                }
            ]
        };

        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    #region Photo Validation Tests

    [Test]
    public async Task SubmitReportRequest_WithMissingOnePhotoView_ShouldReturnError()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var templateId = Id<ReportTemplate>.New();
        var trainee = CreateUser(traineeId);
        var template = CreateTemplateWithPhotos(templateId, new[] { "Front", "SideLeft", "SideRight", "Back" });
        var request = CreateReportRequest(requestId, traineeId, templateId, template);

        var uploadedPhotos = new List<Photo>
        {
            CreatePhoto(Id<Photo>.New(), requestId, traineeId, PhotoViewType.Front),
            CreatePhoto(Id<Photo>.New(), requestId, traineeId, PhotoViewType.SideLeft),
            CreatePhoto(Id<Photo>.New(), requestId, traineeId, PhotoViewType.Back)
        };

        var service = CreateReportingService(
            findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request),
            getPhotosByRequestId: (_, _) => Task.FromResult(uploadedPhotos));

        var command = new SubmitReportRequestCommand
        {
            Answers = new Dictionary<string, JsonElement>
            {
                ["photos"] = JsonDocument.Parse("[]").RootElement
            }
        };

        var result = await service.SubmitReportRequestAsync(trainee, requestId, command);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
        result.Error.Message.Should().Be(Messages.ReportFieldValidationFailed);
    }

    [Test]
    public async Task SubmitReportRequest_WithMissingAllPhotoViews_ShouldReturnError()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var templateId = Id<ReportTemplate>.New();
        var trainee = CreateUser(traineeId);
        var template = CreateTemplateWithPhotos(templateId, new[] { "Front", "SideLeft", "SideRight", "Back" });
        var request = CreateReportRequest(requestId, traineeId, templateId, template);

        var uploadedPhotos = new List<Photo>();

        var service = CreateReportingService(
            findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request),
            getPhotosByRequestId: (_, _) => Task.FromResult(uploadedPhotos));

        var command = new SubmitReportRequestCommand
        {
            Answers = new Dictionary<string, JsonElement>
            {
                ["photos"] = JsonDocument.Parse("[]").RootElement
            }
        };

        var result = await service.SubmitReportRequestAsync(trainee, requestId, command);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
        result.Error.Message.Should().Be(Messages.ReportFieldValidationFailed);
    }

    [Test]
    public async Task SubmitReportRequest_WithOptionalPhotosAndNoUploads_ShouldSucceed()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var templateId = Id<ReportTemplate>.New();
        var trainee = CreateUser(traineeId);
        var template = CreateTemplateWithOptionalPhotos(templateId, new[] { "Front", "SideLeft", "SideRight", "Back" });
        var request = CreateReportRequest(requestId, traineeId, templateId, template);

        var service = CreateReportingService(
            findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request),
            getPhotosByRequestId: (_, _) => Task.FromResult(new List<Photo>()),
            addSubmission: (_, _) => Task.CompletedTask);

        var command = new SubmitReportRequestCommand
        {
            Answers = new Dictionary<string, JsonElement>
            {
                ["feedback"] = JsonDocument.Parse("\"All good\"").RootElement
            }
        };

        var result = await service.SubmitReportRequestAsync(trainee, requestId, command);

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task SubmitReportRequest_WhenSuccessful_EnqueuesTrainerSubmissionNotification()
    {
        var traineeId = Id<User>.New();
        var trainerId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var templateId = Id<ReportTemplate>.New();
        var trainee = CreateUser(traineeId);
        var template = CreateTemplateWithoutPhotos(templateId);
        template.Name = "Weekly check-in";
        var request = CreateReportRequest(requestId, traineeId, templateId, template);
        request.TrainerId = trainerId;
        var commandDispatcher = Substitute.For<ICommandDispatcher>();
        var service = CreateReportingService(
            findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request),
            commandDispatcher: commandDispatcher);

        var command = new SubmitReportRequestCommand
        {
            Answers = new Dictionary<string, JsonElement>
            {
                ["feedback"] = JsonDocument.Parse("\"Done\"").RootElement
            }
        };

        var result = await service.SubmitReportRequestAsync(trainee, requestId, command);

        result.IsSuccess.Should().BeTrue();
        await commandDispatcher.Received(1).EnqueueAsync(Arg.Is<ReportSubmissionCreatedInAppNotificationCommand>(queued =>
            queued.TrainerId == trainerId.Rebind<LgymApi.Identity.Contracts.AccountReference>()
            && queued.TraineeId == traineeId.Rebind<LgymApi.Identity.Contracts.AccountReference>()
            && queued.TemplateName == "Weekly check-in"
            && !queued.SubmissionId.IsEmpty));
    }

    [Test]
    public async Task SubmitReportRequest_WithAllRequiredPhotoViews_ShouldSucceed()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var templateId = Id<ReportTemplate>.New();
        var trainee = CreateUser(traineeId);
        var template = CreateTemplateWithPhotos(templateId, new[] { "Front", "SideLeft", "SideRight", "Back" });
        var request = CreateReportRequest(requestId, traineeId, templateId, template);

        var uploadedPhotos = new List<Photo>
        {
            CreatePhoto(Id<Photo>.New(), requestId, traineeId, PhotoViewType.Front),
            CreatePhoto(Id<Photo>.New(), requestId, traineeId, PhotoViewType.SideLeft),
            CreatePhoto(Id<Photo>.New(), requestId, traineeId, PhotoViewType.SideRight),
            CreatePhoto(Id<Photo>.New(), requestId, traineeId, PhotoViewType.Back)
        };

        var service = CreateReportingService(
            findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request),
            getPhotosByRequestId: (_, _) => Task.FromResult(uploadedPhotos),
            addSubmission: (_, _) => Task.CompletedTask);

        var command = new SubmitReportRequestCommand
        {
            Answers = new Dictionary<string, JsonElement>
            {
                ["photos"] = JsonDocument.Parse("[]").RootElement
            }
        };

        var result = await service.SubmitReportRequestAsync(trainee, requestId, command);

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task SubmitReportRequest_WithNoPhotoFields_ShouldSucceedWithoutPhotoValidation()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var templateId = Id<ReportTemplate>.New();
        var trainee = CreateUser(traineeId);
        var template = CreateTemplateWithoutPhotos(templateId);
        var request = CreateReportRequest(requestId, traineeId, templateId, template);

        var service = CreateReportingService(
            findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request),
            addSubmission: (_, _) => Task.CompletedTask);

        var command = new SubmitReportRequestCommand
        {
            Answers = new Dictionary<string, JsonElement>
            {
                ["feedback"] = JsonDocument.Parse("\"Great progress\"").RootElement
            }
        };

        var result = await service.SubmitReportRequestAsync(trainee, requestId, command);

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task SubmitReportRequest_WithExpiredUnsubmittedRequest_ShouldStillSucceed()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var templateId = Id<ReportTemplate>.New();
        var trainee = CreateUser(traineeId);
        var template = CreateTemplateWithoutPhotos(templateId);
        var request = CreateReportRequest(requestId, traineeId, templateId, template);
        request.Status = ReportRequestStatus.Expired;
        request.DueAt = DateTimeOffset.UtcNow.AddDays(-1);

        var service = CreateReportingService(
            findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request),
            addSubmission: (_, _) => Task.CompletedTask);

        var command = new SubmitReportRequestCommand
        {
            Answers = new Dictionary<string, JsonElement>
            {
                ["feedback"] = JsonDocument.Parse("\"Still submitting after due date\"").RootElement
            }
        };

        var result = await service.SubmitReportRequestAsync(trainee, requestId, command);

        result.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(ReportRequestStatus.Submitted);
        request.SubmittedAt.Should().NotBeNull();
    }

    [Test]
    public async Task SubmitReportRequest_WithNullAnswers_ReturnsValidationError()
    {
        var service = CreateReportingService();

        var result = await service.SubmitReportRequestAsync(CreateUser(Id<User>.New()), Id<ReportRequest>.New(), new SubmitReportRequestCommand { Answers = null! });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
    }

    [Test]
    public async Task SubmitReportRequest_WhenRequestBelongsToDifferentTrainee_ReturnsNotFound()
    {
        var requestId = Id<ReportRequest>.New();
        var templateId = Id<ReportTemplate>.New();
        var currentTrainee = CreateUser(Id<User>.New());
        var request = CreateReportRequest(requestId, Id<User>.New(), templateId, CreateTemplateWithoutPhotos(templateId));
        var service = CreateReportingService(findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request));

        var result = await service.SubmitReportRequestAsync(currentTrainee, requestId, new SubmitReportRequestCommand
        {
            Answers = new Dictionary<string, JsonElement> { ["feedback"] = JsonDocument.Parse("\"ok\"").RootElement }
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingNotFoundError>();
    }

    [Test]
    public async Task SubmitReportRequest_WhenRequestAlreadySubmitted_ReturnsInvalidReportingError()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var templateId = Id<ReportTemplate>.New();
        var request = CreateReportRequest(requestId, traineeId, templateId, CreateTemplateWithoutPhotos(templateId));
        request.Status = ReportRequestStatus.Submitted;
        var service = CreateReportingService(findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request));

        var result = await service.SubmitReportRequestAsync(CreateUser(traineeId), requestId, new SubmitReportRequestCommand
        {
            Answers = new Dictionary<string, JsonElement> { ["feedback"] = JsonDocument.Parse("\"ok\"").RootElement }
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
    }

    [Test]
    public async Task SubmitReportRequest_WhenDuplicateSubmissionDetected_ReturnsInvalidReportingError()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var templateId = Id<ReportTemplate>.New();
        var request = CreateReportRequest(requestId, traineeId, templateId, CreateTemplateWithoutPhotos(templateId));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<Task<int>>(_ => throw new InvalidOperationException("duplicate key in ReportSubmissions on ReportRequestId"));
        var service = CreateReportingService(
            findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request),
            unitOfWork: unitOfWork);

        var result = await service.SubmitReportRequestAsync(CreateUser(traineeId), requestId, new SubmitReportRequestCommand
        {
            Answers = new Dictionary<string, JsonElement> { ["feedback"] = JsonDocument.Parse("\"ok\"").RootElement }
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
    }

    [Test]
    public async Task UpdateTrainerFeedbackAsync_WhenFeedbackCleared_DoesNotEnqueueNotificationAndClearsNextEligibleAt()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var submissionId = Id<ReportSubmission>.New();
        var templateId = Id<ReportTemplate>.New();
        var request = CreateReportRequest(Id<ReportRequest>.New(), traineeId, templateId, CreateTemplateWithoutPhotos(templateId));
        var submission = CreateSubmission(submissionId, traineeId, request);
        submission.TrainerOverallComment = "old";
        submission.TrainerFeedbackAddedAt = DateTimeOffset.UtcNow.AddDays(-1);
        submission.TrainerFieldCommentsJson = "{\"feedback\":\"old\"}";
        var assignment = new RecurringReportAssignment
        {
            Id = Id<RecurringReportAssignment>.New(),
            TrainerId = request.TrainerId,
            TraineeId = traineeId,
            TemplateId = request.TemplateId,
            Template = request.Template,
            NextEligibleAt = DateTimeOffset.UtcNow.AddDays(2)
        };
        var commandDispatcher = Substitute.For<ICommandDispatcher>();
        var service = CreateReportingService(
            findSubmissionByIdForTrainer: (_, _, _, _) => Task.FromResult<ReportSubmission?>(submission),
            recurringAssignmentByRequestId: (_, _) => Task.FromResult<RecurringReportAssignment?>(assignment),
            commandDispatcher: commandDispatcher,
            userHasTrainerRole: true,
            hasActiveTrainerLink: true);

        var result = await service.UpdateTrainerFeedbackAsync(CreateUser(trainerId), traineeId, submissionId, new UpdateReportSubmissionFeedbackCommand
        {
            TrainerOverallComment = "   ",
            FieldComments = []
        });

        result.IsSuccess.Should().BeTrue();
        submission.TrainerOverallComment.Should().BeNull();
        submission.TrainerFieldCommentsJson.Should().BeNull();
        assignment.NextEligibleAt.Should().BeNull();
        await commandDispatcher.DidNotReceive().EnqueueAsync(Arg.Any<ReportFeedbackAddedInAppNotificationCommand>());
    }

    [Test]
    public async Task MarkTrainerFeedbackAsReadAsync_WhenAlreadyRead_SkipsSaveChanges()
    {
        var traineeId = Id<User>.New();
        var templateId = Id<ReportTemplate>.New();
        var submissionId = Id<ReportSubmission>.New();
        var request = CreateReportRequest(Id<ReportRequest>.New(), traineeId, templateId, CreateTemplateWithoutPhotos(templateId));
        var submission = CreateSubmission(submissionId, traineeId, request);
        submission.TrainerFeedbackAddedAt = DateTimeOffset.UtcNow.AddHours(-2);
        submission.TrainerFeedbackReadAt = DateTimeOffset.UtcNow.AddHours(-1);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var service = CreateReportingService(
            findSubmissionByIdForTrainee: (_, _, _) => Task.FromResult<ReportSubmission?>(submission),
            unitOfWork: unitOfWork);

        var result = await service.MarkTrainerFeedbackAsReadAsync(CreateUser(traineeId), submissionId);

        result.IsSuccess.Should().BeTrue();
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MarkTrainerFeedbackAsReadAsync_WhenAssignmentExists_SetsNextEligibleAt()
    {
        var traineeId = Id<User>.New();
        var templateId = Id<ReportTemplate>.New();
        var submissionId = Id<ReportSubmission>.New();
        var request = CreateReportRequest(Id<ReportRequest>.New(), traineeId, templateId, CreateTemplateWithoutPhotos(templateId));
        var submission = CreateSubmission(submissionId, traineeId, request);
        submission.TrainerFeedbackAddedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var assignment = new RecurringReportAssignment
        {
            Id = Id<RecurringReportAssignment>.New(),
            TrainerId = request.TrainerId,
            TraineeId = traineeId,
            TemplateId = request.TemplateId,
            Template = request.Template,
            IntervalValue = 2,
            IntervalUnit = RecurringReportIntervalUnit.Week
        };
        var service = CreateReportingService(
            findSubmissionByIdForTrainee: (_, _, _) => Task.FromResult<ReportSubmission?>(submission),
            recurringAssignmentByRequestId: (_, _) => Task.FromResult<RecurringReportAssignment?>(assignment));

        var result = await service.MarkTrainerFeedbackAsReadAsync(CreateUser(traineeId), submissionId);

        result.IsSuccess.Should().BeTrue();
        submission.TrainerFeedbackReadAt.Should().NotBeNull();
        assignment.NextEligibleAt.Should().BeAfter(submission.TrainerFeedbackReadAt!.Value.AddDays(13));
    }

    [Test]
    public async Task SubmitReportRequest_WhenPendingRequestIsPastDue_MarksExpiredBeforeSubmit()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var templateId = Id<ReportTemplate>.New();
        var request = CreateReportRequest(requestId, traineeId, templateId, CreateTemplateWithoutPhotos(templateId));
        request.DueAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var service = CreateReportingService(
            findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request),
            unitOfWork: unitOfWork);

        var result = await service.SubmitReportRequestAsync(CreateUser(traineeId), requestId, new SubmitReportRequestCommand
        {
            Answers = new Dictionary<string, JsonElement> { ["feedback"] = JsonDocument.Parse("\"ok\"").RootElement }
        });

        result.IsSuccess.Should().BeTrue();
        await unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SubmitReportRequest_WhenAnswersFailValidation_ReturnsInvalidReportingError()
    {
        var traineeId = Id<User>.New();
        var requestId = Id<ReportRequest>.New();
        var templateId = Id<ReportTemplate>.New();
        var request = CreateReportRequest(requestId, traineeId, templateId, CreateTemplateWithoutPhotos(templateId));
        var service = CreateReportingService(findRequestById: (_, _) => Task.FromResult<ReportRequest?>(request));

        var result = await service.SubmitReportRequestAsync(CreateUser(traineeId), requestId, new SubmitReportRequestCommand
        {
            Answers = new Dictionary<string, JsonElement> { ["feedback"] = JsonDocument.Parse("1").RootElement }
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
    }

    [Test]
    public async Task UpdateTrainerFeedbackAsync_WhenOwnershipFails_ReturnsFailure()
    {
        var trainerId = Id<User>.New();
        var submissionLookupCalled = false;
        var service = CreateReportingService(
            findSubmissionByIdForTrainer: (_, _, _, _) =>
            {
                submissionLookupCalled = true;
                return Task.FromResult<ReportSubmission?>(null);
            },
            userHasTrainerRole: false);

        var result = await service.UpdateTrainerFeedbackAsync(
            CreateUser(trainerId),
            Id<User>.New(),
            Id<ReportSubmission>.New(),
            new UpdateReportSubmissionFeedbackCommand());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingForbiddenError>();
        submissionLookupCalled.Should().BeFalse();
    }

    [Test]
    public async Task UpdateTrainerFeedbackAsync_WhenTrainerDoesNotOwnTrainee_ReturnsNotFoundWithoutReadingSubmission()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var submissionLookupCalled = false;
        var service = CreateReportingService(
            findSubmissionByIdForTrainer: (_, _, _, _) =>
            {
                submissionLookupCalled = true;
                return Task.FromResult<ReportSubmission?>(null);
            },
            userHasTrainerRole: true,
            hasActiveTrainerLink: false);

        var result = await service.UpdateTrainerFeedbackAsync(
            CreateUser(trainerId),
            traineeId,
            Id<ReportSubmission>.New(),
            new UpdateReportSubmissionFeedbackCommand());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingNotFoundError>();
        submissionLookupCalled.Should().BeFalse();
    }

    [Test]
    public async Task UpdateTrainerFeedbackAsync_WhenSubmissionIdEmpty_ReturnsFailure()
    {
        var result = await CreateReportingService(userHasTrainerRole: true, hasActiveTrainerLink: true).UpdateTrainerFeedbackAsync(
            CreateUser(Id<User>.New()),
            Id<User>.New(),
            Id<ReportSubmission>.Empty,
            new UpdateReportSubmissionFeedbackCommand());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
    }

    [Test]
    public async Task UpdateTrainerFeedbackAsync_WhenSubmissionNotFound_ReturnsFailure()
    {
        var result = await CreateReportingService(userHasTrainerRole: true, hasActiveTrainerLink: true).UpdateTrainerFeedbackAsync(
            CreateUser(Id<User>.New()),
            Id<User>.New(),
            Id<ReportSubmission>.New(),
            new UpdateReportSubmissionFeedbackCommand());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingNotFoundError>();
    }

    [Test]
    public async Task UpdateTrainerFeedbackAsync_WhenFieldCommentsInvalid_ReturnsFailure()
    {
        var traineeId = Id<User>.New();
        var submissionId = Id<ReportSubmission>.New();
        var templateId = Id<ReportTemplate>.New();
        var request = CreateReportRequest(Id<ReportRequest>.New(), traineeId, templateId, CreateTemplateWithoutPhotos(templateId));
        var submission = CreateSubmission(submissionId, traineeId, request);
        var service = CreateReportingService(
            findSubmissionByIdForTrainer: (_, _, _, _) => Task.FromResult<ReportSubmission?>(submission),
            userHasTrainerRole: true,
            hasActiveTrainerLink: true);

        var result = await service.UpdateTrainerFeedbackAsync(CreateUser(Id<User>.New()), traineeId, submissionId, new UpdateReportSubmissionFeedbackCommand
        {
            FieldComments = new Dictionary<string, string?> { ["unknown"] = "bad" }
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
    }

    [Test]
    public async Task UpdateTrainerFeedbackAsync_WhenFeedbackAdded_EnqueuesNotification()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var submissionId = Id<ReportSubmission>.New();
        var templateId = Id<ReportTemplate>.New();
        var request = CreateReportRequest(Id<ReportRequest>.New(), traineeId, templateId, CreateTemplateWithoutPhotos(templateId));
        var submission = CreateSubmission(submissionId, traineeId, request);
        var commandDispatcher = Substitute.For<ICommandDispatcher>();
        var service = CreateReportingService(
            findSubmissionByIdForTrainer: (_, _, _, _) => Task.FromResult<ReportSubmission?>(submission),
            commandDispatcher: commandDispatcher,
            userHasTrainerRole: true,
            hasActiveTrainerLink: true);

        var result = await service.UpdateTrainerFeedbackAsync(CreateUser(trainerId), traineeId, submissionId, new UpdateReportSubmissionFeedbackCommand
        {
            TrainerOverallComment = "Great progress"
        });

        result.IsSuccess.Should().BeTrue();
        await commandDispatcher.Received(1).EnqueueAsync(Arg.Any<ReportFeedbackAddedInAppNotificationCommand>());
    }

    [Test]
    public async Task MarkTrainerFeedbackAsReadAsync_WhenSubmissionIdEmpty_ReturnsFailure()
    {
        var result = await CreateReportingService().MarkTrainerFeedbackAsReadAsync(CreateUser(Id<User>.New()), Id<ReportSubmission>.Empty);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task MarkTrainerFeedbackAsReadAsync_WhenSubmissionMissing_ReturnsFailure()
    {
        var result = await CreateReportingService().MarkTrainerFeedbackAsReadAsync(CreateUser(Id<User>.New()), Id<ReportSubmission>.New());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingNotFoundError>();
    }

    [Test]
    public async Task MarkTrainerFeedbackAsReadAsync_WhenSubmissionBelongsToAnotherTrainee_ReturnsNotFound()
    {
        var currentTrainee = CreateUser(Id<User>.New());
        var queriedTraineeId = Id<User>.Empty;
        var service = CreateReportingService(
            findSubmissionByIdForTrainee: (_, traineeId, _) =>
            {
                queriedTraineeId = traineeId;
                return Task.FromResult<ReportSubmission?>(null);
            });

        var result = await service.MarkTrainerFeedbackAsReadAsync(currentTrainee, Id<ReportSubmission>.New());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingNotFoundError>();
        queriedTraineeId.Should().Be(currentTrainee.Id);
    }

    [Test]
    public async Task MarkTrainerFeedbackAsReadAsync_WhenFeedbackNotAdded_ReturnsFailure()
    {
        var traineeId = Id<User>.New();
        var templateId = Id<ReportTemplate>.New();
        var submissionId = Id<ReportSubmission>.New();
        var submission = CreateSubmission(submissionId, traineeId, CreateReportRequest(Id<ReportRequest>.New(), traineeId, templateId, CreateTemplateWithoutPhotos(templateId)));
        var service = CreateReportingService(findSubmissionByIdForTrainee: (_, _, _) => Task.FromResult<ReportSubmission?>(submission));

        var result = await service.MarkTrainerFeedbackAsReadAsync(CreateUser(traineeId), submissionId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidReportingError>();
    }

    [Test]
    public async Task GetTraineeSubmissionsAsync_WhenOwnershipFails_ReturnsFailure()
    {
        var result = await CreateReportingService(userHasTrainerRole: false).GetTraineeSubmissionsAsync(CreateUser(Id<User>.New()), Id<User>.New());

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task GetTraineeSubmissionsAsync_WhenTrainerDoesNotOwnTrainee_ReturnsNotFoundWithoutReadingSubmissions()
    {
        var submissionsLookupCalled = false;
        var service = CreateReportingService(
            getSubmissionsByTrainerAndTrainee: (_, _, _) =>
            {
                submissionsLookupCalled = true;
                return Task.FromResult(new List<ReportSubmission>());
            },
            userHasTrainerRole: true,
            hasActiveTrainerLink: false);

        var result = await service.GetTraineeSubmissionsAsync(CreateUser(Id<User>.New()), Id<User>.New());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingNotFoundError>();
        submissionsLookupCalled.Should().BeFalse();
    }

    [Test]
    public async Task GetTraineeSubmissionsAsync_WhenOwnershipSucceeds_ReturnsMappedResults()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var templateId = Id<ReportTemplate>.New();
        var request = CreateReportRequest(Id<ReportRequest>.New(), traineeId, templateId, CreateTemplateWithoutPhotos(templateId));
        var submission = CreateSubmission(Id<ReportSubmission>.New(), traineeId, request);
        var service = CreateReportingService(
            getSubmissionsByTrainerAndTrainee: (_, _, _) => Task.FromResult(new List<ReportSubmission> { submission }),
            userHasTrainerRole: true,
            hasActiveTrainerLink: true);

        var result = await service.GetTraineeSubmissionsAsync(CreateUser(trainerId), traineeId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
    }

    [Test]
    public async Task GetOwnSubmissionsAsync_ReturnsMappedResults()
    {
        var traineeId = Id<User>.New();
        var templateId = Id<ReportTemplate>.New();
        var request = CreateReportRequest(Id<ReportRequest>.New(), traineeId, templateId, CreateTemplateWithoutPhotos(templateId));
        var submission = CreateSubmission(Id<ReportSubmission>.New(), traineeId, request);
        var service = CreateReportingService(getSubmissionsByTrainee: (_, _) => Task.FromResult(new List<ReportSubmission> { submission }));

        var result = await service.GetOwnSubmissionsAsync(CreateUser(traineeId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
    }

    #endregion

    #region Helper Methods

    private static User CreateUser(Id<User> userId)
    {
        return new User
        {
            Id = userId,
            Name = $"user_{userId}",
            Email = $"{userId}@example.com",
            ProfileRank = "Rookie"
        };
    }

    private static ReportTemplate CreateTemplateWithPhotos(Id<ReportTemplate> templateId, string[] requiredViews)
    {
        var config = new { requiredViews };
        return new ReportTemplate
        {
            Id = templateId,
            Name = "Photo Progress Report",
            TrainerId = Id<User>.New(),
            Fields =
            [
                new ReportTemplateField
                {
                    Id = Id<ReportTemplateField>.New(),
                    TemplateId = templateId,
                    Key = "photos",
                    Label = "Progress Photos",
                    Type = ReportFieldType.Photos,
                    IsRequired = true,
                    Order = 1,
                    ModuleConfig = JsonSerializer.Serialize(config)
                }
            ]
        };
    }

    private static ReportTemplate CreateTemplateWithOptionalPhotos(Id<ReportTemplate> templateId, string[] requiredViews)
    {
        var config = new { requiredViews };
        return new ReportTemplate
        {
            Id = templateId,
            Name = "Optional Photo Progress Report",
            TrainerId = Id<User>.New(),
            Fields =
            [
                new ReportTemplateField
                {
                    Id = Id<ReportTemplateField>.New(),
                    TemplateId = templateId,
                    Key = "feedback",
                    Label = "Feedback",
                    Type = ReportFieldType.Text,
                    IsRequired = true,
                    Order = 1,
                },
                new ReportTemplateField
                {
                    Id = Id<ReportTemplateField>.New(),
                    TemplateId = templateId,
                    Key = "photos",
                    Label = "Progress Photos",
                    Type = ReportFieldType.Photos,
                    IsRequired = false,
                    Order = 2,
                    ModuleConfig = JsonSerializer.Serialize(config)
                }
            ]
        };
    }

    private static ReportTemplate CreateTemplateWithoutPhotos(Id<ReportTemplate> templateId)
    {
        return new ReportTemplate
        {
            Id = templateId,
            Name = "Simple Report",
            TrainerId = Id<User>.New(),
            Fields =
            [
                new ReportTemplateField
                {
                    Id = Id<ReportTemplateField>.New(),
                    TemplateId = templateId,
                    Key = "feedback",
                    Label = "Feedback",
                    Type = ReportFieldType.Text,
                    IsRequired = true,
                    Order = 1
                }
            ]
        };
    }

    private static ReportRequest CreateReportRequest(
        Id<ReportRequest> requestId,
        Id<User> traineeId,
        Id<ReportTemplate> templateId,
        ReportTemplate template)
    {
        return new ReportRequest
        {
            Id = requestId,
            TraineeId = traineeId,
            TrainerId = Id<User>.New(),
            TemplateId = templateId,
            Template = template,
            Status = ReportRequestStatus.Pending
        };
    }

    private static Photo CreatePhoto(Id<Photo> photoId, Id<ReportRequest> requestId, Id<User> traineeId, PhotoViewType viewType)
    {
        var normalizedViewType = viewType.ToString();
        return new Photo
        {
            Id = photoId,
            ReportRequestId = requestId,
            OwnerUserId = traineeId,
            UploaderUserId = traineeId,
            ViewType = normalizedViewType,
            StorageKey = $"photos/{traineeId}/{requestId}/{normalizedViewType}/photo.jpg",
            MimeType = "image/jpeg",
            SizeBytes = 1024,
            Checksum = "abc123",
            IsDeleted = false
        };
    }

    private static ReportSubmission CreateSubmission(Id<ReportSubmission> submissionId, Id<User> traineeId, ReportRequest request)
        => new()
        {
            Id = submissionId,
            ReportRequestId = request.Id,
            ReportRequest = request,
            TraineeId = traineeId,
            PayloadJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static ReportingService CreateReportingService(
        Func<Id<ReportRequest>, CancellationToken, Task<ReportRequest?>>? findRequestById = null,
        Func<Id<ReportRequest>, CancellationToken, Task<List<Photo>>>? getPhotosByRequestId = null,
        Func<NewReportSubmissionPersistenceModel, CancellationToken, Task>? addSubmission = null,
        Func<Id<ReportSubmission>, Id<User>, Id<User>, CancellationToken, Task<ReportSubmission?>>? findSubmissionByIdForTrainer = null,
        Func<Id<ReportSubmission>, Id<User>, CancellationToken, Task<ReportSubmission?>>? findSubmissionByIdForTrainee = null,
        Func<Id<User>, Id<User>, CancellationToken, Task<List<ReportSubmission>>>? getSubmissionsByTrainerAndTrainee = null,
        Func<Id<User>, CancellationToken, Task<List<ReportSubmission>>>? getSubmissionsByTrainee = null,
        Func<Id<ReportRequest>, CancellationToken, Task<RecurringReportAssignment?>>? recurringAssignmentByRequestId = null,
        ICommandDispatcher? commandDispatcher = null,
        IUnitOfWork? unitOfWork = null,
        bool userHasTrainerRole = false,
        bool hasActiveTrainerLink = false)
    {
        var templatePersistence = Substitute.For<IReportTemplatePersistence>();
        var requestSubmissionPersistence = Substitute.For<IReportRequestSubmissionPersistence>();
        var recurringAssignmentPersistence = Substitute.For<IRecurringReportAssignmentPersistence>();
        var photoPersistence = Substitute.For<IReportPhotoPersistence>();
        var relationshipAccessPersistence = Substitute.For<IReportingRelationshipAccessPersistence>();
        ReportRequest? loadedRequest = null;
        ReportSubmission? loadedSubmission = null;
        RecurringReportAssignment? loadedAssignment = null;
        unitOfWork ??= Substitute.For<IUnitOfWork>();
        commandDispatcher ??= Substitute.For<ICommandDispatcher>();

        if (findRequestById != null)
        {
            requestSubmissionPersistence.FindRequestByIdAsync(Arg.Any<Id<ReportRequest>>(), Arg.Any<CancellationToken>())
                .Returns(async args =>
                {
                    loadedRequest = await findRequestById((Id<ReportRequest>)args[0], (CancellationToken)args[1]);
                    return loadedRequest is null ? null : ReportingTestData.Request(loadedRequest);
                });
        }

        requestSubmissionPersistence.SetRequestExpiredAsync(Arg.Any<Id<ReportRequest>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (loadedRequest is not null)
                {
                    loadedRequest.Status = ReportRequestStatus.Expired;
                }

                return Task.CompletedTask;
            });
        requestSubmissionPersistence.SetRequestSubmittedAsync(
                Arg.Any<Id<ReportRequest>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(args =>
            {
                if (loadedRequest is not null)
                {
                    loadedRequest.Status = ReportRequestStatus.Submitted;
                    loadedRequest.SubmittedAt = (DateTimeOffset)args[1];
                }

                return Task.CompletedTask;
            });

        if (getPhotosByRequestId != null)
        {
            photoPersistence.ListByRequestAsync(Arg.Any<Id<ReportRequest>>(), Arg.Any<CancellationToken>())
                .Returns(args => MapPhotoModelsAsync(
                    getPhotosByRequestId,
                    (Id<ReportRequest>)args[0],
                    (CancellationToken)args[1]));
        }

        if (addSubmission != null)
        {
            requestSubmissionPersistence.AddSubmissionAsync(Arg.Any<NewReportSubmissionPersistenceModel>(), Arg.Any<CancellationToken>())
                .Returns(args => addSubmission((NewReportSubmissionPersistenceModel)args[0], (CancellationToken)args[1]));
        }

        if (findSubmissionByIdForTrainer != null)
        {
            requestSubmissionPersistence.FindSubmissionForTrainerAsync(Arg.Any<Id<ReportSubmission>>(), Arg.Any<Id<LgymApi.Identity.Contracts.AccountReference>>(), Arg.Any<Id<LgymApi.Identity.Contracts.AccountReference>>(), Arg.Any<CancellationToken>())
                .Returns(async args =>
                {
                    loadedSubmission = await findSubmissionByIdForTrainer(
                        (Id<ReportSubmission>)args[0],
                        ((Id<LgymApi.Identity.Contracts.AccountReference>)args[1]).Rebind<User>(),
                        ((Id<LgymApi.Identity.Contracts.AccountReference>)args[2]).Rebind<User>(),
                        (CancellationToken)args[3]);
                    return loadedSubmission is null ? null : ReportingTestData.Submission(loadedSubmission);
                });
        }

        if (findSubmissionByIdForTrainee != null)
        {
            requestSubmissionPersistence.FindSubmissionForTraineeAsync(Arg.Any<Id<ReportSubmission>>(), Arg.Any<Id<LgymApi.Identity.Contracts.AccountReference>>(), Arg.Any<CancellationToken>())
                .Returns(async args =>
                {
                    loadedSubmission = await findSubmissionByIdForTrainee(
                        (Id<ReportSubmission>)args[0],
                        ((Id<LgymApi.Identity.Contracts.AccountReference>)args[1]).Rebind<User>(),
                        (CancellationToken)args[2]);
                    return loadedSubmission is null ? null : ReportingTestData.Submission(loadedSubmission);
                });
        }

        if (getSubmissionsByTrainerAndTrainee != null)
        {
            requestSubmissionPersistence.ListSubmissionsByTrainerAndTraineeAsync(Arg.Any<Id<LgymApi.Identity.Contracts.AccountReference>>(), Arg.Any<Id<LgymApi.Identity.Contracts.AccountReference>>(), Arg.Any<CancellationToken>())
                .Returns(args => MapSubmissionModelsAsync(
                    getSubmissionsByTrainerAndTrainee,
                    ((Id<LgymApi.Identity.Contracts.AccountReference>)args[0]).Rebind<User>(),
                    ((Id<LgymApi.Identity.Contracts.AccountReference>)args[1]).Rebind<User>(),
                    (CancellationToken)args[2]));
        }

        if (getSubmissionsByTrainee != null)
        {
            requestSubmissionPersistence.ListSubmissionsByTraineeAsync(Arg.Any<Id<LgymApi.Identity.Contracts.AccountReference>>(), Arg.Any<CancellationToken>())
                .Returns(args => MapSubmissionModelsAsync(
                    getSubmissionsByTrainee,
                    ((Id<LgymApi.Identity.Contracts.AccountReference>)args[0]).Rebind<User>(),
                    (CancellationToken)args[1]));
        }

        photoPersistence.CountRecentUploadInitsAsync(Arg.Any<Id<LgymApi.Identity.Contracts.AccountReference>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(0);
        var commandOutboxWriter = Substitute.For<ICommandOutboxWriter>();
        commandOutboxWriter.StageAsync(Arg.Any<ReportSubmissionAcceptedProgressCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CommandEnvelopeStageResult(null, false)));
        relationshipAccessPersistence.GetAccessAsync(
                Arg.Any<Id<LgymApi.Identity.Contracts.AccountReference>>(),
                Arg.Any<Id<LgymApi.Identity.Contracts.AccountReference>>(),
                Arg.Any<CancellationToken>())
            .Returns(new ReportingRelationshipAccessFact(hasActiveTrainerLink));

        if (recurringAssignmentByRequestId != null)
        {
            recurringAssignmentPersistence.FindByCurrentRequestAsync(Arg.Any<Id<ReportRequest>>(), Arg.Any<CancellationToken>())
                .Returns(async args =>
                {
                    loadedAssignment = await recurringAssignmentByRequestId((Id<ReportRequest>)args[0], (CancellationToken)args[1]);
                    return loadedAssignment is null ? null : ReportingTestData.Assignment(loadedAssignment);
                });
        }

        requestSubmissionPersistence.UpdateFeedbackAsync(
                Arg.Any<Id<ReportSubmission>>(),
                Arg.Any<ReportSubmissionFeedbackUpdatePersistenceModel>(),
                Arg.Any<CancellationToken>())
            .Returns(args =>
            {
                if (loadedSubmission is not null)
                {
                    var update = (ReportSubmissionFeedbackUpdatePersistenceModel)args[1];
                    loadedSubmission.TrainerOverallComment = update.TrainerOverallComment;
                    loadedSubmission.TrainerFieldCommentsJson = update.TrainerFieldCommentsJson;
                    loadedSubmission.TrainerFeedbackAddedAt = update.TrainerFeedbackAddedAt;
                    loadedSubmission.TrainerFeedbackReadAt = update.TrainerFeedbackReadAt;
                }

                return Task.CompletedTask;
            });
        recurringAssignmentPersistence.UpdateAsync(
                Arg.Any<Id<RecurringReportAssignment>>(),
                Arg.Any<RecurringReportAssignmentUpdatePersistenceModel>(),
                Arg.Any<CancellationToken>())
            .Returns(args =>
            {
                if (loadedAssignment is not null)
                {
                    var update = (RecurringReportAssignmentUpdatePersistenceModel)args[1];
                    loadedAssignment.NextEligibleAt = update.NextEligibleAt;
                }

                return Task.CompletedTask;
            });

        var dependencies = Substitute.For<IReportingServiceDependencies>();
        dependencies.TemplatePersistence.Returns(templatePersistence);
        dependencies.RequestSubmissionPersistence.Returns(requestSubmissionPersistence);
        dependencies.RecurringAssignmentPersistence.Returns(recurringAssignmentPersistence);
        dependencies.PhotoPersistence.Returns(photoPersistence);
        dependencies.RelationshipAccessPersistence.Returns(relationshipAccessPersistence);
        dependencies.UnitOfWork.Returns(unitOfWork);
        dependencies.CommandDispatcher.Returns(commandDispatcher);
        dependencies.CommandOutboxWriter.Returns(commandOutboxWriter);
        dependencies.ReportSubmissionAcceptedProgressCommandFactory.Returns(new ReportSubmissionAcceptedProgressCommandFactory());
        dependencies.PhotoStorageProvider.Returns(Substitute.For<IPhotoStorageProvider>());
        dependencies.Mapper.Returns(ReportingTestData.Mapper());
        dependencies.Logger.Returns(Substitute.For<ILogger<ReportingService>>());
        dependencies.PhotoStorageOptions.Returns(new PhotoStorageOptions());

        var service = new ReportingService(dependencies);
        ReportingServiceTestExtensions.RegisterTrainerRole(service, userHasTrainerRole);
        return service;
    }

    private static async Task<IReadOnlyList<ReportPhotoPersistenceModel>> MapPhotoModelsAsync(
        Func<Id<ReportRequest>, CancellationToken, Task<List<Photo>>> source,
        Id<ReportRequest> requestId,
        CancellationToken cancellationToken)
        => (await source(requestId, cancellationToken)).Select(ReportingTestData.Photo).ToList();

    private static async Task<IReadOnlyList<ReportSubmissionPersistenceModel>> MapSubmissionModelsAsync(
        Func<Id<User>, Id<User>, CancellationToken, Task<List<ReportSubmission>>> source,
        Id<User> trainerId,
        Id<User> traineeId,
        CancellationToken cancellationToken)
        => (await source(trainerId, traineeId, cancellationToken)).Select(ReportingTestData.Submission).ToList();

    private static async Task<IReadOnlyList<ReportSubmissionPersistenceModel>> MapSubmissionModelsAsync(
        Func<Id<User>, CancellationToken, Task<List<ReportSubmission>>> source,
        Id<User> traineeId,
        CancellationToken cancellationToken)
        => (await source(traineeId, cancellationToken)).Select(ReportingTestData.Submission).ToList();

    #endregion
}
