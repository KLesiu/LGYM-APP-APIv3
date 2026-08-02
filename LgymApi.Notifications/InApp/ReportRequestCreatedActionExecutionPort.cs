using System.Globalization;
using System.Text.Json;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Options;
using LgymApi.Resources;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications.Contracts.InApp
{
    public interface IReportRequestCreatedActionExecutionPort
    {
        Task ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default);
    }
}

namespace LgymApi.Application.Notifications.InApp
{
internal sealed class ReportRequestCreatedActionExecutionPort(
    IInAppNotificationWireWriter notificationWriter,
    AppDefaultsOptions defaults,
    ILogger<ReportRequestCreatedActionExecutionPort> logger) : IReportRequestCreatedActionExecutionPort
{
    public async Task ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        var traineeId = root.GetProperty("traineeId").GetString() ?? string.Empty;
        var trainerId = root.GetProperty("trainerId").GetString() ?? string.Empty;
        var requestId = root.GetProperty("requestId").GetString() ?? string.Empty;
        var templateName = root.GetProperty("templateName").GetString() ?? string.Empty;
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(defaults.PreferredLanguage);
            var template = string.IsNullOrWhiteSpace(templateName) ? Messages.GenericReportDisplayName : templateName.Trim();
            await notificationWriter.CreateAsync(traineeId, trainerId, $"report-request:{requestId}:created",
                string.Format(Messages.TrainerReportRequestReceived, Messages.GenericTrainerDisplayName, template), $"/trainer/report-requests/{requestId}",
                "ReportRequestReceived", cancellationToken);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
}
}
