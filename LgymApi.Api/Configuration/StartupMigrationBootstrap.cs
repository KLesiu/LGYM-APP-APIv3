using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Api.Configuration;

public static class StartupMigrationBootstrap
{
    public static async Task ApplyAsync(WebApplication app, string testingEnvironmentName)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(testingEnvironmentName);

        if (app.Environment.IsEnvironment(testingEnvironmentName))
        {
            return;
        }

        await using var startupScope = app.Services.CreateAsyncScope();
        var dbContext = startupScope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (ShouldApplyMigrations(app.Environment.EnvironmentName, testingEnvironmentName))
        {
            await dbContext.Database.MigrateAsync();
            return;
        }

        var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync()).ToList();
        if (pendingMigrations.Count != 0)
        {
            throw new InvalidOperationException(
                "Database schema is behind the application model. Run the offline DataSeeder with --migrate-only " +
                "and LGYM_MIGRATION_POSTGRES before starting this API instance.");
        }

        await PostgreSqlRuntimeConnectionValidator.ValidateAsync(dbContext, app.Configuration);
    }

    internal static bool ShouldApplyMigrations(string environmentName, string testingEnvironmentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(testingEnvironmentName);
        if (string.Equals(environmentName, testingEnvironmentName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase) ||
               ApiEnvironmentNames.IsE2E(environmentName);
    }
}
