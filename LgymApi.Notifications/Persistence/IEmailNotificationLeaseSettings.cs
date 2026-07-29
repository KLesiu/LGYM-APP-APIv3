namespace LgymApi.Notifications.Persistence;

internal interface IEmailNotificationLeaseSettings
{
    int EmailSendLeaseSeconds { get; }
}

internal sealed class EmailNotificationLeaseSettings : IEmailNotificationLeaseSettings
{
    internal EmailNotificationLeaseSettings(int emailSendLeaseSeconds)
    {
        EmailSendLeaseSeconds = emailSendLeaseSeconds;
    }

    public int EmailSendLeaseSeconds { get; }
}
