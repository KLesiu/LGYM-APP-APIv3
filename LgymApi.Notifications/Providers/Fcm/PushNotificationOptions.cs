namespace LgymApi.Application.Notifications.Providers.Fcm;

internal sealed class PushNotificationOptions
{
    public bool Enabled { get; set; }
    public bool? SendEnabled { get; set; }
    public int[] RetryDelaysSeconds { get; set; } = [30, 120, 600];
    public bool StaleTokenCleanupEnabled { get; set; } = true;
    public int StaleTokenInactivityDays { get; set; } = 45;
    public int StaleTokenCleanupBatchSize { get; set; } = 500;
    public FcmOptions Fcm { get; set; } = new();

    public bool IsSendEnabled => SendEnabled ?? Enabled;

    internal sealed class FcmOptions
    {
        public string ProjectId { get; set; } = string.Empty;
        public string? CredentialsPath { get; set; }
        public string? CredentialsJson { get; set; }
        public string BaseUrl { get; set; } = string.Empty;
    }
}
