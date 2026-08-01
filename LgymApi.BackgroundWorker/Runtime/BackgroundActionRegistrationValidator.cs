using LgymApi.BackgroundWorker.Actions.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.BackgroundWorker.Runtime;

public static class BackgroundActionRegistrationValidator
{
    public static void Validate(IServiceCollection services, CommandContractRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registry);

        var expected = registry.Contracts
            .SelectMany(contract => contract.ExpectedHandlerTypes.Select(handlerType =>
                new HandlerRegistration(contract.CanonicalId, contract.RuntimeType, handlerType)))
            .OrderBy(registration => registration.CommandType.FullName, StringComparer.Ordinal)
            .ThenBy(registration => registration.HandlerType.FullName, StringComparer.Ordinal)
            .ToArray();

        var actual = services
            .Where(descriptor => descriptor.ServiceType.IsConstructedGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IBackgroundAction<>))
            .Select(descriptor => CreateActualRegistration(descriptor, registry))
            .OrderBy(registration => registration.CommandType.FullName, StringComparer.Ordinal)
            .ThenBy(registration => registration.HandlerType.FullName, StringComparer.Ordinal)
            .ToArray();

        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                "Background action registration mismatch. "
                + $"Expected [{Format(expected)}]; actual [{Format(actual)}].");
        }
    }

    private static HandlerRegistration CreateActualRegistration(
        ServiceDescriptor descriptor,
        CommandContractRegistry registry)
    {
        var commandType = descriptor.ServiceType.GetGenericArguments()[0];
        var handlerType = descriptor.ImplementationType
            ?? throw new InvalidOperationException(
                $"Background action registration '{descriptor.ServiceType}' must use an implementation type.");
        var canonicalId = registry.Contracts
            .SingleOrDefault(contract => contract.RuntimeType == commandType)
            ?.CanonicalId
            ?? throw new InvalidOperationException(
                $"Background action registration for '{commandType}' with handler '{handlerType}' is absent from the closed command registry.");

        return new HandlerRegistration(canonicalId, commandType, handlerType);
    }

    private static string Format(IEnumerable<HandlerRegistration> registrations) =>
        string.Join(", ", registrations.Select(registration =>
            $"{registration.CanonicalId}: {registration.CommandType.FullName} -> {registration.HandlerType.FullName}"));

    private sealed record HandlerRegistration(string CanonicalId, Type CommandType, Type HandlerType);
}
