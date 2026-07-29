using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Identity.Persistence;

internal interface IIdentityPersistenceContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RoleClaim> RoleClaims { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
    DbSet<UserExternalLogin> UserExternalLogins { get; }
    DbSet<UserSession> UserSessions { get; }
    DbSet<UserTutorialProgress> UserTutorialProgresses { get; }
    DbSet<UserTutorialStepProgress> UserTutorialStepProgresses { get; }
    string? ProviderName { get; }
}
