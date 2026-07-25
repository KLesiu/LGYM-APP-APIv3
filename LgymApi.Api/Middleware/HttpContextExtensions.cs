using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.AspNetCore.Localization;
using System.Globalization;

namespace LgymApi.Api.Middleware;

public static class HttpContextExtensions
{
    public static User? GetCurrentUser(this HttpContext context)
    {
        if (context.Items.TryGetValue("User", out var user) && user is User typedUser)
        {
            return typedUser;
        }

        return null;
    }

    public static Id<User> GetCurrentUserId(this HttpContext context)
        => context.GetCurrentUser()?.Id ?? Id<User>.Empty;

    public static IReadOnlyList<string> GetCulturePreferences(this HttpContext context)
    {
        var cultures = new List<string>();

        var acceptLanguage = context.Request.Headers.AcceptLanguage.ToString();
        var rawCulture = acceptLanguage
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .FirstOrDefault()?.Trim();

        if (!string.IsNullOrWhiteSpace(rawCulture))
        {
            AddCultureAndNeutral(cultures, rawCulture);
        }

        var requestCulture = context.Features.Get<IRequestCultureFeature>()?.RequestCulture?.UICulture;
        if (requestCulture != null && !string.IsNullOrWhiteSpace(requestCulture.Name))
        {
            AddCultureAndNeutral(cultures, requestCulture.Name);
        }

        var culture = CultureInfo.CurrentUICulture;
        if (!string.IsNullOrWhiteSpace(culture.Name))
        {
            AddCultureAndNeutral(cultures, culture.Name);
        }

        cultures.Add("en");

        return cultures
            .Select(c => c.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void AddCultureAndNeutral(List<string> cultures, string cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return;
        }

        if (!TryGetCulture(cultureName, out var cultureInfo))
        {
            return;
        }

        cultures.Add(cultureInfo.Name);

        if (!string.IsNullOrWhiteSpace(cultureInfo.TwoLetterISOLanguageName) && !string.Equals(cultureInfo.Name, cultureInfo.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
        {
            cultures.Add(cultureInfo.TwoLetterISOLanguageName);
        }
    }

    private static bool TryGetCulture(string cultureName, out CultureInfo cultureInfo)
    {
        cultureInfo = null!;
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return false;
        }

        try
        {
            cultureInfo = CultureInfo.GetCultureInfo(cultureName.Trim());
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
