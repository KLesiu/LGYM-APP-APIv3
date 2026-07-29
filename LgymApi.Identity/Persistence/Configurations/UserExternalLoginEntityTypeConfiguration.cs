using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Identity.Persistence.Configurations;

internal sealed class UserExternalLoginEntityTypeConfiguration : IEntityTypeConfiguration<UserExternalLogin>
{
    public void Configure(EntityTypeBuilder<UserExternalLogin> builder)
    {
        builder.ToTable("UserExternalLogins");

        builder.Property(e => e.Provider).HasMaxLength(50).IsRequired();
        builder.Property(e => e.ProviderKey).HasMaxLength(255).IsRequired();
        builder.Property(e => e.ProviderEmail).HasMaxLength(256);

        builder.HasIndex(e => new { e.Provider, e.ProviderKey })
            .IsUnique()
            .HasFilter(IdentityConfigurationFilters.ActiveRowsFilter);

        builder.HasIndex(e => new { e.UserId, e.Provider })
            .IsUnique()
            .HasFilter(IdentityConfigurationFilters.ActiveRowsFilter);

        builder.HasOne(e => e.User)
            .WithMany(u => u.ExternalLogins)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
