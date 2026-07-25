using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using LgymApi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.TestUtils;

/// <summary>
/// Provides factory methods for seeding test data including users, roles, and permissions.
/// </summary>
public static class TestDataFactory
{
    /// <summary>
    /// Default username for admin test accounts.
    /// </summary>
    public const string DefaultAdminName = "testadmin";

    /// <summary>
    /// Default email address for admin test accounts.
    /// </summary>
    public const string DefaultAdminEmail = "testadmin@example.com";

    /// <summary>
    /// Default password for admin test accounts.
    /// </summary>
    public const string DefaultAdminSecret = "AdminSecret123!";

    /// <summary>
    /// Default password for standard user test accounts.
    /// </summary>
    public const string DefaultUserSecret = "UserSecret123!";

    /// <summary>
    /// Seeds default roles (User, Admin, Tester, Trainer) and associated permissions into the database.
    /// </summary>
    public static async Task SeedDefaultRolesAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (await dbContext.Roles.AnyAsync(cancellationToken))
        {
            return;
        }

        var timestamp = new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero);

        dbContext.Roles.AddRange(
            new Role
            {
                Id = RoleSeedDataConfiguration.UserRoleSeedId,
                Name = AuthConstants.Roles.User,
                Description = "Default role for all users",
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            },
            new Role
            {
                Id = RoleSeedDataConfiguration.AdminRoleSeedId,
                Name = AuthConstants.Roles.Admin,
                Description = "Administrative privileges",
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            },
            new Role
            {
                Id = RoleSeedDataConfiguration.TesterRoleSeedId,
                Name = AuthConstants.Roles.Tester,
                Description = "Excluded from ranking",
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            },
            new Role
            {
                Id = RoleSeedDataConfiguration.TrainerRoleSeedId,
                Name = AuthConstants.Roles.Trainer,
                Description = "Trainer role for coach-facing APIs",
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            });

        dbContext.RoleClaims.AddRange(
            new RoleClaim
            {
                Id = RoleSeedDataConfiguration.AdminAccessClaimSeedId,
                RoleId = RoleSeedDataConfiguration.AdminRoleSeedId,
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = AuthConstants.Permissions.AdminAccess,
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            },
            new RoleClaim
            {
                Id = RoleSeedDataConfiguration.ManageUserRolesClaimSeedId,
                RoleId = RoleSeedDataConfiguration.AdminRoleSeedId,
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = AuthConstants.Permissions.ManageUserRoles,
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            },
            new RoleClaim
            {
                Id = RoleSeedDataConfiguration.ManageAppConfigClaimSeedId,
                RoleId = RoleSeedDataConfiguration.AdminRoleSeedId,
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = AuthConstants.Permissions.ManageAppConfig,
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            },
            new RoleClaim
            {
                Id = RoleSeedDataConfiguration.ManageGlobalExercisesClaimSeedId,
                RoleId = RoleSeedDataConfiguration.AdminRoleSeedId,
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = AuthConstants.Permissions.ManageGlobalExercises,
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            });

    }

    /// <summary>
    /// Seeds an admin user with default credentials and admin role assignment.
    /// </summary>
    public static Task<User> SeedAdminAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        return SeedUserAsync(
            dbContext,
            name: DefaultAdminName,
            email: DefaultAdminEmail,
            password: DefaultAdminSecret,
            isAdmin: true,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Seeds a customizable user with optional admin, tester, and visibility settings plus initial ELO rating.
    /// </summary>
    public static Task<User> SeedUserAsync(
        AppDbContext dbContext,
        string name = "testuser",
        string email = "test@example.com",
        string? password = null,
        bool isAdmin = false,
        bool isVisibleInRanking = true,
        bool isTester = false,
        bool isDeleted = false,
        int elo = 1000,
        CancellationToken cancellationToken = default)
    {
        password ??= DefaultUserSecret;
        var passwordData = new LegacyPasswordService().Create(password);
        var user = new User
        {
            Id = Id<User>.New(),
            Name = name,
            Email = email,
            IsVisibleInRanking = isVisibleInRanking,
            IsDeleted = isDeleted,
            ProfileRank = "Junior 1",
            LegacyHash = passwordData.Hash,
            LegacySalt = passwordData.Salt,
            LegacyIterations = passwordData.Iterations,
            LegacyKeyLength = passwordData.KeyLength,
            LegacyDigest = passwordData.Digest
        };

        dbContext.Users.Add(user);
        dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = RoleSeedDataConfiguration.UserRoleSeedId });

        if (isAdmin)
        {
            dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = RoleSeedDataConfiguration.AdminRoleSeedId });
        }

        if (isTester)
        {
            dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = RoleSeedDataConfiguration.TesterRoleSeedId });
        }

        dbContext.EloRegistries.Add(new EloRegistry
        {
            Id = Id<EloRegistry>.New(),
            UserId = user.Id,
            Date = DateTimeOffset.UtcNow,
            Elo = elo
        });

        return Task.FromResult(user);
    }
}
