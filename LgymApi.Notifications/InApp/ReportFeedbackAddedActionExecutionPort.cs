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
        var submissionId = root.GetProperty("submissionId").GetString() ?? string.Empty;
        var templateName = root.GetProperty("templateName").GetString() ?? string.Empty;

        await ReportNotificationActionHelpers.ExecuteWithParticipantsAsync(
            root,
            "traineeId",
            "trainerId",
            "Report feedback",
            () => Messages.GenericTrainerDisplayName,
            accountLookupService,
            defaults,
            async (traineeId, trainerId, trainerName) =>
            {
            var template = string.IsNullOrWhiteSpace(templateName) ? Messages.GenericReportDisplayName : templateName.Trim();
            await notificationWriter.CreateAsync(
                traineeId.ToString(),
                trainerId.ToString(),
                $"report-feedback:{submissionId}:{root.GetProperty("triggeredAt").GetDateTimeOffset():O}",
                string.Format(Messages.TrainerReportFeedbackReceived, trainerName, template),
                $"/trainer/report-submissions/{submissionId}",
                "ReportFeedbackReceived",
                cancellationToken);
            },
            cancellationToken);
    }

}

}
