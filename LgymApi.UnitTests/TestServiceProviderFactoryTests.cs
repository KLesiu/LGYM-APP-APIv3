using FluentAssertions;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Application.Platform.ReferenceData.AppConfig;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Runtime;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.TestUtils;
using LgymApi.TestUtils.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TestServiceProviderFactoryTests
{
    [Test]
    public void CreateServiceProvider_ComposesProductionFacadesAndCanonicalMappings()
    {
        var configuration = CompositionRootTestHost.CreateConfiguration();
        var composition = CompositionRootTestHost.CreateFactoryComposition(configuration);

        using var provider = TestServiceProviderFactory.CreateServiceProvider(composition);
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        services.GetServices<IAppConfigService>().Should().ContainSingle();
        services.GetServices<ITokenService>().Should().ContainSingle();
        services.GetServices<IPlanDayService>().Should().ContainSingle();
        services.GetServices<IInAppNotificationService>().Should().ContainSingle();
        services.GetServices<IReportingService>().Should().ContainSingle();
        services.GetServices<IUnitOfWork>().Should().ContainSingle();
        services.GetServices<CommandContractRegistry>().Should().ContainSingle();
        services.GetServices<IMappingProfile>().Should().HaveCount(44);
        services.GetServices<IMappingProfile>().Select(profile => profile.GetType()).Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void CreateServiceProvider_WhenTrainingPlanningIsOmitted_FailsForItsPublicContract()
    {
        var configuration = CompositionRootTestHost.CreateConfiguration();
        var composition = CompositionRootTestHost.CreateFactoryComposition(
            configuration,
            omittedFacade: CompositionFacade.TrainingPlanning);
        using var provider = TestServiceProviderFactory.CreateServiceProvider(composition);

        var action = () => provider.GetRequiredService<IPlanDayService>();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{typeof(IPlanDayService).FullName}*");
    }

    [Test]
    public void CreateServiceProvider_WhenFakesAreRegisteredBeforeModules_ModuleRegistrationsWin()
    {
        var configuration = CompositionRootTestHost.CreateConfiguration();
        var composition = CompositionRootTestHost.CreateFactoryComposition(configuration);
        var earlyEmailSender = new TestEmailSender();
        var earlyPushSender = new RecordingPushProviderSender();
        var earlySessionStore = new FakeUserSessionStore();

        using var provider = TestServiceProviderFactory.CreateServiceProvider(
            composition,
            configureServicesBeforeModules: services =>
            {
                services.AddSingleton<IEmailSender>(earlyEmailSender);
                services.AddSingleton<IPushProviderSender>(earlyPushSender);
                services.AddSingleton<IUserSessionStore>(earlySessionStore);
            });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IEmailSender>().Should().NotBeSameAs(earlyEmailSender);
        scope.ServiceProvider.GetRequiredService<IPushProviderSender>().Should().NotBeSameAs(earlyPushSender);
        scope.ServiceProvider.GetRequiredService<IUserSessionStore>().Should().NotBeSameAs(earlySessionStore);
    }

    [Test]
    public void CreateServiceProvider_WhenFakesReplaceAfterModules_CapturedEffectsAndIdentityStoreWin()
    {
        var configuration = CompositionRootTestHost.CreateConfiguration();
        var composition = CompositionRootTestHost.CreateFactoryComposition(configuration);
        var emailSender = new TestEmailSender();
        var pushSender = new RecordingPushProviderSender();
        var sessionStore = new FakeUserSessionStore();

        using var provider = TestServiceProviderFactory.CreateServiceProvider(
            composition,
            replaceServicesAfterModules: services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(emailSender);
                services.RemoveAll<IPushProviderSender>();
                services.AddSingleton<IPushProviderSender>(pushSender);
                services.RemoveAll<IUserSessionStore>();
                services.AddSingleton<IUserSessionStore>(sessionStore);
            });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IEmailSender>().Should().ContainSingle().Which.Should().BeSameAs(emailSender);
        scope.ServiceProvider.GetServices<IPushProviderSender>().Should().ContainSingle().Which.Should().BeSameAs(pushSender);
        scope.ServiceProvider.GetServices<IUserSessionStore>().Should().ContainSingle().Which.Should().BeSameAs(sessionStore);
    }

    private sealed class RecordingPushProviderSender : IPushProviderSender
    {
        public Task<PushSendAttemptResult> SendAsync(
            Id<PushInstallation> installationId,
            PushEventPayload payload,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PushSendAttemptResult(
                PushSendOutcome.Skipped,
                "TestCapture",
                null,
                null,
                null));
        }
    }
}
