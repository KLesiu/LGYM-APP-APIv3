using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Identity.Persistence.Configurations;

internal sealed class UserSessionEntityTypeConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("UserSessions");

        builder.Property(e => e.ExpiresAtUtc).IsRequired();

        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.Jti)
            .IsUnique()
            .HasFilter(IdentityConfigurationFilters.ActiveRowsFilter);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId);
    }
}
