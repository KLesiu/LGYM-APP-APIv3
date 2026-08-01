using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Platform;

internal sealed class CommandEnvelopeEntityTypeConfiguration : IEntityTypeConfiguration<CommandEnvelope>
{
    public void Configure(EntityTypeBuilder<CommandEnvelope> builder)
    {
        builder.ToTable("CommandEnvelopes");

        builder.Property(e => e.PayloadJson).IsRequired();
        builder.Property(e => e.CommandTypeFullName).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>();

        builder.HasIndex(e => e.CorrelationId)
            .IsUnique()
            .HasFilter(PlatformConfigurationFilters.ActiveRowsFilter);
        builder.HasIndex(e => new { e.Status, e.NextAttemptAt })
            .HasFilter(PlatformConfigurationFilters.ActiveRowsFilter);
        builder.HasIndex(e => new { e.Status, e.ProcessingStartedAtUtc })
            .HasFilter(PlatformConfigurationFilters.ActiveRowsFilter);

        builder.HasMany(e => e.ExecutionLogs)
            .WithOne(l => l.CommandEnvelope)
            .HasForeignKey(l => l.CommandEnvelopeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
