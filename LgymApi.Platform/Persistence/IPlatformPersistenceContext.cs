using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace LgymApi.Platform.Persistence;

internal interface IPlatformPersistenceContext
{
    DbSet<AppConfig> AppConfigs { get; }
    DbSet<ActionExecutionLog> ActionExecutionLogs { get; }
    DbSet<CommandEnvelope> CommandEnvelopes { get; }
    DbSet<ApiIdempotencyRecord> ApiIdempotencyRecords { get; }

    EntityEntry<CommandEnvelope> Entry(CommandEnvelope entity);
}
