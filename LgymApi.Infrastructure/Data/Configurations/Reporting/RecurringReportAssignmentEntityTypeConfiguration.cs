using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Reporting;

internal sealed class RecurringReportAssignmentEntityTypeConfiguration : IEntityTypeConfiguration<RecurringReportAssignment>
{
    public void Configure(EntityTypeBuilder<RecurringReportAssignment> builder)
    {
        builder.ToTable("RecurringReportAssignments");

        builder.Property(e => e.IntervalUnit).HasConversion<string>();
        builder.Property(e => e.Note).HasMaxLength(1000);

        builder.HasIndex(e => new { e.TrainerId, e.TraineeId, e.IsActive });
        builder.HasIndex(e => new { e.TraineeId, e.NextEligibleAt });
        builder.HasIndex(e => e.CurrentReportRequestId)
            .IsUnique()
            .HasFilter(ReportingConfigurationFilters.ActiveCurrentReportRequestFilter);

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

        builder.HasOne(e => e.CurrentReportRequest)
            .WithMany()
            .HasForeignKey(e => e.CurrentReportRequestId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
