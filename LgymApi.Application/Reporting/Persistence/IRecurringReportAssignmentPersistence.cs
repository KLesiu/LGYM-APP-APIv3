using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Reporting.Persistence;

public interface IRecurringReportAssignmentPersistence
{
    Task AddAsync(NewRecurringReportAssignmentPersistenceModel assignment, CancellationToken cancellationToken = default);
    Task<RecurringReportAssignmentPersistenceModel?> FindForTrainerAsync(Id<RecurringReportAssignment> assignmentId, Id<AccountReference> trainerId, Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
    Task<RecurringReportAssignmentPersistenceModel?> FindByIdAsync(Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default);
    Task<RecurringReportAssignmentPersistenceModel?> FindByIdForUpdateAsync(Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default);
    Task<RecurringReportAssignmentPersistenceModel?> FindByCurrentRequestAsync(Id<ReportRequest> reportRequestId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecurringReportAssignmentPersistenceModel>> ListByTrainerAndTraineeAsync(Id<AccountReference> trainerId, Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecurringReportAssignmentPersistenceModel>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task UpdateAsync(Id<RecurringReportAssignment> assignmentId, RecurringReportAssignmentUpdatePersistenceModel update, CancellationToken cancellationToken = default);
}
