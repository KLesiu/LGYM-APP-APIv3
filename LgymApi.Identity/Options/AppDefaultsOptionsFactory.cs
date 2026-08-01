using LgymApi.Application.Options;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace LgymApi.Infrastructure.Configuration;

internal static class AppDefaultsOptionsFactory
{
    internal static AppDefaultsOptions Resolve(IConfiguration configuration)
    {
        var preferredLanguage = ResolvePreferredLanguage(configuration["AppDefaults:PreferredLanguage"]);
        var preferredTimeZone = ResolvePreferredTimeZone(configuration["AppDefaults:PreferredTimeZone"]);

        return new AppDefaultsOptions
        {
            PreferredLanguage = preferredLanguage,
            PreferredTimeZone = preferredTimeZone
        };
    }

    private static string ResolvePreferredLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "en-US";
        }

        try
        {
            return CultureInfo.GetCultureInfo(value).Name;
        }
        catch (CultureNotFoundException)
        {
            return "en-US";
        }
    }

    private static string ResolvePreferredTimeZone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Europe/Warsaw";
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(value).Id;
        }
        catch (TimeZoneNotFoundException)
        {
            return "Europe/Warsaw";
        }
        catch (InvalidTimeZoneException)
        {
            return "Europe/Warsaw";
        }
    }
}
