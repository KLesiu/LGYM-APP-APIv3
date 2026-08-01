using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Reporting;

internal sealed class ReportTemplateFieldEntityTypeConfiguration : IEntityTypeConfiguration<ReportTemplateField>
{
    public void Configure(EntityTypeBuilder<ReportTemplateField> builder)
    {
        builder.ToTable("ReportTemplateFields");

        builder.Property(e => e.Key).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Label).HasMaxLength(120).IsRequired();
        builder.Property(e => e.ModuleConfig).HasColumnType("text");
        builder.Property(e => e.Type).HasConversion<string>();

        builder.HasIndex(e => new { e.TemplateId, e.Key })
            .IsUnique()
            .HasFilter(ReportingConfigurationFilters.ActiveRowsFilter);

        builder.HasOne(e => e.Template)
            .WithMany(e => e.Fields)
            .HasForeignKey(e => e.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
