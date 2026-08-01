namespace LgymApi.BackgroundWorker.Runtime;

public sealed class CommandTypeDiscriminatorPolicy
{
    private readonly CommandContractRegistry _registry;

    public CommandTypeDiscriminatorPolicy(CommandContractRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public string GetDiscriminator(Type runtimeType) =>
        _registry.DescribeForWrite(runtimeType).CanonicalId;

    public Type ResolveType(string discriminator) =>
        _registry.Resolve(discriminator).RuntimeType;

    public bool IsExactMatch(string leftDiscriminator, string rightDiscriminator)
    {
        if (string.IsNullOrWhiteSpace(leftDiscriminator)
            || string.IsNullOrWhiteSpace(rightDiscriminator))
        {
            return false;
        }

        try
        {
            var left = _registry.Resolve(leftDiscriminator);
            var right = _registry.Resolve(rightDiscriminator);
            return left.Equals(right);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
