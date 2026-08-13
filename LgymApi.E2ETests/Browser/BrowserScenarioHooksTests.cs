using System.Reflection;
using Reqnroll;

namespace LgymApi.E2ETests.Browser;

[TestFixture]
[Category("Task7BrowserScenario")]
[Category("WebHarness")]
public sealed class BrowserScenarioHooksTests
{
    [Test]
    public void Reqnroll_browser_hooks_are_async_tag_scoped_and_explicitly_ordered()
    {
        var hookType = typeof(BrowserScenarioHooks);

        Assert.Multiple(() =>
        {
            Assert.That(hookType.GetCustomAttribute<BindingAttribute>(), Is.Not.Null);
            AssertHook<BeforeTestRunAttribute>(hookType, nameof(BrowserScenarioHooks.BeforeBrowserRunAsync), BrowserScenarioHooks.RunBeforeOrder, null, isStatic: true);
            AssertHook<BeforeScenarioAttribute>(hookType, nameof(BrowserScenarioHooks.BeforeBrowserScenarioAsync), BrowserScenarioHooks.ScenarioBeforeOrder, "@browser", isStatic: false);
            AssertHook<AfterScenarioAttribute>(hookType, nameof(BrowserScenarioHooks.AfterBrowserScenarioAsync), BrowserScenarioHooks.ScenarioAfterOrder, "@browser", isStatic: false);
            AssertHook<AfterTestRunAttribute>(hookType, nameof(BrowserScenarioHooks.AfterBrowserRunAsync), BrowserScenarioHooks.RunAfterOrder, null, isStatic: true);
        });
    }

    [Test]
    public async Task BrowserScenario_after_hook_tolerates_missing_before_state_and_is_idempotent()
    {
        var hooks = new BrowserScenarioHooks();

        await hooks.AfterBrowserScenarioAsync();
        await hooks.AfterBrowserScenarioAsync();

        Assert.Pass();
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
