using LgymApi.Application.Repositories;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Platform.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace LgymApi.Infrastructure.RowSecurity;

internal sealed class EfActorRowSecurityScopeFactory : IActorRowSecurityScopeFactory
{
    private readonly AppDbContext _dbContext;
    private readonly IUnitOfWork _unitOfWork;

    public EfActorRowSecurityScopeFactory(AppDbContext dbContext, IUnitOfWork unitOfWork)
    {
        _dbContext = dbContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<IUnitOfWorkTransaction> BeginAsync(
        Id<ActorReference> actorId,
        CancellationToken cancellationToken = default)
    {
        if (actorId.IsEmpty)
        {
            throw new ArgumentException("Actor ID cannot be empty.", nameof(actorId));
        }

        if (!_dbContext.Database.IsRelational())
        {
            return await _unitOfWork.BeginTransactionAsync(cancellationToken);
        }

        var unitOfWorkTransaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction
                ?? throw new NotSupportedException("Actor row-security scopes require the PostgreSQL Npgsql provider.");
            var connection = _dbContext.Database.GetDbConnection() as NpgsqlConnection
                ?? throw new NotSupportedException("Actor row-security scopes require the PostgreSQL Npgsql provider.");

            await SetActorAsync(connection, transaction, actorId, cancellationToken);
            return unitOfWorkTransaction;
        }
        catch
        {
            await unitOfWorkTransaction.DisposeAsync();
            throw;
        }
    }

    private static async Task SetActorAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Id<ActorReference> actorId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT set_config('lgym.account_id', @actorId, true);";
        command.Parameters.AddWithValue("actorId", actorId.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
