using System.Collections.Concurrent;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Api.Hubs;

public interface IAccountSessionConnectionRegistry
{
    void Register(Id<AccountReference> accountId, Id<AccountSessionReference> sessionId, string connectionId);

    void Remove(string connectionId);

    IReadOnlyList<AccountSessionConnection> GetConnections(Id<AccountReference> accountId);
}

public sealed record AccountSessionConnection(
    Id<AccountReference> AccountId,
    Id<AccountSessionReference> SessionId,
    string ConnectionId);

public sealed class AccountSessionConnectionRegistry : IAccountSessionConnectionRegistry
{
    private readonly ConcurrentDictionary<AccountSessionKey, ConcurrentDictionary<string, byte>> _connectionsBySession = new();
    private readonly ConcurrentDictionary<string, AccountSessionKey> _sessionByConnection = new(StringComparer.Ordinal);

    public void Register(Id<AccountReference> accountId, Id<AccountSessionReference> sessionId, string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        var key = new AccountSessionKey(accountId, sessionId);
        if (_sessionByConnection.TryGetValue(connectionId, out var previousKey) && previousKey != key)
        {
            Remove(connectionId);
        }

        var connections = _connectionsBySession.GetOrAdd(key, static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        connections.TryAdd(connectionId, 0);
        _sessionByConnection[connectionId] = key;
    }

    public void Remove(string connectionId)
    {
        if (!_sessionByConnection.TryRemove(connectionId, out var key))
        {
            return;
        }

        if (!_connectionsBySession.TryGetValue(key, out var connections))
        {
            return;
        }

        connections.TryRemove(connectionId, out _);
        if (connections.IsEmpty)
        {
            _connectionsBySession.TryRemove(KeyValuePair.Create(key, connections));
        }
    }

    public IReadOnlyList<AccountSessionConnection> GetConnections(Id<AccountReference> accountId)
    {
        return _connectionsBySession
            .Where(pair => pair.Key.AccountId == accountId)
            .SelectMany(pair => pair.Value.Keys.Select(connectionId => new AccountSessionConnection(
                pair.Key.AccountId,
                pair.Key.SessionId,
                connectionId)))
            .ToArray();
    }

    private readonly record struct AccountSessionKey(
        Id<AccountReference> AccountId,
        Id<AccountSessionReference> SessionId);
}
