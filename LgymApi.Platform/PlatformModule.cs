using LgymApi.Application.Platform.ReferenceData;
using LgymApi.Application.Platform.ReferenceData.AppConfig;
using LgymApi.Application.Platform.ReferenceData.Enums;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Application.Pagination;
using LgymApi.Infrastructure.Pagination;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.Repositories.ReferenceData;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Platform;

public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services)
    {
        services.AddReferenceDataServices();
        services.AddPlatformPersistenceRepositories();
        services.AddPlatformPaginationServices();

        return services;
    }

    private static void AddPlatformPersistenceRepositories(this IServiceCollection services)
    {
        services.AddScoped<LgymApi.Application.Repositories.IAppConfigRepository, AppConfigRepository>();
        services.AddScoped<LgymApi.Application.Repositories.ICommandEnvelopeRepository, CommandEnvelopeRepository>();
        services.AddScoped<LgymApi.Application.Repositories.IApiIdempotencyRecordRepository, ApiIdempotencyRecordRepository>();
    }

    private static void AddPlatformPaginationServices(this IServiceCollection services)
    {
        services.AddScoped<GridifyExecutionService>();
        services.AddScoped<IGridifyExecutionService>(static provider => provider.GetRequiredService<GridifyExecutionService>());
        services.AddScoped<IQueryPaginationService, QueryPaginationService>();
    }
}
