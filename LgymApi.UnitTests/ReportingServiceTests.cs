using System.Text.Json;
using FluentAssertions;
using FluentValidation.TestHelper;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Features.Trainer.Validation;
using LgymApi.Application.Abstractions.Storage;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Options;
using LgymApi.Application.Repositories;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
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
            queued.TrainerId == trainerId
            && queued.TraineeId == traineeId
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
        var assignment = new RecurringReportAssignment { Id = Id<RecurringReportAssignment>.New(), NextEligibleAt = DateTimeOffset.UtcNow.AddDays(2) };
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
        Func<ReportSubmission, CancellationToken, Task>? addSubmission = null,
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
        var repository = Substitute.For<IReportingRepository>();
        unitOfWork ??= Substitute.For<IUnitOfWork>();
        commandDispatcher ??= Substitute.For<ICommandDispatcher>();

        if (findRequestById != null)
        {
            repository.FindRequestByIdAsync(Arg.Any<Id<ReportRequest>>(), Arg.Any<CancellationToken>())
                .Returns(args => findRequestById((Id<ReportRequest>)args[0], (CancellationToken)args[1]));
        }

        if (getPhotosByRequestId != null)
        {
            repository.GetPhotosByRequestIdAsync(Arg.Any<Id<ReportRequest>>(), Arg.Any<CancellationToken>())
                .Returns(args => getPhotosByRequestId((Id<ReportRequest>)args[0], (CancellationToken)args[1]));
        }

        if (addSubmission != null)
        {
            repository.AddSubmissionAsync(Arg.Any<ReportSubmission>(), Arg.Any<CancellationToken>())
                .Returns(args => addSubmission((ReportSubmission)args[0], (CancellationToken)args[1]));
        }

        if (findSubmissionByIdForTrainer != null)
        {
            repository.FindSubmissionByIdForTrainerAsync(Arg.Any<Id<ReportSubmission>>(), Arg.Any<Id<User>>(), Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
                .Returns(args => findSubmissionByIdForTrainer((Id<ReportSubmission>)args[0], (Id<User>)args[1], (Id<User>)args[2], (CancellationToken)args[3]));
        }

        if (findSubmissionByIdForTrainee != null)
        {
            repository.FindSubmissionByIdForTraineeAsync(Arg.Any<Id<ReportSubmission>>(), Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
                .Returns(args => findSubmissionByIdForTrainee((Id<ReportSubmission>)args[0], (Id<User>)args[1], (CancellationToken)args[2]));
        }

        if (getSubmissionsByTrainerAndTrainee != null)
        {
            repository.GetSubmissionsByTrainerAndTraineeAsync(Arg.Any<Id<User>>(), Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
                .Returns(args => getSubmissionsByTrainerAndTrainee((Id<User>)args[0], (Id<User>)args[1], (CancellationToken)args[2]));
        }

        if (getSubmissionsByTrainee != null)
        {
            repository.GetSubmissionsByTraineeAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
                .Returns(args => getSubmissionsByTrainee((Id<User>)args[0], (CancellationToken)args[1]));
        }

        var uploadInitTracker = Substitute.For<IPhotoUploadInitTracker>();
        uploadInitTracker.CountRecentUploadInitsAsync(Arg.Any<Id<User>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(0);
        var commandOutboxWriter = Substitute.For<ICommandOutboxWriter>();
        commandOutboxWriter.StageAsync(Arg.Any<ReportSubmissionAcceptedProgressCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CommandEnvelopeStageResult(null, false)));
        var roleRepository = Substitute.For<IRoleRepository>();
        roleRepository.UserHasRoleAsync(Arg.Any<Id<User>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(userHasTrainerRole);
        var relationshipAccess = Substitute.For<ICoachingRelationshipAccessService>();
        relationshipAccess.GetAccessDecisionAsync(Arg.Any<Id<User>>(), Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
            .Returns(new CoachingRelationshipAccessDecision(userHasTrainerRole, hasActiveTrainerLink));
        var recurringRepository = Substitute.For<IRecurringReportAssignmentRepository>();

        if (recurringAssignmentByRequestId != null)
        {
            recurringRepository.FindByCurrentReportRequestIdAsync(Arg.Any<Id<ReportRequest>>(), Arg.Any<CancellationToken>())
                .Returns(args => recurringAssignmentByRequestId((Id<ReportRequest>)args[0], (CancellationToken)args[1]));
        }

        var dependencies = Substitute.For<IReportingServiceDependencies>();
        dependencies.ReportingRepository.Returns(repository);
        dependencies.UnitOfWork.Returns(unitOfWork);
        dependencies.CommandDispatcher.Returns(commandDispatcher);
        dependencies.CommandOutboxWriter.Returns(commandOutboxWriter);
        dependencies.ReportSubmissionAcceptedProgressCommandFactory.Returns(new ReportSubmissionAcceptedProgressCommandFactory());
        dependencies.RoleRepository.Returns(roleRepository);
        dependencies.CoachingRelationshipAccessService.Returns(relationshipAccess);
        dependencies.RecurringReportAssignmentRepository.Returns(recurringRepository);
        dependencies.PhotoStorageProvider.Returns(Substitute.For<IPhotoStorageProvider>());
        dependencies.PhotoUploadInitTracker.Returns(uploadInitTracker);
        dependencies.Logger.Returns(Substitute.For<ILogger<ReportingService>>());
        dependencies.PhotoStorageOptions.Returns(new PhotoStorageOptions());

        return new ReportingService(dependencies);
    }

    #endregion
}
