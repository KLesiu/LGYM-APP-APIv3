using System.Reflection;
using LgymApi.E2ETests.Harness;
using Microsoft.Playwright;

namespace LgymApi.E2ETests.Browser;

[TestFixture]
[Category("Task7BrowserScenario")]
[Category("WebHarness")]
public sealed class BrowserScenarioLeaseTests
{
    [Test]
    public async Task Browser_context_is_fresh_for_each_scenario_lease()
    {
        await using var paths = CreatePaths();
        var factory = new RecordingScenarioBrowserRuntimeFactory();
        await using var browserRun = await BrowserRunLease.CreateAsync(new BrowserRunRequest(paths, 15_000), factory);

        await using (var first = await BrowserScenarioLease.CreateAsync(browserRun, 15_000))
        {
            factory.Contexts[0].Cookies["task-7-cookie"] = "context-a";
            factory.Contexts[0].LocalStorage["task-7-storage"] = "context-a";
        }

        await using var second = await BrowserScenarioLease.CreateAsync(browserRun, 15_000);

        Assert.Multiple(() =>
        {
            Assert.That(factory.Contexts, Has.Count.EqualTo(2));
            Assert.That(factory.Contexts[0].Context, Is.Not.SameAs(factory.Contexts[1].Context));
            Assert.That(factory.Contexts[0].Page, Is.Not.SameAs(factory.Contexts[1].Page));
            Assert.That(factory.Contexts[1].Cookies, Does.Not.ContainKey("task-7-cookie"));
            Assert.That(factory.Contexts[1].LocalStorage, Does.Not.ContainKey("task-7-storage"));
            Assert.That(factory.ContextOptions, Has.All.Matches<BrowserNewContextOptions>(options =>
                options.Locale == BrowserScenarioLease.Locale &&
                options.BaseURL == BrowserScenarioLease.BaseUrl &&
                options.StorageState is null &&
                options.StorageStatePath is null &&
                options.RecordHarPath is null &&
                options.RecordVideoDir is null));
            Assert.That(factory.Contexts, Has.All.Matches<RecordingScenarioContext>(context =>
                context.DefaultTimeout == 15_000));
        });
    }

    [Test]
    public async Task BrowserScenario_disposal_closes_page_then_context_once()
    {
        await using var paths = CreatePaths();
        var factory = new RecordingScenarioBrowserRuntimeFactory();
        await using var browserRun = await BrowserRunLease.CreateAsync(new BrowserRunRequest(paths, 500), factory);
        var lease = await BrowserScenarioLease.CreateAsync(browserRun, 500);

        await Task.WhenAll(lease.DisposeAsync().AsTask(), lease.DisposeAsync().AsTask());

        Assert.That(factory.Contexts.Single().Events, Is.EqualTo(new[] { "page-close", "context-close" }));
    }

    [Test]
    public async Task BrowserScenario_page_creation_failure_closes_partial_context_and_keeps_run_disposable()
    {
        await using var paths = CreatePaths();
        var factory = new RecordingScenarioBrowserRuntimeFactory { PageCreationFailure = new PlaywrightException("page canary") };
        var browserRun = await BrowserRunLease.CreateAsync(new BrowserRunRequest(paths, 500), factory);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await BrowserScenarioLease.CreateAsync(browserRun, 500));
        await browserRun.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(BrowserScenarioLease.SetupMessage));
            Assert.That(exception.ToString(), Does.Not.Contain("page canary"));
            Assert.That(factory.Contexts.Single().Events, Is.EqualTo(new[] { "context-close" }));
            Assert.That(factory.RunEvents, Is.EqualTo(new[] { "browser-close", "playwright-dispose" }));
        });
    }

    [Test]
    public async Task BrowserScenario_context_creation_failure_leaves_run_disposable()
    {
        await using var paths = CreatePaths();
        var factory = new RecordingScenarioBrowserRuntimeFactory { ContextCreationFailure = new PlaywrightException("context canary") };
        var browserRun = await BrowserRunLease.CreateAsync(new BrowserRunRequest(paths, 500), factory);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await BrowserScenarioLease.CreateAsync(browserRun, 500));
        await browserRun.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(BrowserScenarioLease.SetupMessage));
            Assert.That(exception.ToString(), Does.Not.Contain("context canary"));
            Assert.That(factory.Contexts, Is.Empty);
            Assert.That(factory.RunEvents, Is.EqualTo(new[] { "browser-close", "playwright-dispose" }));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Lifecycle_page_failure_retains_partial_context_close_until_terminal(bool closeFaults)
    {
        await using var paths = CreatePaths();
        var closeCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new RecordingScenarioBrowserRuntimeFactory
        {
            PageCreationFailure = new PlaywrightException("page canary"),
            ContextCloseCompletion = closeCompletion.Task
        };
        var browserRun = await BrowserRunLease.CreateAsync(new BrowserRunRequest(paths, 100), factory);
        var creation = BrowserScenarioLifecycleAdapter.CreateAsync(browserRun, 100);

        try
        {
            await factory.ContextCloseStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await Task.Delay(250);
            Assert.That(creation.IsCompleted, Is.False);

            if (closeFaults)
            {
                closeCompletion.SetException(new PlaywrightException("close canary"));
            }
            else
            {
                closeCompletion.SetResult();
            }

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await creation);
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(BrowserScenarioLease.SetupMessage));
                Assert.That(exception.ToString(), Does.Not.Contain("page canary").And.Not.Contain("close canary"));
                Assert.That(factory.Contexts.Single().ContextCloseCount, Is.EqualTo(1));
            });
        }
        finally
        {
            closeCompletion.TrySetResult();
            try
            {
                await creation;
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                await closeCompletion.Task;
            }
            catch (PlaywrightException)
            {
            }

            await browserRun.DisposeAsync();
        }
    }

    private static PrivateRunDirectoryLease CreatePaths() =>
        PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            RepositoryRoot.Find(),
            ".e2e-private/runs",
            TimeSpan.FromSeconds(2)));
}

