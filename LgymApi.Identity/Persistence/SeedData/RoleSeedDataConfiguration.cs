using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Data.SeedData;

internal static class RoleSeedDataConfiguration
{
    public static readonly Id<Role> UserRoleSeedId = ParseSeedId<Role>(IdentitySeedIds.UserRole);
    public static readonly Id<Role> AdminRoleSeedId = ParseSeedId<Role>(IdentitySeedIds.AdminRole);
    public static readonly Id<Role> TesterRoleSeedId = ParseSeedId<Role>(IdentitySeedIds.TesterRole);
    public static readonly Id<Role> TrainerRoleSeedId = ParseSeedId<Role>(IdentitySeedIds.TrainerRole);
    public static readonly Id<RoleClaim> AdminAccessClaimSeedId = ParseSeedId<RoleClaim>(IdentitySeedIds.AdminAccessClaim);
    public static readonly Id<RoleClaim> ManageUserRolesClaimSeedId = ParseSeedId<RoleClaim>(IdentitySeedIds.ManageUserRolesClaim);
    public static readonly Id<RoleClaim> ManageAppConfigClaimSeedId = ParseSeedId<RoleClaim>(IdentitySeedIds.ManageAppConfigClaim);
    public static readonly Id<RoleClaim> ManageGlobalExercisesClaimSeedId = ParseSeedId<RoleClaim>(IdentitySeedIds.ManageGlobalExercisesClaim);
    public static readonly Id<RoleClaim> TrainerAccessClaimSeedId = ParseSeedId<RoleClaim>(IdentitySeedIds.TrainerAccessClaim);
    private static readonly DateTimeOffset RoleSeedTimestamp = new(2026, 2, 15, 0, 0, 0, TimeSpan.Zero);

    public static void Apply(ModelBuilder modelBuilder)
    {
        var roleEntity = modelBuilder.Entity<Role>();
        roleEntity.HasData(
            new Role
            {
                Id = (Id<Role>)UserRoleSeedId,
                Name = AuthConstants.Roles.User,
                Description = "Default role for all users",
                CreatedAt = RoleSeedTimestamp,
                UpdatedAt = RoleSeedTimestamp
            },
            new Role
            {
                Id = (Id<Role>)AdminRoleSeedId,
                Name = AuthConstants.Roles.Admin,
                Description = "Administrative privileges",
                CreatedAt = RoleSeedTimestamp,
                UpdatedAt = RoleSeedTimestamp
            },
            new Role
            {
                Id = (Id<Role>)TesterRoleSeedId,
                Name = AuthConstants.Roles.Tester,
                Description = "Excluded from ranking",
                CreatedAt = RoleSeedTimestamp,
                UpdatedAt = RoleSeedTimestamp
            },
            new Role
            {
                Id = (Id<Role>)TrainerRoleSeedId,
                Name = AuthConstants.Roles.Trainer,
                Description = "Trainer role for coach-facing APIs",
                CreatedAt = RoleSeedTimestamp,
                UpdatedAt = RoleSeedTimestamp
            });

        var roleClaimEntity = modelBuilder.Entity<RoleClaim>();
        roleClaimEntity.HasData(
            new RoleClaim
            {
                Id = (Id<RoleClaim>)AdminAccessClaimSeedId,
                RoleId = (Id<Role>)AdminRoleSeedId,
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = AuthConstants.Permissions.AdminAccess,
                CreatedAt = RoleSeedTimestamp,
                UpdatedAt = RoleSeedTimestamp
            },
            new RoleClaim
            {
                Id = (Id<RoleClaim>)ManageUserRolesClaimSeedId,
                RoleId = (Id<Role>)AdminRoleSeedId,
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = AuthConstants.Permissions.ManageUserRoles,
                CreatedAt = RoleSeedTimestamp,
                UpdatedAt = RoleSeedTimestamp
            },
            new RoleClaim
            {
                Id = (Id<RoleClaim>)ManageAppConfigClaimSeedId,
                RoleId = (Id<Role>)AdminRoleSeedId,
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = AuthConstants.Permissions.ManageAppConfig,
                CreatedAt = RoleSeedTimestamp,
                UpdatedAt = RoleSeedTimestamp
            },
            new RoleClaim
            {
                Id = (Id<RoleClaim>)ManageGlobalExercisesClaimSeedId,
                RoleId = (Id<Role>)AdminRoleSeedId,
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = AuthConstants.Permissions.ManageGlobalExercises,
                CreatedAt = RoleSeedTimestamp,
                UpdatedAt = RoleSeedTimestamp
            },
            new RoleClaim
            {
                Id = (Id<RoleClaim>)TrainerAccessClaimSeedId,
                RoleId = (Id<Role>)TrainerRoleSeedId,
                ClaimType = AuthConstants.PermissionClaimType,
                ClaimValue = AuthConstants.Permissions.TrainerAccess,
                CreatedAt = RoleSeedTimestamp,
                UpdatedAt = RoleSeedTimestamp
            });
    }

    private static Id<TEntity> ParseSeedId<TEntity>(string idString)
    {
        if (!Id<TEntity>.TryParse(idString, out var id))
        {
            throw new InvalidOperationException($"Failed to parse seed ID: {idString}");
        }
        return id;
    }
}
