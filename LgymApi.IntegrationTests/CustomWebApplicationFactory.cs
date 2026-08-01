using LgymApi.Infrastructure.Data;
using LgymApi.Domain.ValueObjects;
using LgymApi.TestUtils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LgymApi.IntegrationTests;

/// <summary>
/// Configure the web application host for integration testing with in-memory database and test email sender.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly InMemoryDatabaseRoot _databaseRoot = new();

    /// <summary>
    /// Gets a unique database name for this test instance.
    /// </summary>
    public string DatabaseName { get; } = $"lgymtests_{Id<CustomWebApplicationFactory>.New():N}";
    /// <summary>
    /// Gets the test email sender instance for capturing outbound emails during tests.
    /// </summary>
    public TestEmailSender EmailSender { get; } = new();

    public TestPushProviderSender PushSender { get; } = new();

    /// <summary>
    /// The JWT signing key used for generating test tokens during integration tests.
    /// </summary>
    public const string TestJwtSigningKey = "IntegrationTestSigningKey_MustBeAtLeast32Characters!";

    /// <summary>
    /// Configure the test web host with in-memory database, JWT settings, email configuration, and CORS policies.
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
        });
        builder.ConfigureServices(services =>
        {
            RemoveAppDbContextRegistrations(services);

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(DatabaseName, _databaseRoot);
                options.EnableSensitiveDataLogging();
            });

            IntegrationHostServiceOverrides.ReplaceExternalEffects(services, EmailSender, PushSender);

            using var serviceProvider = services.BuildServiceProvider();
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            TestDataFactory.SeedDefaultRolesAsync(db).GetAwaiter().GetResult();
            db.SaveChanges();
        });

        builder.UseSetting("Jwt:SigningKey", TestJwtSigningKey);
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost");
        builder.UseSetting("Email:Enabled", "true");
        builder.UseSetting("Email:FromAddress", "no-reply@test.local");
        builder.UseSetting("Email:SmtpHost", "localhost");
        builder.UseSetting("Email:SmtpPort", "1025");
        builder.UseSetting("Email:InvitationBaseUrl", "https://app.test.local/invitations");
        builder.UseSetting("Email:TemplateRootPath", Path.Combine(AppContext.BaseDirectory, "EmailTemplates"));
        builder.UseSetting("Email:DefaultCulture", "en-US");
    }

    internal static void RemoveAppDbContextRegistrations(IServiceCollection services)
        => IntegrationHostServiceOverrides.RemoveAppDbContextRegistrations(services);
}
