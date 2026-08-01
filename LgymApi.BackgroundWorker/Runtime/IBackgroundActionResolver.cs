namespace LgymApi.BackgroundWorker.Runtime;

public interface IBackgroundActionResolver
{
    IReadOnlyList<string> GetHandlerTypeNames(Type commandType);

    IBackgroundActionResolutionScope CreateScope(Type commandType);
}

public interface IBackgroundActionResolutionScope : IDisposable
{
    IReadOnlyList<string> HandlerTypeNames { get; }

    object ResolveHandler(string handlerTypeName);
}
