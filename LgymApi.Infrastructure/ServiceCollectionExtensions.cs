using LgymApi.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        EmailOptionsFactory.Validate(
            EmailOptionsFactory.Create(configuration, AppDefaultsOptionsFactory.Resolve(configuration)));

        services.AddPlatformServices(configuration, enableSensitiveLogging, isTesting, hostBackgroundServer);
        services.AddIdentityInfrastructure();
        services.AddTrainingPlanningInfrastructure();
        services.AddWorkoutProgressInfrastructure();
        services.AddCoachingInfrastructure();
        services.AddNutritionInfrastructure();
        services.AddReportingInfrastructure(configuration, isDevelopmentOrTesting);
        services.AddNotificationsInfrastructure(configuration);

        return services;
    }
}
