using System.Globalization;
using System.Text.Json;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Options;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
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
            var traineeId = ParseAccountId(root, "traineeId");
            var trainerId = ParseAccountId(root, "trainerId");
            var submissionId = root.GetProperty("submissionId").GetString() ?? string.Empty;
            var templateName = root.GetProperty("templateName").GetString() ?? string.Empty;
            var trainee = await accountLookupService.GetByIdAsync(traineeId, cancellationToken);
            var trainer = await accountLookupService.GetByIdAsync(trainerId, cancellationToken);
            var previousCulture = CultureInfo.CurrentUICulture;

            try
            {
                CultureInfo.CurrentUICulture = ResolveCulture(trainer?.PreferredLanguage);
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

        private static Id<AccountReference> ParseAccountId(JsonElement root, string propertyName)
        {
            var value = root.GetProperty(propertyName).GetString() ?? string.Empty;
            if (!Id<AccountReference>.TryParse(value, out var id))
            {
                throw new InvalidOperationException($"Report submission notification payload has an invalid {propertyName}.");
            }

            return id;
        }

        private CultureInfo ResolveCulture(string? preferredLanguage)
        {
            var cultureName = string.IsNullOrWhiteSpace(preferredLanguage) ? defaults.PreferredLanguage : preferredLanguage;
            try
            {
                return CultureInfo.GetCultureInfo(cultureName);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.GetCultureInfo(defaults.PreferredLanguage);
            }
        }
    }
}
