using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.Reporting;

public sealed class ReportingRelationshipAccessPersistenceRepository : IReportingRelationshipAccessPersistence
{
    private readonly AppDbContext _dbContext;

    public ReportingRelationshipAccessPersistenceRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<ReportingRelationshipAccessFact> GetAccessAsync(
        Id<AccountReference> trainerId,
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken = default)
    {
        var persistedTrainerId = ReportingPersistenceAccountIds.ToPersisted(trainerId);
        var persistedTraineeId = ReportingPersistenceAccountIds.ToPersisted(traineeId);
        var hasActiveRelationship = await _dbContext.TrainerTraineeLinks
            .AsNoTracking()
            .AnyAsync(link => link.TrainerId == persistedTrainerId && link.TraineeId == persistedTraineeId, cancellationToken);
        return new ReportingRelationshipAccessFact(hasActiveRelationship);
    }
}
