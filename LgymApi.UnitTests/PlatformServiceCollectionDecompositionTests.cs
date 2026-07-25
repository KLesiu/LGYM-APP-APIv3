using FluentAssertions;
using Hangfire;
using Hangfire.Logging;
using System.Reflection;
using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Infrastructure;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Pagination;
using LgymApi.Infrastructure.UnitOfWork;
using LgymApi.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.UnitTests;

[TestFixture]
[NonParallelizable]
public sealed class PlatformServiceCollectionDecompositionTests
{
    private static readonly MethodInfo ResolveLogProvider = typeof(LogProvider).GetMethod(
        "ResolveLogProvider",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    [TestCase(true)]
    [TestCase(false)]
    public void AddPlatformServices_Preserves_Baseline_Descriptors_And_Resolves_Providers(bool isTesting)
    {
        var originalLogProvider = (ILogProvider)ResolveLogProvider.Invoke(null, null)!;
        try
        {
            var services = CreateServices();
            var configuration = CreateConfiguration();

            services.AddPlatformServices(configuration, enableSensitiveLogging: false, isTesting, hostBackgroundServer: true);
            AssertPlatformDescriptors(services, isTesting);

            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            using var scope = provider.CreateScope();
            var scopedServices = scope.ServiceProvider;

            scopedServices.GetRequiredService<AppDbContext>().Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
            scopedServices.GetRequiredService<IUnitOfWork>().Should().BeOfType<EfUnitOfWork>();
            scopedServices.GetRequiredService<IAppConfigRepository>().GetType().Name.Should().Be("AppConfigRepository");
            scopedServices.GetRequiredService<ICommittedIntentDispatcher>().GetType().Name.Should().Be("CommittedIntentDispatcher");
            scopedServices.GetRequiredService<ICommandEnvelopeRepository>().GetType().Name.Should().Be("CommandEnvelopeRepository");
            scopedServices.GetRequiredService<IApiIdempotencyRecordRepository>().GetType().Name.Should().Be("ApiIdempotencyRecordRepository");
            scopedServices.GetRequiredService<IQueryPaginationService>().Should().BeOfType<QueryPaginationService>();
            provider.GetRequiredService<IMapperRegistry>().GetType().Name.Should().Be("MapperRegistry");

            if (isTesting)
            {
                provider.GetService<JobStorage>().Should().BeNull();
            }
            else
            {
                provider.GetRequiredService<JobStorage>().GetType().FullName.Should().Be("Hangfire.PostgreSql.PostgreSqlStorage");
            }
        }
        finally
        {
            LogProvider.SetCurrentLogProvider(originalLogProvider);
        }
    }

    [Test]
    public void PlatformDescriptorValidation_Fixture_Should_Reject_Duplicate_DbContext()
    {
        var services = CreateServices();
        services.AddPlatformServices(CreateConfiguration(), enableSensitiveLogging: false, isTesting: true);
        services.AddScoped<AppDbContext>(_ => throw new InvalidOperationException());

        var action = () => AssertPlatformDescriptors(services, isTesting: true);

        action.Should().Throw<InvalidOperationException>().WithMessage("*AppDbContext*exactly once*");
    }

    [Test]
    public void PlatformDescriptorValidation_Fixture_Should_Reject_Duplicate_UnitOfWork()
    {
        var services = CreateServices();
        services.AddPlatformServices(CreateConfiguration(), enableSensitiveLogging: false, isTesting: true);
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        var action = () => AssertPlatformDescriptors(services, isTesting: true);

        action.Should().Throw<InvalidOperationException>().WithMessage("*IUnitOfWork*exactly once*");
    }

    [Test]
    public void AddNotificationsInfrastructure_Validates_EmailOptions_Before_Adding_Descriptors()
    {
        var services = CreateServices();
        var values = TestConfigurationBuilder.ToDictionary(TestConfigurationBuilder.BuildEnabledEmailConfiguration());
        values["Email:DeliveryMode"] = "invalid";

        var action = () => services.AddNotificationsInfrastructure(TestConfigurationBuilder.BuildConfiguration(values));

        action.Should().Throw<InvalidOperationException>().WithMessage("Email:DeliveryMode must be one of: Smtp, Dummy.");
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(Microsoft.Extensions.Logging.ILoggerFactory));
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    private static IConfiguration CreateConfiguration()
    {
        return TestConfigurationBuilder.BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=platform-decomposition;Username=test;Password=test"
        });
    }

    private static void AssertPlatformDescriptors(IServiceCollection services, bool isTesting)
    {
        AssertSingleTypeDescriptor(services, typeof(AppDbContext), ServiceLifetime.Scoped, typeof(AppDbContext));
        AssertSingleTypeDescriptor(services, typeof(IUnitOfWork), ServiceLifetime.Scoped, typeof(EfUnitOfWork));
        AssertSingleFactoryDescriptor(services, typeof(IMapperRegistry), ServiceLifetime.Singleton);
        AssertSingleTypeDescriptor(services, typeof(IQueryPaginationService), ServiceLifetime.Scoped, typeof(QueryPaginationService));
        AssertSingleInstanceDescriptor(services, typeof(PaginationPolicy), ServiceLifetime.Singleton);

        if (isTesting)
        {
            services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(JobStorage));
            return;
        }

        AssertSingleFactoryDescriptor(services, typeof(JobStorage), ServiceLifetime.Singleton);
    }

    private static void AssertSingleTypeDescriptor(
        IServiceCollection services,
        Type serviceType,
        ServiceLifetime lifetime,
        Type implementationType)
    {
        var descriptor = GetSingleDescriptor(services, serviceType);
        if (descriptor.Lifetime != lifetime || descriptor.ImplementationType != implementationType ||
            descriptor.ImplementationFactory is not null || descriptor.ImplementationInstance is not null)
        {
            throw new InvalidOperationException($"Service '{serviceType.Name}' must use one {lifetime} type descriptor for '{implementationType.Name}'.");
        }
    }

    private static void AssertSingleFactoryDescriptor(IServiceCollection services, Type serviceType, ServiceLifetime lifetime)
    {
        var descriptor = GetSingleDescriptor(services, serviceType);
        if (descriptor.Lifetime != lifetime || descriptor.ImplementationFactory is null ||
            descriptor.ImplementationType is not null || descriptor.ImplementationInstance is not null)
        {
            throw new InvalidOperationException($"Service '{serviceType.Name}' must use one {lifetime} factory descriptor.");
        }
    }

    private static void AssertSingleInstanceDescriptor(IServiceCollection services, Type serviceType, ServiceLifetime lifetime)
    {
        var descriptor = GetSingleDescriptor(services, serviceType);
        if (descriptor.Lifetime != lifetime || descriptor.ImplementationInstance is null ||
            descriptor.ImplementationType is not null || descriptor.ImplementationFactory is not null)
        {
            throw new InvalidOperationException($"Service '{serviceType.Name}' must use one {lifetime} instance descriptor.");
        }
    }

    private static ServiceDescriptor GetSingleDescriptor(IServiceCollection services, Type serviceType)
    {
        var descriptors = services.Where(descriptor => descriptor.ServiceType == serviceType).ToArray();
        if (descriptors.Length != 1)
        {
            throw new InvalidOperationException($"Service '{serviceType.Name}' must be registered exactly once; actual count is {descriptors.Length}.");
        }

        return descriptors[0];
    }
}
