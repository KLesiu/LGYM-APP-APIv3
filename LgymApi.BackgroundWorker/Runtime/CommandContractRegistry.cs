using LgymApi.BackgroundWorker.Actions;
using LgymApi.BackgroundWorker.Actions.Contracts;
using ApplicationActionCommand = LgymApi.Application.Platform.Contracts.BackgroundCommands.IActionCommand;

namespace LgymApi.BackgroundWorker.Runtime;

public sealed record CommandContract(
    string CanonicalId,
    Type RuntimeType,
    string ReadAlias,
    IReadOnlyList<Type> ExpectedHandlerTypes);

public sealed partial class CommandContractRegistry
{
    public const string InvitationAcceptedCanonicalId =
        "LgymApi.BackgroundWorker.Common.Commands.InvitationAcceptedCommand";

    private readonly IReadOnlyDictionary<Type, CommandContract> _contractsByRuntimeType;
    private readonly IReadOnlyDictionary<string, CommandContract> _contractsByReadId;

    private CommandContractRegistry(IEnumerable<CommandContract> contracts, bool requireDefaultContract)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        var contractArray = contracts
            .Select(contract => contract with
            {
                ExpectedHandlerTypes = Array.AsReadOnly(contract.ExpectedHandlerTypes?.ToArray() ?? [])
            })
            .ToArray();

        Validate(contractArray);
        if (requireDefaultContract)
        {
            ValidateDefaultContract(contractArray);
        }

        Contracts = Array.AsReadOnly(contractArray);
        _contractsByRuntimeType = contractArray.ToDictionary(contract => contract.RuntimeType);
        _contractsByReadId = contractArray
            .SelectMany(contract => new[]
            {
                KeyValuePair.Create(contract.CanonicalId, contract),
                KeyValuePair.Create(contract.ReadAlias, contract)
            })
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    public IReadOnlyList<CommandContract> Contracts { get; }

    public static CommandContractRegistry CreateDefault() =>
        new(CreateDefaultContracts(), requireDefaultContract: true);

    public static CommandContractRegistry CreateForTesting(
        IEnumerable<CommandContract> contracts,
        bool requireDefaultContract = false) =>
        new(contracts, requireDefaultContract);

    public CommandDescriptor DescribeForWrite(Type runtimeType)
    {
        return new CommandDescriptor(this, GetContractForWrite(runtimeType));
    }

    internal CommandContract GetContractForWrite(Type runtimeType)
    {
        ArgumentNullException.ThrowIfNull(runtimeType);

        if (!_contractsByRuntimeType.TryGetValue(runtimeType, out var contract))
        {
            throw new InvalidOperationException(
                $"Runtime command type '{runtimeType}' is absent from the closed command registry.");
        }

        return contract;
    }

    public CommandDescriptor Resolve(string persistedId)
    {
        if (string.IsNullOrWhiteSpace(persistedId))
        {
            throw new ArgumentException("Durable command identifier must not be empty.", nameof(persistedId));
        }

        if (!_contractsByReadId.TryGetValue(persistedId, out var contract))
        {
            throw new InvalidOperationException($"Unknown durable command identifier '{persistedId}'.");
        }

        return new CommandDescriptor(this, contract);
    }

    internal CommandDescriptor DescribeForDispatch(Type commandType)
    {
        return new CommandDescriptor(this, GetContractForWrite(commandType));
    }

