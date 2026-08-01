using LgymApi.Infrastructure.Data;
using LgymApi.TestUtils;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LgymApi.IntegrationTests;

public sealed class PostgreSqlWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlDatabaseLease _lease;

    private PostgreSqlWebApplicationFactory(PostgreSqlDatabaseLease lease)
    {
        _lease = lease;
    }

    public string DatabaseName => _lease.DatabaseName;

    public TestEmailSender EmailSender { get; } = new();

    public TestPushProviderSender PushSender { get; } = new();

    public static async Task<PostgreSqlWebApplicationFactory> CreateAsync(CancellationToken cancellationToken = default)
    {
        var lease = await PostgreSqlDatabaseLease.CreateAsync(cancellationToken);
        var factory = new PostgreSqlWebApplicationFactory(lease);

        return await CompleteInitializationAsync(
            factory,
            static (target, token) => target.InitializeAsync(token),
            static target => target.DisposeAsync(),
            cancellationToken);
    }

    internal static async Task<TFactory> CompleteInitializationAsync<TFactory>(
        TFactory factory,
        Func<TFactory, CancellationToken, Task> initializeAsync,
        Func<TFactory, ValueTask> cleanupAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            await initializeAsync(factory, cancellationToken);
            return factory;
        }
        catch (Exception initializationException)
        {
            try
            {
                await cleanupAsync(factory);
            }
            catch (Exception cleanupException)
            {
                if (initializationException is OperationCanceledException cancellationException)
                {
                    throw new PostgreSqlFactoryInitializationCanceledException(
                        cancellationException,
                        cleanupException);
                }

                throw new PostgreSqlFactoryInitializationException(
                    initializationException,
                    cleanupException);
            }

            throw;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            RemoveAppDbContextRegistrations(services);

            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_lease.ConnectionString));
            IntegrationHostServiceOverrides.ReplaceExternalEffects(services, EmailSender, PushSender);
        });

        builder.UseSetting("Jwt:SigningKey", CustomWebApplicationFactory.TestJwtSigningKey);
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

    public override async ValueTask DisposeAsync()
    {
        await CompleteDisposalAsync(
            () => base.DisposeAsync(),
            () => _lease.DisposeAsync());
    }

    internal static async ValueTask CompleteDisposalAsync(
        Func<ValueTask> disposeFactoryAsync,
        Func<ValueTask> disposeLeaseAsync)
    {
        try
        {
            await disposeFactoryAsync();
        }
        catch (Exception factoryException)
        {
            try
            {
                await disposeLeaseAsync();
            }
            catch (Exception leaseCleanupException)
            {
                throw new PostgreSqlFactoryDisposalException(factoryException, leaseCleanupException);
            }

            throw;
        }

        await disposeLeaseAsync();
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
        await TestDataFactory.SeedDefaultRolesAsync(database, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class PostgreSqlFactoryInitializationException : InvalidOperationException
{
    public PostgreSqlFactoryInitializationException(
        Exception initializationException,
        Exception cleanupException)
        : base(
            "Could not initialize the isolated PostgreSQL integration database and cleanup also failed. The connection value is redacted.",
            initializationException)
    {
        CleanupException = cleanupException;
    }

    public Exception CleanupException { get; }
}

internal sealed class PostgreSqlFactoryInitializationCanceledException : OperationCanceledException
{
    public PostgreSqlFactoryInitializationCanceledException(
        OperationCanceledException cancellationException,
        Exception cleanupException)
        : base(
            "Initialization of the isolated PostgreSQL integration database was canceled and cleanup also failed. The connection value is redacted.",
            cancellationException,
            cancellationException.CancellationToken)
    {
        CleanupException = cleanupException;
    }

    public Exception CleanupException { get; }
}

internal sealed class PostgreSqlFactoryDisposalException : InvalidOperationException
{
    public PostgreSqlFactoryDisposalException(
        Exception factoryException,
        Exception leaseCleanupException)
        : base(
            "Could not dispose the isolated PostgreSQL integration factory and lease cleanup also failed. The connection value is redacted.",
            factoryException)
    {
        LeaseCleanupException = leaseCleanupException;
    }

    public Exception LeaseCleanupException { get; }
}
