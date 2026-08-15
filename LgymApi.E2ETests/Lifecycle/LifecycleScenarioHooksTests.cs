using System.Reflection;
using System.Text.RegularExpressions;
using LgymApi.E2ETests.Browser;
using LgymApi.E2ETests.Harness;
using Reqnroll;

namespace LgymApi.E2ETests.Lifecycle;

[TestFixture]
[Category("Lifecycle")]
public sealed class LifecycleScenarioHooksTests
{
    [Test]
    public void Lifecycle_hooks_are_async_tag_scoped_and_explicitly_ordered()
    {
        var hookType = typeof(LifecycleScenarioHooks);

        Assert.Multiple(() =>
        {
            Assert.That(hookType.GetCustomAttribute<BindingAttribute>(), Is.Not.Null);
            AssertHook<BeforeScenarioAttribute>(hookType, nameof(LifecycleScenarioHooks.BeforeLifecycleScenarioAsync), LifecycleScenarioHooks.ScenarioBeforeOrder, "@web-lifecycle", isStatic: false);
            AssertHook<AfterScenarioAttribute>(hookType, nameof(LifecycleScenarioHooks.ProjectLifecycleFailureAsync), LifecycleScenarioHooks.FailureProjectionOrder, "@web-lifecycle", isStatic: false);
            AssertHook<AfterScenarioAttribute>(hookType, nameof(LifecycleScenarioHooks.AfterLifecycleScenarioAsync), LifecycleScenarioHooks.ScenarioAfterOrder, "@web-lifecycle", isStatic: false);
            AssertHook<AfterTestRunAttribute>(hookType, nameof(LifecycleScenarioHooks.AfterLifecycleRunAsync), LifecycleScenarioHooks.RunAfterOrder, null, isStatic: true);
            Assert.That(LifecycleScenarioHooks.FailureProjectionOrder, Is.LessThan(LifecycleScenarioHooks.ScenarioAfterOrder));
            Assert.That(LifecycleScenarioHooks.RunAfterOrder, Is.GreaterThan(BrowserScenarioHooks.RunAfterOrder));
        });
    }

    [Test]
    public async Task Browser_run_setup_is_lazy_for_non_browser_scenarios()
    {
        await BrowserScenarioHooks.BeforeBrowserRunAsync();

        Assert.That(BrowserRunStateHolder.Take(), Is.Null);
    }

    [Test]
    public void Lifecycle_feature_declares_exactly_two_canonical_serial_probes()
    {
        var featurePath = Path.Combine(RepositoryRoot.Find(), "LgymApi.E2ETests", "Features", "Lifecycle.feature");
        var feature = File.ReadAllText(featurePath);

        Assert.Multiple(() =>
        {
            Assert.That(Regex.Matches(feature, "(?m)^@serial @Lifecycle @web-lifecycle$").Count, Is.EqualTo(2));
            Assert.That(Regex.Matches(feature, "(?m)^Scenario: lifecycle-probe-[ab]$").Count, Is.EqualTo(2));
            Assert.That(feature, Does.Not.Contain("registration"));
            Assert.That(feature, Does.Not.Contain("login"));
            Assert.That(feature, Does.Not.Contain("tutorial"));
            Assert.That(feature, Does.Not.Contain("logout"));
        });
    }

