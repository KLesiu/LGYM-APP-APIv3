using LgymApi.BackgroundWorker.Runtime;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using Microsoft.Extensions.Logging;

namespace LgymApi.BackgroundWorker;

/// <summary>
/// Concrete typed command dispatcher.
/// Validates exact-type handler availability, performs idempotency checks, and persists a durable envelope.
/// </summary>
public sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly IBackgroundActionResolver _backgroundActionResolver;
    private readonly CommandContractRegistry _commandContractRegistry;
    private readonly ICommandEnvelopeRuntime _commandEnvelopeRuntime;
    private readonly ILogger<CommandDispatcher> _logger;

    public CommandDispatcher(
        IBackgroundActionResolver backgroundActionResolver,
        CommandContractRegistry commandContractRegistry,
        ICommandEnvelopeRuntime commandEnvelopeRuntime,
        ILogger<CommandDispatcher> logger)
    {
        _backgroundActionResolver = backgroundActionResolver;
        _commandContractRegistry = commandContractRegistry;
        _commandEnvelopeRuntime = commandEnvelopeRuntime;
        _logger = logger;
    }

    /// <summary>
    /// Persists a strongly-typed command for background action execution asynchronously.
    /// Validates exact-type handler availability (1:1), checks idempotency, and persists an envelope.
    /// Zero-handler path short-circuits safely with warning and no persistence.
    /// </summary>
    public async Task EnqueueAsync<TCommand>(TCommand command) where TCommand : class, IActionCommand
    {
        if (command == default(TCommand))
        {
            throw new ArgumentNullException(nameof(command));
        }

        var commandType = typeof(TCommand);

        // Validate exact-type handler availability (1:1 matching, no polymorphism)
        var handlerCount = _backgroundActionResolver.GetHandlerTypeNames(commandType).Count;

        if (handlerCount == 0)
        {
            _logger.LogWarning(
                "No handlers registered for command. Skipping durable envelope persistence.");
            return; // Zero-handler path: safe no-op, no failure, no persistence
        }

        var request = CommandEnvelopeFactory.Create(command, _commandContractRegistry);

        _logger.LogInformation(
                "Persisting command {CommandId} with correlation {CorrelationId}.",
            request.CommandId,
            "deterministic");

        _logger.LogInformation(
            "Found {HandlerCount} handler(s) for command {CommandId}.",
            handlerCount,
            request.CommandId);

        // Check idempotency: attempt to add envelope with unique CorrelationId
        // Uses DB-level uniqueness constraint (IX_CommandEnvelopes_CorrelationId) for atomic duplicate detection

        // AddOrGetExistingAsync records the envelope or returns an existing one.
        var receipt = await _commandEnvelopeRuntime.PersistAsync(request);

        _logger.LogInformation(
            "Command envelope {EnvelopeId} {Disposition} for command {CommandId}.",
            receipt.EnvelopeId,
            receipt.WasExisting ? "already existed" : "persisted",
            request.CommandId);
    }

    /// <summary>
    /// Computes a deterministic correlation ID from the canonical command ID and payload.
    /// Uses SHA256 hash to ensure identical commands produce identical correlation IDs for idempotency.
    /// </summary>
}
