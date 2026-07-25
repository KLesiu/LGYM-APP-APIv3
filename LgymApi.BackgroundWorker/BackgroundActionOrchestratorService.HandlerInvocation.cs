using System.Collections.Concurrent;
using System.Linq.Expressions;
using LgymApi.BackgroundWorker.Actions.Contracts;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using Microsoft.Extensions.Logging;

namespace LgymApi.BackgroundWorker;

public sealed partial class BackgroundActionOrchestratorService
{
    // Delegate type for cached handler invocation
    private delegate Task HandlerInvoker(object handler, object command, CancellationToken cancellationToken);

    // Cache compiled invokers per command type (setup-time reflection, execution-time cached delegates)
    private static readonly ConcurrentDictionary<Type, HandlerInvoker> _invokerCache = new();

    /// <summary>
    /// Executes a single handler in an isolated scope.
    /// Resolves handler instance from scope to ensure isolated scoped dependencies.
    /// Returns execution result with success flag and error details.
    /// </summary>
    private async Task<HandlerExecutionResult> ExecuteHandlerInIsolatedScopeAsync(
        object command,
        Type commandType,
        string canonicalCommandId,
        string expectedHandlerTypeName,
        CancellationToken cancellationToken)
    {
        var resolvedHandlerTypeName = expectedHandlerTypeName;
        try
        {
            using var scope = _backgroundActionResolver.CreateScope(commandType);
            var handler = scope.ResolveHandler(expectedHandlerTypeName);
            resolvedHandlerTypeName = handler.GetType().FullName ?? expectedHandlerTypeName;

            // Get or create cached invoker delegate for this command type
            var invoker = _invokerCache.GetOrAdd(commandType, static cmdType =>
            {
                // Build expression: (handler, cmd, ct) => ((IBackgroundAction<TCommand>)handler).ExecuteAsync((TCommand)cmd, ct)
                var handlerParam = Expression.Parameter(typeof(object), "handler");
                var commandParam = Expression.Parameter(typeof(object), "command");
                var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

                var interfaceType = typeof(IBackgroundAction<>).MakeGenericType(cmdType);
                var executeMethod = interfaceType.GetMethod(nameof(IBackgroundAction<IActionCommand>.ExecuteAsync))
                    ?? throw new InvalidOperationException($"ExecuteAsync method not found on {interfaceType.FullName}");

                var handlerCast = Expression.Convert(handlerParam, interfaceType);
                var commandCast = Expression.Convert(commandParam, cmdType);
                var methodCall = Expression.Call(handlerCast, executeMethod, commandCast, ctParam);

                return Expression.Lambda<HandlerInvoker>(methodCall, handlerParam, commandParam, ctParam).Compile();
            });

            // Execute via cached compiled delegate (no MethodInfo.Invoke)
            await invoker(handler, command, cancellationToken);

            _logger.LogInformation(
                "Handler {HandlerType} executed successfully for command {CommandId}.",
                resolvedHandlerTypeName,
                canonicalCommandId);

            return new HandlerExecutionResult
            {
                Success = true,
                HandlerTypeName = resolvedHandlerTypeName
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Exceptions from compiled delegates are direct (no TargetInvocationException wrapping)
            var inner = ex;

            _logger.LogError(ex,
                "Handler {HandlerType} failed for command {CommandId}.",
                resolvedHandlerTypeName,
                canonicalCommandId);

            return new HandlerExecutionResult
            {
                Success = false,
                ErrorMessage = $"{resolvedHandlerTypeName}: {inner.Message}",
                HandlerTypeName = resolvedHandlerTypeName,
                ErrorDetails = inner.ToString() // Full exception with stack trace
            };
        }
    }
}
