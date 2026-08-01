using System.Globalization;
using System.Text.Json;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Application.Options;
using LgymApi.Infrastructure.Options;
using EmailNotificationType = LgymApi.Domain.Notifications.EmailNotificationType;
using EmailNotificationTypes = LgymApi.Domain.Notifications.EmailNotificationTypes;

namespace LgymApi.Infrastructure.Services;

internal sealed class TrainerInvitationEmailTemplateComposer : EmailTemplateComposerBase, IEmailTemplateComposer
{
    private readonly AppDefaultsOptions _appDefaultsOptions;

    public TrainerInvitationEmailTemplateComposer(EmailOptions emailOptions)
        : base(emailOptions)
    {
        _appDefaultsOptions = new AppDefaultsOptions();
    }

    public TrainerInvitationEmailTemplateComposer(EmailOptions emailOptions, AppDefaultsOptions appDefaultsOptions)
        : base(emailOptions)
    {
        _appDefaultsOptions = appDefaultsOptions;
    }

    public EmailNotificationType NotificationType => EmailNotificationTypes.TrainerInvitation;

    public EmailMessage Compose(string payloadJson)
    {
        var payload = DeserializePayload(payloadJson);
        return ComposeTrainerInvitation(payload);
    }

    public EmailMessage ComposeTrainerInvitation(InvitationEmailPayload payload)
    {
        var culture = payload.Culture;
        var template = LoadTemplate("TrainerInvitation", culture);
        var baseUrl = EmailOptions.InvitationBaseUrl.TrimEnd('/');
        var acceptUrl = $"{baseUrl}/accept/{payload.InvitationId}";
        var rejectUrl = $"{baseUrl}/reject/{payload.InvitationId}";
        var timeZone = ResolveTimeZone(payload.PreferredTimeZone, _appDefaultsOptions.PreferredTimeZone);
        var expiresAt = TimeZoneInfo.ConvertTime(payload.ExpiresAt, timeZone).ToString("yyyy-MM-dd HH:mm", culture);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{{TrainerName}}"] = SanitizeTemplateValue(payload.TrainerName),
            ["{{InvitationCode}}"] = SanitizeTemplateValue(payload.InvitationCode),
            ["{{AcceptUrl}}"] = SanitizeTemplateValue(acceptUrl),
            ["{{RejectUrl}}"] = SanitizeTemplateValue(rejectUrl),
            ["{{ExpiresAt}}"] = expiresAt
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

    private static InvitationEmailPayload DeserializePayload(string payloadJson)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<InvitationEmailPayload>(payloadJson, SharedSerializationOptions.Current);
            if (payload == null)
            {
                throw new InvalidOperationException("Invitation email payload is empty.");
            }

            return payload;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to deserialize invitation email payload.", ex);
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string? preferredTimeZone, string fallbackTimeZone)
    {
        if (!string.IsNullOrWhiteSpace(preferredTimeZone))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(preferredTimeZone);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.FindSystemTimeZoneById(fallbackTimeZone);
    }

}