internal sealed class RecordingScenarioBrowserRuntimeFactory : IBrowserRuntimeFactory
{
    internal List<string> RunEvents { get; } = [];
    internal List<BrowserNewContextOptions> ContextOptions { get; } = [];
    internal List<RecordingScenarioContext> Contexts { get; } = [];
    internal Exception? ContextCreationFailure { get; init; }
    internal Exception? PageCreationFailure { get; init; }
    internal Task? ContextCloseCompletion { get; init; }
    internal TaskCompletionSource ContextCloseStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<IBrowserRuntime> CreateAsync() =>
        Task.FromResult<IBrowserRuntime>(new RecordingScenarioBrowserRuntime(this));

    private sealed class RecordingScenarioBrowserRuntime(RecordingScenarioBrowserRuntimeFactory owner) : IBrowserRuntime
    {
        public Task<IBrowserHandle> LaunchChromiumAsync(BrowserTypeLaunchOptions options) =>
            Task.FromResult<IBrowserHandle>(new RecordingScenarioBrowserHandle(owner));

        public void Dispose() => owner.RunEvents.Add("playwright-dispose");
    }

    private sealed class RecordingScenarioBrowserHandle(RecordingScenarioBrowserRuntimeFactory owner) : IBrowserHandle
    {
        public Task<IBrowserContext> NewContextAsync(BrowserNewContextOptions options)
        {
            if (owner.ContextCreationFailure is not null)
            {
                throw owner.ContextCreationFailure;
            }

            owner.ContextOptions.Add(options);
            var context = new RecordingScenarioContext(
                owner.PageCreationFailure,
                owner.ContextCloseCompletion,
                owner.ContextCloseStarted);
            owner.Contexts.Add(context);
            return Task.FromResult(context.Context);
        }

        public Task CloseAsync()
        {
            owner.RunEvents.Add("browser-close");
            return Task.CompletedTask;
        }
    }
}

internal sealed class RecordingScenarioContext
{
    private readonly Exception? _pageCreationFailure;
    private readonly Task? _contextCloseCompletion;
    private readonly TaskCompletionSource? _contextCloseStarted;

    internal RecordingScenarioContext(
        Exception? pageCreationFailure,
        Task? contextCloseCompletion = null,
        TaskCompletionSource? contextCloseStarted = null)
    {
        _pageCreationFailure = pageCreationFailure;
        _contextCloseCompletion = contextCloseCompletion;
        _contextCloseStarted = contextCloseStarted;
        Page = PlaywrightInterfaceProxy.Create<IPage>(InvokePage);
        Context = PlaywrightInterfaceProxy.Create<IBrowserContext>(InvokeContext);
    }

    internal IBrowserContext Context { get; }
    internal IPage Page { get; }
    internal List<string> Events { get; } = [];
    internal Dictionary<string, string> Cookies { get; } = [];
    internal Dictionary<string, string> LocalStorage { get; } = [];
    internal float? DefaultTimeout { get; private set; }
    internal int ContextCloseCount { get; private set; }

    private object? InvokeContext(MethodInfo method, object?[]? arguments)
    {
        return method.Name switch
        {
            nameof(IBrowserContext.SetDefaultTimeout) => SetDefaultTimeout(arguments),
            nameof(IBrowserContext.NewPageAsync) => CreatePage(),
            nameof(IBrowserContext.CloseAsync) => CloseContext(),
            _ => PlaywrightInterfaceProxy.Default(method.ReturnType)
        };
    }

    private object? InvokePage(MethodInfo method, object?[]? arguments) =>
        method.Name == nameof(IPage.CloseAsync)
            ? Record("page-close")
            : PlaywrightInterfaceProxy.Default(method.ReturnType);

    private object? SetDefaultTimeout(object?[]? arguments)
    {
        DefaultTimeout = Convert.ToSingle(arguments![0]);
        return null;
    }

    private Task<IPage> CreatePage()
    {
        if (_pageCreationFailure is not null)
        {
            throw _pageCreationFailure;
        }

        return Task.FromResult(Page);
    }

    private Task CloseContext()
    {
        Events.Add("context-close");
        ContextCloseCount++;
        _contextCloseStarted?.TrySetResult();
        return _contextCloseCompletion ?? Task.CompletedTask;
    }

    private Task Record(string value)
    {
        Events.Add(value);
        return Task.CompletedTask;
    }
}

public class PlaywrightInterfaceProxy : DispatchProxy
{
    private Func<MethodInfo, object?[]?, object?> _handler = null!;

    public PlaywrightInterfaceProxy()
    {
    }

    internal static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = Create<T, PlaywrightInterfaceProxy>();
        ((PlaywrightInterfaceProxy)(object)proxy)._handler = handler;
        return proxy;
    }

    internal static object? Default(Type returnType)
    {
        if (returnType == typeof(void))
        {
            return null;
        }

        if (returnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments) =>
        _handler(targetMethod!, arguments);
}
