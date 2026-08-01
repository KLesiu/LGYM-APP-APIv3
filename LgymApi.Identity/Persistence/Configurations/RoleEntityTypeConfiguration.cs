using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Identity.Persistence.Configurations;

internal sealed class RoleEntityTypeConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");

        builder.Property(e => e.Name).IsRequired();

        builder.HasIndex(e => e.Name)
            .IsUnique()
            .HasFilter(IdentityConfigurationFilters.ActiveRowsFilter);
    }
}
