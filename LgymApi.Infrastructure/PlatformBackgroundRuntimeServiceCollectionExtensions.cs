using Hangfire;
using Hangfire.PostgreSql;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    private static void AddPlatformBackgroundRuntime(
        IServiceCollection services,
        IConfiguration configuration,
        bool isTesting,
        bool hostBackgroundServer)
    {
        if (isTesting)
        {
            return;
        }

        services.AddHangfire(hangfire =>
        {
            hangfire
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseTypeResolver(ResolvePersistedJobType)
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(storage =>
                {
                    storage.UseNpgsqlConnection(configuration.GetConnectionString("Postgres"));
                }, new PostgreSqlStorageOptions
                {
                    PrepareSchemaIfNecessary = false
                });
        });

        if (hostBackgroundServer)
        {
            services.AddInfrastructureBackgroundServer();
        }
    }

    public static Type ResolvePersistedJobType(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        return typeName switch
        {
            "LgymApi.Domain.ValueObjects.Id`1[[LgymApi.Domain.Entities.CommandEnvelope, LgymApi.Domain]], LgymApi.Domain" => typeof(string),
            "LgymApi.Domain.ValueObjects.Id`1[[LgymApi.Domain.Entities.NotificationMessage, LgymApi.Domain]], LgymApi.Domain" => typeof(string),
            "LgymApi.Domain.ValueObjects.Id`1[[LgymApi.Domain.Entities.PushNotificationMessage, LgymApi.Domain]], LgymApi.Domain" => typeof(string),
            _ => Type.GetType(typeName, throwOnError: true)!
        };
    }

    public static IServiceCollection AddInfrastructureBackgroundServer(this IServiceCollection services)
    {
        services.AddHangfireServer();
        return services;
    }
}
