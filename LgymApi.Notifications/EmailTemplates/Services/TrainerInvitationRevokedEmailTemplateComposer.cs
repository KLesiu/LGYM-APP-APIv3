using System.Text.Json;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Infrastructure.Options;
using EmailNotificationType = LgymApi.Domain.Notifications.EmailNotificationType;
using EmailNotificationTypes = LgymApi.Domain.Notifications.EmailNotificationTypes;

namespace LgymApi.Infrastructure.Services;

internal sealed class TrainerInvitationRevokedEmailTemplateComposer : EmailTemplateComposerBase, IEmailTemplateComposer
{
    public TrainerInvitationRevokedEmailTemplateComposer(EmailOptions emailOptions)
        : base(emailOptions)
    {
    }

    public EmailNotificationType NotificationType => EmailNotificationTypes.TrainerInvitationRevoked;

    public EmailMessage Compose(string payloadJson)
    {
        var payload = DeserializePayload(payloadJson);
        var template = LoadTemplate("TrainerInvitationRevoked", payload.Culture);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
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

    private static InvitationRevokedEmailPayload DeserializePayload(string payloadJson)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<InvitationRevokedEmailPayload>(payloadJson, SharedSerializationOptions.Current);
            if (payload == null)
            {
                throw new InvalidOperationException("Invitation revoked email payload is empty.");
            }

            return payload;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to deserialize invitation revoked email payload.", ex);
        }
    }
}
