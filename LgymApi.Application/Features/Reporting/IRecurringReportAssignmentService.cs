using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.Reporting;

public interface IRecurringReportAssignmentService
{
    Task<Result<RecurringReportAssignmentResult, AppError>> CreateAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, UpsertRecurringReportAssignmentCommand command, CancellationToken cancellationToken = default);
    Task<Result<List<RecurringReportAssignmentResult>, AppError>> GetForTraineeAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, CancellationToken cancellationToken = default);
    Task<Result<RecurringReportAssignmentResult, AppError>> UpdateAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, Id<RecurringReportAssignment> assignmentId, UpsertRecurringReportAssignmentCommand command, CancellationToken cancellationToken = default);
    Task<Result<RecurringReportAssignmentResult, AppError>> PauseAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default);
    Task<Result<RecurringReportAssignmentResult, AppError>> ResumeAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default);
    Task ProcessDueAssignmentsAsync(CancellationToken cancellationToken = default);
}
