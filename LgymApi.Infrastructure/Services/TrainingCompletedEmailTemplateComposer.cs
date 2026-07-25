using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.Json;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Application.Options;
using LgymApi.Domain.Enums;
using LgymApi.Infrastructure.Options;
using LgymApi.Resources;
using EmailNotificationType = LgymApi.Domain.Notifications.EmailNotificationType;
using EmailNotificationTypes = LgymApi.Domain.Notifications.EmailNotificationTypes;

namespace LgymApi.Infrastructure.Services;

public sealed class TrainingCompletedEmailTemplateComposer : EmailTemplateComposerBase, IEmailTemplateComposer
{
    private readonly AppDefaultsOptions _appDefaultsOptions;

    public TrainingCompletedEmailTemplateComposer(EmailOptions emailOptions)
        : base(emailOptions)
    {
        _appDefaultsOptions = new AppDefaultsOptions();
    }

    public TrainingCompletedEmailTemplateComposer(EmailOptions emailOptions, AppDefaultsOptions appDefaultsOptions)
        : base(emailOptions)
    {
        _appDefaultsOptions = appDefaultsOptions;
    }

    public EmailNotificationType NotificationType => EmailNotificationTypes.TrainingCompleted;

    public EmailMessage Compose(string payloadJson)
    {
        var payload = DeserializePayload(payloadJson);
        return ComposeTrainingCompleted(payload);
    }

    public EmailMessage ComposeTrainingCompleted(TrainingCompletedEmailPayload payload)
    {
        var culture = payload.Culture;
        var template = LoadTemplate("TrainingCompleted", culture);
        var timeZone = ResolveTimeZone(payload.PreferredTimeZone, _appDefaultsOptions.PreferredTimeZone);
        var trainingDate = TimeZoneInfo.ConvertTime(payload.TrainingDate, timeZone).ToString("yyyy-MM-dd HH:mm", culture);
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = culture;
            var table = BuildTrainingTableHtml(payload, culture);
            var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["{{PlanDayName}}"] = SanitizeTemplateValue(payload.PlanDayName),
                ["{{TrainingDate}}"] = trainingDate,
                ["{{TrainingTable}}"] = table
            };

            var subject = Render(template.Subject, replacements);
            var body = Render(template.Body, replacements);

            return new EmailMessage
            {
                To = payload.RecipientEmail,
                Subject = subject,
                Body = body,
                IsHtml = true
            };
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    private static TrainingCompletedEmailPayload DeserializePayload(string payloadJson)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<TrainingCompletedEmailPayload>(payloadJson, SharedSerializationOptions.Current);
            if (payload == null)
            {
                throw new InvalidOperationException("Training completed email payload is empty.");
            }

            return payload;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to deserialize training completed email payload.", ex);
        }
    }

    private static string BuildTrainingTableHtml(TrainingCompletedEmailPayload payload, CultureInfo culture)
    {
        if (payload.Exercises.Count == 0)
        {
            return $"<p style=\"margin:0; font-size:14px; color:#4b5563;\">{Emails.TrainingNoExercises}</p>";
        }

        var seriesLabel = Emails.TrainingSeriesLabel ?? string.Empty;
        var builder = new StringBuilder();
        builder.AppendLine("<table style=\"width:100%; border-collapse:collapse; margin:12px 0; font-family:Arial, sans-serif;\">");
        builder.AppendLine("  <thead>");
        builder.AppendLine("    <tr>");
        builder.AppendLine($"      <th style=\"text-align:left; padding:8px 10px; border-bottom:2px solid #e5e7eb; font-size:12px; text-transform:uppercase; letter-spacing:0.04em; color:#6b7280;\">{Emails.TrainingTableHeaderSeries}</th>");
        builder.AppendLine($"      <th style=\"text-align:left; padding:8px 10px; border-bottom:2px solid #e5e7eb; font-size:12px; text-transform:uppercase; letter-spacing:0.04em; color:#6b7280;\">{Emails.TrainingTableHeaderReps}</th>");
        builder.AppendLine($"      <th style=\"text-align:left; padding:8px 10px; border-bottom:2px solid #e5e7eb; font-size:12px; text-transform:uppercase; letter-spacing:0.04em; color:#6b7280;\">{Emails.TrainingTableHeaderWeight}</th>");
        builder.AppendLine($"      <th style=\"text-align:left; padding:8px 10px; border-bottom:2px solid #e5e7eb; font-size:12px; text-transform:uppercase; letter-spacing:0.04em; color:#6b7280;\">{Emails.TrainingTableHeaderUnit}</th>");
        builder.AppendLine("    </tr>");
        builder.AppendLine("  </thead>");
        builder.AppendLine("  <tbody>");
        var groups = payload.Exercises
            .GroupBy(exercise => exercise.ExerciseName, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var exerciseName = SanitizeTemplateValue(group.Key);
            builder.AppendLine("    <tr>");
            builder.AppendLine($"      <td colspan=\"4\" style=\"padding:14px 10px 6px; font-weight:600; color:#111827; border-bottom:1px solid #e5e7eb;\">{exerciseName}</td>");
            builder.AppendLine("    </tr>");

            foreach (var entry in group.OrderBy(x => x.Series))
            {
                var weightValue = entry.Weight.ToString("0.##", culture);
                var unitValue = SanitizeTemplateValue(GetLocalizedUnitDisplayName(entry.Unit, culture));
                builder.AppendLine("    <tr>");
                builder.AppendLine($"      <td style=\"padding:8px 10px; border-bottom:1px solid #f3f4f6; color:#111827;\">{seriesLabel} #{entry.Series.ToString(culture)}</td>");
                builder.AppendLine($"      <td style=\"padding:8px 10px; border-bottom:1px solid #f3f4f6; color:#111827;\">{entry.Reps.ToString(culture)}</td>");
                builder.AppendLine($"      <td style=\"padding:8px 10px; border-bottom:1px solid #f3f4f6; color:#111827;\">{weightValue}</td>");
                builder.AppendLine($"      <td style=\"padding:8px 10px; border-bottom:1px solid #f3f4f6; color:#111827;\">{unitValue}</td>");
                builder.AppendLine("    </tr>");
            }
        }

        builder.AppendLine("  </tbody>");
        builder.AppendLine("</table>");

        return builder.ToString();
    }

    private static string GetLocalizedUnitDisplayName(WeightUnits unit, CultureInfo culture)
    {
        var unitType = unit.GetType();
        var enumName = unit.ToString();
        var translationKey = $"{unitType.Name}_{enumName}";

        var resourceManager = new ResourceManager("LgymApi.Resources.Resources.Enums", typeof(LgymApi.Resources.Enums).Assembly);
        var displayName = resourceManager.GetString(translationKey, culture) ?? enumName;

        return displayName;
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
