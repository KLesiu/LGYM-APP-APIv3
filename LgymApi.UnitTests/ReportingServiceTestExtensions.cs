using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts.Accounts;
using System.Runtime.CompilerServices;

namespace LgymApi.UnitTests;

internal static class ReportingServiceTestExtensions
{
    private sealed class TrainerRole(bool value)
    {
        public bool Value { get; } = value;
    }

    private static readonly ConditionalWeakTable<IReportingService, TrainerRole> TrainerRoles = new();

    public static void RegisterTrainerRole(IReportingService service, bool isTrainer)
    {
        TrainerRoles.Remove(service);
        TrainerRoles.Add(service, new TrainerRole(isTrainer));
    }

    private static AuthenticatedAccountContext Account(IReportingService service, Id<User> userId)
        => ReportingTestData.Account(userId, TrainerRoles.TryGetValue(service, out var role) && role.Value);

    public static Task<Result<ReportTemplateResult, AppError>> CreateTemplateAsync(
        this IReportingService service,
        User trainer,
        CreateReportTemplateCommand command,
        CancellationToken cancellationToken = default)
        => service.CreateTemplateAsync(Account(service, trainer.Id), command, cancellationToken);

    public static Task<Result<List<ReportTemplateResult>, AppError>> GetTrainerTemplatesAsync(
        this IReportingService service,
        User trainer,
        CancellationToken cancellationToken = default)
        => service.GetTrainerTemplatesAsync(Account(service, trainer.Id), cancellationToken);

    public static Task<Result<ReportTemplateResult, AppError>> GetTrainerTemplateAsync(
        this IReportingService service,
        User trainer,
        Id<ReportTemplate> templateId,
        CancellationToken cancellationToken = default)
        => service.GetTrainerTemplateAsync(Account(service, trainer.Id), templateId, cancellationToken);

    public static Task<Result<ReportTemplateResult, AppError>> UpdateTemplateAsync(
        this IReportingService service,
        User trainer,
        Id<ReportTemplate> templateId,
        CreateReportTemplateCommand command,
        CancellationToken cancellationToken = default)
        => service.UpdateTemplateAsync(Account(service, trainer.Id), templateId, command, cancellationToken);

    public static Task<Result<Unit, AppError>> DeleteTemplateAsync(
        this IReportingService service,
        User trainer,
        Id<ReportTemplate> templateId,
        CancellationToken cancellationToken = default)
        => service.DeleteTemplateAsync(Account(service, trainer.Id), templateId, cancellationToken);

    public static Task<Result<ReportRequestResult, AppError>> CreateReportRequestAsync(
        this IReportingService service,
        User trainer,
        Id<User> traineeId,
        CreateReportRequestCommand command,
        CancellationToken cancellationToken = default)
        => service.CreateReportRequestAsync(
            Account(service, trainer.Id),
            ReportingTestData.AccountId(traineeId),
            command,
            cancellationToken);

    public static Task<Result<List<ReportRequestResult>, AppError>> GetPendingRequestsForTraineeAsync(
        this IReportingService service,
        User trainee,
        CancellationToken cancellationToken = default)
        => service.GetPendingRequestsForTraineeAsync(ReportingTestData.Account(trainee.Id), cancellationToken);

    public static Task<Result<ReportSubmissionResult, AppError>> SubmitReportRequestAsync(
        this IReportingService service,
        User trainee,
        Id<ReportRequest> requestId,
        SubmitReportRequestCommand command,
        CancellationToken cancellationToken = default)
        => service.SubmitReportRequestAsync(ReportingTestData.Account(trainee.Id), requestId, command, cancellationToken);

    public static Task<Result<ReportSubmissionResult, AppError>> UpdateTrainerFeedbackAsync(
        this IReportingService service,
        User trainer,
        Id<User> traineeId,
        Id<ReportSubmission> submissionId,
        UpdateReportSubmissionFeedbackCommand command,
        CancellationToken cancellationToken = default)
        => service.UpdateTrainerFeedbackAsync(
            Account(service, trainer.Id),
            ReportingTestData.AccountId(traineeId),
            submissionId,
            command,
            cancellationToken);

