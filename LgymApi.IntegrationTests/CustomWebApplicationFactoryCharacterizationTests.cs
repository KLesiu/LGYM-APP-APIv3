using LgymApi.Domain.Security;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Application.Repositories;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class CustomWebApplicationFactoryCharacterizationTests
{
    [Test]
    public async Task CustomWebApplicationFactory_ConfiguresInMemoryDatabaseAndSeedsRoles()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        database.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.InMemory");
        database.Database.EnsureCreated().Should().BeFalse();

        var roleNames = await database.Roles.Select(role => role.Name).ToListAsync();

        roleNames.Should().Contain(
            AuthConstants.Roles.User,
            AuthConstants.Roles.Admin,
            AuthConstants.Roles.Tester,
            AuthConstants.Roles.Trainer);
    }

    [Test]
    public async Task CustomWebApplicationFactory_SuppressesMigrationsAndUsesNoOpTransactions()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var migrationAttempt = () => database.Database.MigrateAsync();

        await migrationAttempt.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*relational-specific methods*");
        await using var transaction = await unitOfWork.BeginTransactionAsync();
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task CustomWebApplicationFactory_ReplacesLiveEmailAndFcmWithCapturedFakes()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetServices<IEmailSender>().Should().ContainSingle()
            .Which.Should().BeSameAs(factory.EmailSender);
        scope.ServiceProvider.GetServices<IPushProviderSender>().Should().ContainSingle()
            .Which.Should().BeSameAs(factory.PushSender);
    }
}
