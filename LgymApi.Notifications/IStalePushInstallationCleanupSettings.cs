namespace LgymApi.Application.Notifications;

public interface IStalePushInstallationCleanupSettings
{
    bool Enabled { get; }
    int InactivityDays { get; }
    int BatchSize { get; }
}
