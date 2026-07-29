using FluentAssertions;
using Hangfire;
using LgymApi.Application.Abstractions.Storage;
using LgymApi.Application.Coaching.ManagedPlans;
using LgymApi.Application.Platform.ReferenceData.AppConfig;
using LgymApi.Application.Platform.ReferenceData.AppConfig.Contracts;
using LgymApi.Application.Platform.ReferenceData.Enums;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Application.Features.PasswordReset.Contracts;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Application.Pagination;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Application.Services;
using LgymApi.Application.WorkoutProgress.Adapters;
using LgymApi.BackgroundWorker;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Notifications;
using LgymApi.BackgroundWorker.Runtime;
using LgymApi.BackgroundWorker.Services;
using LgymApi.Notifications;
using LgymApi.Domain.Enums;
using LgymApi.Infrastructure.Pagination;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.Repositories.Reporting;
using LgymApi.Infrastructure.Repositories.ReferenceData;
using LgymApi.Infrastructure.Services;
using LgymApi.Infrastructure.UnitOfWork;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ApplicationMapper = LgymApi.Application.Mapping.Core.IMapper;
using QueryPaginationFacade = LgymApi.Infrastructure.Pagination.QueryPaginationService;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class CompositionRootStartupTests : IntegrationTestBase
{
    [Test]
    public void Worker_Facade_Should_Remain_The_Composition_Entry_Point()
    {
        typeof(BackgroundWorkerRecurringJobs).Assembly.GetName().Name.Should().Be("LgymApi.BackgroundWorker");
    }

    [Test]
    public void TestingApiHost_ResolvesCanonicalPlatformAndProviderServices()
    {
        using var scope = Factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        services.GetServices<IAppConfigService>().Should().ContainSingle()
            .Which.Should().BeOfType<AppConfigService>();
        services.GetServices<IAppConfigAuthorizationPort>().Should().ContainSingle()
            .Which.GetType().FullName.Should().Be("LgymApi.Application.Identity.Adapters.AppConfigAuthorizationAdapter");
        services.GetServices<IEnumService>().Should().ContainSingle()
            .Which.Should().BeOfType<EnumService>();
        services.GetServices<IUnitConverter<WeightUnits>>().Should().ContainSingle()
            .Which.Should().BeOfType<LinearUnitConverter<WeightUnits>>();
        services.GetServices<IUnitConverter<HeightUnits>>().Should().ContainSingle()
            .Which.Should().BeOfType<LinearUnitConverter<HeightUnits>>();
        services.GetServices<IAppConfigRepository>().Should().ContainSingle()
            .Which.Should().BeOfType<AppConfigRepository>();
        services.GetServices<ICommittedIntentDispatcher>().Should().ContainSingle()
            .Which.Should().BeOfType<CommittedIntentDispatcher>();
        services.GetServices<IUnitOfWork>().Should().ContainSingle()
            .Which.Should().BeOfType<EfUnitOfWork>();
        services.GetServices<ICommandEnvelopeRepository>().Should().ContainSingle()
            .Which.Should().BeOfType<CommandEnvelopeRepository>();
        services.GetServices<IApiIdempotencyRecordRepository>().Should().ContainSingle()
            .Which.Should().BeOfType<ApiIdempotencyRecordRepository>();
        services.GetServices<IEmailNotificationsFeature>().Should().ContainSingle()
            .Which.Should().BeOfType<EmailNotificationsFeature>();
        services.GetServices<IEmailMetrics>().Should().ContainSingle()
            .Which.Should().BeOfType<EmailMetrics>();
        services.GetServices<IEmailTemplateComposerFactory>().Should().ContainSingle()
            .Which.Should().BeOfType<EmailTemplateComposerFactory>();
        services.GetServices<ApplicationMapper>().Should().ContainSingle()
            .Which.Should().BeOfType<Mapper>();
        services.GetServices<IMapperRegistry>().Should().ContainSingle()
            .Which.Should().BeOfType<MapperRegistry>();
        services.GetServices<IQueryPaginationService>().Should().ContainSingle()
            .Which.Should().BeOfType<QueryPaginationFacade>();
        services.GetServices<PaginationPolicy>().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new PaginationPolicy
            {
                MaxPageSize = 100,
                DefaultPageSize = 20,
                DefaultSortField = "id",
                TieBreakerField = "id"
            });
        services.GetServices<CommandContractRegistry>().Should().ContainSingle();
        services.GetServices<ICommandDispatcher>().Should().ContainSingle()
            .Which.Should().BeOfType<CommandDispatcher>();
        services.GetServices<IPasswordRecoveryEmailScheduler>().Should().ContainSingle()
            .Which.Should().BeOfType<PasswordRecoveryEmailSchedulerAdapter>();
        services.GetServices<ICoachingEmailNotificationFeature>().Should().ContainSingle()
            .Which.Should().BeOfType<CoachingEmailNotificationSchedulerAdapter>();
        services.GetServices<ICoachingEmailNotificationScheduler>().Should().ContainSingle()
            .Which.Should().BeOfType<CoachingEmailNotificationSchedulerAdapter>();
        services.GetServices<IPushBackgroundScheduler>().Should().ContainSingle()
            .Which.Should().BeOfType<NoOpPushBackgroundScheduler>();
        services.GetServices<IPushProviderSender>().Should().ContainSingle()
            .Which.Should().BeSameAs(Factory.PushSender);
        services.GetServices<IPhotoStorageProvider>().Should().ContainSingle()
            .Which.Should().BeOfType<LocalPhotoStorageProvider>();
        services.GetServices<IReportPhotoPersistence>().Should().ContainSingle()
            .Which.Should().BeOfType<ReportPhotoPersistenceRepository>();
        services.GetServices<ITokenService>().Should().ContainSingle()
            .Which.Should().BeOfType<TokenService>();
        services.GetServices<IGoogleTokenValidator>().Should().ContainSingle()
            .Which.Should().BeOfType<GoogleTokenValidator>();
        services.GetServices<ILegacyPasswordService>().Should().ContainSingle()
            .Which.Should().BeOfType<LegacyPasswordService>();
        services.GetServices<IUserSessionStore>().Should().ContainSingle()
            .Which.Should().BeOfType<UserSessionStore>();
        services.GetServices<LgymApi.Application.Identity.Contracts.Sessions.IAccountSessionDisassociationPort>()
            .Should()
            .ContainSingle()
            .Which
            .GetType()
            .FullName
            .Should()
            .Be("LgymApi.Application.Notifications.Adapters.PushInstallationSessionDisassociationAdapter");
        services.GetServices<IEmailSender>().Should().ContainSingle()
            .Which.Should().BeSameAs(Factory.EmailSender);
        var emailTemplateComposers = services.GetServices<IEmailTemplateComposer>().ToArray();
        emailTemplateComposers.Should().HaveCount(6);
        emailTemplateComposers.Select(composer => composer.GetType()).Should().OnlyHaveUniqueItems();
        var mappingProfiles = services.GetServices<IMappingProfile>().ToArray();
        mappingProfiles.Should().HaveCount(44);
        mappingProfiles.Select(profile => profile.GetType()).Should().OnlyHaveUniqueItems();
        mappingProfiles.OfType<EnumLookupMappingProfile>().Should().ContainSingle();
        mappingProfiles.OfType<PlanExerciseWorkoutAdapterMappingProfile>().Should().ContainSingle();
        mappingProfiles.OfType<ManagedPlanCollaborationMappingProfile>().Should().ContainSingle();
        services.GetService<JobStorage>().Should().BeNull();
    }

    [Test]
    public async Task TestingApiHost_StartsWithTestingAndProductionSafeProviderConfigurations()
    {
        using var baseFactory = new CustomWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Email:DeliveryMode", "Smtp");
            builder.UseSetting("PhotoStorage:Provider", "CloudflareR2");
            builder.UseSetting("PhotoStorage:BucketName", "lgym-report-photos-test");
            builder.UseSetting("PhotoStorage:Endpoint", "https://example.r2.cloudflarestorage.com");
            builder.UseSetting("PhotoStorage:AccessKeyId", "test-access-key");
            builder.UseSetting("PhotoStorage:SecretAccessKey", "test-secret-key");
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetServices<IEmailSender>().Should().ContainSingle()
            .Which.Should().BeSameAs(baseFactory.EmailSender);
        scope.ServiceProvider.GetServices<IPhotoStorageProvider>().Should().ContainSingle()
            .Which.Should().BeOfType<CloudflareR2PhotoStorageProvider>();
        scope.ServiceProvider.GetServices<IPushBackgroundScheduler>().Should().ContainSingle()
            .Which.Should().BeOfType<NoOpPushBackgroundScheduler>();
        scope.ServiceProvider.GetServices<IPushProviderSender>().Should().ContainSingle()
            .Which.Should().BeSameAs(baseFactory.PushSender);
        scope.ServiceProvider.GetService<JobStorage>().Should().BeNull();
    }

    [TestCase("Email:DeliveryMode", "invalid", "Email:DeliveryMode must be one of: Smtp, Dummy.")]
    [TestCase("PhotoStorage:Provider", "Unsupported", "Unsupported photo storage provider: Unsupported")]
    public void TestingApiHost_RejectsInvalidProviderConfigurationDuringStartup(
        string configurationKey,
        string configurationValue,
        string expectedMessage)
    {
        using var baseFactory = new CustomWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.UseSetting(configurationKey, configurationValue));

        var action = () =>
        {
            using var client = factory.CreateClient();
        };

        action.Should().Throw<Exception>()
            .Which.ToString().Should().Contain(expectedMessage);
    }
}
