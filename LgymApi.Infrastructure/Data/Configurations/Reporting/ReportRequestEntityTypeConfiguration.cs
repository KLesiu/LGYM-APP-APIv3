using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Reporting;

internal sealed class ReportRequestEntityTypeConfiguration : IEntityTypeConfiguration<ReportRequest>
{
    public void Configure(EntityTypeBuilder<ReportRequest> builder)
    {
        builder.ToTable("ReportRequests");

        builder.Property(e => e.Status).HasConversion<string>();
        builder.Property(e => e.Note).HasMaxLength(1000);

        builder.HasIndex(e => new { e.TrainerId, e.TraineeId, e.CreatedAt });
        builder.HasIndex(e => new { e.TraineeId, e.Status, e.CreatedAt });
        builder.HasIndex(e => e.RecurringReportAssignmentId);

        builder.HasOne(e => e.Trainer)
            .WithMany()
            .HasForeignKey(e => e.TrainerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Trainee)
            .WithMany()
            .HasForeignKey(e => e.TraineeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Template)
            .WithMany()
            .HasForeignKey(e => e.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.RecurringReportAssignment)
            .WithMany()
            .HasForeignKey(e => e.RecurringReportAssignmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Submission)
            .WithOne(e => e.ReportRequest)
            .HasForeignKey<ReportSubmission>(e => e.ReportRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
