using LgymApi.E2ETests.Browser;
using LgymApi.E2ETests.Browser.Locators;
using LgymApi.E2ETests.Configuration;
using Microsoft.Playwright;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class WebHarnessSmokeTests
{
    private static readonly Uri ScenarioApiBaseUri = new("http://127.0.0.1:48123/");

    [Test]
    public async Task Pinned_source_is_exported_started_and_navigated_by_Chromium()
    {
        var fixture = CreateFixture();
        var sourceStateBefore = await fixture.InspectSourceStateAsync();
        WebSourceRunLease? source = null;
        ExpoWebLease? expo = null;
        PrivateRunDirectoryLease? browserPaths = null;
        BrowserRunLease? browser = null;
        BrowserScenarioLease? firstScenario = null;
        BrowserScenarioLease? secondScenario = null;
        Exception? primaryFailure = null;
        Exception? cleanupFailure = null;
        var renderedReady = false;
        var publicHttpBoundaryUsed = false;
        string? sourceRunDirectory = null;
        string? npmCacheDirectory = null;
        string? browserRunDirectory = null;

        try
        {
            source = await fixture.CreateSourceAsync();
            sourceRunDirectory = source.RunDirectory;
            npmCacheDirectory = source.NpmCacheDirectory;
            await source.EnsureInstalledAsync();
            Assert.That(source.IsInstalled, Is.True);
            Assert.That(
                File.ReadAllText(Path.Combine(source.SourceDirectory, "api", "custom-instance.ts")),
                Does.Contain("resolveBackendBaseUrl(process.env.REACT_APP_BACKEND)"));

            expo = await ExpoWebLease.StartAsync(
                new ExpoWebStartRequest(source, ScenarioApiBaseUri) { Options = fixture.Options });
            browserPaths = fixture.CreateBrowserPaths();
            browserRunDirectory = browserPaths.RunDirectory;
            browser = await BrowserRunLease.CreateAsync(new BrowserRunRequest(
                browserPaths,
                fixture.Options.Timeouts.BrowserActionMilliseconds));

            firstScenario = await BrowserScenarioLease.CreateAsync(
                browser,
                fixture.Options.Timeouts.BrowserActionMilliseconds);
            await firstScenario.Context.AddInitScriptAsync(
                "localStorage.setItem('user-language', 'en')");
            var observation = new BrowserReadinessObservation();
            firstScenario.Page.Console += (_, message) =>
            {
                if (message.Type == "error")
                {
                    observation.RecordConsoleError(message.Text);
                }
            };
            firstScenario.Page.PageError += (_, message) => observation.RecordPageError(message);
            firstScenario.Page.Response += (_, response) => observation.RecordResponse(response);
            firstScenario.Page.RequestFailed += (_, request) =>
                observation.RecordRequestFailure(request.ResourceType);

            await NavigateAsync(firstScenario.Page, "/", fixture.Options.Timeouts.WebStartupSeconds);
            observation.NavigationCommitted = true;
            publicHttpBoundaryUsed = true;
            await WaitForBootstrapRootAsync(
                firstScenario.Page,
                TimeSpan.FromSeconds(fixture.Options.Timeouts.WebStartupSeconds),
                observation);
            await AssertPreloadAsync(firstScenario.Page, observation);
            renderedReady = true;
            await NavigateToLoginAndBackAsync(firstScenario.Page);
            await NavigateToRegistrationAsync(firstScenario.Page);
            await firstScenario.Context.AddCookiesAsync(
            [
                new Microsoft.Playwright.Cookie
                {
                    Name = "task11-isolation",
                    Value = "present",
                    Domain = "localhost",
                    Path = "/"
                }
            ]);
            await firstScenario.Page.EvaluateAsync("localStorage.setItem('task11-isolation', 'present')");
            await firstScenario.DisposeAsync();
            firstScenario = null;

            secondScenario = await BrowserScenarioLease.CreateAsync(
                browser,
                fixture.Options.Timeouts.BrowserActionMilliseconds);
            await NavigateAsync(secondScenario.Page, "/", fixture.Options.Timeouts.WebStartupSeconds);
            var cookies = await secondScenario.Context.CookiesAsync();
            var localStorageValue = await secondScenario.Page.EvaluateAsync<string?>(
                "localStorage.getItem('task11-isolation')");

            Assert.Multiple(() =>
            {
                Assert.That(cookies, Is.Empty);
                Assert.That(localStorageValue, Is.Null);
                Assert.That(observation.ConsoleErrorCount, Is.Zero, "The unauthenticated landing flow emitted a browser console error.");
            });
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }
        finally
        {
            cleanupFailure = await DisposeAllAsync(
                secondScenario,
                firstScenario,
                browser,
                browserPaths,
                expo,
                source);
        }

        if (primaryFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (cleanupFailure is not null)
        {
            if (cleanupFailure is WebSourceRunCleanupException sourceCleanupFailure)
            {
                throw new InvalidOperationException($"{WebSourceRunLease.CleanupMessage} [{sourceCleanupFailure.Stage}]");
            }

            throw cleanupFailure;
        }

        var sourceStateAfter = await fixture.InspectSourceStateAsync();
        Assert.Multiple(() =>
        {
            Assert.That(sourceStateAfter, Is.EqualTo(sourceStateBefore));
            Assert.That(Directory.Exists(sourceRunDirectory!), Is.False);
            Assert.That(Directory.Exists(npmCacheDirectory!), Is.False);
            Assert.That(Directory.Exists(browserRunDirectory!), Is.False);
            Assert.That(expo!.CleanupReceipt, Is.Not.Null);
            Assert.That(expo.CleanupReceipt!.ProcessTreeAbsent, Is.True);
            Assert.That(expo.CleanupReceipt.DrainsCompleted, Is.True);
            Assert.That(expo.CleanupReceipt.InspectionCompleted, Is.True);
        });

        FinalWebHarnessEvidenceReceiptWriter.Write(new FinalWebHarnessEvidenceReceipt(
            fixture.Options.WebSource.CommitSha,
            sourceStateAfter == sourceStateBefore,
            true,
             source!.IsInstalled,
             renderedReady,
             true,
             expo!.BrowserSuppressed,
             expo.PortWasAvailableBeforeStart,
             publicHttpBoundaryUsed,
             true,
             true,
             expo.CleanupReceipt!.ProcessTreeAbsent,
             expo.CleanupReceipt.DrainsCompleted,
             expo.CleanupReceipt.InspectionCompleted,
             !Directory.Exists(sourceRunDirectory!),
             !Directory.Exists(npmCacheDirectory!),
             !Directory.Exists(browserRunDirectory!),
             LgymWebLocatorCatalog.Surfaces.Count));
    }

    private static async Task AssertPreloadAsync(IPage page, BrowserReadinessObservation? observation = null)
    {
        var preload = (PreloadPage)LgymWebLocatorCatalog.CreatePage(page, LgymWebSurface.Preload);
        try
        {
            await AssertStrictVisibleAsync(preload.Screen);
            await AssertStrictVisibleAsync(preload.Login);
            await AssertStrictVisibleAsync(preload.Register);
        }
        catch (TimeoutException)
        {
            observation?.CaptureRendererState(await BrowserRendererState.CaptureAsync(page));
            throw new InvalidOperationException(
                $"E2E browser page initialization failed: {observation?.FailureReport ?? "RendererStateUnavailable"}.");
        }
    }

    private static async Task WaitForBootstrapRootAsync(
        IPage page,
        TimeSpan timeout,
        BrowserReadinessObservation observation)
    {
        var preload = (PreloadPage)LgymWebLocatorCatalog.CreatePage(page, LgymWebSurface.Preload);
        try
        {
            await preload.Screen.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Attached,
                Timeout = (float)timeout.TotalMilliseconds
            });
        }
        catch (TimeoutException)
        {
            observation.CaptureRendererState(await BrowserRendererState.CaptureAsync(page));
            throw new InvalidOperationException(
                $"E2E browser page initialization failed: {observation.FailureReport}.");
        }
    }





    private static async Task NavigateToLoginAndBackAsync(IPage page)
    {
        var preload = (PreloadPage)LgymWebLocatorCatalog.CreatePage(page, LgymWebSurface.Preload);
        await preload.Login.ClickAsync();
        await page.WaitForURLAsync("**/Login");
        var login = (LoginPage)LgymWebLocatorCatalog.CreatePage(page, LgymWebSurface.Login);
        await AssertStrictVisibleAsync(login.Username);
        await AssertStrictVisibleAsync(login.Password);
        await AssertStrictVisibleAsync(login.Submit);
        await NavigateAsync(page, "/", 120);
        await AssertPreloadAsync(page);
    }

    private static async Task NavigateToRegistrationAsync(IPage page)
    {
        var preload = (PreloadPage)LgymWebLocatorCatalog.CreatePage(page, LgymWebSurface.Preload);
        await preload.Register.ClickAsync();
        await page.WaitForURLAsync("**/Register");
        var registration = (RegistrationPage)LgymWebLocatorCatalog.CreatePage(page, LgymWebSurface.Registration);
        foreach (var input in registration.Inputs)
        {
            await AssertStrictVisibleAsync(input);
        }

        await AssertStrictVisibleAsync(registration.Submit);
    }

    private static async Task AssertStrictVisibleAsync(ILocator locator)
    {
        await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.That(await locator.CountAsync(), Is.EqualTo(1));
    }

    private static Task NavigateAsync(IPage page, string url, int timeoutSeconds) => page.GotoAsync(
        url,
        new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Commit,
            Timeout = timeoutSeconds * 1000
        });

    private static async Task<Exception?> DisposeAllAsync(params IAsyncDisposable?[] resources)
    {
        Exception? failure = null;
        foreach (var resource in resources)
        {
            if (resource is null)
            {
                continue;
            }

            try
            {
                await resource.DisposeAsync();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }

        return failure;
    }

    private static WebHarnessSmokeFixture CreateFixture() => new(
        E2EConfiguration.Load(TestContext.CurrentContext.TestDirectory, RepositoryRoot.Find()),
        RepositoryRoot.Find());
}

