using LgymApi.Application.Repositories;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using IIdentityPersistenceContext = LgymApi.Identity.Persistence.IIdentityPersistenceContext;
using INotificationsPersistenceContext = LgymApi.Notifications.Persistence.INotificationsPersistenceContext;
using IPlatformPersistenceContext = LgymApi.Platform.Persistence.IPlatformPersistenceContext;
using ICommandEnvelopeDuplicateFailureClassifier = LgymApi.Platform.Persistence.ICommandEnvelopeDuplicateFailureClassifier;
using ITrainingPlanningPersistenceContext = LgymApi.TrainingPlanning.Persistence.ITrainingPlanningPersistenceContext;

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
        services.AddScoped<IPlatformPersistenceContext>(static provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IIdentityPersistenceContext>(static provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<ITrainingPlanningPersistenceContext>(static provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<INotificationsPersistenceContext>(static provider => provider.GetRequiredService<AppDbContext>());
    }

    private static void AddPlatformUnitOfWork(IServiceCollection services)
    {
        services.AddScoped<ICommandEnvelopeDuplicateFailureClassifier, CommandRuntime.NpgsqlCommandEnvelopeDuplicateFailureClassifier>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
    }
}
