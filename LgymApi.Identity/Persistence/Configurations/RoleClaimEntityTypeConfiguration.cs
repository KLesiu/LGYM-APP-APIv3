using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Identity.Persistence.Configurations;

internal sealed class RoleClaimEntityTypeConfiguration : IEntityTypeConfiguration<RoleClaim>
{
    public void Configure(EntityTypeBuilder<RoleClaim> builder)
    {
        builder.ToTable("RoleClaims");

        builder.Property(e => e.ClaimType).IsRequired();
        builder.Property(e => e.ClaimValue).IsRequired();

        builder.HasIndex(e => new { e.RoleId, e.ClaimType, e.ClaimValue })
            .IsUnique()
            .HasFilter(IdentityConfigurationFilters.ActiveRowsFilter);

        builder.HasOne(e => e.Role)
            .WithMany(r => r.RoleClaims)
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
