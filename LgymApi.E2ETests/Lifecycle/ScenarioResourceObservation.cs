using System.Security.Cryptography;
using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.Lifecycle;

internal sealed class ScenarioResourceIdentity : IEquatable<ScenarioResourceIdentity>
{
    private readonly byte[] _value;

    private ScenarioResourceIdentity(byte[] value)
    {
        _value = value;
    }

    internal static ScenarioResourceIdentity Create() =>
        new(RandomNumberGenerator.GetBytes(32));

    public bool Equals(ScenarioResourceIdentity? other) =>
        other is not null && CryptographicOperations.FixedTimeEquals(_value, other._value);

    public override bool Equals(object? obj) => obj is ScenarioResourceIdentity other && Equals(other);

    public override int GetHashCode() => BitConverter.ToInt32(SHA256.HashData(_value), 0);

    public override string ToString() => "<scenario-resource-identity>";
}

internal sealed class ScenarioResourceObservation(
    ScenarioResourceIdentity identity,
    Func<Task<bool>> confirmAbsent)
{
    internal ScenarioResourceIdentity Identity { get; } = identity;

    internal Task<bool> ConfirmAbsentAsync() => confirmAbsent();

    public override string ToString() => "<scenario-resource-observation>";
}

internal sealed class ScenarioDatabaseOwnership(
    IApiHostDatabaseLease database,
    ScenarioResourceObservation observation) : IAsyncDisposable
{
    private IApiHostDatabaseLease? _database = database;

    internal ScenarioResourceObservation Observation { get; } = observation;

    internal IApiHostDatabaseLease TransferToApiHost() =>
        Interlocked.Exchange(ref _database, null)
        ?? throw new InvalidOperationException("Scenario database ownership has already transferred.");

    public async ValueTask DisposeAsync()
    {
        var database = Interlocked.Exchange(ref _database, null);
        if (database is not null)
        {
            await database.DisposeAsync();
        }
    }
}