    public static Task<Result<ReportSubmissionResult, AppError>> MarkTrainerFeedbackAsReadAsync(
        this IReportingService service,
        User trainee,
        Id<ReportSubmission> submissionId,
        CancellationToken cancellationToken = default)
        => service.MarkTrainerFeedbackAsReadAsync(ReportingTestData.Account(trainee.Id), submissionId, cancellationToken);

    public static Task<Result<List<ReportSubmissionResult>, AppError>> GetOwnSubmissionsAsync(
        this IReportingService service,
        User trainee,
        CancellationToken cancellationToken = default)
        => service.GetOwnSubmissionsAsync(ReportingTestData.Account(trainee.Id), cancellationToken);

    public static Task<Result<List<ReportSubmissionResult>, AppError>> GetTraineeSubmissionsAsync(
        this IReportingService service,
        User trainer,
        Id<User> traineeId,
        CancellationToken cancellationToken = default)
        => service.GetTraineeSubmissionsAsync(
            Account(service, trainer.Id),
            ReportingTestData.AccountId(traineeId),
            cancellationToken);

    public static Task<Result<RecurringReportAssignmentResult, AppError>> CreateAsync(
        this IRecurringReportAssignmentService service,
        User trainer,
        Id<User> traineeId,
        UpsertRecurringReportAssignmentCommand command,
        CancellationToken cancellationToken = default)
        => service.CreateAsync(ReportingTestData.Account(trainer.Id, true), ReportingTestData.AccountId(traineeId), command, cancellationToken);

    public static Task<Result<List<RecurringReportAssignmentResult>, AppError>> GetForTraineeAsync(
        this IRecurringReportAssignmentService service,
        User trainer,
        Id<User> traineeId,
        CancellationToken cancellationToken = default)
        => service.GetForTraineeAsync(ReportingTestData.Account(trainer.Id, true), ReportingTestData.AccountId(traineeId), cancellationToken);

    public static Task<Result<RecurringReportAssignmentResult, AppError>> UpdateAsync(
        this IRecurringReportAssignmentService service,
        User trainer,
        Id<User> traineeId,
        Id<RecurringReportAssignment> assignmentId,
        UpsertRecurringReportAssignmentCommand command,
        CancellationToken cancellationToken = default)
        => service.UpdateAsync(ReportingTestData.Account(trainer.Id, true), ReportingTestData.AccountId(traineeId), assignmentId, command, cancellationToken);

    public static Task<Result<RecurringReportAssignmentResult, AppError>> PauseAsync(
        this IRecurringReportAssignmentService service,
        User trainer,
        Id<User> traineeId,
        Id<RecurringReportAssignment> assignmentId,
        CancellationToken cancellationToken = default)
        => service.PauseAsync(ReportingTestData.Account(trainer.Id, true), ReportingTestData.AccountId(traineeId), assignmentId, cancellationToken);

    public static Task<Result<RecurringReportAssignmentResult, AppError>> ResumeAsync(
        this IRecurringReportAssignmentService service,
        User trainer,
        Id<User> traineeId,
        Id<RecurringReportAssignment> assignmentId,
        CancellationToken cancellationToken = default)
        => service.ResumeAsync(ReportingTestData.Account(trainer.Id, true), ReportingTestData.AccountId(traineeId), assignmentId, cancellationToken);

    public static Task<Result<Unit, AppError>> DeleteAsync(
        this IRecurringReportAssignmentService service,
        User trainer,
        Id<User> traineeId,
        Id<RecurringReportAssignment> assignmentId,
        CancellationToken cancellationToken = default)
        => service.DeleteAsync(ReportingTestData.Account(trainer.Id, true), ReportingTestData.AccountId(traineeId), assignmentId, cancellationToken);
}
