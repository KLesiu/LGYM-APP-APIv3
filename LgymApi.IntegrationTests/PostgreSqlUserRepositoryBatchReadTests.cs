using FluentAssertions;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
[Category("PostgreSql")]
internal sealed class PostgreSqlUserRepositoryBatchReadTests : PostgreSqlIntegrationTestBase
{
    [Test]
    public async Task GetByIdsAsync_ReturnsOnlyActiveMatches()
    {
        var suffix = Id<PostgreSqlUserRepositoryBatchReadTests>.New().ToString().Replace("-", string.Empty, StringComparison.Ordinal);
        var activeUser = await SeedUserAsync($"batch-active-{suffix}", $"batch-active-{suffix}@example.com");
        var deletedUser = await SeedUserAsync($"batch-deleted-{suffix}", $"batch-deleted-{suffix}@example.com");
        var missingUserId = Id<User>.New();

        using (var deleteScope = Factory.Services.CreateScope())
        {
            var database = deleteScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var trackedDeletedUser = await database.Users.SingleAsync(user => user.Id == deletedUser.Id);
            trackedDeletedUser.IsDeleted = true;
            await database.SaveChangesAsync();
        }

        using var queryScope = Factory.Services.CreateScope();
        var repository = queryScope.ServiceProvider.GetRequiredService<IUserRepository>();

        var users = await repository.GetByIdsAsync([deletedUser.Id, missingUserId, activeUser.Id]);

        users.Select(user => user.Id).Should().Equal(activeUser.Id);
    }
}
