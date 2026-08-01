using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.Infrastructure.Options;

namespace LgymApi.Infrastructure.Services;

internal sealed class EmailNotificationsFeature : IEmailNotificationsFeature
{
    private readonly EmailOptions _emailOptions;

    public EmailNotificationsFeature(EmailOptions emailOptions)
    {
        _emailOptions = emailOptions;
    }

    public bool Enabled => _emailOptions.Enabled;
}
