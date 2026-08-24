using System.Globalization;
using System.Text.Json;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Application.Options;

namespace LgymApi.Application.Notifications.InApp;

internal static class ReportNotificationActionHelpers
{
    public static async Task ExecuteWithParticipantsAsync(
        JsonElement root,
        string recipientProperty,
        string actorProperty,
        string notificationName,
        Func<string> actorFallback,
        IAccountLookupService accountLookupService,
        AppDefaultsOptions defaults,
        Func<Id<AccountReference>, Id<AccountReference>, string, Task> action,
        CancellationToken cancellationToken)
    {
        var recipientId = ParseAccountId(root, recipientProperty, notificationName);
        var actorId = ParseAccountId(root, actorProperty, notificationName);
        var recipient = await accountLookupService.GetByIdAsync(recipientId, cancellationToken);
        var actor = await accountLookupService.GetByIdAsync(actorId, cancellationToken);
        var previousCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = ResolveCulture(
                recipient?.PreferredLanguage,
                defaults.PreferredLanguage);
            var actorName = string.IsNullOrWhiteSpace(actor?.Name)
                ? actorFallback()
                : actor.Name;
            await action(recipientId, actorId, actorName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    private static Id<AccountReference> ParseAccountId(
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

    private static CultureInfo ResolveCulture(
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
