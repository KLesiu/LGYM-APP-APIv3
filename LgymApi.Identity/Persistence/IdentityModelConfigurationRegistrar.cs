using LgymApi.Identity.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Identity.Persistence;

internal static class IdentityModelConfigurationRegistrar
{
    internal static void Apply(ModelBuilder modelBuilder)
    {
        Register(modelBuilder, new UserEntityTypeConfiguration());
        Register(modelBuilder, new RoleEntityTypeConfiguration());
        Register(modelBuilder, new UserRoleEntityTypeConfiguration());
        Register(modelBuilder, new RoleClaimEntityTypeConfiguration());
        Register(modelBuilder, new PasswordResetTokenEntityTypeConfiguration());
        Register(modelBuilder, new UserExternalLoginEntityTypeConfiguration());
        Register(modelBuilder, new UserSessionEntityTypeConfiguration());
        Register(modelBuilder, new UserTutorialProgressEntityTypeConfiguration());
        Register(modelBuilder, new UserTutorialStepProgressEntityTypeConfiguration());
    }

    private static void Register<TEntity>(ModelBuilder modelBuilder, IEntityTypeConfiguration<TEntity> configuration)
        where TEntity : class
    {
        modelBuilder.ApplyConfiguration(configuration);
    }
}
