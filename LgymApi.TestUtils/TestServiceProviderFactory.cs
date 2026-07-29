using LgymApi.Application;
using LgymApi.BackgroundWorker;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.TestUtils;

/// <summary>
/// Builds service providers through the same ordered composition phases as the production host.
/// </summary>
public static class TestServiceProviderFactory
{
    public static IServiceCollection AddApplicationAndWorkerServicesForTesting(this IServiceCollection services)
    {
        services.AddApplication();
        services.AddBackgroundWorkerServices(isTesting: true);
        return services;
    }

    public static ServiceCollection CreateServiceCollection(
        TestHostServiceComposition composition,
        Action<IServiceCollection>? configureServicesBeforeModules = null,
        Action<IServiceCollection>? replaceServicesAfterModules = null)
    {
        ArgumentNullException.ThrowIfNull(composition);

        var services = new ServiceCollection();
        services.AddLogging();
        configureServicesBeforeModules?.Invoke(services);
        composition.AddMappings(services);
        composition.AddPlatformModule(services);
        composition.AddIdentityModule(services);
        composition.AddTrainingPlanningModule(services);
        composition.AddNotificationsModule(services);
        composition.AddApplication(services);
        composition.AddInfrastructure(services);
        composition.AddApplicationApiAdapters(services);
        composition.AddNotificationsApiAdapters(services);
        composition.AddWorker?.Invoke(services);
        replaceServicesAfterModules?.Invoke(services);

        return services;
    }

    public static Microsoft.Extensions.DependencyInjection.ServiceProvider CreateServiceProvider(
        TestHostServiceComposition composition,
        Action<IServiceCollection>? configureServicesBeforeModules = null,
        Action<IServiceCollection>? replaceServicesAfterModules = null)
        => CreateServiceCollection(
                composition,
                configureServicesBeforeModules,
                replaceServicesAfterModules)
            .BuildServiceProvider();
}

public sealed record TestHostServiceComposition(
    Action<IServiceCollection> AddMappings,
    Action<IServiceCollection> AddPlatformModule,
    Action<IServiceCollection> AddIdentityModule,
    Action<IServiceCollection> AddTrainingPlanningModule,
    Action<IServiceCollection> AddNotificationsModule,
    Action<IServiceCollection> AddApplication,
    Action<IServiceCollection> AddInfrastructure,
    Action<IServiceCollection> AddApplicationApiAdapters,
    Action<IServiceCollection> AddNotificationsApiAdapters,
    Action<IServiceCollection>? AddWorker);
