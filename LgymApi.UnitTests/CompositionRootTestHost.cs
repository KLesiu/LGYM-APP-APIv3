using LgymApi.Api.Features.InAppNotification;
using LgymApi.Application;
using LgymApi.Application.Mapping;
using LgymApi.Application.Notifications;
using LgymApi.BackgroundWorker;
using LgymApi.Identity;
using LgymApi.Infrastructure;
using LgymApi.Platform;
using LgymApi.TestUtils;
using LgymApi.TrainingPlanning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.UnitTests;

public enum CompositionFacade
{
    Platform,
    Identity,
    TrainingPlanning,
    Notifications,
    Application,
    Infrastructure,
    ApplicationApiAdapters,
    NotificationsApiAdapters,
    Worker
}

internal static class CompositionRootTestHost
{
    public static TestHostServiceComposition CreateFactoryComposition(
        IConfiguration configuration,
        bool isTesting = true,
        bool includeWorker = true,
        CompositionFacade? omittedFacade = null)
    {
        return new TestHostServiceComposition(
            AddMappings: services =>
            {
                services.AddSingleton(configuration);
                services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
            },
            AddPlatformModule: services => AddUnlessOmitted(CompositionFacade.Platform, omittedFacade, services.AddPlatformModule),
            AddIdentityModule: services => AddUnlessOmitted(CompositionFacade.Identity, omittedFacade, services.AddIdentityModule),
            AddTrainingPlanningModule: services => AddUnlessOmitted(CompositionFacade.TrainingPlanning, omittedFacade, services.AddTrainingPlanningModule),
            AddNotificationsModule: services => AddUnlessOmitted(
                CompositionFacade.Notifications,
                omittedFacade,
                () => services.AddNotificationsModule(configuration)),
            AddApplication: services => AddUnlessOmitted(CompositionFacade.Application, omittedFacade, services.AddApplication),
            AddInfrastructure: services => AddUnlessOmitted(
                CompositionFacade.Infrastructure,
                omittedFacade,
                () => services.AddInfrastructure(
                    configuration,
                    enableSensitiveLogging: false,
                    isTesting,
                    hostBackgroundServer: true)),
            AddApplicationApiAdapters: services => AddUnlessOmitted(
                CompositionFacade.ApplicationApiAdapters,
                omittedFacade,
                services.AddTask7ApiCompatibility),
            AddNotificationsApiAdapters: services =>
            {
                AddUnlessOmitted(
                    CompositionFacade.NotificationsApiAdapters,
                    omittedFacade,
                    services.AddNotificationsApiAdapters);
                services.AddSignalR();
                services.AddScoped<IInAppNotificationPushPublisher, SignalRNotificationPushPublisher>();
            },
            AddWorker: includeWorker
                ? services => AddUnlessOmitted(
                    CompositionFacade.Worker,
                    omittedFacade,
                    () => services.AddBackgroundWorkerServices(isTesting))
                : null);
    }

    public static ServiceCollection Create(
        CompositionFacade? omittedFacade = null,
        bool registerWorkerBeforeInfrastructure = false)
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSignalR();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        AddUnlessOmitted(CompositionFacade.Platform, omittedFacade, services.AddPlatformModule);
        AddUnlessOmitted(CompositionFacade.Identity, omittedFacade, services.AddIdentityModule);
        AddUnlessOmitted(CompositionFacade.TrainingPlanning, omittedFacade, services.AddTrainingPlanningModule);
        AddUnlessOmitted(
            CompositionFacade.Notifications,
            omittedFacade,
            () => services.AddNotificationsModule(configuration));
        AddUnlessOmitted(CompositionFacade.Application, omittedFacade, services.AddApplication);

        if (registerWorkerBeforeInfrastructure)
        {
            AddUnlessOmitted(
                CompositionFacade.Worker,
                omittedFacade,
                () => services.AddBackgroundWorkerServices(isTesting: true));
        }

        AddUnlessOmitted(
            CompositionFacade.Infrastructure,
            omittedFacade,
            () => services.AddInfrastructure(
                configuration,
                enableSensitiveLogging: false,
                isTesting: true,
                hostBackgroundServer: true));
        AddUnlessOmitted(
            CompositionFacade.ApplicationApiAdapters,
            omittedFacade,
            services.AddTask7ApiCompatibility);
        AddUnlessOmitted(
            CompositionFacade.NotificationsApiAdapters,
            omittedFacade,
            services.AddNotificationsApiAdapters);

        services.AddScoped<IInAppNotificationPushPublisher, SignalRNotificationPushPublisher>();

        if (!registerWorkerBeforeInfrastructure)
        {
            AddUnlessOmitted(
                CompositionFacade.Worker,
                omittedFacade,
                () => services.AddBackgroundWorkerServices(isTesting: true));
        }

        return services;
    }

    private static void AddUnlessOmitted(
        CompositionFacade facade,
        CompositionFacade? omittedFacade,
        Func<IServiceCollection> registration)
    {
        if (facade != omittedFacade)
        {
            registration();
        }
    }

    internal static IConfiguration CreateConfiguration()
    {
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["ConnectionStrings:Postgres"] = "Host=localhost;Database=composition;Username=test;Password=test";
        values["Email:DeliveryMode"] = "Dummy";
        values["Email:DummyOutputDirectory"] = "DummyOutbox";
        values["PhotoStorage:Provider"] = "Local";

        return TestConfigurationBuilder.BuildConfiguration(values);
    }
}