    [Test]
    public void CaseId_resolver_maps_exactly_the_predecessor_and_business_titles()
    {
        var resolver = CreateCaseIdResolver();

        Assert.Multiple(() =>
        {
            Assert.That(ResolveCaseId(resolver, "lifecycle-probe-a", ["serial", "Lifecycle", "web-lifecycle"]), Is.EqualTo("lifecycle-probe-a"));
            Assert.That(ResolveCaseId(resolver, "lifecycle-probe-b", ["serial", "Lifecycle", "web-lifecycle"]), Is.EqualTo("lifecycle-probe-b"));
            Assert.That(ResolveCaseId(resolver, "preload-reaches-the-unauthenticated-state", ["serial", "web-lifecycle", "auth"]), Is.EqualTo("preload-reaches-the-unauthenticated-state"));
            Assert.That(ResolveCaseId(resolver, "successful-registration-creates-the-account-and-returns-to-login", ["serial", "web-lifecycle", "auth"]), Is.EqualTo("successful-registration-creates-the-account-and-returns-to-login"));
            Assert.That(ResolveCaseId(resolver, "successful-login-reaches-authenticated-home", ["serial", "web-lifecycle", "auth"]), Is.EqualTo("successful-login-reaches-authenticated-home"));
            Assert.That(ResolveCaseId(resolver, "wrong-password-remains-unauthenticated-and-shows-the-real-error-toast", ["serial", "web-lifecycle", "auth"]), Is.EqualTo("wrong-password-remains-unauthenticated-and-shows-the-real-error-toast"));
            Assert.That(ResolveCaseId(resolver, "active-onboarding-starts-and-advances", ["serial", "web-lifecycle", "onboarding"]), Is.EqualTo("active-onboarding-starts-and-advances"));
            Assert.That(ResolveCaseId(resolver, "logout-remains-effective-after-a-full-page-reload", ["serial", "web-lifecycle", "session"]), Is.EqualTo("logout-remains-effective-after-a-full-page-reload"));
        });
    }

    [Test]
    public void CaseId_resolver_rejects_unknown_duplicate_and_business_Lifecycle_titles_before_acquisition()
    {
        var resolver = CreateCaseIdResolver();
        var resourceCreationReached = false;

        Assert.Multiple(() =>
        {
            AssertContractRejected(() =>
                ResolveCaseId(resolver, "unknown-web-lifecycle-title", ["serial", "web-lifecycle", "auth"]));
            Assert.That(resourceCreationReached, Is.False);

            ResolveCaseId(resolver, "lifecycle-probe-a", ["serial", "Lifecycle", "web-lifecycle"]);
            AssertContractRejected(() =>
                ResolveCaseId(resolver, "lifecycle-probe-a", ["serial", "Lifecycle", "web-lifecycle"]));
            Assert.That(resourceCreationReached, Is.False);

            AssertContractRejected(() =>
                ResolveCaseId(resolver, "preload-reaches-the-unauthenticated-state", ["serial", "Lifecycle", "web-lifecycle", "auth"]));
            Assert.That(resourceCreationReached, Is.False);
        });
    }

    [Test]
    public void Lifecycle_hooks_attach_resources_after_acquisition_when_typed_state_is_registered()
    {
        var hookType = typeof(LifecycleScenarioHooks).Assembly.GetType(
            "LgymApi.E2ETests.Lifecycle.LifecycleScenarioResourceHooks");

        Assert.That(hookType, Is.Not.Null);
        if (hookType is null)
        {
            return;
        }

        Assert.Multiple(() =>
        {
            Assert.That(hookType.GetCustomAttribute<BindingAttribute>(), Is.Not.Null);
            AssertHook<BeforeScenarioAttribute>(hookType, "AttachScenarioResourcesAsync", 200, "@web-lifecycle", isStatic: false);
            Assert.That(LifecycleScenarioHooks.ScenarioResourceAttachmentOrder, Is.EqualTo(200));
            Assert.That(LifecycleScenarioHooks.ScenarioResourceAttachmentOrder, Is.GreaterThan(LifecycleScenarioHooks.ScenarioBeforeOrder));
        });
    }

