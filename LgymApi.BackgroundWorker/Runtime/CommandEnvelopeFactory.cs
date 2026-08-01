using System.Text.Json;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Platform.Contracts.Serialization;

namespace LgymApi.BackgroundWorker.Runtime;

internal static class CommandEnvelopeFactory
{
    public static CommandEnvelopeRequest Create<TCommand>(
        TCommand command,
        CommandContractRegistry commandContractRegistry)
        where TCommand : class, IActionCommand
    {
        ArgumentNullException.ThrowIfNull(command);

        var commandType = typeof(TCommand);
        var descriptor = commandContractRegistry.DescribeForDispatch(commandType);
        var payloadJson = Serialize(command, commandType, descriptor.CanonicalId);

        return new CommandEnvelopeRequest(descriptor.CanonicalId, payloadJson);
    }

    public static string Serialize(object command, Type commandType, string canonicalCommandId)
    {
        return JsonSerializer.Serialize(command, commandType, SharedSerializationOptions.Current);
    }
}
