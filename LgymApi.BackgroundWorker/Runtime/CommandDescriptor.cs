namespace LgymApi.BackgroundWorker.Runtime;

public sealed class CommandDescriptor : IEquatable<CommandDescriptor>
{
    public CommandDescriptor(CommandContractRegistry registry, Type runtimeType)
        : this(registry, registry.GetContractForWrite(runtimeType))
    {
    }

    internal CommandDescriptor(CommandContractRegistry registry, CommandContract contract)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(contract);

        CanonicalId = contract.CanonicalId;
        RuntimeType = contract.RuntimeType;
    }

    public string CanonicalId { get; }

    public Type RuntimeType { get; }

    public static CommandDescriptor FromPersistedId(CommandContractRegistry registry, string persistedId) =>
        registry.Resolve(persistedId);

    public bool Equals(CommandDescriptor? other) =>
        other != null && string.Equals(CanonicalId, other.CanonicalId, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is CommandDescriptor other && Equals(other);

    public override int GetHashCode() => CanonicalId.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => CanonicalId;
}
