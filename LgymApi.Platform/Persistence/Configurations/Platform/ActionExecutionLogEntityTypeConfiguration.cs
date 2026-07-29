using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Platform;

internal sealed class ActionExecutionLogEntityTypeConfiguration : IEntityTypeConfiguration<ActionExecutionLog>
{
    public void Configure(EntityTypeBuilder<ActionExecutionLog> builder)
    {
        builder.ToTable("ActionExecutionLogs");

        builder.Property(e => e.ActionType).HasConversion<string>();
        builder.Property(e => e.HandlerTypeName);
        builder.Property(e => e.Status).HasConversion<string>();

        builder.HasIndex(e => new { e.CommandEnvelopeId, e.Status })
            .HasFilter(PlatformConfigurationFilters.ActiveRowsFilter);
        builder.HasIndex(e => new { e.CommandEnvelopeId, e.ActionType })
            .HasFilter(PlatformConfigurationFilters.ActiveRowsFilter);
        builder.HasIndex(e => e.CreatedAt)
            .HasFilter(PlatformConfigurationFilters.ActiveRowsFilter);
    }
}
