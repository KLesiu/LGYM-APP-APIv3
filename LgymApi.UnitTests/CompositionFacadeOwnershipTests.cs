using FluentAssertions;
using LgymApi.Application;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Identity.ApiAdapters;
using LgymApi.Application.Nutrition.DietPlans.CreateTraineePlan;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Application.Platform.ReferenceData.AppConfig;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Application.Platform.ReferenceData.ApiAdapters;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.TrainingPlanning.PlanDay.Persistence;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.BackgroundWorker;
using LgymApi.BackgroundWorker.Common;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Runtime;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Infrastructure;
using LgymApi.Infrastructure.Services;
using LgymApi.Notifications.ApiAdapters;
using LgymApi.Notifications.Contracts;
using LgymApi.Platform.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CompositionFacadeOwnershipTests
{
    [Test]
    public void OrderedHostComposition_RegistersAndResolvesExactOwnedDescriptorsOnce()
    {
        var services = CompositionRootTestHost.Create();
        var expectations = CreateExpectations();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        foreach (var expectation in expectations)
        {
            var descriptor = services
                .Where(candidate => candidate.ServiceType == expectation.ServiceType)
                .Should()
                .ContainSingle(expectation.ServiceType.FullName)
                .Which;
            descriptor.Lifetime.Should().Be(expectation.Lifetime, expectation.ServiceType.FullName);
            descriptor.ImplementationType.Should().NotBeNull(expectation.ServiceType.FullName);
            descriptor.ImplementationType!.Assembly.Should().BeSameAs(expectation.OwnerAssembly, expectation.ServiceType.FullName);

            var resolved = scope.ServiceProvider.GetRequiredService(expectation.ServiceType);
            resolved.GetType().Assembly.Should().BeSameAs(expectation.OwnerAssembly, expectation.ServiceType.FullName);
        }
    }

    [Test]
    public void AddApplication_RegistersOnlyReportingWorkoutCoachingAndNutritionServices()
    {
        var services = new ServiceCollection();

        services.AddApplication();

        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IReportingService));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IWorkoutProgressReadWriteService));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(ICoachingRelationshipAccessService));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(ICreateTraineeDietPlanUseCase));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IAppConfigService));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(ITokenService));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IPlanDayService));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IInAppNotificationService));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IAppConfigApiAdapter));
    }

    [Test]
    public void AddInfrastructure_RegistersRemainingPersistenceWithoutExtractedModuleImplementations()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(
            CompositionRootTestHost.CreateConfiguration(),
            enableSensitiveLogging: false,
            isTesting: true,
            hostBackgroundServer: true);

        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IUnitOfWork));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IPlanDayPersistence));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IAppConfigService));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(ITokenService));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IPlanDayService));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IInAppNotificationService));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IEmailSender));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IPushProviderSender));
    }

    [Test]
    public void WorkerFacade_RegistersSchedulersAndRuntimeWithoutModuleProvidersOrRepositories()
    {
        var services = new ServiceCollection();

        services.AddBackgroundWorkerServices(isTesting: true);

        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IBackgroundActionResolver));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IEmailBackgroundScheduler));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IActionMessageScheduler));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IPushBackgroundScheduler));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IUnitOfWork));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IInAppNotificationService));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IPushProviderSender));
    }

    [Test]
    public void OrderedHostComposition_RejectsDuplicateCanonicalRepository()
    {
        var services = CompositionRootTestHost.Create();
        var repositoryDescriptor = services.Single(descriptor => descriptor.ServiceType == typeof(IAppConfigRepository));
        services.AddScoped(repositoryDescriptor.ServiceType, repositoryDescriptor.ImplementationType!);

        var action = () => GetSingleDescriptor(services, typeof(IAppConfigRepository));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{typeof(IAppConfigRepository).FullName}*exactly once*2*");
    }

    [Test]
    public void HostComposition_RequiresWorkerAfterApiAdapters()
    {
        var services = CompositionRootTestHost.Create(registerWorkerBeforeInfrastructure: true);

        var action = () => AssertWorkerIsLast(services);

        action.Should().Throw<InvalidOperationException>().WithMessage("*Worker*last*");
    }

    private static FacadeDescriptorExpectation[] CreateExpectations() =>
    [
        new(typeof(IAppConfigService), ServiceLifetime.Scoped, typeof(ActorReference).Assembly),
        new(typeof(ITokenService), ServiceLifetime.Scoped, typeof(AccountReference).Assembly),
        new(typeof(IPlanDayService), ServiceLifetime.Scoped, typeof(PlanReference).Assembly),
        new(typeof(IInAppNotificationService), ServiceLifetime.Scoped, typeof(NotificationReference).Assembly),
        new(typeof(IReportingService), ServiceLifetime.Scoped, typeof(LgymApi.Application.ServiceCollectionExtensions).Assembly),
        new(typeof(IWorkoutProgressReadWriteService), ServiceLifetime.Scoped, typeof(LgymApi.Application.ServiceCollectionExtensions).Assembly),
        new(typeof(ICoachingRelationshipAccessService), ServiceLifetime.Scoped, typeof(LgymApi.Application.ServiceCollectionExtensions).Assembly),
        new(typeof(ICreateTraineeDietPlanUseCase), ServiceLifetime.Scoped, typeof(LgymApi.Application.ServiceCollectionExtensions).Assembly),
        new(typeof(IUnitOfWork), ServiceLifetime.Scoped, typeof(LgymApi.Infrastructure.ServiceCollectionExtensions).Assembly),
        new(typeof(IAppConfigApiAdapter), ServiceLifetime.Scoped, typeof(LgymApi.Application.ServiceCollectionExtensions).Assembly),
        new(typeof(IPushInstallationApiAdapter), ServiceLifetime.Scoped, typeof(NotificationReference).Assembly),
        new(typeof(IBackgroundActionResolver), ServiceLifetime.Scoped, typeof(LgymApi.BackgroundWorker.ServiceProvider).Assembly),
        new(typeof(IEmailBackgroundScheduler), ServiceLifetime.Scoped, typeof(NoOpEmailBackgroundScheduler).Assembly)
    ];

    private static ServiceDescriptor GetSingleDescriptor(IServiceCollection services, Type serviceType)
    {
        var descriptors = services.Where(descriptor => descriptor.ServiceType == serviceType).ToArray();
        if (descriptors.Length != 1)
        {
            throw new InvalidOperationException(
                $"Service '{serviceType.FullName}' must be registered exactly once; actual count is {descriptors.Length}.");
        }

        return descriptors[0];
    }

    private static void AssertWorkerIsLast(IServiceCollection services)
    {
        var workerIndex = services.IndexOf(GetSingleDescriptor(services, typeof(IEmailBackgroundScheduler)));
        var apiAdapterIndex = services.IndexOf(GetSingleDescriptor(services, typeof(IPushInstallationApiAdapter)));
        if (workerIndex <= apiAdapterIndex)
        {
            throw new InvalidOperationException("Worker registrations must be last after API adapters.");
        }
    }

    private sealed record FacadeDescriptorExpectation(
        Type ServiceType,
        ServiceLifetime Lifetime,
        System.Reflection.Assembly OwnerAssembly);
}
