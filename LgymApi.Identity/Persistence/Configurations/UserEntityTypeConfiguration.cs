using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Identity.Persistence.Configurations;

internal sealed class UserEntityTypeConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.Property(u => u.Email)
            .HasConversion(email => email.Value, value => new Email(value));

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter(IdentityConfigurationFilters.ActiveRowsFilter);

        builder.HasIndex(u => u.Name)
            .IsUnique()
            .HasFilter(IdentityConfigurationFilters.ActiveRowsFilter);

        builder.HasOne(u => u.Plan)
            .WithMany()
            .HasForeignKey(u => u.PlanId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
