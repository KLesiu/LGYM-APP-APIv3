using Hangfire;
using Hangfire.PostgreSql;
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
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(storage =>
                {
                    storage.UseNpgsqlConnection(configuration.GetConnectionString("Postgres"));
                });
        });

        if (hostBackgroundServer)
        {
            services.AddHangfireServer();
        }
    }
}
