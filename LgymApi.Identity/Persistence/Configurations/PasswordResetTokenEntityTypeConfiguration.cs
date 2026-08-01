using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Identity.Persistence.Configurations;

internal sealed class PasswordResetTokenEntityTypeConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("PasswordResetTokens");

        builder.HasIndex(e => e.TokenHash)
            .IsUnique();

        builder.HasIndex(e => new { e.UserId, e.IsUsed, e.ExpiresAt })
            .HasFilter(IdentityConfigurationFilters.ActiveRowsFilter);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
