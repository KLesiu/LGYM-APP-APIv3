using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Reporting;

internal sealed class ReportSubmissionEntityTypeConfiguration : IEntityTypeConfiguration<ReportSubmission>
{
    public void Configure(EntityTypeBuilder<ReportSubmission> builder)
    {
        builder.ToTable("ReportSubmissions");

        builder.Property(e => e.PayloadJson).IsRequired();
        builder.Property(e => e.TrainerOverallComment).HasMaxLength(4000);
        builder.Property(e => e.TrainerFieldCommentsJson).HasColumnType("text");

        builder.HasIndex(e => e.ReportRequestId)
            .IsUnique()
            .HasFilter(ReportingConfigurationFilters.ActiveRowsFilter);

        builder.HasOne(e => e.Trainee)
            .WithMany()
            .HasForeignKey(e => e.TraineeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
