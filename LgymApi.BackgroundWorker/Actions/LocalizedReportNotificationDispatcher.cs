using System.Globalization;
using LgymApi.Application.Options;
using LgymApi.Application.WorkerRuntime;
using LgymApi.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace LgymApi.BackgroundWorker.Actions;

internal static class LocalizedReportNotificationDispatcher
{
    public static async Task DispatchAsync(
        IInAppNotificationWireWriter notificationWriter,
        AppDefaultsOptions appDefaultsOptions,
        ILogger logger,
        string traineeId,
        string trainerId,
        string? templateName,
        string deliveryKey,
        string redirectUrl,
        string notificationType,
        Func<string> localizedMessageTemplateFactory,
        string logCategory,
        CancellationToken cancellationToken)
    {
        var previousUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = ResolveCulture(null, appDefaultsOptions.PreferredLanguage);
            var resolvedTrainerName = WorkerNotificationLocalization.GenericTrainerDisplayName;
            var resolvedTemplateName = string.IsNullOrWhiteSpace(templateName)
                ? WorkerNotificationLocalization.GenericReportDisplayName
                : templateName.Trim();

            await notificationWriter.CreateAsync(
                traineeId.ToString(),
                trainerId.ToString(),
                deliveryKey,
                string.Format(localizedMessageTemplateFactory(), resolvedTrainerName, resolvedTemplateName),
                redirectUrl,
                notificationType,
                cancellationToken);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static CultureInfo ResolveCulture(string? preferredLanguage, string fallbackLanguage)
    {
        var cultureName = string.IsNullOrWhiteSpace(preferredLanguage)
            ? fallbackLanguage
            : preferredLanguage;

        try
        {
            return CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo(fallbackLanguage);
        }
    }
}
