using System.Globalization;
using System.Text.Json;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Infrastructure.Options;
using EmailNotificationType = LgymApi.Domain.Notifications.EmailNotificationType;
using EmailNotificationTypes = LgymApi.Domain.Notifications.EmailNotificationTypes;

namespace LgymApi.Infrastructure.Services;

public sealed class PasswordRecoveryEmailTemplateComposer : EmailTemplateComposerBase, IEmailTemplateComposer
{
    private readonly EmailOptions _emailOptions;

    public PasswordRecoveryEmailTemplateComposer(EmailOptions emailOptions)
        : base(emailOptions)
    {
        _emailOptions = emailOptions;
    }

    public EmailNotificationType NotificationType => EmailNotificationTypes.PasswordRecovery;

    public EmailMessage Compose(string payloadJson)
    {
        var payload = DeserializePayload(payloadJson);
        return ComposePasswordRecovery(payload);
    }

    public EmailMessage ComposePasswordRecovery(PasswordRecoveryEmailPayload payload)
    {
        var culture = payload.Culture;
        var template = LoadTemplate("PasswordRecovery", culture);

        // Generate reset URL if base URL is configured
        var resetUrl = GenerateResetUrl(payload.ResetToken);

        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{{UserName}}"] = SanitizeTemplateValue(payload.UserName),
            ["{{ResetToken}}"] = SanitizeTemplateValue(payload.ResetToken),
            ["{{ResetUrl}}"] = SanitizeTemplateValue(resetUrl),
            ["{{ExpiryMinutes}}"] = "30"
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

    private string GenerateResetUrl(string plainTextToken)
    {
        if (string.IsNullOrEmpty(_emailOptions.PasswordRecoveryBaseUrl))
        {
            // If base URL not configured, return plain token (fallback for backward compatibility)
            return plainTextToken;
        }

        var baseUrl = _emailOptions.PasswordRecoveryBaseUrl.TrimEnd('/');
        return $"{baseUrl}?token={Uri.EscapeDataString(plainTextToken)}";
    }

    private static PasswordRecoveryEmailPayload DeserializePayload(string payloadJson)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<PasswordRecoveryEmailPayload>(payloadJson, SharedSerializationOptions.Current);
            if (payload == null)
            {
                throw new InvalidOperationException("Password recovery email payload is empty.");
            }

            return payload;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to deserialize password recovery email payload.", ex);
        }
    }
}
