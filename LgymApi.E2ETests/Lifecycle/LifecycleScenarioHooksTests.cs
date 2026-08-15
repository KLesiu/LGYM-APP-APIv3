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
            AssertHook<BeforeScenarioAttribute>(hookType, nameof(LifecycleScenarioHooks.BeforeLifecycleScenarioAsync), LifecycleScenarioHooks.ScenarioBeforeOrder, "@Lifecycle", isStatic: false);
            AssertHook<AfterScenarioAttribute>(hookType, nameof(LifecycleScenarioHooks.ProjectLifecycleFailureAsync), LifecycleScenarioHooks.FailureProjectionOrder, "@Lifecycle", isStatic: false);
            AssertHook<AfterScenarioAttribute>(hookType, nameof(LifecycleScenarioHooks.AfterLifecycleScenarioAsync), LifecycleScenarioHooks.ScenarioAfterOrder, "@Lifecycle", isStatic: false);
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
            Assert.That(Regex.Matches(feature, "(?m)^@serial @Lifecycle$").Count, Is.EqualTo(2));
            Assert.That(Regex.Matches(feature, "(?m)^Scenario: lifecycle-probe-[ab]$").Count, Is.EqualTo(2));
            Assert.That(feature, Does.Not.Contain("registration"));
            Assert.That(feature, Does.Not.Contain("login"));
            Assert.That(feature, Does.Not.Contain("tutorial"));
            Assert.That(feature, Does.Not.Contain("logout"));
        });
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
}
