using LgymApi.Application.Repositories;
using LgymApi.Application.Notifications.Repositories;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications;

internal sealed class StalePushInstallationCleanupService : IStalePushInstallationCleanupService
{
    internal const string StaleInstallationDisabledReason = "InactiveStale";

    private readonly IPushInstallationRepository _pushInstallationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStalePushInstallationCleanupSettings _settings;
    private readonly ILogger<StalePushInstallationCleanupService> _logger;

    public StalePushInstallationCleanupService(
        IPushInstallationRepository pushInstallationRepository,
        IUnitOfWork unitOfWork,
        IStalePushInstallationCleanupSettings settings,
        ILogger<StalePushInstallationCleanupService> logger)
    {
        _pushInstallationRepository = pushInstallationRepository;
        _unitOfWork = unitOfWork;
        _settings = settings;
        _logger = logger;
    }

    public async Task<int> CleanupAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Push stale-installation cleanup skipped because the cleanup flag is disabled.");
            return 0;
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_settings.InactivityDays);
        var staleInstallations = await _pushInstallationRepository.GetStaleActiveAsync(
            cutoff,
            _settings.BatchSize,
            cancellationToken);

        foreach (var installation in staleInstallations)
        {
            installation.DisabledAt = DateTimeOffset.UtcNow;
            installation.DisabledReason = StaleInstallationDisabledReason;
            await _pushInstallationRepository.UpdateAsync(installation, cancellationToken);
        }

        if (staleInstallations.Count == 0)
        {
            _logger.LogInformation(
                "Push stale-installation cleanup found no candidates before cutoff {CutoffUtc}.",
                cutoff);
            return 0;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Push stale-installation cleanup disabled {InstallationCount} installations before cutoff {CutoffUtc}.",
            staleInstallations.Count,
            cutoff);

        return staleInstallations.Count;
    }
}
