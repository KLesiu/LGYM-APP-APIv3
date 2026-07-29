using LgymApi.Application.Options;
using LgymApi.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformServices(
        this IServiceCollection services,
        IConfiguration configuration,
        bool enableSensitiveLogging,
        bool isTesting = false,
        bool hostBackgroundServer = false)
    {
        var appDefaultsOptions = AppDefaultsOptionsFactory.Resolve(configuration);
        var backgroundCommandOptions = configuration.GetSection("BackgroundCommands").Get<BackgroundCommandOptions>() ?? new BackgroundCommandOptions();

        backgroundCommandOptions.Validate();

        services.AddSingleton(appDefaultsOptions);
        services.AddSingleton(backgroundCommandOptions);
        services.AddHttpContextAccessor();
        // Google auth fallback uses Google userinfo over HTTP when the ID token omits profile/email claims.
        services.AddHttpClient();

        AddPlatformPersistence(services, configuration, enableSensitiveLogging);
        AddPlatformBackgroundRuntime(services, configuration, isTesting, hostBackgroundServer);

        AddPlatformMapperRegistry(services);
        AddPlatformReliabilityDispatcher(services);
        AddPlatformUnitOfWork(services);

        return services;
    }
}
