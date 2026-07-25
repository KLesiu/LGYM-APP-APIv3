using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class UserSeeder : IEntitySeeder
{
    private readonly ILegacyPasswordService _legacyPasswordService;

    public UserSeeder(ILegacyPasswordService legacyPasswordService)
    {
        _legacyPasswordService = legacyPasswordService;
    }

    public int Order => 0;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("users");
        var adminEmail = seedContext.AdminUser?.Email ?? "admin@lgym.app";
        var testerEmail = seedContext.TesterUser?.Email ?? "tester@lgym.app";
        seedContext.AdminUser ??= await EnsureUserAsync(context, "Admin", adminEmail, cancellationToken);
        seedContext.TesterUser ??= await EnsureUserAsync(context, "Tester", testerEmail, cancellationToken);

        if (seedContext.AdminUser == null || seedContext.TesterUser == null)
        {
            SeedOperationConsole.Skip("users");
            return;
        }

        if (!seedContext.SeedDemoData || seedContext.DemoUsers.Any())
        {
            SeedOperationConsole.Done("users");
            return;
        }

        var demoUsers = new[]
        {
            (Name: "DemoUser1", Email: "demo1@lgym.app"),
            (Name: "DemoUser2", Email: "demo2@lgym.app")
        };

        foreach (var demo in demoUsers)
        {
            var user = await EnsureUserAsync(context, demo.Name, demo.Email, cancellationToken);
            if (user != null)
            {
                seedContext.DemoUsers.Add(user);
            }
        }

        SeedOperationConsole.Done("users");
    }

    private async Task<User?> EnsureUserAsync(
        AppDbContext context,
        string name,
        string email,
        CancellationToken cancellationToken)
    {
        var existing = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Name == name, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var passwordData = _legacyPasswordService.Create(name);
        var user = new User
        {
            Id = Id<User>.New(),
            Name = name,
            Email = email,
            IsVisibleInRanking = true,
            ProfileRank = "Junior 1",
            LegacyHash = passwordData.Hash,
            LegacySalt = passwordData.Salt,
            LegacyIterations = passwordData.Iterations,
            LegacyKeyLength = passwordData.KeyLength,
            LegacyDigest = passwordData.Digest
        };

        await context.Users.AddAsync(user, cancellationToken);
        return user;
    }
}