internal enum BrowserPageErrorCategory { None, FirebaseWebCompatibility, Other }

internal enum BrowserResourceFailureCategory { None, StaticAsset, RequestFailedFont, RequestFailedScript, RequestFailedOther }

internal enum BrowserCountBucket { Zero, One, Many }

internal sealed record BrowserRendererState(
    string ReadyState,
    string FontStatus,
    BrowserCountBucket BodyChildCount,
    BrowserCountBucket RootChildCount,
    BrowserCountBucket LoginCount,
    BrowserCountBucket RegisterCount)
{
    internal static async Task<BrowserRendererState> CaptureAsync(IPage page)
    {
        var documentState = await page.EvaluateAsync<BrowserRendererDocumentState>("""
            () => ({
                readyState: document.readyState,
                fontStatus: document.fonts.status,
                bodyChildCount: document.body.childElementCount,
                rootChildCount: document.getElementById('root')?.childElementCount ?? 0
            })
            """);
        return new BrowserRendererState(
            documentState.ReadyState,
            documentState.FontStatus,
            ToBucket(documentState.BodyChildCount),
            ToBucket(documentState.RootChildCount),
            ToBucket(await page.GetByTestId(LgymWebTestIds.PreloadLogin).CountAsync()),
            ToBucket(await page.GetByTestId(LgymWebTestIds.PreloadRegister).CountAsync()));
    }

    public override string ToString() =>
        $"Ready={ReadyState};Fonts={FontStatus};Body={BodyChildCount};Root={RootChildCount};Login={LoginCount};Register={RegisterCount}";

    private static BrowserCountBucket ToBucket(int count) => count switch
    {
        0 => BrowserCountBucket.Zero,
        1 => BrowserCountBucket.One,
        _ => BrowserCountBucket.Many
    };

    internal static bool HasBootstrapRoot(int rootChildCount) => rootChildCount > 0;
}

