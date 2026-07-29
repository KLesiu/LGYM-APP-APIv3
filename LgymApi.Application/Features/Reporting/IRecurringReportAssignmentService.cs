using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Features.Reporting;

public interface IRecurringReportAssignmentService
{
    Task<Result<RecurringReportAssignmentResult, AppError>> CreateAsync(AuthenticatedAccountContext currentTrainer, Id<AccountReference> traineeId, UpsertRecurringReportAssignmentCommand command, CancellationToken cancellationToken = default);
    Task<Result<List<RecurringReportAssignmentResult>, AppError>> GetForTraineeAsync(AuthenticatedAccountContext currentTrainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
    Task<Result<RecurringReportAssignmentResult, AppError>> UpdateAsync(AuthenticatedAccountContext currentTrainer, Id<AccountReference> traineeId, Id<RecurringReportAssignment> assignmentId, UpsertRecurringReportAssignmentCommand command, CancellationToken cancellationToken = default);
    Task<Result<RecurringReportAssignmentResult, AppError>> PauseAsync(AuthenticatedAccountContext currentTrainer, Id<AccountReference> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default);
    Task<Result<RecurringReportAssignmentResult, AppError>> ResumeAsync(AuthenticatedAccountContext currentTrainer, Id<AccountReference> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteAsync(AuthenticatedAccountContext currentTrainer, Id<AccountReference> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default);
    Task ProcessDueAssignmentsAsync(CancellationToken cancellationToken = default);
}
