using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.WorkoutProgress;

internal sealed class EloRegistryEntityTypeConfiguration : IEntityTypeConfiguration<EloRegistry>
{
    public void Configure(EntityTypeBuilder<EloRegistry> builder)
    {
        builder.ToTable("EloRegistries");

        builder.Property(e => e.Elo)
            .HasConversion(elo => elo.Value, value => new Elo(value));

        builder.HasOne(e => e.User)
            .WithMany(u => u.EloRegistries)
            .HasForeignKey(e => e.UserId);
    }
}
