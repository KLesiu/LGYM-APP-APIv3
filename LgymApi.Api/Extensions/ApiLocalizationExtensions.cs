using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Api.Extensions;

public static class ApiLocalizationExtensions
{
    public static RequestLocalizationOptions AddApiLocalization(this IServiceCollection services)
    {
        services.AddLocalization();

        var supportedCultures = new[]
        {
            new CultureInfo("en"),
            new CultureInfo("pl")
        };

        var localizationOptions = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture("en"),
            SupportedCultures = supportedCultures,
            SupportedUICultures = supportedCultures
        };

        localizationOptions.RequestCultureProviders = new List<IRequestCultureProvider>
        {
            new AcceptLanguageHeaderRequestCultureProvider()
        };

        return localizationOptions;
    }
}
