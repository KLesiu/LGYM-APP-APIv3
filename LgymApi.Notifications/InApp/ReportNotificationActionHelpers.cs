using System.Globalization;
using System.Text.Json;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Notifications.InApp;

internal static class ReportNotificationActionHelpers
{
    public static Id<AccountReference> ParseAccountId(
        JsonElement root,
        string propertyName,
        string notificationName)
    {
        var value = root.GetProperty(propertyName).GetString() ?? string.Empty;
        if (!Id<AccountReference>.TryParse(value, out var id))
        {
            throw new InvalidOperationException(
                $"{notificationName} notification payload has an invalid {propertyName}.");
        }

        return id;
    }

    public static CultureInfo ResolveCulture(
        string? preferredLanguage,
        string fallbackLanguage)
    {
        var cultureName = string.IsNullOrWhiteSpace(preferredLanguage)
            ? fallbackLanguage
            : preferredLanguage;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            return culture.TwoLetterISOLanguageName is "en" or "pl"
                ? culture
                : CultureInfo.GetCultureInfo(fallbackLanguage);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo(fallbackLanguage);
        }
    }
}
