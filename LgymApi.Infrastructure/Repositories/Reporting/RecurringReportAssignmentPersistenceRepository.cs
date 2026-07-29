using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.Reporting;

public sealed class RecurringReportAssignmentPersistenceRepository : IRecurringReportAssignmentPersistence
{
    private readonly AppDbContext _dbContext;

    public RecurringReportAssignmentPersistenceRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public Task AddAsync(NewRecurringReportAssignmentPersistenceModel assignment, CancellationToken cancellationToken = default)
        => _dbContext.RecurringReportAssignments.AddAsync(new RecurringReportAssignment
        {
            Id = assignment.Id,
            TrainerId = ReportingPersistenceAccountIds.ToPersisted(assignment.TrainerId),
            TraineeId = ReportingPersistenceAccountIds.ToPersisted(assignment.TraineeId),
            TemplateId = assignment.TemplateId,
            IntervalValue = assignment.IntervalValue,
            IntervalUnit = assignment.IntervalUnit,
            StartsAt = assignment.StartsAt,
            EndsAt = assignment.EndsAt,
            IsActive = assignment.IsActive,
            Note = assignment.Note,
            CurrentReportRequestId = assignment.CurrentReportRequestId,
            LastRequestCreatedAt = assignment.LastRequestCreatedAt,
            NextEligibleAt = assignment.NextEligibleAt,
            CreatedAt = assignment.CreatedAt
        }, cancellationToken).AsTask();

    public async Task<RecurringReportAssignmentPersistenceModel?> FindForTrainerAsync(
        Id<RecurringReportAssignment> assignmentId,
        Id<AccountReference> trainerId,
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken = default)
    {
        var persistedTrainerId = ReportingPersistenceAccountIds.ToPersisted(trainerId);
        var persistedTraineeId = ReportingPersistenceAccountIds.ToPersisted(traineeId);
        var entity = await BaseQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(assignment =>
                assignment.Id == assignmentId
                && assignment.TrainerId == persistedTrainerId
                && assignment.TraineeId == persistedTraineeId,
                cancellationToken);
        return entity is null ? null : ReportingPersistenceProjection.Assignment(entity);
    }

    public async Task<RecurringReportAssignmentPersistenceModel?> FindByIdAsync(
        Id<RecurringReportAssignment> assignmentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await BaseQuery()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(assignment => !assignment.IsDeleted)
            .FirstOrDefaultAsync(assignment => assignment.Id == assignmentId, cancellationToken);
        return entity is null ? null : ReportingPersistenceProjection.Assignment(entity);
    }

    public async Task<RecurringReportAssignmentPersistenceModel?> FindByCurrentRequestAsync(
        Id<ReportRequest> reportRequestId,
        CancellationToken cancellationToken = default)
    {
        var entity = await BaseQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(assignment => assignment.CurrentReportRequestId == reportRequestId, cancellationToken);
        return entity is null ? null : ReportingPersistenceProjection.Assignment(entity);
    }

    public async Task<IReadOnlyList<RecurringReportAssignmentPersistenceModel>> ListByTrainerAndTraineeAsync(
        Id<AccountReference> trainerId,
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken = default)
    {
        var persistedTrainerId = ReportingPersistenceAccountIds.ToPersisted(trainerId);
        var persistedTraineeId = ReportingPersistenceAccountIds.ToPersisted(traineeId);
        var entities = await BaseQuery()
            .AsNoTracking()
            .Where(assignment => assignment.TrainerId == persistedTrainerId && assignment.TraineeId == persistedTraineeId)
            .OrderByDescending(assignment => assignment.CreatedAt)
            .ToListAsync(cancellationToken);
        return entities.Select(ReportingPersistenceProjection.Assignment).ToList();
    }

    public async Task<IReadOnlyList<RecurringReportAssignmentPersistenceModel>> ListDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var entities = await BaseQuery()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(assignment => !assignment.IsDeleted && assignment.IsActive)
            .ToListAsync(cancellationToken);
        return entities
            .Select(ReportingPersistenceProjection.Assignment)
            .Where(assignment => assignment.StartsAt <= now)
            .Where(assignment => !assignment.EndsAt.HasValue || assignment.EndsAt.Value >= now)
            .OrderBy(assignment => assignment.NextEligibleAt ?? assignment.StartsAt)
            .ThenBy(assignment => assignment.CreatedAt)
            .ToList();
    }

    public async Task UpdateAsync(
        Id<RecurringReportAssignment> assignmentId,
        RecurringReportAssignmentUpdatePersistenceModel update,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.RecurringReportAssignments
            .IgnoreQueryFilters()
            .FirstAsync(assignment => assignment.Id == assignmentId, cancellationToken);
        entity.TemplateId = update.TemplateId;
        entity.IntervalValue = update.IntervalValue;
        entity.IntervalUnit = update.IntervalUnit;
        entity.StartsAt = update.StartsAt;
        entity.EndsAt = update.EndsAt;
        entity.IsActive = update.IsActive;
        entity.Note = update.Note;
        entity.CurrentReportRequestId = update.CurrentReportRequestId;
        entity.LastRequestCreatedAt = update.LastRequestCreatedAt;
        entity.NextEligibleAt = update.NextEligibleAt;
        entity.IsDeleted = update.IsDeleted;
    }

    private IQueryable<RecurringReportAssignment> BaseQuery()
        => _dbContext.RecurringReportAssignments
            .Include(assignment => assignment.Template)
                .ThenInclude(template => template.Fields)
            .Include(assignment => assignment.CurrentReportRequest)
                .ThenInclude(request => request!.Template)
                    .ThenInclude(template => template.Fields)
            .Include(assignment => assignment.CurrentReportRequest)
                .ThenInclude(request => request!.Submission);
}
