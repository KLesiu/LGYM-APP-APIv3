using LgymApi.DataSeeder;
using LgymApi.DataSeeder.Seeders;
using LgymApi.Identity;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.DataSeeder;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== LgymApi DataSeeder ===");

        var basePath = Environment.GetEnvironmentVariable("LGYM_SEEDER_BASE_PATH") ?? AppContext.BaseDirectory;
        var configuration = DataSeederProgram.BuildConfiguration(basePath);
        var connectionString = configuration.GetConnectionString("Postgres") ?? string.Empty;
        Console.WriteLine("Reading configuration from LgymApi.Api/appsettings.json...");
        Console.WriteLine($"Connection: {DataSeederProgram.MaskConnectionString(connectionString)}");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("Connection string 'ConnectionStrings:Postgres' is missing. Aborting.");
            return 1;
        }

        Console.WriteLine();

        var dropDatabase = ConsolePrompt.Confirm("Drop existing database before seeding?", false);
        Console.WriteLine("EF Core migrations are required for relational databases. EnsureCreated is reserved only for non-relational test stores.");
        var seedDemo = ConsolePrompt.Confirm("Seed demo data?", false);

        var options = new SeedOptions
        {
            DropDatabase = dropDatabase,
            UseMigrations = true,
            SeedDemoData = seedDemo
        };

        if (IsTestModeEnabled())
        {
            Console.WriteLine("Test mode enabled. Skipping database seeding.");
            return 0;
        }

        await using var provider = BuildServiceProvider(configuration, connectionString);
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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