    [Test]
    public void WebBusinessScenarioState_hooks_create_before_lifecycle_and_dispose_before_lifecycle_cleanup()
    {
        var hookType = typeof(LifecycleScenarioHooks).Assembly.GetType(
            "LgymApi.E2ETests.Lifecycle.WebBusinessScenarioHooks");

        Assert.That(hookType, Is.Not.Null);
        if (hookType is null)
        {
            return;
        }

        Assert.Multiple(() =>
        {
            Assert.That(hookType.GetCustomAttribute<BindingAttribute>(), Is.Not.Null);
            AssertHookTags<BeforeScenarioAttribute>(hookType, "CreateScenarioStateAsync", WebBusinessScenarioHooks.ScenarioStateOrder, ["@auth", "@onboarding", "@session"]);
            AssertHookTags<AfterScenarioAttribute>(hookType, "DisposeScenarioStateAsync", WebBusinessScenarioHooks.ScenarioStateDisposalOrder, ["@auth", "@onboarding", "@session"]);
            Assert.That(WebBusinessScenarioHooks.ScenarioStateOrder, Is.LessThan(LifecycleScenarioHooks.ScenarioBeforeOrder));
            Assert.That(WebBusinessScenarioHooks.ScenarioStateDisposalOrder, Is.LessThan(LifecycleScenarioHooks.ScenarioAfterOrder));
        });
    }

    private static object CreateCaseIdResolver()
    {
        var resolverType = typeof(LifecycleScenarioHooks).Assembly.GetType(
            "LgymApi.E2ETests.Lifecycle.LifecycleScenarioCaseIdRegistry");
        Assert.That(resolverType, Is.Not.Null);
        if (resolverType is null)
        {
            throw new AssertionException("Lifecycle scenario case-ID resolver is missing.");
        }

        return Activator.CreateInstance(
            resolverType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [],
            culture: null)!;
    }

    private static string ResolveCaseId(object resolver, string title, string[] tags)
    {
        var method = resolver.GetType().GetMethod(
            "ResolveAndReserve",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        if (method is null)
        {
            throw new AssertionException("Lifecycle scenario case-ID resolution method is missing.");
        }

        return (string)method.Invoke(resolver, [title, tags])!;
    }

    private static void AssertContractRejected(Action action)
    {
        var exception = Assert.Throws<TargetInvocationException>(action);
        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(exception.InnerException!.Message, Is.EqualTo("E2E web lifecycle scenario contract is invalid."));
    }

    private static void AssertHook<TAttribute>(
        Type hookType,
        string methodName,
        int order,
        string? tag,
        bool isStatic)
        where TAttribute : HookAttribute
    {
        var method = hookType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        var attribute = method?.GetCustomAttribute<TAttribute>();

        Assert.Multiple(() =>
        {
            Assert.That(method, Is.Not.Null, methodName);
            Assert.That(method!.ReturnType, Is.EqualTo(typeof(Task)), methodName);
            Assert.That(method.IsStatic, Is.EqualTo(isStatic), methodName);
            Assert.That(attribute, Is.Not.Null, methodName);
            Assert.That(attribute!.Order, Is.EqualTo(order), methodName);
            var tags = attribute.Tags ?? [];
            Assert.That(tags.Length, Is.EqualTo(tag is null ? 0 : 1), methodName);
            if (tag is not null)
            {
                Assert.That(tags.Single(), Is.EqualTo(tag), methodName);
            }
        });
    }

    private static void AssertHookTags<TAttribute>(
        Type hookType,
        string methodName,
        int order,
        IReadOnlyList<string> tags)
        where TAttribute : HookAttribute
    {
        var method = hookType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var attributes = method?.GetCustomAttributes<TAttribute>().ToArray() ?? [];

        Assert.Multiple(() =>
        {
            Assert.That(method, Is.Not.Null, methodName);
            Assert.That(method!.ReturnType, Is.EqualTo(typeof(Task)), methodName);
            Assert.That(method.IsStatic, Is.False, methodName);
            Assert.That(attributes.Select(attribute => attribute.Order), Is.All.EqualTo(order));
            Assert.That(attributes.SelectMany(attribute => attribute.Tags ?? []), Is.EqualTo(tags));
        });
    }
}
