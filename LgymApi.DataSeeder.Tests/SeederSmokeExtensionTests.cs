using LgymApi.DataSeeder.Seeders;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Tests;

[TestFixture]
public sealed class SeederSmokeExtensionTests
{
    [Test]
    public async Task RoleSeeder_Should_Add_Default_Roles()
    {
        var context = await CreateContextAsync();
        var seedContext = new SeedContext();

        var seeder = new RoleSeeder();
        await seeder.SeedAsync(context, seedContext, CancellationToken.None);
        await context.SaveChangesAsync();

        var names = await context.Roles.Select(role => role.Name).ToListAsync();
        names.Should().Contain(AuthConstants.Roles.Admin);
        names.Should().Contain(AuthConstants.Roles.User);
        names.Should().Contain(AuthConstants.Roles.Trainer);
        names.Should().Contain(AuthConstants.Roles.Tester);
    }

    [Test]
    public async Task RoleClaimSeeder_Should_Add_Admin_Permission_Claims()
    {
        var context = await CreateContextAsync();
        var seedContext = new SeedContext();

        var roleSeeder = new RoleSeeder();
        await roleSeeder.SeedAsync(context, seedContext, CancellationToken.None);
        await context.SaveChangesAsync();

        var seeder = new RoleClaimSeeder();
        await seeder.SeedAsync(context, seedContext, CancellationToken.None);
        await context.SaveChangesAsync();

        var adminClaims = await context.RoleClaims
            .Where(claim => claim.RoleId == (Id<Role>)RoleSeedDataConfiguration.AdminRoleSeedId)
            .Select(claim => claim.ClaimValue)
            .ToListAsync();

        adminClaims.Should().Contain(AuthConstants.Permissions.AdminAccess);
        adminClaims.Should().Contain(AuthConstants.Permissions.ManageUserRoles);
    }

    [Test]
    public async Task TrainerTraineeLinkSeeder_Should_Add_Link_For_Demo_Users()
    {
        var context = await CreateContextAsync();
        var seedContext = new SeedContext();

        seedContext.DemoUsers.Add(new User { Id = Id<User>.New(), Name = "Trainer" });
        seedContext.DemoUsers.Add(new User { Id = Id<User>.New(), Name = "Trainee" });

        var seeder = new TrainerTraineeLinkSeeder();
        await seeder.SeedAsync(context, seedContext, CancellationToken.None);
        await context.SaveChangesAsync();

        (await context.TrainerTraineeLinks.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task ReportTemplateSeeder_Should_Add_Template_With_Fields()
    {
        var context = await CreateContextAsync();
        var seedContext = new SeedContext();

        seedContext.DemoUsers.Add(new User { Id = Id<User>.New(), Name = "Trainer" });

        var seeder = new ReportTemplateSeeder();
        await seeder.SeedAsync(context, seedContext, CancellationToken.None);
        await context.SaveChangesAsync();

        (await context.ReportTemplates.CountAsync()).Should().Be(1);
        (await context.ReportTemplateFields.CountAsync()).Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ReportRequestSeeder_Should_Add_Request_And_Submission()
    {
        var context = await CreateContextAsync();
        var seedContext = new SeedContext();

        var trainer = new User { Id = Id<User>.New(), Name = "Trainer" };
        var trainee = new User { Id = Id<User>.New(), Name = "Trainee" };
        seedContext.DemoUsers.Add(trainer);
        seedContext.DemoUsers.Add(trainee);

        var templateSeeder = new ReportTemplateSeeder();
        await templateSeeder.SeedAsync(context, seedContext, CancellationToken.None);
        await context.SaveChangesAsync();

        var seeder = new ReportRequestSeeder();
        await seeder.SeedAsync(context, seedContext, CancellationToken.None);
        await context.SaveChangesAsync();

        (await context.ReportRequests.CountAsync()).Should().Be(1);
        (await context.ReportSubmissions.CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task SupplementPlanSeeder_Should_Add_Plan_Items_And_Log()
    {
        var context = await CreateContextAsync();
        var seedContext = new SeedContext();

        seedContext.DemoUsers.Add(new User { Id = Id<User>.New(), Name = "Trainer" });
        seedContext.DemoUsers.Add(new User { Id = Id<User>.New(), Name = "Trainee" });

        var planSeeder = new SupplementPlanSeeder();
        await planSeeder.SeedAsync(context, seedContext, CancellationToken.None);
        await context.SaveChangesAsync();

        var itemSeeder = new SupplementPlanItemSeeder();
        await itemSeeder.SeedAsync(context, seedContext, CancellationToken.None);
        await context.SaveChangesAsync();

        var logSeeder = new SupplementIntakeLogSeeder();
        await logSeeder.SeedAsync(context, seedContext, CancellationToken.None);
        await context.SaveChangesAsync();

        (await context.SupplementPlans.CountAsync()).Should().Be(1);
        (await context.SupplementPlanItems.CountAsync()).Should().BeGreaterThan(0);
        (await context.SupplementIntakeLogs.CountAsync()).Should().BeGreaterThan(0);
    }

    [Test]
    public async Task NotificationMessageSeeder_Should_Add_Messages()
    {
        var context = await CreateContextAsync();
        var seedContext = new SeedContext();

        var seeder = new NotificationMessageSeeder();
        await seeder.SeedAsync(context, seedContext, CancellationToken.None);
        await context.SaveChangesAsync();

        (await context.NotificationMessages.CountAsync()).Should().Be(2);
    }

    [Test]
    public async Task ReportSubmissionSeeder_Should_Skip_When_No_Requests()
    {
        var context = await CreateContextAsync();
        var seedContext = new SeedContext();

        var seeder = new ReportSubmissionSeeder();
        await seeder.SeedAsync(context, seedContext, CancellationToken.None);
        await context.SaveChangesAsync();

        (await context.ReportSubmissions.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task ReportTemplateFieldSeeder_Should_Add_Fields_For_Seeded_Template()
    {
        var context = await CreateContextAsync();
        var seedContext = new SeedContext();

        var trainer = new User { Id = Id<User>.New(), Name = "Trainer" };
        seedContext.DemoUsers.Add(trainer);

        var templateSeeder = new ReportTemplateSeeder();
        await templateSeeder.SeedAsync(context, seedContext, CancellationToken.None);
        await context.SaveChangesAsync();

        var fieldSeeder = new ReportTemplateFieldSeeder();
        await fieldSeeder.SeedAsync(context, seedContext, CancellationToken.None);
        await context.SaveChangesAsync();

        (await context.ReportTemplateFields.CountAsync()).Should().BeGreaterThan(0);
    }

    private static async Task<AppDbContext> CreateContextAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Id<Exercise>.New().ToString())
            .Options;

        var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }
}
