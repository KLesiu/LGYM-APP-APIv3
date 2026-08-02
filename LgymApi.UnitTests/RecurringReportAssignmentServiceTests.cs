using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Repositories;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories.Reporting;
using LgymApi.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed partial class RecurringReportAssignmentServiceTests
{
    [Test]
    public async Task CreateAsync_CreatesRecurringAssignment_ForOwnedTrainee()
    {
        await using var db = CreateDbContext("recurring-create-success");
        var trainer = CreateUser();
        var traineeId = Id<User>.New();
        var template = CreateTemplate(trainer.Id);
        db.ReportTemplates.Add(template);
        await db.SaveChangesAsync();

        var service = CreateService(db, trainer.Id, traineeId, ownsTrainee: true);

        var result = await service.CreateAsync(ReportingTestData.Account(trainer.Id, true), ReportingTestData.AccountId(traineeId), new UpsertRecurringReportAssignmentCommand
        {
            TemplateId = template.Id,
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = DateTimeOffset.UtcNow,
            Note = "weekly check-in"
        });

        result.IsSuccess.Should().BeTrue();
        db.RecurringReportAssignments.Should().ContainSingle();
        db.RecurringReportAssignments.Single().TemplateId.Should().Be(template.Id);
    }

    [Test]
    public async Task CreateAsync_Fails_WhenTraineeIsNotOwnedByTrainer()
    {
        await using var db = CreateDbContext("recurring-create-forbidden");
        var trainer = CreateUser();
        var traineeId = Id<User>.New();
        var template = CreateTemplate(trainer.Id);
        db.ReportTemplates.Add(template);
        await db.SaveChangesAsync();

        var service = CreateService(db, trainer.Id, traineeId, ownsTrainee: false);

        var result = await service.CreateAsync(ReportingTestData.Account(trainer.Id, true), ReportingTestData.AccountId(traineeId), new UpsertRecurringReportAssignmentCommand
        {
            TemplateId = template.Id,
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = DateTimeOffset.UtcNow,
        });

        result.IsFailure.Should().BeTrue();
        db.RecurringReportAssignments.Should().BeEmpty();
    }

    [Test]
    public async Task PauseAsync_WhenTrainerNoLongerOwnsTrainee_ReturnsNotFoundWithoutMutatingAssignment()
    {
        await using var db = CreateDbContext("recurring-pause-not-owned");
        var trainer = CreateUser();
        var traineeId = Id<User>.New();
        var template = CreateTemplate(trainer.Id);
        var assignment = CreateAssignment(trainer.Id, traineeId, template.Id);
        db.ReportTemplates.Add(template);
        db.RecurringReportAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var service = CreateService(db, trainer.Id, traineeId, ownsTrainee: false);
        var result = await service.PauseAsync(ReportingTestData.Account(trainer.Id, true), ReportingTestData.AccountId(traineeId), assignment.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingNotFoundError>();
        db.RecurringReportAssignments.Single().IsActive.Should().BeTrue();
    }

    [Test]
    public async Task PauseAsync_WhenCallerIsNotTrainer_ReturnsForbiddenWithoutMutatingAssignment()
    {
        await using var db = CreateDbContext("recurring-pause-not-trainer");
        var caller = CreateUser();
        var traineeId = Id<User>.New();
        var template = CreateTemplate(caller.Id);
        var assignment = CreateAssignment(caller.Id, traineeId, template.Id);
        db.ReportTemplates.Add(template);
        db.RecurringReportAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var service = CreateService(db, caller.Id, traineeId, ownsTrainee: true, isTrainer: false);
        var result = await service.PauseAsync(ReportingTestData.Account(caller.Id), ReportingTestData.AccountId(traineeId), assignment.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingForbiddenError>();
        db.RecurringReportAssignments.Single().IsActive.Should().BeTrue();
    }

    [Test]
    public async Task ProcessDueAssignmentsAsync_DoesNotCreateNextRequest_BeforeFeedbackRead()
    {
        await using var db = CreateDbContext("recurring-worker-blocked");
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var template = CreateTemplate(trainerId);
        var currentRequest = new ReportRequest
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            TemplateId = template.Id,
            Template = template,
            Status = ReportRequestStatus.Submitted,
            SubmittedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        var submission = new ReportSubmission
        {
            Id = Id<ReportSubmission>.New(),
            ReportRequestId = currentRequest.Id,
            ReportRequest = currentRequest,
            TraineeId = traineeId,
            PayloadJson = "{}",
            TrainerFeedbackAddedAt = DateTimeOffset.UtcNow.AddDays(-1),
            TrainerFeedbackReadAt = null
        };
        currentRequest.Submission = submission;
        var assignment = new RecurringReportAssignment
        {
            Id = Id<RecurringReportAssignment>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            TemplateId = template.Id,
            Template = template,
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-10),
            IsActive = true,
            CurrentReportRequestId = currentRequest.Id,
            CurrentReportRequest = currentRequest,
            NextEligibleAt = null
        };

        db.ReportTemplates.Add(template);
        db.ReportRequests.Add(currentRequest);
        db.ReportSubmissions.Add(submission);
        db.RecurringReportAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var commandOutboxWriter = CreateOutboxWriter();
        var service = CreateService(db, trainerId, traineeId, ownsTrainee: true, commandOutboxWriter: commandOutboxWriter);

        await service.ProcessDueAssignmentsAsync();

        db.ReportRequests.Should().HaveCount(1);
        await commandOutboxWriter.DidNotReceiveWithAnyArgs().StageAsync(
            Arg.Any<ReportRequestCreatedInAppNotificationCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessDueAssignmentsAsync_CreatesNextRequest_AfterFeedbackReadAndIntervalElapsed()
    {
        await using var db = CreateDbContext("recurring-worker-success");
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var template = CreateTemplate(trainerId);
        var currentRequest = new ReportRequest
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            TemplateId = template.Id,
            Template = template,
            Status = ReportRequestStatus.Submitted,
            SubmittedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
        var submission = new ReportSubmission
        {
            Id = Id<ReportSubmission>.New(),
            ReportRequestId = currentRequest.Id,
            ReportRequest = currentRequest,
            TraineeId = traineeId,
            PayloadJson = "{}",
            TrainerFeedbackAddedAt = DateTimeOffset.UtcNow.AddDays(-9),
            TrainerFeedbackReadAt = DateTimeOffset.UtcNow.AddDays(-8)
        };
        currentRequest.Submission = submission;
        var assignment = new RecurringReportAssignment
        {
            Id = Id<RecurringReportAssignment>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            TemplateId = template.Id,
            Template = template,
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-20),
            IsActive = true,
            CurrentReportRequestId = currentRequest.Id,
            CurrentReportRequest = currentRequest,
            NextEligibleAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        db.ReportTemplates.Add(template);
        db.ReportRequests.Add(currentRequest);
        db.ReportSubmissions.Add(submission);
        db.RecurringReportAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var commandOutboxWriter = CreateOutboxWriter();
        var service = CreateService(db, trainerId, traineeId, ownsTrainee: true, commandOutboxWriter: commandOutboxWriter);

        await service.ProcessDueAssignmentsAsync();

        db.ReportRequests.Should().HaveCount(2);
        db.RecurringReportAssignments.Single().CurrentReportRequestId.Should().NotBe(currentRequest.Id);
        await commandOutboxWriter.Received(1).StageAsync(
            Arg.Any<ReportRequestCreatedInAppNotificationCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessDueAssignmentsAsync_IsIdempotent_WhenCalledTwice()
    {
        await using var db = CreateDbContext("recurring-worker-idempotent");
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var template = CreateTemplate(trainerId);
        var currentRequest = new ReportRequest
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            TemplateId = template.Id,
            Template = template,
            Status = ReportRequestStatus.Submitted,
            SubmittedAt = DateTimeOffset.UtcNow.AddDays(-10)
        };
        var submission = new ReportSubmission
        {
            Id = Id<ReportSubmission>.New(),
            ReportRequestId = currentRequest.Id,
            ReportRequest = currentRequest,
            TraineeId = traineeId,
            PayloadJson = "{}",
            TrainerFeedbackAddedAt = DateTimeOffset.UtcNow.AddDays(-9),
            TrainerFeedbackReadAt = DateTimeOffset.UtcNow.AddDays(-8)
        };
        currentRequest.Submission = submission;
        var assignment = new RecurringReportAssignment
        {
            Id = Id<RecurringReportAssignment>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            TemplateId = template.Id,
            Template = template,
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-20),
            IsActive = true,
            CurrentReportRequestId = currentRequest.Id,
            CurrentReportRequest = currentRequest,
            NextEligibleAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        db.ReportTemplates.Add(template);
        db.ReportRequests.Add(currentRequest);
        db.ReportSubmissions.Add(submission);
        db.RecurringReportAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var commandOutboxWriter = CreateOutboxWriter();
        var service = CreateService(db, trainerId, traineeId, ownsTrainee: true, commandOutboxWriter: commandOutboxWriter);

        await service.ProcessDueAssignmentsAsync();
        await service.ProcessDueAssignmentsAsync();

        db.ReportRequests.Should().HaveCount(2);
        await commandOutboxWriter.Received(1).StageAsync(
            Arg.Any<ReportRequestCreatedInAppNotificationCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateAsync_UpdatesAssignmentFields()
    {
        await using var db = CreateDbContext("recurring-update-success");
        var trainer = CreateUser();
        var traineeId = Id<User>.New();
        var oldTemplate = CreateTemplate(trainer.Id);
        var newTemplate = CreateTemplate(trainer.Id);
        var assignment = CreateAssignment(trainer.Id, traineeId, oldTemplate.Id);
        db.ReportTemplates.AddRange(oldTemplate, newTemplate);
        db.RecurringReportAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var service = CreateService(db, trainer.Id, traineeId, ownsTrainee: true);
        var startsAt = DateTimeOffset.UtcNow.AddDays(2);

        var result = await service.UpdateAsync(ReportingTestData.Account(trainer.Id, true), ReportingTestData.AccountId(traineeId), assignment.Id, new UpsertRecurringReportAssignmentCommand
        {
            TemplateId = newTemplate.Id,
            IntervalValue = 2,
            IntervalUnit = RecurringReportIntervalUnit.Month,
            StartsAt = startsAt,
            EndsAt = startsAt.AddDays(30),
            Note = "updated note"
        });

        result.IsSuccess.Should().BeTrue();
        var stored = db.RecurringReportAssignments.Single();
        stored.TemplateId.Should().Be(newTemplate.Id);
        stored.IntervalValue.Should().Be(2);
        stored.IntervalUnit.Should().Be(RecurringReportIntervalUnit.Month);
        stored.Note.Should().Be("updated note");
    }

    [Test]
    public async Task PauseAndResumeAsync_ToggleActiveState()
    {
        await using var db = CreateDbContext("recurring-pause-resume");
        var trainer = CreateUser();
        var traineeId = Id<User>.New();
        var template = CreateTemplate(trainer.Id);
        var assignment = CreateAssignment(trainer.Id, traineeId, template.Id);
        db.ReportTemplates.Add(template);
        db.RecurringReportAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var service = CreateService(db, trainer.Id, traineeId, ownsTrainee: true);

        (await service.PauseAsync(ReportingTestData.Account(trainer.Id, true), ReportingTestData.AccountId(traineeId), assignment.Id)).IsSuccess.Should().BeTrue();
        db.RecurringReportAssignments.Single().IsActive.Should().BeFalse();

        (await service.ResumeAsync(ReportingTestData.Account(trainer.Id, true), ReportingTestData.AccountId(traineeId), assignment.Id)).IsSuccess.Should().BeTrue();
        db.RecurringReportAssignments.Single().IsActive.Should().BeTrue();
    }

    [Test]
    public async Task DeleteAsync_SoftDeletesAssignment()
    {
        await using var db = CreateDbContext("recurring-delete");
        var trainer = CreateUser();
        var traineeId = Id<User>.New();
        var template = CreateTemplate(trainer.Id);
        var assignment = CreateAssignment(trainer.Id, traineeId, template.Id);
        db.ReportTemplates.Add(template);
        db.RecurringReportAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var service = CreateService(db, trainer.Id, traineeId, ownsTrainee: true);

        var result = await service.DeleteAsync(ReportingTestData.Account(trainer.Id, true), ReportingTestData.AccountId(traineeId), assignment.Id);

        result.IsSuccess.Should().BeTrue();
        var stored = db.RecurringReportAssignments.IgnoreQueryFilters().Single();
        stored.IsDeleted.Should().BeTrue();
        stored.IsActive.Should().BeFalse();
    }

    [Test]
    public async Task UpdateAsync_WhenTemplateDeleted_ReturnsFailure()
    {
        await using var db = CreateDbContext("recurring-update-template-deleted");
        var trainer = CreateUser();
        var traineeId = Id<User>.New();
        var template = CreateTemplate(trainer.Id);
        template.IsDeleted = true;
        var activeTemplate = CreateTemplate(trainer.Id);
        var assignment = new RecurringReportAssignment
        {
            Id = Id<RecurringReportAssignment>.New(),
            TrainerId = trainer.Id,
            TraineeId = traineeId,
            TemplateId = activeTemplate.Id,
            Template = activeTemplate,
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-10),
            IsActive = true
        };

        db.ReportTemplates.AddRange(template, activeTemplate);
        db.RecurringReportAssignments.Add(assignment);
        await db.SaveChangesAsync();

        var service = CreateService(db, trainer.Id, traineeId, ownsTrainee: true);

        var result = await service.UpdateAsync(ReportingTestData.Account(trainer.Id, true), ReportingTestData.AccountId(traineeId), assignment.Id, new UpsertRecurringReportAssignmentCommand
        {
            TemplateId = template.Id,
            IntervalValue = 2,
            IntervalUnit = RecurringReportIntervalUnit.Month,
            StartsAt = DateTimeOffset.UtcNow,
            Note = "updated"
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ReportingNotFoundError>();
    }

    private static RecurringReportAssignmentService CreateService(
        AppDbContext db,
        Id<User> trainerId,
        Id<User> traineeId,
        bool ownsTrainee,
        ICommandOutboxWriter? commandOutboxWriter = null,
        bool isTrainer = true)
    {
        var relationshipAccess = Substitute.For<IReportingRelationshipAccessPersistence>();
        relationshipAccess
            .GetAccessAsync(ReportingTestData.AccountId(trainerId), ReportingTestData.AccountId(traineeId), Arg.Any<CancellationToken>())
            .Returns(new ReportingRelationshipAccessFact(ownsTrainee));

        return new RecurringReportAssignmentService(
            new ReportTemplatePersistenceRepository(db),
            new ReportRequestSubmissionPersistenceRepository(db),
            new RecurringReportAssignmentPersistenceRepository(db),
            relationshipAccess,
            ReportingTestData.Mapper(),
            commandOutboxWriter ?? CreateOutboxWriter(),
            new EfUnitOfWork(db),
            NullLogger<RecurringReportAssignmentService>.Instance);
    }

    private static ICommandOutboxWriter CreateOutboxWriter()
    {
        var writer = Substitute.For<ICommandOutboxWriter>();
        writer.StageAsync(
                Arg.Any<ReportRequestCreatedInAppNotificationCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new CommandEnvelopeStageResult("test-envelope", false));
        return writer;
    }

    private static AppDbContext CreateDbContext(string name)
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"{name}-{Id<RecurringReportAssignmentServiceTests>.New():N}")
            .Options);

    private static User CreateUser()
        => new()
        {
            Id = Id<User>.New(),
            Name = "Trainer",
            Email = "trainer@example.com",
            ProfileRank = "Rookie"
        };

    private static ReportTemplate CreateTemplate(Id<User> trainerId)
        => new()
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = trainerId,
            Name = "Weekly check-in",
            Fields =
            [
                new ReportTemplateField
                {
                    Id = Id<ReportTemplateField>.New(),
                    Key = "summary",
                    Label = "Summary",
                    Order = 1,
                    Type = ReportFieldType.Text,
                    IsRequired = true,
                }
            ]
        };

    private static RecurringReportAssignment CreateAssignment(Id<User> trainerId, Id<User> traineeId, Id<ReportTemplate> templateId)
        => new()
        {
            Id = Id<RecurringReportAssignment>.New(),
            TrainerId = trainerId,
            TraineeId = traineeId,
            TemplateId = templateId,
            IntervalValue = 1,
            IntervalUnit = RecurringReportIntervalUnit.Week,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-5),
            IsActive = true,
            Note = "weekly",
            NextEligibleAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
}
