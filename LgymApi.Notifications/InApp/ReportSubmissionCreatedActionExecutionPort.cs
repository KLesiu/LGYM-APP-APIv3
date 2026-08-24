using System.Globalization;
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
            var traineeId = ReportNotificationActionHelpers.ParseAccountId(
                root,
                "traineeId",
                "Report submission");
            var trainerId = ReportNotificationActionHelpers.ParseAccountId(
                root,
                "trainerId",
                "Report submission");
            var submissionId = root.GetProperty("submissionId").GetString() ?? string.Empty;
            var templateName = root.GetProperty("templateName").GetString() ?? string.Empty;
            var trainee = await accountLookupService.GetByIdAsync(traineeId, cancellationToken);
            var trainer = await accountLookupService.GetByIdAsync(trainerId, cancellationToken);
            var previousCulture = CultureInfo.CurrentUICulture;

            try
            {
                CultureInfo.CurrentUICulture = ReportNotificationActionHelpers.ResolveCulture(
                    trainer?.PreferredLanguage,
                    defaults.PreferredLanguage);
                var traineeName = string.IsNullOrWhiteSpace(trainee?.Name) ? Messages.GenericTraineeDisplayName : trainee.Name;
                var template = string.IsNullOrWhiteSpace(templateName) ? Messages.GenericReportDisplayName : templateName.Trim();
                await notificationWriter.CreateAsync(
                    trainerId.ToString(),
                    traineeId.ToString(),
                    $"report-submission:{submissionId}",
                    string.Format(Messages.TrainerReportSubmissionReceived, traineeName, template),
                    $"/trainer/members/{traineeId}?tab=reports&submissionId={submissionId}",
                    "ReportSubmissionReceived",
                    cancellationToken);
            }
            finally
            {
                CultureInfo.CurrentUICulture = previousCulture;
            }
        }

    }
}
