using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories;

public sealed class ReportingRepository : IReportingRepository
{
    private readonly AppDbContext _dbContext;

    public ReportingRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddTemplateAsync(ReportTemplate template, CancellationToken cancellationToken = default)
    {
        await _dbContext.ReportTemplates.AddAsync(template, cancellationToken);
    }

    public Task<ReportTemplate?> FindTemplateByIdAsync(Id<ReportTemplate> templateId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ReportTemplates
            .Include(x => x.Fields.OrderBy(f => f.Order).ThenBy(f => f.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == templateId, cancellationToken);
    }

    public Task<List<ReportTemplate>> GetTemplatesByTrainerIdAsync(Id<User> trainerId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ReportTemplates
            .AsNoTracking()
            .Where(x => x.TrainerId == trainerId && !x.IsDeleted)
            .Include(x => x.Fields.OrderBy(f => f.Order).ThenBy(f => f.CreatedAt))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddRequestAsync(ReportRequest request, CancellationToken cancellationToken = default)
    {
        await _dbContext.ReportRequests.AddAsync(request, cancellationToken);
    }

    public Task<ReportRequest?> FindRequestByIdAsync(Id<ReportRequest> requestId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ReportRequests
            .Include(x => x.Template)
            .ThenInclude(x => x.Fields.OrderBy(f => f.Order).ThenBy(f => f.CreatedAt))
            .Include(x => x.Submission)
            .FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);
    }

    public Task<List<ReportRequest>> GetPendingOrExpiredRequestsByTraineeIdAsync(Id<User> traineeId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ReportRequests
            .Where(x => x.TraineeId == traineeId && (x.Status == ReportRequestStatus.Pending || x.Status == ReportRequestStatus.Expired))
            .Include(x => x.Template)
            .ThenInclude(x => x.Fields.OrderBy(f => f.Order).ThenBy(f => f.CreatedAt))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<ReportRequest?> FindRequestByIdWithSubmissionAsync(Id<ReportRequest> requestId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ReportRequests
            .Include(x => x.Template)
            .ThenInclude(x => x.Fields.OrderBy(f => f.Order).ThenBy(f => f.CreatedAt))
            .Include(x => x.Submission)
            .FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);
    }

    public async Task AddSubmissionAsync(ReportSubmission submission, CancellationToken cancellationToken = default)
    {
        await _dbContext.ReportSubmissions.AddAsync(submission, cancellationToken);
    }

    public Task<ReportSubmission?> FindSubmissionByIdForTrainerAsync(Id<ReportSubmission> submissionId, Id<User> trainerId, Id<User> traineeId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ReportSubmissions
            .Include(x => x.ReportRequest)
            .ThenInclude(x => x.Template)
            .ThenInclude(x => x.Fields.OrderBy(f => f.Order).ThenBy(f => f.CreatedAt))
            .FirstOrDefaultAsync(
                x => x.Id == submissionId
                     && x.TraineeId == traineeId
                     && x.ReportRequest.TrainerId == trainerId,
                cancellationToken);
    }

    public Task<ReportSubmission?> FindSubmissionByIdForTraineeAsync(Id<ReportSubmission> submissionId, Id<User> traineeId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ReportSubmissions
            .Include(x => x.ReportRequest)
            .ThenInclude(x => x.Template)
            .ThenInclude(x => x.Fields.OrderBy(f => f.Order).ThenBy(f => f.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == submissionId && x.TraineeId == traineeId, cancellationToken);
    }

    public Task<List<ReportSubmission>> GetSubmissionsByTraineeAsync(Id<User> traineeId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ReportSubmissions
            .AsNoTracking()
            .Where(x => x.TraineeId == traineeId)
            .Include(x => x.ReportRequest)
            .ThenInclude(x => x.Template)
            .ThenInclude(x => x.Fields.OrderBy(f => f.Order).ThenBy(f => f.CreatedAt))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<List<ReportSubmission>> GetSubmissionsByTrainerAndTraineeAsync(Id<User> trainerId, Id<User> traineeId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ReportSubmissions
            .AsNoTracking()
            .Where(x => x.ReportRequest.TrainerId == trainerId && x.TraineeId == traineeId)
            .Include(x => x.ReportRequest)
            .ThenInclude(x => x.Template)
            .ThenInclude(x => x.Fields.OrderBy(f => f.Order).ThenBy(f => f.CreatedAt))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<Photo?> FindPhotoByIdAsync(Id<Photo> photoId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Photos
            .Include(x => x.ReportRequest)
            .FirstOrDefaultAsync(x => x.Id == photoId && !x.IsDeleted, cancellationToken);
    }

    public Task<Photo?> FindActivePhotoByRequestAndViewAsync(
        Id<ReportRequest> requestId,
        string viewType,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Photos
            .FirstOrDefaultAsync(x => x.ReportRequestId == requestId && x.ViewType == viewType && !x.IsDeleted, cancellationToken);
    }

    public Task<List<Photo>> GetPhotosByTraineeIdAsync(Id<User> traineeId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Photos
            .AsNoTracking()
            .Where(x => x.OwnerUserId == traineeId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Photo>> GetPhotosByRequestIdAsync(Id<ReportRequest> requestId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Photos
            .AsNoTracking()
            .Where(x => x.ReportRequestId == requestId && !x.IsDeleted)
            .OrderBy(x => x.ViewType)
            .ToListAsync(cancellationToken);
    }

    public async Task<long> GetActivePhotoStorageBytesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Photos
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .SumAsync(x => (long?)x.SizeBytes, cancellationToken) ?? 0L;
    }

    public Task<int> CountPhotosCreatedSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default)
    {
        return _dbContext.Photos
            .IgnoreQueryFilters()
            .CountAsync(x => x.CreatedAt >= sinceUtc, cancellationToken);
    }

    public async Task SavePhotoAsync(Photo photo, CancellationToken cancellationToken = default)
    {
        var existingPhoto = await _dbContext.Photos
            .Where(p => p.ReportRequestId == photo.ReportRequestId
                     && p.ViewType == photo.ViewType
                     && !p.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingPhoto != null)
        {
            existingPhoto.IsDeleted = true;
            existingPhoto.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.Photos.AddAsync(photo, cancellationToken);
    }
}
