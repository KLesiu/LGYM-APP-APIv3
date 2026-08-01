using System.Globalization;
using System.Text.Json;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Infrastructure.Options;
using EmailNotificationType = LgymApi.Domain.Notifications.EmailNotificationType;
using EmailNotificationTypes = LgymApi.Domain.Notifications.EmailNotificationTypes;

namespace LgymApi.Infrastructure.Services;

internal sealed class WelcomeEmailTemplateComposer : EmailTemplateComposerBase, IEmailTemplateComposer
{
    public WelcomeEmailTemplateComposer(EmailOptions emailOptions)
        : base(emailOptions)
    {
    }

    public EmailNotificationType NotificationType => EmailNotificationTypes.Welcome;

    public EmailMessage Compose(string payloadJson)
    {
        var payload = DeserializePayload(payloadJson);
        return ComposeWelcome(payload);
    }

    public EmailMessage ComposeWelcome(WelcomeEmailPayload payload)
    {
        var culture = payload.Culture;
        var template = LoadTemplate("Welcome", culture);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{{UserName}}"] = SanitizeTemplateValue(payload.UserName)
        };

        var subject = Render(template.Subject, replacements);
        var body = Render(template.Body, replacements);

        return new EmailMessage
        {
            To = payload.RecipientEmail,
            Subject = subject,
            Body = body
        };
    }

    private static WelcomeEmailPayload DeserializePayload(string payloadJson)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<WelcomeEmailPayload>(payloadJson, SharedSerializationOptions.Current);
            if (payload == null)
            {
                throw new InvalidOperationException("Welcome email payload is empty.");
            }

            return payload;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to deserialize welcome email payload.", ex);
        }
    }


}
