using LgymApi.Domain.Entities;
using LgymApi.Infrastructure.Data;
using LgymApi.TestUtils;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

public abstract class PostgreSqlIntegrationTestBase
{
    private protected PostgreSqlWebApplicationFactory Factory { get; private set; } = null!;

    protected HttpClient Client { get; private set; } = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        Factory = await PostgreSqlWebApplicationFactory.CreateAsync();
        Client = Factory.CreateClient();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        Client?.Dispose();

        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }

    protected async Task<User> SeedAdminAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.SeedAdminAsync(database);
        await database.SaveChangesAsync();
        return user;
    }

    protected async Task<User> SeedUserAsync(
        string name = "testuser",
        string email = "test@example.com",
        string password = "password123")
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.SeedUserAsync(database, name, email, password);
        await database.SaveChangesAsync();
        return user;
    }
}
