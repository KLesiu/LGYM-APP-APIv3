using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Coaching;

internal sealed class TrainerInvitationEntityTypeConfiguration : IEntityTypeConfiguration<TrainerInvitation>
{
    public void Configure(EntityTypeBuilder<TrainerInvitation> builder)
    {
        builder.ToTable("TrainerInvitations");

        builder.Property(e => e.Code).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>();

        builder.HasIndex(e => e.Code)
            .IsUnique()
            .HasFilter(CoachingConfigurationFilters.ActiveRowsFilter);

        builder.HasOne(e => e.Trainer)
            .WithMany()
            .HasForeignKey(e => e.TrainerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Trainee)
            .WithMany()
            .HasForeignKey(e => e.TraineeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
