using LgymApi.Application.WorkoutProgress.ReportingIntegration;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.Reporting;

public sealed class ReportSubmissionAcceptedProgressPersistenceRepository : IReportSubmissionAcceptedProgressPersistence
{
    private readonly AppDbContext _dbContext;

    public ReportSubmissionAcceptedProgressPersistenceRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlySet<BodyParts>> GetExistingBodyPartsAsync(
        LgymApi.Domain.ValueObjects.Id<LgymApi.Identity.Contracts.AccountReference> traineeId,
        IReadOnlyCollection<BodyParts> bodyParts,
        DateTimeOffset createdAtFromUtc,
        DateTimeOffset createdAtToUtc,
        CancellationToken cancellationToken = default)
    {
        if (bodyParts.Count == 0)
        {
            return new HashSet<BodyParts>();
        }

        var persistedTraineeId = ReportingPersistenceAccountIds.ToPersisted(traineeId);
        var existing = await _dbContext.Measurements
            .AsNoTracking()
            .Where(measurement => measurement.UserId == persistedTraineeId
                && bodyParts.Contains(measurement.BodyPart)
                && measurement.CreatedAt >= createdAtFromUtc
                && measurement.CreatedAt < createdAtToUtc)
            .Select(measurement => measurement.BodyPart)
            .Distinct()
            .ToListAsync(cancellationToken);
        return existing.ToHashSet();
    }

    public Task AddAsync(
        AcceptedReportMeasurementPersistenceModel measurement,
        CancellationToken cancellationToken = default)
        => _dbContext.Measurements.AddAsync(new Measurement
        {
            Id = Id<Measurement>.New(),
            UserId = ReportingPersistenceAccountIds.ToPersisted(measurement.TraineeId),
            BodyPart = measurement.BodyPart,
            Unit = measurement.Unit.ToString(),
            Value = measurement.Value,
            CreatedAt = measurement.CreatedAt
        }, cancellationToken).AsTask();
}
