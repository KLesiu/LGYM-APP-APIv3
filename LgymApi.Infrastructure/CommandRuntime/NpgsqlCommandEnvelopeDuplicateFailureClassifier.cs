using LgymApi.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LgymApi.Infrastructure.CommandRuntime;

internal sealed class NpgsqlCommandEnvelopeDuplicateFailureClassifier : ICommandEnvelopeDuplicateFailureClassifier
{
    public bool IsDuplicateCorrelationFailure(Exception commitFailure)
    {
        return commitFailure is DbUpdateException
        {
            InnerException: PostgresException postgres
        }
        && postgres.SqlState == PostgresErrorCodes.UniqueViolation
        && postgres.ConstraintName == "IX_CommandEnvelopes_CorrelationId";
    }
}
