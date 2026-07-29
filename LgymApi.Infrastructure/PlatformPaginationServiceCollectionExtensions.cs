using LgymApi.Application.Pagination;
using LgymApi.Infrastructure.Configuration;
using LgymApi.Infrastructure.Pagination;
using Microsoft.Extensions.DependencyInjection;
using GridifyExecutionServiceContract = LgymApi.Infrastructure.Pagination.IGridifyExecutionService;
using QueryPaginationFacade = LgymApi.Infrastructure.Pagination.QueryPaginationService;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    private static void AddPlatformMapperRegistry(IServiceCollection services)
    {
        services.AddSingleton<IMapperRegistry>(sp =>
        {
            var registry = new MapperRegistry();
            InfrastructureMappingRegistration.RegisterAll(registry);
            return registry;
        });
        services.AddSingleton(new PaginationPolicy
        {
            MaxPageSize = 100,
            DefaultPageSize = 20,
            DefaultSortField = "id",
            TieBreakerField = "id"
        });
    }
}
