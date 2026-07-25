using LgymApi.Application.Platform.ReferenceData;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Application.Platform;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services)
    {
        services.AddReferenceDataServices();

        return services;
    }
}
