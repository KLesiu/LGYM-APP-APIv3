using System.Text.Json;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Options;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Resources;

namespace LgymApi.Application.Notifications.Contracts.InApp
{
    public interface IReportSubmissionCreatedActionExecutionPort
    {
        Task ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default);
    }
}

namespace LgymApi.Application.Notifications.InApp
{
    internal sealed class ReportSubmissionCreatedActionExecutionPort(
        IInAppNotificationWireWriter notificationWriter,
        IAccountLookupService accountLookupService,
        AppDefaultsOptions defaults) : IReportSubmissionCreatedActionExecutionPort
    {
        public async Task ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var submissionId = root.GetProperty("submissionId").GetString() ?? string.Empty;
            var templateName = root.GetProperty("templateName").GetString() ?? string.Empty;

            await ReportNotificationActionHelpers.ExecuteWithParticipantsAsync(
                root,
                "trainerId",
                "traineeId",
                "Report submission",
                () => Messages.GenericTraineeDisplayName,
                accountLookupService,
                defaults,
                async (trainerId, traineeId, traineeName) =>
                {
                var template = string.IsNullOrWhiteSpace(templateName) ? Messages.GenericReportDisplayName : templateName.Trim();
                await notificationWriter.CreateAsync(
                    trainerId.ToString(),
                    traineeId.ToString(),
                    $"report-submission:{submissionId}",
                    string.Format(Messages.TrainerReportSubmissionReceived, traineeName, template),
                    $"/trainer/members/{traineeId}?tab=reports&submissionId={submissionId}",
                    "ReportSubmissionReceived",
                    cancellationToken);
                },
                cancellationToken);
        }

    }
}
