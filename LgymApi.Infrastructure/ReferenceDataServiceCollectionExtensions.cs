using LgymApi.Application.Repositories;
using LgymApi.Infrastructure.Repositories.ReferenceData;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    private static void AddReferenceDataInfrastructure(IServiceCollection services)
    {
        services.AddScoped<IAppConfigRepository, AppConfigRepository>();
    }
}
