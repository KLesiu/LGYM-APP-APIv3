using LgymApi.Application.Repositories;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    private static void AddPlatformPersistence(
        IServiceCollection services,
        IConfiguration configuration,
        bool enableSensitiveLogging)
    {
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            options
                .UseLoggerFactory(loggerFactory)
                .UseNpgsql(configuration.GetConnectionString("Postgres"));

            if (enableSensitiveLogging)
            {
                options
                    .EnableSensitiveDataLogging()
                    .EnableDetailedErrors();
            }
        });
    }

    private static void AddPlatformUnitOfWork(IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
    }
}
