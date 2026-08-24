using System.Globalization;
using System.Text.Json;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Options;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Resources;

namespace LgymApi.Application.Notifications.Contracts.InApp
{

public interface IReportFeedbackAddedActionExecutionPort
{
    Task ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default);
}

}

namespace LgymApi.Application.Notifications.InApp
{

internal sealed class ReportFeedbackAddedActionExecutionPort(
    IInAppNotificationWireWriter notificationWriter,
    IAccountLookupService accountLookupService,
    AppDefaultsOptions defaults) : IReportFeedbackAddedActionExecutionPort
{
    public async Task ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        var traineeId = ReportNotificationActionHelpers.ParseAccountId(
            root,
            "traineeId",
            "Report feedback");
        var trainerId = ReportNotificationActionHelpers.ParseAccountId(
            root,
            "trainerId",
            "Report feedback");
        var submissionId = root.GetProperty("submissionId").GetString() ?? string.Empty;
        var templateName = root.GetProperty("templateName").GetString() ?? string.Empty;

        var trainee = await accountLookupService.GetByIdAsync(traineeId, cancellationToken);
        var trainer = await accountLookupService.GetByIdAsync(trainerId, cancellationToken);
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = ReportNotificationActionHelpers.ResolveCulture(
                trainee?.PreferredLanguage,
                defaults.PreferredLanguage);
            var trainerName = string.IsNullOrWhiteSpace(trainer?.Name)
                ? Messages.GenericTrainerDisplayName
                : trainer.Name;
            var template = string.IsNullOrWhiteSpace(templateName) ? Messages.GenericReportDisplayName : templateName.Trim();
            await notificationWriter.CreateAsync(
                traineeId.ToString(),
                trainerId.ToString(),
                $"report-feedback:{submissionId}:{root.GetProperty("triggeredAt").GetDateTimeOffset():O}",
                string.Format(Messages.TrainerReportFeedbackReceived, trainerName, template),
                $"/trainer/report-submissions/{submissionId}",
                "ReportFeedbackReceived",
                cancellationToken);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

}

}
