using FluentAssertions;
using Hangfire;
using LgymApi.Application.Coaching;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Identity;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker;
using LgymApi.BackgroundWorker.Common.Jobs;
using LgymApi.Application.Options;
using LgymApi.Application.Pagination;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Application.Notifications.Models;
using LgymApi.Application.Notifications.Repositories;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Infrastructure;
using LgymApi.Infrastructure.Options;
using LgymApi.Infrastructure.Pagination;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.Repositories.ReferenceData;
using LgymApi.Infrastructure.Services;
using LgymApi.Infrastructure.UnitOfWork;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.BackgroundWorker.Actions.Contracts;
using LgymApi.BackgroundWorker.Notifications;
using LgymApi.Application.Abstractions.Storage;
using LgymApi.TestUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ApplicationCommandDispatcher = LgymApi.Application.Platform.Contracts.BackgroundCommands.ICommandDispatcher;
using IActionMessageScheduler = LgymApi.BackgroundWorker.Common.IActionMessageScheduler;
using IEmailBackgroundScheduler = LgymApi.BackgroundWorker.Common.IEmailBackgroundScheduler;
using QueryPaginationFacade = LgymApi.Infrastructure.Pagination.QueryPaginationService;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class InfrastructureServiceCollectionExtensionsTests
{
    [Test]
    public void AddInfrastructure_RegistersNoOpScheduler_WhenTesting()
    {
        var configuration = TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["PhotoStorage:Provider"] = "CloudflareR2",
            ["PhotoStorage:BucketName"] = "lgym-report-photos-dev",
            ["PhotoStorage:Endpoint"] = "https://38c1c25f99af223efee28a9afcf5d575.r2.cloudflarestorage.com",
            ["PhotoStorage:AccessKeyId"] = "test-access-key",
            ["PhotoStorage:SecretAccessKey"] = "test-secret-key"
        });

        using var provider = TestServiceProviderFactory.CreateInfrastructureProvider(
            configuration,
            isTesting: true,
            includeBackgroundWorker: true);
        var scheduler = provider.GetRequiredService<IEmailBackgroundScheduler>();
        scheduler.Should().BeOfType<NoOpEmailBackgroundScheduler>();
    }

    [Test]
    public void AddInfrastructure_UsesSmtpDeliveryModeByDefault()
    {
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values.Remove("Email:DeliveryMode");
        var configuration = TestConfigurationBuilder.BuildConfiguration(values);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration, enableSensitiveLogging: false, isTesting: true);

        AssertFactoryDescriptor<IEmailSender>(services, ServiceLifetime.Scoped);
        AssertTypeDescriptor<SmtpEmailSender, SmtpEmailSender>(services, ServiceLifetime.Scoped);
        AssertTypeDescriptor<DummyEmailSender, DummyEmailSender>(services, ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IEmailSender>();
        sender.Should().BeOfType<SmtpEmailSender>();
    }

    [Test]
    public void AddInfrastructure_UsesDummyEmailSender_WhenModeIsDummy()
    {
        var services = new ServiceCollection();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["Email:DeliveryMode"] = "Dummy";
        values["Email:DummyOutputDirectory"] = "DummyOutbox";
        values["Email:SmtpHost"] = string.Empty;
        var configuration = TestConfigurationBuilder.BuildConfiguration(values);

        services.AddInfrastructure(configuration, enableSensitiveLogging: false, isTesting: true);

        AssertFactoryDescriptor<IEmailSender>(services, ServiceLifetime.Scoped);
        AssertTypeDescriptor<SmtpEmailSender, SmtpEmailSender>(services, ServiceLifetime.Scoped);
        AssertTypeDescriptor<DummyEmailSender, DummyEmailSender>(services, ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IEmailSender>();
        sender.Should().BeOfType<DummyEmailSender>();
    }

    [TestCase(true, "Local", typeof(LocalPhotoStorageProvider))]
    [TestCase(false, "CloudflareR2", typeof(CloudflareR2PhotoStorageProvider))]
    public void AddInfrastructure_RegistersAndResolvesExactPlatformAdapters(
        bool isTesting,
        string photoStorageProvider,
        Type expectedPhotoStorageProviderType)
    {
        var services = new ServiceCollection();
        var configuration = CreateInfrastructureConfiguration(photoStorageProvider);

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddInfrastructure(configuration, enableSensitiveLogging: false, isTesting);

        AssertTypeDescriptor<IAppConfigRepository, AppConfigRepository>(services, ServiceLifetime.Scoped);
        AssertTypeDescriptor<ICommittedIntentDispatcher, CommittedIntentDispatcher>(services, ServiceLifetime.Scoped);
        AssertTypeDescriptor<IUnitOfWork, EfUnitOfWork>(services, ServiceLifetime.Scoped);
        AssertTypeDescriptor<ICommandEnvelopeRepository, CommandEnvelopeRepository>(services, ServiceLifetime.Scoped);
        AssertTypeDescriptor<IApiIdempotencyRecordRepository, ApiIdempotencyRecordRepository>(services, ServiceLifetime.Scoped);
        AssertTypeDescriptor<IEmailNotificationsFeature, EmailNotificationsFeature>(services, ServiceLifetime.Singleton);
        AssertTypeDescriptor<IEmailMetrics, EmailMetrics>(services, ServiceLifetime.Singleton);
        AssertTypeDescriptor<IEmailTemplateComposerFactory, EmailTemplateComposerFactory>(services, ServiceLifetime.Scoped);
        AssertFactoryDescriptor<IMapperRegistry>(services, ServiceLifetime.Singleton);
        AssertTypeDescriptor<IQueryPaginationService, QueryPaginationFacade>(services, ServiceLifetime.Scoped);
        AssertInstanceDescriptor<PaginationPolicy>(services, ServiceLifetime.Singleton);
        AssertInstanceDescriptor<EmailOptions>(services, ServiceLifetime.Singleton);
        AssertInstanceDescriptor<PhotoStorageOptions>(services, ServiceLifetime.Singleton);
        AssertTypeDescriptor<LocalPhotoDevelopmentStore, LocalPhotoDevelopmentStore>(services, ServiceLifetime.Singleton);
        AssertTypeDescriptor<InMemoryPhotoUploadInitTracker, InMemoryPhotoUploadInitTracker>(services, ServiceLifetime.Singleton);
        AssertTypeDescriptor<IPhotoUploadInitTracker, DbPhotoUploadInitTracker>(services, ServiceLifetime.Scoped);
        AssertTypeDescriptor(services, typeof(IPhotoStorageProvider), expectedPhotoStorageProviderType, ServiceLifetime.Scoped);
        AssertTypeDescriptor<IPushProviderSender, FcmPushSender>(services, ServiceLifetime.Scoped);
        AssertTypeDescriptor<ITokenService, TokenService>(services, ServiceLifetime.Scoped);
        AssertTypeDescriptor<IGoogleTokenValidator, GoogleTokenValidator>(services, ServiceLifetime.Scoped);
        AssertTypeDescriptor<ILegacyPasswordService, LegacyPasswordService>(services, ServiceLifetime.Scoped);
        AssertTypeDescriptor<IUserSessionStore, UserSessionStore>(services, ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var scopedServices = scope.ServiceProvider;

        scopedServices.GetRequiredService<IAppConfigRepository>().Should().BeOfType<AppConfigRepository>();
        scopedServices.GetRequiredService<ICommittedIntentDispatcher>().Should().BeOfType<CommittedIntentDispatcher>();
        scopedServices.GetRequiredService<IUnitOfWork>().Should().BeOfType<EfUnitOfWork>();
        scopedServices.GetRequiredService<ICommandEnvelopeRepository>().Should().BeOfType<CommandEnvelopeRepository>();
        scopedServices.GetRequiredService<IApiIdempotencyRecordRepository>().Should().BeOfType<ApiIdempotencyRecordRepository>();
        provider.GetRequiredService<IEmailNotificationsFeature>().Should().BeOfType<EmailNotificationsFeature>();
        provider.GetRequiredService<IEmailMetrics>().Should().BeOfType<EmailMetrics>();
        scopedServices.GetRequiredService<IEmailTemplateComposerFactory>().Should().BeOfType<EmailTemplateComposerFactory>();
        provider.GetRequiredService<IMapperRegistry>().Should().BeOfType<MapperRegistry>();
        scopedServices.GetRequiredService<IQueryPaginationService>().Should().BeOfType<QueryPaginationFacade>();
        provider.GetRequiredService<LocalPhotoDevelopmentStore>().Should().NotBeNull();
        provider.GetRequiredService<InMemoryPhotoUploadInitTracker>().Should().NotBeNull();
        scopedServices.GetRequiredService<IPhotoUploadInitTracker>().Should().BeOfType<DbPhotoUploadInitTracker>();
        scopedServices.GetRequiredService<IPhotoStorageProvider>().Should().BeOfType(expectedPhotoStorageProviderType);
        scopedServices.GetRequiredService<IPushProviderSender>().Should().BeOfType<FcmPushSender>();
        scopedServices.GetRequiredService<ITokenService>().Should().BeOfType<TokenService>();
        scopedServices.GetRequiredService<IGoogleTokenValidator>().Should().BeOfType<GoogleTokenValidator>();
        scopedServices.GetRequiredService<ILegacyPasswordService>().Should().BeOfType<LegacyPasswordService>();
        scopedServices.GetRequiredService<IUserSessionStore>().Should().BeOfType<UserSessionStore>();
    }

    [Test]
    public void AddInfrastructure_Throws_WhenDeliveryModeInvalid()
    {
        var services = new ServiceCollection();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["Email:DeliveryMode"] = "something-else";

        var action = () =>
            services.AddInfrastructure(TestConfigurationBuilder.BuildConfiguration(values), enableSensitiveLogging: false, isTesting: true);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Email:DeliveryMode must be one of: Smtp, Dummy.");
        services.Should().BeEmpty();
    }

    [TestCase("Unsupported", "Unsupported photo storage provider: Unsupported")]
    [TestCase("CloudflareR2", "PhotoStorage:BucketName is required for CloudflareR2.")]
    public void AddInfrastructure_RejectsInvalidPhotoProviderDuringRegistration(
        string providerName,
        string expectedMessage)
    {
        var services = new ServiceCollection();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["PhotoStorage:Provider"] = providerName;
        if (providerName == "CloudflareR2")
        {
            values["PhotoStorage:BucketName"] = "";
            values["PhotoStorage:Endpoint"] = "https://example.r2.cloudflarestorage.com";
            values["PhotoStorage:AccessKeyId"] = "test-access-key";
            values["PhotoStorage:SecretAccessKey"] = "test-secret-key";
        }

        var action = () => services.AddInfrastructure(
            TestConfigurationBuilder.BuildConfiguration(values),
            enableSensitiveLogging: false,
            isTesting: true);

        action.Should().Throw<InvalidOperationException>().WithMessage(expectedMessage);
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IAppConfigRepository));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IPhotoStorageProvider));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IPushProviderSender));
    }

    [Test]
    public void AddInfrastructure_Throws_WhenDummyOutputDirectoryMissingInDummyMode()
    {
        var services = new ServiceCollection();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["Email:DeliveryMode"] = "Dummy";
        values["Email:DummyOutputDirectory"] = "   ";

        var action = () =>
            services.AddInfrastructure(TestConfigurationBuilder.BuildConfiguration(values), enableSensitiveLogging: false, isTesting: true);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Email:DummyOutputDirectory is required when Email:DeliveryMode is Dummy.");
    }

    [TestCase(null, "Email:InvitationBaseUrl is required.")]
    [TestCase("not-an-url", "Email:InvitationBaseUrl must be a valid absolute URL.")]
    public void AddInfrastructure_Throws_ForInvalidInvitationBaseUrl(string? invitationBaseUrl, string expectedMessage)
    {
        var services = new ServiceCollection();
        var configuration = TestConfigurationBuilder.BuildEnabledEmailConfiguration();
        var values = new Dictionary<string, string?>(configuration.AsEnumerable().ToDictionary(k => k.Key, v => v.Value))
        {
            ["Email:InvitationBaseUrl"] = invitationBaseUrl
        };

        var action = () =>
            services.AddInfrastructure(TestConfigurationBuilder.BuildConfiguration(values), enableSensitiveLogging: false, isTesting: true);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(expectedMessage);
    }

    [Test]
    public void AddInfrastructure_Throws_WhenTemplateRootPathMissing()
    {
        var services = new ServiceCollection();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["Email:TemplateRootPath"] = "";

        var action = () =>
            services.AddInfrastructure(TestConfigurationBuilder.BuildConfiguration(values), enableSensitiveLogging: false, isTesting: true);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Email:TemplateRootPath is required when email is enabled.");
    }

    [Test]
    public void AddInfrastructure_Throws_WhenFromAddressInvalid()
    {
        var services = new ServiceCollection();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["Email:FromAddress"] = "invalid-email";

        var action = () =>
            services.AddInfrastructure(TestConfigurationBuilder.BuildConfiguration(values), enableSensitiveLogging: false, isTesting: true);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Email:FromAddress must be a valid email address.");
    }

    [Test]
    public void AddInfrastructure_Throws_WhenSmtpHostMissing()
    {
        var services = new ServiceCollection();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["Email:SmtpHost"] = "";

        var action = () =>
            services.AddInfrastructure(TestConfigurationBuilder.BuildConfiguration(values), enableSensitiveLogging: false, isTesting: true);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Email:SmtpHost is required when email is enabled.");
    }

    [Test]
    public void AddInfrastructure_Throws_WhenSmtpPortNonPositive()
    {
        var services = new ServiceCollection();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["Email:SmtpPort"] = "0";

        var action = () =>
            services.AddInfrastructure(TestConfigurationBuilder.BuildConfiguration(values), enableSensitiveLogging: false, isTesting: true);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Email:SmtpPort must be greater than 0 when email is enabled.");
    }

    [Test]
    public void AddBackgroundWorkerServices_RegistersCommandDispatcher()
    {
        var configuration = TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["PhotoStorage:Provider"] = "CloudflareR2",
            ["PhotoStorage:BucketName"] = "lgym-report-photos-dev",
            ["PhotoStorage:Endpoint"] = "https://38c1c25f99af223efee28a9afcf5d575.r2.cloudflarestorage.com",
            ["PhotoStorage:AccessKeyId"] = "test-access-key",
            ["PhotoStorage:SecretAccessKey"] = "test-secret-key"
        });

        using var provider = TestServiceProviderFactory.CreateInfrastructureProvider(
            configuration,
            isTesting: true,
            includeBackgroundWorker: true);
        var dispatcher = provider.GetRequiredService<ApplicationCommandDispatcher>();
        dispatcher.Should().BeOfType<CommandDispatcher>();
    }

    [Test]
    public void AddBackgroundWorkerServices_RegistersNoOpSchedulersAndRetainsApplicationBridge_WhenTesting()
    {
        var services = new ServiceCollection();
        var configuration = TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test"
        });

        services.AddLogging();
        services.AddInfrastructure(configuration, enableSensitiveLogging: false, isTesting: true);
        services.AddNotificationsModule(configuration);
        services.AddBackgroundWorkerServices(isTesting: true);
        services.AddScoped<IInAppNotificationPushPublisher, FakeInAppNotificationPushPublisher>();

        using var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IActionMessageScheduler>();
        scheduler.Should().BeOfType<NoOpActionMessageScheduler>();
        provider.GetRequiredService<IPushBackgroundScheduler>().Should().BeOfType<LgymApi.BackgroundWorker.Services.NoOpPushBackgroundScheduler>();
        provider.GetRequiredService<INotificationEventBridge>().Should().BeOfType<NotificationEventBridge>();
    }

    [Test]
    public void AddBackgroundWorkerServices_DoesNotDuplicateNotificationInfrastructureRegistrations()
    {
        var services = new ServiceCollection();
        var configuration = TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["PhotoStorage:Provider"] = "CloudflareR2",
            ["PhotoStorage:BucketName"] = "lgym-report-photos-dev",
            ["PhotoStorage:Endpoint"] = "https://38c1c25f99af223efee28a9afcf5d575.r2.cloudflarestorage.com",
            ["PhotoStorage:AccessKeyId"] = "test-access-key",
            ["PhotoStorage:SecretAccessKey"] = "test-secret-key"
        });

        services.AddLogging();
        services.AddInfrastructure(configuration, enableSensitiveLogging: false, isTesting: true);
        services.AddBackgroundWorkerServices(isTesting: true);
        services.AddScoped<IInAppNotificationPushPublisher, FakeInAppNotificationPushPublisher>();

        services.Count(descriptor => descriptor.ServiceType == typeof(IPushInstallationRepository)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IPushNotificationMessageRepository)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IInAppNotificationRepository)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IPushBackgroundScheduler)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IPushProviderSender)).Should().Be(1);
    }

    [Test]
    public void AddNotificationsModule_RegistersApplicationAndInfrastructureWithoutWorkerScheduler()
    {
        var services = new ServiceCollection();
        var configuration = TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["PhotoStorage:Provider"] = "CloudflareR2",
            ["PhotoStorage:BucketName"] = "lgym-report-photos-dev",
            ["PhotoStorage:Endpoint"] = "https://38c1c25f99af223efee28a9afcf5d575.r2.cloudflarestorage.com",
            ["PhotoStorage:AccessKeyId"] = "test-access-key",
            ["PhotoStorage:SecretAccessKey"] = "test-secret-key"
        });

        services.AddNotificationsModule(configuration);

        services.Count(descriptor => descriptor.ServiceType == typeof(IInAppNotificationService)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(INotificationEventBridge)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IPushInstallationRepository)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IPushNotificationMessageRepository)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IInAppNotificationRepository)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IPushBackgroundScheduler)).Should().Be(0);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void FullHostComposition_RetainsApplicationNotificationBridge(bool isTesting)
    {
        var services = new ServiceCollection();
        var configuration = TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test"
        });

        services.AddNotificationsModule(configuration);
        services.AddBackgroundWorkerServices(isTesting);

        services.Count(descriptor => descriptor.ServiceType == typeof(IInAppNotificationService)).Should().Be(1);
        var bridge = services
            .Where(descriptor => descriptor.ServiceType == typeof(INotificationEventBridge))
            .Should()
            .ContainSingle()
            .Which;
        bridge.ImplementationType.Should().Be(typeof(NotificationEventBridge));
    }

    [TestCase(true, typeof(NoOpEmailBackgroundScheduler))]
    [TestCase(false, typeof(HangfireEmailBackgroundScheduler))]
    public void FullHostComposition_ResolvesCoachingEmailPortsExactlyOnce(
        bool isTesting,
        Type expectedBackgroundScheduler)
    {
        var services = new ServiceCollection();
        var configuration = TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["PhotoStorage:Provider"] = "CloudflareR2",
            ["PhotoStorage:BucketName"] = "lgym-report-photos-dev",
            ["PhotoStorage:Endpoint"] = "https://38c1c25f99af223efee28a9afcf5d575.r2.cloudflarestorage.com",
            ["PhotoStorage:AccessKeyId"] = "test-access-key",
            ["PhotoStorage:SecretAccessKey"] = "test-secret-key"
        });

        services.AddLogging();
        services.AddInfrastructure(configuration, enableSensitiveLogging: false, isTesting);
        services.AddNotificationsModule(configuration);
        services.AddBackgroundWorkerServices(isTesting);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var scopedServices = scope.ServiceProvider;

        scopedServices.GetServices<ICoachingEmailNotificationFeature>().Should().ContainSingle()
            .Which.Should().BeOfType<CoachingEmailNotificationSchedulerAdapter>();
        scopedServices.GetServices<ICoachingEmailNotificationScheduler>().Should().ContainSingle()
            .Which.Should().BeOfType<CoachingEmailNotificationSchedulerAdapter>();
        scopedServices.GetRequiredService<IEmailBackgroundScheduler>().Should().BeOfType(expectedBackgroundScheduler);
    }

    [TestCaseSource(nameof(FullHostPushCompositionManifest))]
    public void FullHostPushComposition_CharacterizesCurrentDescriptorsAndKeepsFutureOwnershipManifest(PushCompositionManifest expectation)
    {
        var services = new ServiceCollection();
        var configuration = TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test"
        });

        services.AddNotificationsModule(configuration);
        services.AddBackgroundWorkerServices(expectation.IsTesting);

        var schedulerDescriptor = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPushBackgroundScheduler))
            .Should()
            .ContainSingle()
            .Which;
        var providerDescriptor = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPushProviderSender))
            .Should()
            .ContainSingle()
            .Which;

        schedulerDescriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
        schedulerDescriptor.ImplementationType.Should().Be(expectation.CurrentSchedulerImplementation);
        providerDescriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
        providerDescriptor.ImplementationType.Should().Be(expectation.CurrentProviderImplementation);
        expectation.FutureSchedulerSelector.Should().Be("Worker");
        expectation.FutureProviderImplementationOwner.Should().Be("Infrastructure");
    }

    [TestCase(
        true,
        typeof(NoOpEmailBackgroundScheduler),
        typeof(NoOpActionMessageScheduler),
        typeof(LgymApi.BackgroundWorker.Services.NoOpPushBackgroundScheduler))]
    [TestCase(
        false,
        typeof(HangfireEmailBackgroundScheduler),
        typeof(HangfireActionMessageScheduler),
        typeof(LgymApi.BackgroundWorker.Services.HangfirePushBackgroundScheduler))]
    public void AddBackgroundWorkerServices_RegistersAndResolvesExactEnvironmentSchedulers(
        bool isTesting,
        Type expectedEmailSchedulerType,
        Type expectedActionSchedulerType,
        Type expectedPushSchedulerType)
    {
        var services = new ServiceCollection();
        var configuration = CreateInfrastructureConfiguration(isTesting ? "Local" : "CloudflareR2");

        services.AddLogging();
        services.AddInfrastructure(configuration, enableSensitiveLogging: false, isTesting);
        services.AddBackgroundWorkerServices(isTesting);

        AssertTypeDescriptor(services, typeof(IEmailBackgroundScheduler), expectedEmailSchedulerType, ServiceLifetime.Scoped);
        AssertTypeDescriptor(services, typeof(IActionMessageScheduler), expectedActionSchedulerType, ServiceLifetime.Scoped);
        AssertTypeDescriptor(services, typeof(IPushBackgroundScheduler), expectedPushSchedulerType, ServiceLifetime.Scoped);
        if (isTesting)
        {
            services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(JobStorage));
        }
        else
        {
            AssertFactoryDescriptor<JobStorage>(services, ServiceLifetime.Singleton);
        }

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var scopedServices = scope.ServiceProvider;

        scopedServices.GetRequiredService<IEmailBackgroundScheduler>().Should().BeOfType(expectedEmailSchedulerType);
        scopedServices.GetRequiredService<IActionMessageScheduler>().Should().BeOfType(expectedActionSchedulerType);
        scopedServices.GetRequiredService<IPushBackgroundScheduler>().Should().BeOfType(expectedPushSchedulerType);

        if (isTesting)
        {
            provider.GetService<JobStorage>().Should().BeNull();
        }
        else
        {
            provider.GetRequiredService<JobStorage>().GetType().FullName.Should().Be("Hangfire.PostgreSql.PostgreSqlStorage");
        }
    }

    [Test]
    public void AddInfrastructure_Throws_WhenPushSendsEnabledWithoutProjectId()
    {
        var services = new ServiceCollection();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["PushNotifications:SendEnabled"] = "true";
        values["PushNotifications:Fcm:CredentialsJson"] = "{ }";

        var action = () => services.AddInfrastructure(TestConfigurationBuilder.BuildConfiguration(values), enableSensitiveLogging: false, isTesting: true);

        action.Should()
             .Throw<InvalidOperationException>()
             .WithMessage("PushNotifications:Fcm:ProjectId is required when push notifications are enabled.");
    }

    [Test]
    public void AddInfrastructure_DoesNotRequireFcmCredentials_WhenPushSendsDisabled()
    {
        var services = new ServiceCollection();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["PushNotifications:SendEnabled"] = "false";
        values["PushNotifications:StaleTokenCleanupEnabled"] = "true";

        var action = () => services.AddInfrastructure(TestConfigurationBuilder.BuildConfiguration(values), enableSensitiveLogging: false, isTesting: true);

        action.Should().NotThrow();
    }

    [Test]
    public void AddBackgroundWorkerServices_RegistersHangfireScheduler_WhenNotTesting()
    {
        var services = new ServiceCollection();
        var configuration = TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["PhotoStorage:Provider"] = "CloudflareR2",
            ["PhotoStorage:BucketName"] = "lgym-report-photos-dev",
            ["PhotoStorage:Endpoint"] = "https://38c1c25f99af223efee28a9afcf5d575.r2.cloudflarestorage.com",
            ["PhotoStorage:AccessKeyId"] = "test-access-key",
            ["PhotoStorage:SecretAccessKey"] = "test-secret-key"
        });

        services.AddLogging();
        services.AddInfrastructure(configuration, enableSensitiveLogging: false, isTesting: false);
        services.AddBackgroundWorkerServices(isTesting: false);

        using var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IActionMessageScheduler>();
        scheduler.Should().BeOfType<HangfireActionMessageScheduler>();
    }

    [Test]
    public void AddBackgroundWorkerServices_RegistersOrchestratorService()
    {
        var configuration = TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test"
        });

        using var provider = TestServiceProviderFactory.CreateInfrastructureProvider(
            configuration,
            isTesting: true,
            includeBackgroundWorker: true);
        var orchestrator = provider.GetRequiredService<BackgroundActionOrchestratorService>();
        orchestrator.Should().NotBeNull();
    }

    [Test]
    public void AddBackgroundWorkerServices_RegistersCommittedIntentDispatchJob()
    {
        var configuration = TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["PhotoStorage:Provider"] = "CloudflareR2",
            ["PhotoStorage:BucketName"] = "lgym-report-photos-dev",
            ["PhotoStorage:Endpoint"] = "https://38c1c25f99af223efee28a9afcf5d575.r2.cloudflarestorage.com",
            ["PhotoStorage:AccessKeyId"] = "test-access-key",
            ["PhotoStorage:SecretAccessKey"] = "test-secret-key"
        });

        using var provider = TestServiceProviderFactory.CreateInfrastructureProvider(
            configuration,
            isTesting: true,
            includeBackgroundWorker: true);

        var job = provider.GetRequiredService<ICommittedIntentDispatchJob>();
        job.Should().BeOfType<LgymApi.BackgroundWorker.Jobs.CommittedIntentDispatchJob>();
    }

    [Test]
    public void AddBackgroundWorkerServices_RegistersInvitationInAppNotificationHandlers()
    {
        var services = new ServiceCollection();
        var configuration = TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["PhotoStorage:Provider"] = "CloudflareR2",
            ["PhotoStorage:BucketName"] = "lgym-report-photos-dev",
            ["PhotoStorage:Endpoint"] = "https://38c1c25f99af223efee28a9afcf5d575.r2.cloudflarestorage.com",
            ["PhotoStorage:AccessKeyId"] = "test-access-key",
            ["PhotoStorage:SecretAccessKey"] = "test-secret-key"
        });

        services.AddLogging();
        services.AddApplicationMapping(typeof(IMappingProfile).Assembly);
        services.AddIdentityModule();
        services.AddCoachingModule();
        services.AddInfrastructure(configuration, enableSensitiveLogging: false, isTesting: true);
        services.AddNotificationsModule(configuration);
        services.AddBackgroundWorkerServices(isTesting: true);
        services.AddScoped<IInAppNotificationPushPublisher, FakeInAppNotificationPushPublisher>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBackgroundAction<TrainerInvitationCreatedInAppNotificationCommand>>()
            .Should().BeOfType<TrainerInvitationCreatedInAppNotificationCommandHandler>();
        provider.GetRequiredService<IBackgroundAction<TrainerInvitationAcceptedInAppNotificationCommand>>()
            .Should().BeOfType<TrainerInvitationAcceptedInAppNotificationCommandHandler>();
        provider.GetRequiredService<IBackgroundAction<TrainerInvitationRejectedInAppNotificationCommand>>()
            .Should().BeOfType<TrainerInvitationRejectedInAppNotificationCommandHandler>();
    }

    [Test]
    public void AddInfrastructure_RegistersConfiguredAppDefaults()
    {
        var services = new ServiceCollection();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["AppDefaults:PreferredLanguage"] = "pl-PL";
        values["AppDefaults:PreferredTimeZone"] = "UTC";
        var configuration = TestConfigurationBuilder.BuildConfiguration(values);

        services.AddInfrastructure(configuration, enableSensitiveLogging: false, isTesting: true);

        using var provider = services.BuildServiceProvider();
        var defaults = provider.GetRequiredService<AppDefaultsOptions>();

        defaults.PreferredLanguage.Should().Be("pl-PL");
        defaults.PreferredTimeZone.Should().Be("UTC");
    }

    [Test]
    public void AddInfrastructure_UsesLocalPhotoStorageProvider_WhenTestingAndProviderLocal()
    {
        var services = new ServiceCollection();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["PhotoStorage:Provider"] = "Local";
        var configuration = TestConfigurationBuilder.BuildConfiguration(values);

        services.AddInfrastructure(configuration, enableSensitiveLogging: false, isTesting: true);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().Should().NotBeNull();
        provider.GetRequiredService<IPhotoStorageProvider>().Should().BeOfType<LocalPhotoStorageProvider>();
    }

    [Test]
    public void AddInfrastructure_UsesCloudflareR2PhotoStorageProvider_WhenConfigured()
    {
        var services = new ServiceCollection();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["PhotoStorage:Provider"] = "CloudflareR2";
        values["PhotoStorage:BucketName"] = "lgym-report-photos-dev";
        values["PhotoStorage:Endpoint"] = "https://38c1c25f99af223efee28a9afcf5d575.r2.cloudflarestorage.com";
        values["PhotoStorage:AccessKeyId"] = "test-access-key";
        values["PhotoStorage:SecretAccessKey"] = "test-secret-key";
        var configuration = TestConfigurationBuilder.BuildConfiguration(values);

        services.AddInfrastructure(configuration, enableSensitiveLogging: false, isTesting: true);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IPhotoStorageProvider>().Should().BeOfType<CloudflareR2PhotoStorageProvider>();
    }

    [Test]
    public void AddInfrastructure_Throws_WhenProviderLocalOutsideDevelopmentOrTesting()
    {
        var services = new ServiceCollection();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["PhotoStorage:Provider"] = "Local";
        var configuration = TestConfigurationBuilder.BuildConfiguration(values);

        var action = () => services.AddInfrastructure(configuration, enableSensitiveLogging: false, isTesting: false);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("LocalPhotoStorageProvider cannot be used outside Development.");
    }

    private static IConfiguration CreateInfrastructureConfiguration(string photoStorageProvider)
    {
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["PhotoStorage:Provider"] = photoStorageProvider;
        values["PhotoStorage:BucketName"] = "lgym-report-photos-test";
        values["PhotoStorage:Endpoint"] = "https://example.r2.cloudflarestorage.com";
        values["PhotoStorage:AccessKeyId"] = "test-access-key";
        values["PhotoStorage:SecretAccessKey"] = "test-secret-key";

        return TestConfigurationBuilder.BuildConfiguration(values);
    }

    private static void AssertTypeDescriptor<TService, TImplementation>(
        IServiceCollection services,
        ServiceLifetime expectedLifetime)
    {
        AssertTypeDescriptor(services, typeof(TService), typeof(TImplementation), expectedLifetime);
    }

    private static void AssertTypeDescriptor(
        IServiceCollection services,
        Type serviceType,
        Type implementationType,
        ServiceLifetime expectedLifetime)
    {
        var descriptor = GetSingleDescriptor(services, serviceType);
        descriptor.Lifetime.Should().Be(expectedLifetime);
        descriptor.ImplementationType.Should().Be(implementationType);
        descriptor.ImplementationFactory.Should().BeNull();
        descriptor.ImplementationInstance.Should().BeNull();
    }

    private static void AssertFactoryDescriptor<TService>(
        IServiceCollection services,
        ServiceLifetime expectedLifetime)
    {
        var descriptor = GetSingleDescriptor(services, typeof(TService));
        descriptor.Lifetime.Should().Be(expectedLifetime);
        descriptor.ImplementationFactory.Should().NotBeNull();
        descriptor.ImplementationType.Should().BeNull();
        descriptor.ImplementationInstance.Should().BeNull();
    }

    private static void AssertInstanceDescriptor<TService>(
        IServiceCollection services,
        ServiceLifetime expectedLifetime)
    {
        var descriptor = GetSingleDescriptor(services, typeof(TService));
        descriptor.Lifetime.Should().Be(expectedLifetime);
        descriptor.ImplementationInstance.Should().NotBeNull();
        descriptor.ImplementationType.Should().BeNull();
        descriptor.ImplementationFactory.Should().BeNull();
    }

    private static ServiceDescriptor GetSingleDescriptor(IServiceCollection services, Type serviceType)
    {
        return services
            .Where(descriptor => descriptor.ServiceType == serviceType)
            .Should()
            .ContainSingle()
            .Which;
    }

    private sealed class FakeInAppNotificationPushPublisher : IInAppNotificationPushPublisher
    {
        public Task PushAsync(InAppNotificationResult notification, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }

    private static IEnumerable<TestCaseData> FullHostPushCompositionManifest()
    {
        yield return new TestCaseData(new PushCompositionManifest(
            true,
            typeof(LgymApi.BackgroundWorker.Services.NoOpPushBackgroundScheduler),
            typeof(FcmPushSender),
            "Worker",
            "Infrastructure"));
        yield return new TestCaseData(new PushCompositionManifest(
            false,
            typeof(LgymApi.BackgroundWorker.Services.HangfirePushBackgroundScheduler),
            typeof(FcmPushSender),
            "Worker",
            "Infrastructure"));
    }

    public sealed record PushCompositionManifest(
        bool IsTesting,
        Type CurrentSchedulerImplementation,
        Type CurrentProviderImplementation,
        string FutureSchedulerSelector,
        string FutureProviderImplementationOwner);

    [Test]
    public void AddInfrastructure_FallsBackAppDefaults_WhenConfigurationInvalid()
    {
        var services = new ServiceCollection();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["AppDefaults:PreferredLanguage"] = "@@invalid-culture@@";
        values["AppDefaults:PreferredTimeZone"] = "Not/ARealTimeZone";
        var configuration = TestConfigurationBuilder.BuildConfiguration(values);

        services.AddInfrastructure(configuration, enableSensitiveLogging: false, isTesting: true);

        using var provider = services.BuildServiceProvider();
        var defaults = provider.GetRequiredService<AppDefaultsOptions>();

        defaults.PreferredLanguage.Should().Be("en-US");
        defaults.PreferredTimeZone.Should().Be("Europe/Warsaw");
    }

    [Test]
    public void AddInfrastructure_UsesAppDefaultLanguage_WhenEmailDefaultCultureInvalid()
    {
        var services = new ServiceCollection();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["AppDefaults:PreferredLanguage"] = "pl-PL";
        values["Email:DefaultCulture"] = "@@invalid-culture@@";
        var configuration = TestConfigurationBuilder.BuildConfiguration(values);

        services.AddInfrastructure(configuration, enableSensitiveLogging: false, isTesting: true);

        using var provider = services.BuildServiceProvider();
        var emailOptions = provider.GetRequiredService<EmailOptions>();

        emailOptions.DefaultCulture.Name.Should().Be("pl-PL");
    }

}
