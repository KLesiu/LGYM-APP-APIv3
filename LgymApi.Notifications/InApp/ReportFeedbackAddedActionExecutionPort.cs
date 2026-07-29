using System.Globalization;
using System.Text.Json;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Options;
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
    AppDefaultsOptions defaults) : IReportFeedbackAddedActionExecutionPort
{
    public async Task ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        var traineeId = root.GetProperty("traineeId").GetString() ?? string.Empty;
        var trainerId = root.GetProperty("trainerId").GetString() ?? string.Empty;
        var submissionId = root.GetProperty("submissionId").GetString() ?? string.Empty;
        var templateName = root.GetProperty("templateName").GetString() ?? string.Empty;

        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(defaults.PreferredLanguage);
            var template = string.IsNullOrWhiteSpace(templateName) ? Messages.GenericReportDisplayName : templateName.Trim();
            await notificationWriter.CreateAsync(
                trainerId,
                traineeId,
                $"report-feedback:{submissionId}:{root.GetProperty("triggeredAt").GetDateTimeOffset():O}",
                string.Format(Messages.TrainerReportFeedbackReceived, template),
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
