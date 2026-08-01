using LgymApi.Application.Abstractions.Storage;
using LgymApi.Application.Repositories;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Features.Reporting;

public sealed class ExpiredPhotoUploadCleanupService : IExpiredPhotoUploadCleanupService
{
    private readonly IReportPhotoPersistence _photoPersistence;
    private readonly IPhotoStorageProvider _photoStorageProvider;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ExpiredPhotoUploadCleanupService> _logger;

    public ExpiredPhotoUploadCleanupService(
        IReportPhotoPersistence photoPersistence,
        IPhotoStorageProvider photoStorageProvider,
        IUnitOfWork unitOfWork,
        ILogger<ExpiredPhotoUploadCleanupService> logger)
    {
        _photoPersistence = photoPersistence;
        _photoStorageProvider = photoStorageProvider;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<int> CleanupExpiredUploadsAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await _photoPersistence.ListCleanupCandidatesAsync(DateTimeOffset.UtcNow, cancellationToken);
        var cleaned = 0;

        foreach (var candidate in candidates)
        {
            try
            {
                await _photoStorageProvider.DeleteAsync(candidate.StorageKey, cancellationToken);

                await _photoPersistence.MarkUploadExpiredAsync(candidate.StorageKey, cancellationToken);

                cleaned++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean expired photo upload session {StorageKey}", candidate.StorageKey);
            }
        }

        if (cleaned > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return cleaned;
    }
}