internal sealed class BrowserRendererDocumentState
{
    public string ReadyState { get; init; } = string.Empty;

    public string FontStatus { get; init; } = string.Empty;

    public int BodyChildCount { get; init; }

    public int RootChildCount { get; init; }
}

internal sealed class BrowserReadinessObservation
{
    private int _pageErrors;
    private int _consoleErrors;
    private int _resourceFailures;

    internal bool NavigationCommitted { get; set; }

    internal int PageErrorCount => Volatile.Read(ref _pageErrors);

    internal int ConsoleErrorCount => Volatile.Read(ref _consoleErrors);

    internal int ResourceFailureCount => Volatile.Read(ref _resourceFailures);

    internal bool HasInitializationFailure => PageErrorCount != 0 || ResourceFailureCount != 0;

    internal BrowserPageErrorCategory PageErrorCategory { get; private set; }

    internal BrowserResourceFailureCategory ResourceFailureCategory { get; private set; }

    internal string InitializationFailureCategory => PageErrorCount != 0
        ? PageErrorCategory.ToString()
        : ResourceFailureCategory.ToString();

    internal BrowserRendererState? RendererState { get; private set; }

    internal string FailureReport =>
        $"{InitializationFailureCategory};PageErrors={PageErrorCount};ConsoleErrors={ConsoleErrorCount};ResourceFailures={ResourceFailureCount};{RendererState}";

    internal void RecordPageError(string message)
    {
        Interlocked.Increment(ref _pageErrors);
        if (message.Contains("firebase", StringComparison.OrdinalIgnoreCase))
        {
            PageErrorCategory = BrowserPageErrorCategory.FirebaseWebCompatibility;
        }
        else if (PageErrorCategory == BrowserPageErrorCategory.None)
        {
            PageErrorCategory = BrowserPageErrorCategory.Other;
        }
    }

    internal void RecordConsoleError(string _) => Interlocked.Increment(ref _consoleErrors);

    internal void RecordResponse(IResponse response)
    {
        RecordResourceResponse(response.Request.ResourceType, response.Status);
    }

