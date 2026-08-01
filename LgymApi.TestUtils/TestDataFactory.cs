using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Identity.Contracts;
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
                Id = ParseSeedId<Role>(IdentitySeedIds.UserRole),
                Name = AuthConstants.Roles.User,
                Description = "Default role for all users",
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            },
            new Role
            {
                Id = ParseSeedId<Role>(IdentitySeedIds.AdminRole),
                Name = AuthConstants.Roles.Admin,
                Description = "Administrative privileges",
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            },
            new Role
            {
                Id = ParseSeedId<Role>(IdentitySeedIds.TesterRole),
                Name = AuthConstants.Roles.Tester,
                Description = "Excluded from ranking",
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            },
            new Role
            {
                Id = ParseSeedId<Role>(IdentitySeedIds.TrainerRole),
                Name = AuthConstants.Roles.Trainer,
                Description = "Trainer role for coach-facing APIs",
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            });

        dbContext.RoleClaims.AddRange(
            new RoleClaim
            {
                Id = ParseSeedId<RoleClaim>(IdentitySeedIds.AdminAccessClaim),
                RoleId = ParseSeedId<Role>(IdentitySeedIds.AdminRole),
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = AuthConstants.Permissions.AdminAccess,
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            },
            new RoleClaim
            {
                Id = ParseSeedId<RoleClaim>(IdentitySeedIds.ManageUserRolesClaim),
                RoleId = ParseSeedId<Role>(IdentitySeedIds.AdminRole),
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = AuthConstants.Permissions.ManageUserRoles,
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            },
            new RoleClaim
            {
                Id = ParseSeedId<RoleClaim>(IdentitySeedIds.ManageAppConfigClaim),
                RoleId = ParseSeedId<Role>(IdentitySeedIds.AdminRole),
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = AuthConstants.Permissions.ManageAppConfig,
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            },
            new RoleClaim
            {
                Id = ParseSeedId<RoleClaim>(IdentitySeedIds.ManageGlobalExercisesClaim),
                RoleId = ParseSeedId<Role>(IdentitySeedIds.AdminRole),
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
        var passwordData = CreateLegacyPasswordData(password);
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
        dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = ParseSeedId<Role>(IdentitySeedIds.UserRole) });

        if (isAdmin)
        {
            dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = ParseSeedId<Role>(IdentitySeedIds.AdminRole) });
        }

        if (isTester)
        {
            dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = ParseSeedId<Role>(IdentitySeedIds.TesterRole) });
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

    [SuppressMessage("Critical Vulnerability", "S5344", Justification = "Test-only passport-local-mongoose compatibility fixture; production password hashing is unchanged.")]
    private static (string Hash, string Salt, int Iterations, int KeyLength, string Digest) CreateLegacyPasswordData(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var saltHex = Convert.ToHexString(salt).ToLowerInvariant();
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, Encoding.UTF8.GetBytes(saltHex), 25000, HashAlgorithmName.SHA256, 512);

        return (Convert.ToHexString(hash).ToLowerInvariant(), saltHex, 25000, 512, "sha256");
    }

    private static Id<TEntity> ParseSeedId<TEntity>(string value)
    {
        return Id<TEntity>.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException($"Invalid Identity seed ID '{value}'.");
    }
}
