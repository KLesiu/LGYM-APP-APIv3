using LgymApi.DataSeeder;
using LgymApi.DataSeeder.Seeders;
using LgymApi.Identity;
using LgymApi.Infrastructure.Data;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.DataSeeder;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== LgymApi DataSeeder ===");

        if (!TryParseMode(args, out var mode))
        {
            Console.Error.WriteLine("Supported modes: --migrate-only, --prepare-hangfire, --seed.");
            return 1;
        }

        var basePath = Environment.GetEnvironmentVariable("LGYM_SEEDER_BASE_PATH") ?? AppContext.BaseDirectory;
        var configuration = DataSeederProgram.BuildConfiguration(basePath);
        var connectionString = DataSeederProgram.GetMigrationConnectionString();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("LGYM_MIGRATION_POSTGRES is required for offline database operations.");
            return 1;
        }

        if (IsTestModeEnabled())
        {
            Console.WriteLine("Test mode enabled. Skipping offline database operation.");
            return 0;
        }

        await using var provider = BuildServiceProvider(configuration, connectionString);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (mode == SeederMode.MigrateOnly)
        {
            await context.Database.MigrateAsync();
            Console.WriteLine("EF Core migrations are current.");
            return 0;
        }

        if (mode == SeederMode.PrepareHangfire)
        {
            _ = new PostgreSqlStorage(connectionString, new PostgreSqlStorageOptions
            {
                PrepareSchemaIfNecessary = true,
                StartupConnectionMaxRetries = 0,
                AllowDegradedModeWithoutStorage = false
            });
            Console.WriteLine("Hangfire PostgreSQL schema is prepared.");
            return 0;
        }

        var dropDatabase = ConsolePrompt.Confirm("Drop existing database before seeding?", false);
        Console.WriteLine("EF Core migrations are required for relational databases. EnsureCreated is reserved only for non-relational test stores.");
        var seedDemo = ConsolePrompt.Confirm("Seed demo data?", false);
        var options = new SeedOptions
        {
            DropDatabase = dropDatabase,
            UseMigrations = true,
            SeedDemoData = seedDemo
        };
        var orchestrator = scope.ServiceProvider.GetRequiredService<SeedOrchestrator>();

        var seedContext = new SeedContext();
        await orchestrator.RunAsync(context, seedContext, options, CancellationToken.None);
        Console.WriteLine("All done! Database is ready.");
        return 0;
    }

    private static bool IsTestModeEnabled()
    {
        var value = Environment.GetEnvironmentVariable("LGYM_SEEDER_TEST_MODE");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseMode(string[] args, out SeederMode mode)
    {
        mode = SeederMode.Seed;
        return args.Length switch
        {
            0 => true,
            1 when string.Equals(args[0], "--seed", StringComparison.Ordinal) => true,
            1 when string.Equals(args[0], "--migrate-only", StringComparison.Ordinal) => SetMode(SeederMode.MigrateOnly, out mode),
            1 when string.Equals(args[0], "--prepare-hangfire", StringComparison.Ordinal) => SetMode(SeederMode.PrepareHangfire, out mode),
            _ => false
        };
    }

    private static bool SetMode(SeederMode selectedMode, out SeederMode mode)
    {
        mode = selectedMode;
        return true;
    }

    internal static ServiceProvider BuildServiceProvider(IConfiguration configuration, string connectionString)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<AppDbContext>(optionsBuilder =>
            optionsBuilder.UseNpgsql(connectionString));
        services.AddIdentityModule();
        services.AddScoped<IEntitySeeder, UserSeeder>();
        services.AddScoped<IEntitySeeder, PasswordResetTokenSeeder>();
        services.AddScoped<IEntitySeeder, EloRegistrySeeder>();
        services.AddScoped<IEntitySeeder, RoleSeeder>();
        services.AddScoped<IEntitySeeder, RoleClaimSeeder>();
        services.AddScoped<IEntitySeeder, ExerciseSeeder>();
        services.AddScoped<IEntitySeeder, ExerciseTranslationSeeder>();
        services.AddScoped<IEntitySeeder, AddressSeeder>();
        services.AddScoped<IEntitySeeder, GymSeeder>();
        services.AddScoped<IEntitySeeder, PlanSeeder>();
        services.AddScoped<IEntitySeeder, PlanDaySeeder>();
        services.AddScoped<IEntitySeeder, PlanDayExerciseSeeder>();
        services.AddScoped<IEntitySeeder, TrainingSeeder>();
        services.AddScoped<IEntitySeeder, ExerciseScoreSeeder>();
        services.AddScoped<IEntitySeeder, TrainingExerciseScoreSeeder>();
        services.AddScoped<IEntitySeeder, MeasurementSeeder>();
        services.AddScoped<IEntitySeeder, MainRecordSeeder>();
        services.AddScoped<IEntitySeeder, AppConfigSeeder>();
        services.AddScoped<IEntitySeeder, TrainerInvitationSeeder>();
        services.AddScoped<IEntitySeeder, TrainerTraineeLinkSeeder>();
        services.AddScoped<IEntitySeeder, InAppNotificationSeeder>();
        services.AddScoped<IEntitySeeder, NotificationMessageSeeder>();
        services.AddScoped<IEntitySeeder, EmailNotificationSubscriptionSeeder>();
        services.AddScoped<IEntitySeeder, ReportTemplateSeeder>();
        services.AddScoped<IEntitySeeder, ReportTemplateFieldSeeder>();
        services.AddScoped<IEntitySeeder, ReportRequestSeeder>();
        services.AddScoped<IEntitySeeder, ReportSubmissionSeeder>();
        services.AddScoped<IEntitySeeder, RecurringReportAssignmentSeeder>();
        services.AddScoped<IEntitySeeder, SupplementPlanSeeder>();
        services.AddScoped<IEntitySeeder, SupplementPlanItemSeeder>();
        services.AddScoped<IEntitySeeder, SupplementIntakeLogSeeder>();
        services.AddScoped<IEntitySeeder, UserTutorialProgressSeeder>();
        services.AddScoped<IEntitySeeder, UserTutorialStepProgressSeeder>();
        services.AddScoped<IEntitySeeder, UserSessionSeeder>();
        services.AddScoped<IEntitySeeder, UserExternalLoginSeeder>();
        services.AddScoped<IEntitySeeder, PhotoSeeder>();
        services.AddScoped<IEntitySeeder, PhotoUploadSessionSeeder>();
        services.AddScoped<IEntitySeeder, PushInstallationSeeder>();
        services.AddScoped<IEntitySeeder, PushNotificationMessageSeeder>();
        services.AddScoped<SeedOrchestrator>();
        return services.BuildServiceProvider();
    }
}

public enum SeederMode
{
    Seed,
    MigrateOnly,
    PrepareHangfire
}
