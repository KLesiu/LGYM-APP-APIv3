using LgymApi.Application.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    private static IServiceCollection AddRowSecurity(this IServiceCollection services)
    {
        services.AddScoped<IActorRowSecurityScopeFactory, RowSecurity.EfActorRowSecurityScopeFactory>();
        return services;
    }
}
