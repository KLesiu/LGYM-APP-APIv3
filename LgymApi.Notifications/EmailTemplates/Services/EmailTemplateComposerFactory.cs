using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Domain.Notifications;

namespace LgymApi.Infrastructure.Services;

internal sealed class EmailTemplateComposerFactory : IEmailTemplateComposerFactory
{
    private readonly IReadOnlyDictionary<EmailNotificationType, IEmailTemplateComposer> _composers;

    public EmailTemplateComposerFactory(IEnumerable<IEmailTemplateComposer> composers)
    {
        _composers = composers.ToDictionary(x => x.NotificationType);
    }

    public EmailMessage ComposeMessage(EmailNotificationType notificationType, string payloadJson)
    {
        if (!_composers.TryGetValue(notificationType, out var composer))
        {
            throw new InvalidOperationException($"Email template composer not registered for notification type: {notificationType}");
        }

        return composer.Compose(payloadJson);
    }
}
