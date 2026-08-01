using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Infrastructure.CommandRuntime;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool enableSensitiveLogging,
        bool isTesting = false,
        bool hostBackgroundServer = false)
    {
        var isDevelopmentOrTesting = enableSensitiveLogging || isTesting;

        services.AddPlatformServices(configuration, enableSensitiveLogging, isTesting, hostBackgroundServer);
        services.AddTrainingPlanningInfrastructure();
        services.AddWorkoutProgressInfrastructure();
        services.AddCoachingInfrastructure();
        services.AddNutritionInfrastructure();
        services.AddReportingInfrastructure(configuration, isDevelopmentOrTesting);
        services.AddNotificationsInfrastructure();

        return services;
    }
}
