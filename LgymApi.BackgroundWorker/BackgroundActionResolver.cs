using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker.Actions.Contracts;
using LgymApi.BackgroundWorker.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.BackgroundWorker;

public sealed class BackgroundActionResolver : IBackgroundActionResolver
{
    private readonly IServiceScopeFactory _scopeFactory;

    public BackgroundActionResolver(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public IReadOnlyList<string> GetHandlerTypeNames(Type commandType)
    {
        using var scope = CreateScope(commandType);
        return scope.HandlerTypeNames;
    }

    public IBackgroundActionResolutionScope CreateScope(Type commandType)
    {
        var scope = _scopeFactory.CreateScope();
        return new BackgroundActionResolutionScope(scope, CreateHandlerContract(commandType));
    }

    private static Type CreateHandlerContract(Type commandType)
        => typeof(IBackgroundAction<>).MakeGenericType(commandType);

    private sealed class BackgroundActionResolutionScope : IBackgroundActionResolutionScope
    {
        private readonly IServiceScope _scope;
        private readonly IReadOnlyList<object> _handlers;

        public BackgroundActionResolutionScope(IServiceScope scope, Type handlerContract)
        {
            _scope = scope;
            _handlers = scope.ServiceProvider.GetServices(handlerContract)
                .Where(handler => handler is not null)
                .Cast<object>()
                .ToList();
            HandlerTypeNames = _handlers
                .Select(handler => handler.GetType().FullName ?? "UnknownHandler")
                .ToList();
        }

        public IReadOnlyList<string> HandlerTypeNames { get; }

        public object ResolveHandler(string handlerTypeName)
        {
            return _handlers.FirstOrDefault(handler => handler.GetType().FullName == handlerTypeName)
                ?? throw new InvalidOperationException(
                    $"Handler with type '{handlerTypeName}' is not registered in the action execution scope.");
        }

        public void Dispose()
        {
            _scope.Dispose();
        }
    }
}
