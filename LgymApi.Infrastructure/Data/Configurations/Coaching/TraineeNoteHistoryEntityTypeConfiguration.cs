using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Coaching;

internal sealed class TraineeNoteHistoryEntityTypeConfiguration : IEntityTypeConfiguration<TraineeNoteHistory>
{
    public void Configure(EntityTypeBuilder<TraineeNoteHistory> builder)
    {
        builder.ToTable("TraineeNoteHistories");

        builder.Property(e => e.PreviousContent).HasMaxLength(8000);
        builder.Property(e => e.NewContent).HasMaxLength(8000).IsRequired();
        builder.Property(e => e.ChangeType).HasMaxLength(64).IsRequired();

        builder.HasIndex(e => new { e.TraineeNoteId, e.ChangedAt });

        builder.HasOne(e => e.TraineeNote)
            .WithMany(e => e.HistoryEntries)
            .HasForeignKey(e => e.TraineeNoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.ChangedByUser)
            .WithMany()
            .HasForeignKey(e => e.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
