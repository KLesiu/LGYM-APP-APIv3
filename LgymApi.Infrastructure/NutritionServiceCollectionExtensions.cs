using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Infrastructure.Repositories.Nutrition;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddNutritionInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IDietPlanPersistence, DietPlanPersistenceRepository>();
        services.AddScoped<ISupplementationPersistence, SupplementationPersistenceRepository>();

        return services;
    }
}