    internal void RecordResourceResponse(string resourceType, int status)
    {
        if (resourceType is "font" or "stylesheet" or "image" && status >= 400)
        {
            Interlocked.Increment(ref _resourceFailures);
            ResourceFailureCategory = BrowserResourceFailureCategory.StaticAsset;
        }
    }

    internal void RecordRequestFailure(string resourceType)
    {
        Interlocked.Increment(ref _resourceFailures);
        ResourceFailureCategory = resourceType switch
        {
            "font" => BrowserResourceFailureCategory.RequestFailedFont,
            "script" => BrowserResourceFailureCategory.RequestFailedScript,
            _ => BrowserResourceFailureCategory.RequestFailedOther
        };
    }

    internal void CaptureRendererState(BrowserRendererState state) => RendererState = state;
}

[TestFixture]
public sealed class BrowserReadinessObservationTests
{
    [Test]
    public void Bootstrap_root_signal_rejects_shell_and_accepts_mounted_application()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BrowserRendererState.HasBootstrapRoot(0), Is.False);
            Assert.That(BrowserRendererState.HasBootstrapRoot(1), Is.True);
        });
    }

    [Test]
    public void Page_error_classifier_retains_Firebase_category_across_shared_observation()
    {
        var observation = new BrowserReadinessObservation();

        observation.RecordPageError("firebase web runtime failure");
        observation.RecordPageError("other");

        Assert.Multiple(() =>
        {
            Assert.That(observation.PageErrorCount, Is.EqualTo(2));
            Assert.That(observation.PageErrorCategory, Is.EqualTo(BrowserPageErrorCategory.FirebaseWebCompatibility));
        });
    }

    [Test]
    public void Resource_failure_classifier_records_failed_static_assets()
    {
        var observation = new BrowserReadinessObservation();

        observation.RecordResourceResponse("font", 404);

        Assert.Multiple(() =>
        {
            Assert.That(observation.ResourceFailureCount, Is.EqualTo(1));
            Assert.That(observation.ResourceFailureCategory, Is.EqualTo(BrowserResourceFailureCategory.StaticAsset));
            Assert.That(observation.InitializationFailureCategory, Is.EqualTo("StaticAsset"));
        });
    }

    [Test]
    public void Request_failure_classifier_distinguishes_font_failures_from_HTTP_static_asset_failures()
    {
        var observation = new BrowserReadinessObservation();

        observation.RecordRequestFailure("font");

        Assert.Multiple(() =>
        {
            Assert.That(observation.ResourceFailureCount, Is.EqualTo(1));
            Assert.That(observation.ResourceFailureCategory, Is.EqualTo(BrowserResourceFailureCategory.RequestFailedFont));
            Assert.That(observation.InitializationFailureCategory, Is.EqualTo("RequestFailedFont"));
        });
    }
}

internal sealed class WebHarnessSmokeFixture
{
    private readonly string _gitExecutable;
    private readonly ExternalGitCommandTimeouts _gitTimeouts;

    internal WebHarnessSmokeFixture(E2EOptions options, string repositoryRoot)
    {
        Options = options;
        RepositoryRoot = repositoryRoot;
        _gitExecutable = ApiRepositoryStateReader.ResolveGitExecutable();
        _gitTimeouts = new ExternalGitCommandTimeouts(
            TimeSpan.FromSeconds(options.Timeouts.WebStartupSeconds),
            TimeSpan.FromSeconds(options.Timeouts.ProcessShutdownSeconds));
    }

    internal E2EOptions Options { get; }

    private string RepositoryRoot { get; }

    internal string SourcePath => Options.WebSource.SourcePath
        ?? throw new InvalidOperationException("E2E web source prerequisite is unavailable.");

    internal async Task<WebSourceRunLease> CreateSourceAsync() => await WebSourceRunLease.CreateAsync(
        new WebSourceRunRequest(RepositoryRoot, Options, _gitExecutable, []),
        new WebSourceRunDependencies
        {
            Stager = new WebSourceStager(_gitExecutable),
            ToolResolver = new NodeNpmToolResolver(),
            CommandRunner = new NodeNpmCommandRunner()
        });

    internal PrivateRunDirectoryLease CreateBrowserPaths() => PrivateRunDirectoryLease.Create(
        new PrivateRunDirectoryRequest(
            RepositoryRoot,
            Options.Runtime.PrivateRunRoot,
            TimeSpan.FromSeconds(Options.Timeouts.ProcessShutdownSeconds)));

    internal async Task<ExternalGitWorktreeState> InspectSourceStateAsync() =>
        (await new ExternalGitWorktreeInspector(new ExternalGitCommandRunner(_gitExecutable)).InspectAsync(
            SourcePath,
            Options.WebSource.CommitSha,
            _gitTimeouts,
            CancellationToken.None)).InitialState;
}
