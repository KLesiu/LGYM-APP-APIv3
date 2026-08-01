using FluentAssertions;
using Hangfire.Logging;
using LgymApi.Application;
using LgymApi.Application.Coaching.ApiAdapters;
using LgymApi.Application.Features.PasswordReset.Contracts;
using LgymApi.Application.Identity.ApiAdapters;
using LgymApi.Application.Identity.Contracts.Sessions;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Adapters;
using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.Application.Nutrition.ApiAdapters;
using LgymApi.Application.Platform.ReferenceData.ApiAdapters;
using LgymApi.Application.Reporting.ApiAdapters;
using LgymApi.Application.TrainingPlanning.ApiAdapters;
using LgymApi.Application.WorkoutProgress.ApiAdapters;
using LgymApi.Notifications;
using LgymApi.Notifications.ApiAdapters;
using LgymApi.Notifications.Contracts;
using LgymApi.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ApplicationApiAdapterRegistrationTests
{
    private static readonly MethodInfo ResolveLogProvider = typeof(LogProvider).GetMethod(
        "ResolveLogProvider",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private ILogProvider? _initialLogProvider;

    private static readonly IReadOnlyDictionary<Type, Type> ExpectedApplicationRegistrations =
        new Dictionary<Type, Type>
        {
            [typeof(IAuthenticatedAccountApiAdapter)] = typeof(AuthenticatedAccountApiAdapter),
            [typeof(IAccountAccessApiAdapter)] = typeof(AccountAccessApiAdapter),
            [typeof(IAccountEloApiAdapter)] = typeof(AccountEloApiAdapter),
            [typeof(IAccountExternalLoginApiAdapter)] = typeof(AccountExternalLoginApiAdapter),
            [typeof(IAccountTutorialApiAdapter)] = typeof(AccountTutorialApiAdapter),
            [typeof(IAdminAccountManagementApiAdapter)] = typeof(AdminAccountManagementApiAdapter),
            [typeof(IRoleManagementApiAdapter)] = typeof(RoleManagementApiAdapter),
            [typeof(IPlanAccountApiAdapter)] = typeof(PlanApiAdapter),
            [typeof(IManagedPlanAccountApiAdapter)] = typeof(ManagedPlanApiAdapter),
            [typeof(IDietPlanAccountApiAdapter)] = typeof(DietPlanApiAdapter),
            [typeof(ISupplementationApiAdapter)] = typeof(SupplementationApiAdapter),
            [typeof(IExerciseApiAdapter)] = typeof(ExerciseApiAdapter),
            [typeof(IMainRecordsApiAdapter)] = typeof(MainRecordsApiAdapter),
            [typeof(IAppConfigApiAdapter)] = typeof(AppConfigApiAdapter),
            [typeof(ITrainerInvitationApiPort)] = typeof(TrainerInvitationApiAdapter),
            [typeof(ITrainerDashboardProgressApiPort)] = typeof(TrainerDashboardProgressApiAdapter),
            [typeof(ITrainerTraineeNotesApiPort)] = typeof(TrainerTraineeNotesApiAdapter),
            [typeof(ITraineeNotesApiPort)] = typeof(TraineeNotesApiAdapter),
            [typeof(ITraineeRelationshipApiPort)] = typeof(TraineeRelationshipApiAdapter),
            [typeof(ITrainerReportTemplateApiPort)] = typeof(TrainerReportTemplateApiAdapter),
            [typeof(ITrainerReportRequestApiPort)] = typeof(TrainerReportRequestApiAdapter),
            [typeof(ITraineeReportRequestApiPort)] = typeof(TraineeReportRequestApiAdapter),
            [typeof(ITrainerReportPhotoApiPort)] = typeof(TrainerReportPhotoApiAdapter),
            [typeof(ITraineeReportPhotoApiPort)] = typeof(TraineeReportPhotoApiAdapter),
            [typeof(IRecurringReportAssignmentApiPort)] = typeof(RecurringReportAssignmentApiAdapter)
        };

    private static readonly IReadOnlyDictionary<Type, Type> ExpectedNotificationsApiRegistrations =
        new Dictionary<Type, Type>
        {
            [typeof(IInAppNotificationApiAdapter)] = typeof(InAppNotificationApiAdapter),
            [typeof(INotificationEventApiAdapter)] = typeof(NotificationEventApiAdapter),
            [typeof(IPushInstallationApiAdapter)] = typeof(PushInstallationApiAdapter)
        };

    private static readonly IReadOnlyDictionary<Type, Type> ExpectedRegistrations =
        ExpectedApplicationRegistrations.Concat(ExpectedNotificationsApiRegistrations)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

    [SetUp]
    public void CaptureHangfireLogProvider()
    {
        _initialLogProvider = (ILogProvider)ResolveLogProvider.Invoke(null, null)!;
    }

    [TearDown]
    public void RestoreHangfireLogProvider()
    {
        LogProvider.SetCurrentLogProvider(_initialLogProvider!);
    }

    [Test]
    public void ApiAdapterFacades_RegisterTheExact25Plus3ManifestExactlyOnceAsScoped()
    {
        var services = new ServiceCollection();
        services.AddApplicationApiAdapters();
        services.AddNotificationsApiAdapters();

        AssertExpectedApiAdapters(services);

        var discoveredContracts = new[]
            {
                typeof(ServiceCollectionExtensions).Assembly,
                typeof(NotificationReference).Assembly
            }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsPublic && type.IsInterface && IsApiAdapterContractNamespace(type.Namespace))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        discoveredContracts.Should().Equal(ExpectedRegistrations.Keys.OrderBy(type => type.FullName, StringComparer.Ordinal));
        ExpectedApplicationRegistrations.Should().HaveCount(25);
        ExpectedNotificationsApiRegistrations.Should().HaveCount(3);
        ExpectedRegistrations.Should().HaveCount(28);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void HostComposition_Captures_Current_Api_Adapter_And_Integration_Descriptor_Ledgers(bool isTesting)
    {
        var configuration = CreateHostConfiguration(isTesting);
        var services = TestServiceProviderFactory.CreateServiceCollection(
            CompositionRootTestHost.CreateFactoryComposition(configuration, isTesting));

        AssertExpectedApiAdapters(services);
        AssertRetainedNotificationsIntegrationAdapters(services);
        AssertKnownMultiRegistrations(services);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        AssertApiAdaptersResolve(scope.ServiceProvider);

        TestContext.Progress.WriteLine(
            $"Issue-395 descriptor baseline: testing={isTesting}; api={ExpectedRegistrations.Count}; "
            + $"application={ExpectedApplicationRegistrations.Count}; notifications={ExpectedNotificationsApiRegistrations.Count}.");
    }

    [Test]
    public void HostCompositionLedgerFixture_Rejects_A_Duplicate_Single_Value_Registration()
    {
        var services = TestServiceProviderFactory.CreateServiceCollection(
            CompositionRootTestHost.CreateFactoryComposition(CompositionRootTestHost.CreateConfiguration()));
        services.AddScoped(
            typeof(LgymApi.Application.Platform.ReferenceData.ApiAdapters.IAppConfigApiAdapter),
            typeof(LgymApi.Application.Platform.ReferenceData.ApiAdapters.AppConfigApiAdapter));

        var action = () => AssertExpectedApiAdapters(services);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*IAppConfigApiAdapter*exactly once*");
    }

    [Test]
    public void HostCompositionLedgerFixture_Rejects_An_Omitted_Owner_Api_Adapter_Registration()
    {
        var services = new ServiceCollection();
        services.AddApplicationApiAdapters();
        services.AddNotificationsApiAdapters();
        var descriptor = services.Single(candidate => candidate.ServiceType == typeof(ITrainerInvitationApiPort));
        services.Remove(descriptor);

        var action = () => AssertExpectedApiAdapters(services);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*ITrainerInvitationApiPort*exactly once; actual count is 0*");
    }

    [Test]
    public void HostCompositionLedgerFixture_Rejects_A_WrongLifetimeRegistration()
    {
        var services = TestServiceProviderFactory.CreateServiceCollection(
            CompositionRootTestHost.CreateFactoryComposition(CompositionRootTestHost.CreateConfiguration()));
        var descriptor = services.Single(candidate => candidate.ServiceType == typeof(IAppConfigApiAdapter));
        services.Remove(descriptor);
        services.AddSingleton<IAppConfigApiAdapter, AppConfigApiAdapter>();

        var action = () => AssertExpectedApiAdapters(services);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*IAppConfigApiAdapter*lifetime 'Scoped'*Singleton*");
    }

    [Test]
    public void NotificationsModule_RetainsTheThreeIntegrationAdaptersWithTheirExactOwnerContracts()
    {
        var services = new ServiceCollection();
        services.AddNotificationsModule();

        AssertRetainedNotificationsIntegrationAdapters(services);
        typeof(PushInstallationSessionDisassociationAdapter).GetInterfaces().Should().BeEquivalentTo(
            new[] { typeof(IAccountSessionDisassociationPort) });
        typeof(CoachingEmailNotificationSchedulerAdapter).GetInterfaces().Should().BeEquivalentTo(
            new[] { typeof(ICoachingEmailNotificationFeature), typeof(ICoachingEmailNotificationScheduler) });
        typeof(PasswordRecoveryEmailSchedulerAdapter).GetInterfaces().Should().BeEquivalentTo(
            new[] { typeof(IPasswordRecoveryEmailScheduler) });
    }

    private static void AssertExpectedApiAdapters(IServiceCollection services)
    {
        foreach (var expected in ExpectedRegistrations)
        {
            var descriptors = services.Where(candidate => candidate.ServiceType == expected.Key).ToArray();
            if (descriptors.Length != 1)
            {
                throw new InvalidOperationException($"API adapter '{expected.Key.FullName}' must be registered exactly once; actual count is {descriptors.Length}.");
            }

            if (descriptors[0].Lifetime != ServiceLifetime.Scoped)
            {
                throw new InvalidOperationException(
                    $"API adapter '{expected.Key.FullName}' must use lifetime '{ServiceLifetime.Scoped}'; actual lifetime is '{descriptors[0].Lifetime}'.");
            }

            descriptors[0].ImplementationType.Should().Be(expected.Value, expected.Key.FullName);
            descriptors[0].ImplementationType!.IsNotPublic.Should().BeTrue(expected.Key.FullName);
        }
    }

    private static void AssertRetainedNotificationsIntegrationAdapters(IServiceCollection services)
    {
        AssertSingle(services, typeof(IAccountSessionDisassociationPort), typeof(PushInstallationSessionDisassociationAdapter));
        AssertSingle(services, typeof(ICoachingEmailNotificationFeature), typeof(CoachingEmailNotificationSchedulerAdapter));
        AssertSingle(services, typeof(ICoachingEmailNotificationScheduler), typeof(CoachingEmailNotificationSchedulerAdapter));
        AssertSingle(services, typeof(IPasswordRecoveryEmailScheduler), typeof(PasswordRecoveryEmailSchedulerAdapter));
    }

    private static void AssertApiAdaptersResolve(IServiceProvider serviceProvider)
    {
        foreach (var expected in ExpectedRegistrations)
        {
            serviceProvider.GetRequiredService(expected.Key).GetType().Should().Be(expected.Value, expected.Key.FullName);
        }

        serviceProvider.GetRequiredService<IAccountSessionDisassociationPort>().Should().BeOfType<PushInstallationSessionDisassociationAdapter>();
        serviceProvider.GetRequiredService<ICoachingEmailNotificationFeature>().Should().BeOfType<CoachingEmailNotificationSchedulerAdapter>();
        serviceProvider.GetRequiredService<ICoachingEmailNotificationScheduler>().Should().BeOfType<CoachingEmailNotificationSchedulerAdapter>();
        serviceProvider.GetRequiredService<IPasswordRecoveryEmailScheduler>().Should().BeOfType<PasswordRecoveryEmailSchedulerAdapter>();
    }

    private static void AssertKnownMultiRegistrations(IServiceCollection services)
    {
        Assert.That(services.Count(descriptor => descriptor.ServiceType.FullName == "LgymApi.Application.WorkoutProgress.Scoring.Elo.IExerciseEloCalculator"), Is.EqualTo(4));
        Assert.That(services.Count(descriptor => descriptor.ServiceType.FullName == "LgymApi.BackgroundWorker.Common.Notifications.IEmailTemplateComposer"), Is.EqualTo(6));
        Assert.That(services.Count(descriptor => descriptor.ServiceType.FullName == "LgymApi.Application.Mapping.Core.IMappingProfile"), Is.EqualTo(46));
        Assert.That(services.Count(descriptor => descriptor.ServiceType == typeof(IMappingProfile)
            && descriptor.ImplementationType?.FullName == "LgymApi.Application.Coaching.ApiAdapters.CoachingApiAdapterMappingProfile"), Is.EqualTo(1));
        Assert.That(services.Count(descriptor => descriptor.ServiceType == typeof(IMappingProfile)
            && descriptor.ImplementationType?.FullName == "LgymApi.Application.Reporting.ApiAdapters.ReportingApiAdapterMappingProfile"), Is.EqualTo(1));
    }

    private static void AssertSingle(IServiceCollection services, Type serviceType, Type implementationType)
    {
        var descriptors = services.Where(descriptor => descriptor.ServiceType == serviceType).ToArray();
        Assert.That(descriptors, Has.Length.EqualTo(1), serviceType.FullName);
        Assert.That(descriptors[0].Lifetime, Is.EqualTo(ServiceLifetime.Scoped), serviceType.FullName);
        Assert.That(descriptors[0].ImplementationType, Is.EqualTo(implementationType), serviceType.FullName);
    }

    private static Microsoft.Extensions.Configuration.IConfiguration CreateHostConfiguration(bool isTesting)
    {
        if (isTesting)
        {
            return CompositionRootTestHost.CreateConfiguration();
        }

        var values = TestConfigurationBuilder.ToDictionary(CompositionRootTestHost.CreateConfiguration());
        values["PhotoStorage:Provider"] = "CloudflareR2";
        values["PhotoStorage:BucketName"] = "issue-395-descriptor-ledger";
        values["PhotoStorage:Endpoint"] = "https://example.r2.cloudflarestorage.com";
        values["PhotoStorage:AccessKeyId"] = "test-access-key";
        values["PhotoStorage:SecretAccessKey"] = "test-secret-key";
        return TestConfigurationBuilder.BuildConfiguration(values);
    }

    private static bool IsApiAdapterContractNamespace(string? namespaceName)
        => namespaceName is not null
            && (namespaceName.StartsWith("LgymApi.Application.Identity.ApiAdapters", StringComparison.Ordinal)
                 || namespaceName.StartsWith("LgymApi.Application.TrainingPlanning.ApiAdapters", StringComparison.Ordinal)
                 || namespaceName.StartsWith("LgymApi.Application.Coaching.ApiAdapters", StringComparison.Ordinal)
                 || namespaceName.StartsWith("LgymApi.Application.Nutrition.ApiAdapters", StringComparison.Ordinal)
                 || namespaceName.StartsWith("LgymApi.Application.Platform.ReferenceData.ApiAdapters", StringComparison.Ordinal)
                  || namespaceName.StartsWith("LgymApi.Application.WorkoutProgress.ApiAdapters", StringComparison.Ordinal)
                  || namespaceName.StartsWith("LgymApi.Notifications.ApiAdapters", StringComparison.Ordinal)
                 || namespaceName.StartsWith("LgymApi.Application.Reporting.ApiAdapters", StringComparison.Ordinal));
}
