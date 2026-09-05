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

        if (_dbContext.Database.CurrentTransaction is { } currentTransaction)
        {
            await SetActorAsync(
                GetNpgsqlConnection(),
                GetNpgsqlTransaction(currentTransaction),
                actorId,
                cancellationToken);
            return NonOwningActorRowSecurityScope.Instance;
        }

        var unitOfWorkTransaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            await SetActorAsync(
                GetNpgsqlConnection(),
                GetNpgsqlTransaction(_dbContext.Database.CurrentTransaction),
                actorId,
                cancellationToken);
            return unitOfWorkTransaction;
        }
        catch
        {
            await unitOfWorkTransaction.DisposeAsync();
            throw;
        }
    }

    private NpgsqlConnection GetNpgsqlConnection()
    {
        return _dbContext.Database.GetDbConnection() as NpgsqlConnection
            ?? throw new NotSupportedException("Actor row-security scopes require the PostgreSQL Npgsql provider.");
    }

    private static NpgsqlTransaction GetNpgsqlTransaction(IDbContextTransaction? transaction)
    {
        return transaction?.GetDbTransaction() as NpgsqlTransaction
            ?? throw new NotSupportedException("Actor row-security scopes require the PostgreSQL Npgsql provider.");
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
