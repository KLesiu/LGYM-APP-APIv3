using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Coaching;

internal sealed class TraineeNoteEntityTypeConfiguration : IEntityTypeConfiguration<TraineeNote>
{
    public void Configure(EntityTypeBuilder<TraineeNote> builder)
    {
        builder.ToTable("TraineeNotes");

        builder.Property(e => e.Title).HasMaxLength(160);
        builder.Property(e => e.Content).HasMaxLength(8000).IsRequired();

        builder.HasIndex(e => new { e.TrainerId, e.TraineeId, e.LastUpdatedAt });
        builder.HasIndex(e => new { e.TraineeId, e.VisibleToTrainee, e.LastUpdatedAt });

        builder.HasOne(e => e.Trainer)
            .WithMany()
            .HasForeignKey(e => e.TrainerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Trainee)
            .WithMany()
            .HasForeignKey(e => e.TraineeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.LastUpdatedByUser)
            .WithMany()
            .HasForeignKey(e => e.LastUpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
