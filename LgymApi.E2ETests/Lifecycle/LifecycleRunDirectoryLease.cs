using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.Lifecycle;

internal sealed class LifecycleRunDirectoryLease : IAsyncDisposable
{
    private readonly PrivateRunDirectoryLease _runLease;
    private readonly HashSet<string> _caseIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _finalizationLock = new(1, 1);
    private int _finalized;

    private LifecycleRunDirectoryLease(PrivateRunDirectoryLease runLease)
    {
        _runLease = runLease;
        RunId = Path.GetFileName(runLease.RunDirectory);
        PrivateRunDirectoryLease.EnsureCanonicalLifecycleId(RunId);
    }

    internal string RunId { get; }

    internal string RunDirectory => _runLease.RunDirectory;

    internal PrivateRunDirectoryLease RunLease => _runLease;

    internal static LifecycleRunDirectoryLease Create(
        PrivateRunDirectoryRequest request,
        IRunDirectoryCleaner? cleaner = null) =>
        new(PrivateRunDirectoryLease.Create(request, cleaner));

    internal LifecycleScenarioDirectoryLease CreateScenario(string caseId)
    {
        PrivateRunDirectoryLease.EnsureCanonicalLifecycleId(caseId);
        if (Volatile.Read(ref _finalized) != 0 || !_caseIds.Add(caseId))
        {
            throw new InvalidOperationException(PrivateRunDirectoryLease.PathValidationMessage);
        }

        try
        {
            return new LifecycleScenarioDirectoryLease(_runLease, caseId);
        }
        catch
        {
            _runLease.DeleteLifecycleScenarioAsync(caseId).GetAwaiter().GetResult();
            _caseIds.Remove(caseId);
            throw;
        }
    }

    internal ValueTask FinalizeSuccessAsync() =>
        FinalizeAsync(success: true);

    internal ValueTask FinalizeFailureAsync() =>
        FinalizeAsync(success: false);

    public ValueTask DisposeAsync() =>
        FinalizeSuccessAsync();

    private async ValueTask FinalizeAsync(bool success)
    {
        await _finalizationLock.WaitAsync();
        try
        {
            if (Volatile.Read(ref _finalized) != 0)
            {
                return;
            }

            if (success)
            {
                await _runLease.DisposeAsync();
            }
            else
            {
                await _runLease.FinalizeLifecycleFailureAsync();
            }

            Volatile.Write(ref _finalized, 1);
        }
        finally
        {
            _finalizationLock.Release();
        }
    }
}

internal sealed class LifecycleScenarioDirectoryLease : IAsyncDisposable
{
    private readonly PrivateRunDirectoryLease _runLease;
    private readonly HashSet<string> _components = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private int _disposed;

    internal LifecycleScenarioDirectoryLease(PrivateRunDirectoryLease runLease, string caseId)
    {
        _runLease = runLease;
        CaseId = caseId;
        ScenarioDirectory = runLease.CreateLifecycleScenarioDirectory(caseId);
        ArtifactDirectory = runLease.CreateLifecycleArtifactDirectory(caseId);
    }

    internal string CaseId { get; }

    internal string ScenarioDirectory { get; }

    internal string ArtifactDirectory { get; }

    internal LifecycleComponentDirectoryLease CreateApiComponent() =>
        CreateComponent("api");

    internal LifecycleComponentDirectoryLease CreateWebRuntimeComponent() =>
        CreateComponent("web-runtime");

    internal LifecycleComponentDirectoryLease CreateBrowserRuntimeComponent() =>
        CreateComponent("browser-runtime");

    public async ValueTask DisposeAsync()
    {
        await _disposeLock.WaitAsync();
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            await _runLease.DeleteLifecycleScenarioAsync(CaseId);
            Volatile.Write(ref _disposed, 1);
        }
        finally
        {
            _disposeLock.Release();
        }
    }

    private LifecycleComponentDirectoryLease CreateComponent(string componentName)
    {
        if (Volatile.Read(ref _disposed) != 0 || !_components.Add(componentName))
        {
            throw new InvalidOperationException(PrivateRunDirectoryLease.PathValidationMessage);
        }

        try
        {
            return new LifecycleComponentDirectoryLease(_runLease, CaseId, componentName);
        }
        catch
        {
            _components.Remove(componentName);
            throw;
        }
    }
}

internal sealed class LifecycleComponentDirectoryLease : IAsyncDisposable
{
    private readonly PrivateRunDirectoryLease _runLease;
    private readonly string _caseId;
    private readonly string _componentName;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private int _disposed;

    internal LifecycleComponentDirectoryLease(PrivateRunDirectoryLease runLease, string caseId, string componentName)
    {
        _runLease = runLease;
        _caseId = caseId;
        _componentName = componentName;
        ComponentDirectory = runLease.CreateLifecycleComponentDirectory(caseId, componentName);
    }

    internal string ComponentDirectory { get; }

    internal void EnsureSafeArtifact(string artifactPath) =>
        _runLease.EnsureSafeLifecycleComponentArtifact(_caseId, _componentName, artifactPath);

    public async ValueTask DisposeAsync()
    {
        await _disposeLock.WaitAsync();
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            await _runLease.DeleteLifecycleComponentAsync(_caseId, _componentName);
            Volatile.Write(ref _disposed, 1);
        }
        finally
        {
            _disposeLock.Release();
        }
    }
}