    private static void Validate(IReadOnlyList<CommandContract> contracts)
    {
        if (contracts.Any(contract => string.IsNullOrWhiteSpace(contract.CanonicalId)))
        {
            throw new InvalidOperationException("Canonical command IDs must not be empty.");
        }

        if (contracts.Any(contract => string.IsNullOrWhiteSpace(contract.ReadAlias)))
        {
            throw new InvalidOperationException("Command read aliases must not be empty.");
        }

        if (contracts.Select(contract => contract.CanonicalId).Distinct(StringComparer.Ordinal).Count() != contracts.Count)
        {
            throw new InvalidOperationException("Canonical command IDs must be unique.");
        }

        if (contracts.Select(contract => contract.ReadAlias).Distinct(StringComparer.Ordinal).Count() != contracts.Count)
        {
            throw new InvalidOperationException("Command read aliases must be unique.");
        }

        if (contracts.Select(contract => contract.RuntimeType).Distinct().Count() != contracts.Count)
        {
            throw new InvalidOperationException("Runtime command types must be unique.");
        }

        if (contracts.Select(contract => contract.CanonicalId)
            .Intersect(contracts.Select(contract => contract.ReadAlias), StringComparer.Ordinal)
            .Any())
        {
            throw new InvalidOperationException("Canonical command IDs and read aliases must not overlap.");
        }

        foreach (var contract in contracts)
        {
            if (!typeof(ApplicationActionCommand).IsAssignableFrom(contract.RuntimeType))
            {
                throw new InvalidOperationException(
                    $"Runtime type '{contract.RuntimeType}' must implement IActionCommand.");
            }

            if (!string.Equals(contract.RuntimeType.FullName, contract.ReadAlias, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Read alias for '{contract.CanonicalId}' must equal its runtime CLR FullName.");
            }

            if (contract.ExpectedHandlerTypes == null || contract.ExpectedHandlerTypes.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Command '{contract.CanonicalId}' must declare at least one expected handler.");
            }

            if (contract.ExpectedHandlerTypes.Distinct().Count() != contract.ExpectedHandlerTypes.Count)
            {
                throw new InvalidOperationException(
                    $"Expected handlers for '{contract.CanonicalId}' must be unique.");
            }

            foreach (var handlerType in contract.ExpectedHandlerTypes)
            {
                if (handlerType == null)
                {
                    throw new InvalidOperationException(
                        $"Handler metadata for '{contract.CanonicalId}' does not target its runtime command.");
                }

                var handledCommandTypes = handlerType.GetInterfaces()
                    .Where(type => type.IsConstructedGenericType
                        && type.GetGenericTypeDefinition() == typeof(IBackgroundAction<>))
                    .Select(type => type.GetGenericArguments()[0]);

                if (!handledCommandTypes.Any(commandType => commandType == contract.RuntimeType))
                {
                    throw new InvalidOperationException(
                        $"Handler metadata for '{contract.CanonicalId}' does not target its runtime command.");
                }
            }
        }
    }

    private static void ValidateDefaultContract(IReadOnlyList<CommandContract> contracts)
    {
        var expectedContracts = CreateDefaultContracts();

        if (contracts.Count != expectedContracts.Length)
        {
            throw new InvalidOperationException("The default command registry must contain exactly 15 rows.");
        }

        if (contracts.Sum(contract => contract.ExpectedHandlerTypes.Count)
            != expectedContracts.Sum(contract => contract.ExpectedHandlerTypes.Count))
        {
            throw new InvalidOperationException("The default command registry must declare exactly 16 handlers.");
        }

        if (!contracts.Select(contract => contract.CanonicalId).ToHashSet(StringComparer.Ordinal)
            .SetEquals(expectedContracts.Select(contract => contract.CanonicalId)))
        {
            throw new InvalidOperationException("Default canonical command IDs must match the fixed 15-command contract.");
        }

        foreach (var expectedContract in expectedContracts)
        {
            var actualContract = contracts.Single(contract =>
                string.Equals(contract.CanonicalId, expectedContract.CanonicalId, StringComparison.Ordinal));

            if (actualContract.RuntimeType != expectedContract.RuntimeType
                || !string.Equals(actualContract.ReadAlias, expectedContract.ReadAlias, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Default runtime metadata mismatch for '{expectedContract.CanonicalId}'.");
            }

            if (!actualContract.ExpectedHandlerTypes.ToHashSet()
                .SetEquals(expectedContract.ExpectedHandlerTypes))
            {
                throw new InvalidOperationException(
                    $"Default handler metadata mismatch for '{expectedContract.CanonicalId}'.");
            }
        }
    }

}
