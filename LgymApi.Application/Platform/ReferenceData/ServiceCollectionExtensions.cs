using LgymApi.Application.Platform.ReferenceData.AppConfig;
using LgymApi.Application.Platform.ReferenceData.Enums;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Application.Platform.ReferenceData;

internal static class ServiceCollectionExtensions
{
    internal static IServiceCollection AddReferenceDataServices(this IServiceCollection services)
    {
        services.AddScoped<IAppConfigService, AppConfigService>();
        services.AddScoped<IEnumService, EnumService>();
        services.AddSingleton<ILinearUnitStrategy<WeightUnits>, WeightLinearUnitStrategy>();
        services.AddSingleton<IUnitConverter<WeightUnits>, LinearUnitConverter<WeightUnits>>();
        services.AddSingleton<ILinearUnitStrategy<HeightUnits>, HeightLinearUnitStrategy>();
        services.AddSingleton<IUnitConverter<HeightUnits>, LinearUnitConverter<HeightUnits>>();

        return services;
    }
}
