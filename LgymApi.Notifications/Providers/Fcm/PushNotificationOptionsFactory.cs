using Microsoft.Extensions.Configuration;

namespace LgymApi.Application.Notifications.Providers.Fcm;

internal static class PushNotificationOptionsFactory
{
    internal static PushNotificationOptions Create(IConfiguration configuration)
    {
        var options = configuration.GetSection("PushNotifications").Get<PushNotificationOptions>() ?? new PushNotificationOptions();
        options.SendEnabled ??= options.Enabled;
        options.RetryDelaysSeconds = options.RetryDelaysSeconds
            .Where(x => x > 0)
            .Distinct()
            .ToArray();

        if (options.RetryDelaysSeconds.Length == 0)
        {
            options.RetryDelaysSeconds = [30, 120, 600];
        }

        options.Fcm.ProjectId = options.Fcm.ProjectId?.Trim() ?? string.Empty;
        options.Fcm.CredentialsPath = NormalizeOptional(options.Fcm.CredentialsPath);
        options.Fcm.CredentialsJson = NormalizeOptional(options.Fcm.CredentialsJson);
        options.Fcm.BaseUrl = NormalizeOptional(options.Fcm.BaseUrl)?.TrimEnd('/') ?? string.Empty;
        options.StaleTokenInactivityDays = options.StaleTokenInactivityDays <= 0 ? 45 : options.StaleTokenInactivityDays;
        options.StaleTokenCleanupBatchSize = options.StaleTokenCleanupBatchSize <= 0 ? 500 : options.StaleTokenCleanupBatchSize;
        options.MessageHistoryDays = options.MessageHistoryDays <= 0 ? 30 : options.MessageHistoryDays;
        options.DisabledInstallationDays = options.DisabledInstallationDays <= 0 ? 30 : options.DisabledInstallationDays;
        options.InAppNotificationDays = options.InAppNotificationDays <= 0 ? 90 : options.InAppNotificationDays;
        options.RetentionPurgeBatchSize = options.RetentionPurgeBatchSize <= 0 ? 500 : options.RetentionPurgeBatchSize;
        return options;
    }

    internal static void Validate(PushNotificationOptions options)
    {
        if (!options.IsSendEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Fcm.ProjectId))
        {
            throw new InvalidOperationException("PushNotifications:Fcm:ProjectId is required when push notifications are enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.Fcm.CredentialsPath) && string.IsNullOrWhiteSpace(options.Fcm.CredentialsJson))
        {
            throw new InvalidOperationException("PushNotifications:Fcm:CredentialsPath or PushNotifications:Fcm:CredentialsJson is required when push notifications are enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.Fcm.BaseUrl))
        {
            throw new InvalidOperationException("PushNotifications:Fcm:BaseUrl is required when push notifications are enabled.");
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
