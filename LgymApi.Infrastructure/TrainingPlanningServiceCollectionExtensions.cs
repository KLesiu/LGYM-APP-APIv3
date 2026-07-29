using LgymApi.Application.TrainingPlanning.PlanDay.Persistence;
using LgymApi.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddTrainingPlanningInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IPlanDayPersistence, PlanDayPersistence>();

        return services;
    }
}
