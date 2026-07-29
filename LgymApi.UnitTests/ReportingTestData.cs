using System.Reflection;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.UnitTests;

internal static class ReportingTestData
{
    public static Id<AccountReference> AccountId(Id<User> userId) => userId.Rebind<AccountReference>();

    public static AuthenticatedAccountContext Account(Id<User> userId, bool isTrainer = false)
        => new(
            AccountId(userId),
            null,
            isTrainer ? [LgymApi.Domain.Security.AuthConstants.Roles.Trainer] : [],
            [],
            false,
            false);

    public static AccountLookup Lookup(
        Id<AccountReference> accountId,
        string name,
        string preferredLanguage = "en")
        => new(accountId, name, $"{accountId}@example.com", null, preferredLanguage, "UTC", DateTimeOffset.UtcNow);

    public static ReportTemplatePersistenceModel Template(ReportTemplate entity)
        => new(
            entity.Id,
            AccountId(entity.TrainerId),
            entity.Name,
            entity.Description,
            entity.CreatedAt,
            entity.IsDeleted,
            entity.Fields
                .OrderBy(field => field.Order)
                .ThenBy(field => field.CreatedAt)
                .Select(field => new ReportTemplateFieldPersistenceModel(
                    field.Id,
                    field.Key,
                    field.Label,
                    field.Type,
                    field.IsRequired,
                    field.Order,
                    field.ModuleConfig,
                    field.CreatedAt))
                .ToList());

    public static ReportRequestPersistenceModel Request(ReportRequest entity)
        => new(
            entity.Id,
            AccountId(entity.TrainerId),
            AccountId(entity.TraineeId),
            entity.TemplateId,
            entity.RecurringReportAssignmentId,
            entity.Status,
            entity.DueAt,
            entity.SubmittedAt,
            entity.Note,
            entity.CreatedAt,
            entity.IsDeleted,
            Template(entity.Template),
            entity.Submission is null
                ? null
                : new ReportSubmissionFeedbackPersistenceModel(
                    entity.Submission.TrainerFeedbackAddedAt,
                    entity.Submission.TrainerFeedbackReadAt));

    public static ReportSubmissionPersistenceModel Submission(ReportSubmission entity)
        => new(
            entity.Id,
            entity.ReportRequestId,
            AccountId(entity.TraineeId),
            entity.PayloadJson,
            entity.TrainerOverallComment,
            entity.TrainerFieldCommentsJson,
            entity.TrainerFeedbackAddedAt,
            entity.TrainerFeedbackReadAt,
            entity.CreatedAt,
            Request(entity.ReportRequest));

    public static RecurringReportAssignmentPersistenceModel Assignment(RecurringReportAssignment entity)
        => new(
            entity.Id,
            AccountId(entity.TrainerId),
            AccountId(entity.TraineeId),
            entity.TemplateId,
            entity.IntervalValue,
            entity.IntervalUnit,
            entity.StartsAt,
            entity.EndsAt,
            entity.IsActive,
            entity.Note,
            entity.CurrentReportRequestId,
            entity.LastRequestCreatedAt,
            entity.NextEligibleAt,
            entity.CreatedAt,
            entity.IsDeleted,
            Template(entity.Template),
            entity.CurrentReportRequest is null ? null : Request(entity.CurrentReportRequest));

    public static ReportPhotoPersistenceModel Photo(Photo entity)
        => new(
            entity.Id,
            entity.StorageKey,
            entity.MimeType,
            entity.SizeBytes,
            entity.Checksum,
            entity.ThumbnailStorageKey,
            entity.ViewType,
            entity.ReportRequestId,
            AccountId(entity.UploaderUserId),
            AccountId(entity.OwnerUserId),
            entity.CreatedAt,
            entity.IsDeleted);

    public static IMapper Mapper()
        => (IMapper)Activator.CreateInstance(
            typeof(Mapper),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new IMappingProfile[] { new ReportingPersistenceMappingProfile() }],
            culture: null)!;
}
