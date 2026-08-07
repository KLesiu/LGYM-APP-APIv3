using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LgymApi.Infrastructure.Data;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string MigrationConnectionEnvironmentVariable = "LGYM_MIGRATION_POSTGRES";
    private const string RuntimeConnectionEnvironmentVariable = "ConnectionStrings__Postgres";
    private const string LocalDevelopmentConnectionString = "Host=localhost;Port=5433;Database=LGYM-APP;Username=postgres;Password=REPLACE_ME;TimeZone=Europe/Warsaw";

    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        var connectionString = ResolveConnectionString();
        optionsBuilder.UseNpgsql(connectionString);
        return new AppDbContext(optionsBuilder.Options);
    }

    internal static string ResolveConnectionString()
    {
        var migrationConnectionString = Environment.GetEnvironmentVariable(MigrationConnectionEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(migrationConnectionString))
        {
            return migrationConnectionString;
        }

        if (!IsDevelopmentEnvironment())
        {
            throw new InvalidOperationException(
                "LGYM_MIGRATION_POSTGRES is required for design-time migrations outside Development.");
        }

        return Environment.GetEnvironmentVariable(RuntimeConnectionEnvironmentVariable)
               ?? LocalDevelopmentConnectionString;
    }

    private static bool IsDevelopmentEnvironment()
        => string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            "Development",
            StringComparison.OrdinalIgnoreCase);
}
