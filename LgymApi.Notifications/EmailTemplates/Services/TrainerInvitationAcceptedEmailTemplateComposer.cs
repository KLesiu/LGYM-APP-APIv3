using System.Text.Json;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Infrastructure.Options;
using EmailNotificationType = LgymApi.Domain.Notifications.EmailNotificationType;
using EmailNotificationTypes = LgymApi.Domain.Notifications.EmailNotificationTypes;

namespace LgymApi.Infrastructure.Services;

internal sealed class TrainerInvitationAcceptedEmailTemplateComposer : EmailTemplateComposerBase, IEmailTemplateComposer
{
    public TrainerInvitationAcceptedEmailTemplateComposer(EmailOptions emailOptions)
        : base(emailOptions)
    {
    }

    public EmailNotificationType NotificationType => EmailNotificationTypes.TrainerInvitationAccepted;

    public EmailMessage Compose(string payloadJson)
    {
        var payload = DeserializePayload(payloadJson);
        var template = LoadTemplate("TrainerInvitationAccepted", payload.Culture);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{{TraineeName}}"] = SanitizeTemplateValue(payload.TraineeName),
            ["{{TrainerName}}"] = SanitizeTemplateValue(payload.TrainerName)
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

    private static InvitationAcceptedEmailPayload DeserializePayload(string payloadJson)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<InvitationAcceptedEmailPayload>(payloadJson, SharedSerializationOptions.Current);
            if (payload == null)
            {
                throw new InvalidOperationException("Invitation accepted email payload is empty.");
            }

            return payload;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to deserialize invitation accepted email payload.", ex);
        }
    }
}
