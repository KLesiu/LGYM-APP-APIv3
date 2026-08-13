using Microsoft.Playwright;

namespace LgymApi.E2ETests.Browser.Locators;

internal enum LgymWebSurface
{
    Preload,
    Registration,
    Login,
    WrongPasswordToast,
    ActiveTutorial,
    ProfileLogout
}

internal enum LgymWebDynamicLocator
{
    ToastBody
}

internal sealed record LgymWebToast(string Title, LgymWebDynamicLocator Body);

internal sealed record LgymWebSurfaceContract(
    LgymWebSurface Surface,
    string Route,
    string Component,
    IReadOnlyList<string> Text,
    IReadOnlyList<string> OrderedInputs,
    string? ResultRoute,
    LgymWebToast? Toast,
    bool LiveResolvable,
    string? DeferredTo);

internal abstract class LgymWebPageComponent(IPage page, LgymWebSurfaceContract contract)
{
    protected IPage Page { get; } = page;

    internal LgymWebSurfaceContract Contract { get; } = contract;
}

internal sealed class PreloadPage(IPage page, LgymWebSurfaceContract contract) : LgymWebPageComponent(page, contract)
{
    internal ILocator Login => Page.GetByText("Login", new PageGetByTextOptions { Exact = true });

    internal ILocator Register => Page.GetByText("Register", new PageGetByTextOptions { Exact = true });
}

internal sealed class RegistrationPage(IPage page, LgymWebSurfaceContract contract) : LgymWebPageComponent(page, contract)
{
    internal ILocator Input(int order) => Page.Locator("input").Nth(order);

    internal ILocator Submit => Page.GetByText("Register", new PageGetByTextOptions { Exact = true }).Nth(1);
}

internal sealed class LoginPage(IPage page, LgymWebSurfaceContract contract) : LgymWebPageComponent(page, contract)
{
    internal ILocator Input(int order) => Page.Locator("input").Nth(order);

    internal ILocator Submit => Page.GetByText("Login", new PageGetByTextOptions { Exact = true }).Nth(1);
}

internal sealed class WrongPasswordToastComponent(IPage page, LgymWebSurfaceContract contract) : LgymWebPageComponent(page, contract)
{
    internal ILocator Title => Page.GetByText(Contract.Toast!.Title, new PageGetByTextOptions { Exact = true });

    internal ILocator DynamicBody => Page.Locator("[role='alert']").Locator("p").Last;
}

internal sealed class ActiveTutorialComponent(IPage page, LgymWebSurfaceContract contract) : LgymWebPageComponent(page, contract)
{
    internal ILocator Title => Page.GetByText("Your Arenas", new PageGetByTextOptions { Exact = true });

    internal ILocator Advance => Page.GetByText("Define Arena", new PageGetByTextOptions { Exact = true });
}

internal sealed class ProfileLogoutComponent(IPage page, LgymWebSurfaceContract contract) : LgymWebPageComponent(page, contract)
{
    internal ILocator Profile => Page.GetByText("Profile", new PageGetByTextOptions { Exact = true });

    internal ILocator Logout => Page.GetByText("Logout", new PageGetByTextOptions { Exact = true });
}

internal static class LgymWebLocatorCatalog
{
    private const string DeferredIssue = "#435";
    private static readonly LgymWebSurface[] ExpectedSurfaces =
    [
        LgymWebSurface.Preload,
        LgymWebSurface.Registration,
        LgymWebSurface.Login,
        LgymWebSurface.WrongPasswordToast,
        LgymWebSurface.ActiveTutorial,
        LgymWebSurface.ProfileLogout
    ];

    internal static IReadOnlyList<LgymWebSurfaceContract> Surfaces { get; } = CreateExpectedContracts();

    internal static LgymWebPageComponent CreatePage(IPage page, LgymWebSurface surface)
    {
        ArgumentNullException.ThrowIfNull(page);
        var contract = Surfaces.Single(candidate => candidate.Surface == surface);
        return surface switch
        {
            LgymWebSurface.Preload => new PreloadPage(page, contract),
            LgymWebSurface.Registration => new RegistrationPage(page, contract),
            LgymWebSurface.Login => new LoginPage(page, contract),
            LgymWebSurface.WrongPasswordToast => new WrongPasswordToastComponent(page, contract),
            LgymWebSurface.ActiveTutorial => new ActiveTutorialComponent(page, contract),
            LgymWebSurface.ProfileLogout => new ProfileLogoutComponent(page, contract),
            _ => throw new ArgumentOutOfRangeException(nameof(surface))
        };
    }

    internal static IReadOnlyList<string> ValidateCatalog(IEnumerable<LgymWebSurfaceContract> catalog)
    {
        var contracts = catalog.ToArray();
        var failures = new List<string>();
        if (contracts.Length != ExpectedSurfaces.Length ||
            !contracts.Select(contract => contract.Surface).Order().SequenceEqual(ExpectedSurfaces.Order()))
        {
            failures.Add("The locator catalog must contain exactly the six approved surfaces.");
        }

        foreach (var contract in contracts)
        {
            var expected = CreateExpectedContracts().SingleOrDefault(candidate => candidate.Surface == contract.Surface);
            if (expected is null || !Matches(expected, contract))
            {
                failures.Add($"{contract.Surface} does not match the approved source-backed contract.");
            }

            if (contract.LiveResolvable == (contract.DeferredTo is not null))
            {
                failures.Add($"{contract.Surface} has invalid live-resolution metadata.");
            }

            if (!contract.LiveResolvable && contract.DeferredTo != DeferredIssue)
            {
                failures.Add($"{contract.Surface} must be deferred to {DeferredIssue}.");
            }
        }

        return failures;
    }

    internal static IReadOnlyList<string> ValidateArchivedSource(string sourceDirectory)
    {
        var source = new ArchivedSource(sourceDirectory);
        var failures = new List<string>();
        RequireAll(source.Read("app/index.tsx"), ["router.push(\"/Login\")", "router.push(\"/Register\")"], failures);
        RequireAll(source.Read("app/Login.tsx"), ["router.push(\"/Start\")", "t('auth.login')", "t('auth.username')", "t('auth.password')", "t(\"auth.loginFailed\")"], failures);
        RequireAll(source.Read("app/Register.tsx"), ["router.push(\"Login\")", "t(\"auth.register\")"], failures);
        RequireInOrder(source.Read("app/Register.tsx"), ["t(\"auth.username\")", "t(\"auth.email\")", "t(\"auth.password\")", "t(\"auth.repeatPassword\")"], failures);
        RequireCount(source.Read("app/Login.tsx"), "<TextInput", 2, failures);
        RequireCount(source.Read("app/Register.tsx"), "<TextInput", 4, failures);
        RequireAll(source.Read("app/components/home/profile/MainProfileInfo.tsx"), ["t('profile.logout')", "router.push(\"/\")"], failures);
        RequireAll(source.Read("app/onboarding/tutorialStepsConfig.ts"), ["t(\"onboarding.tutorial.steps.gymIntro.title\")", "t(\"onboarding.tutorial.steps.gymIntro.primaryActionLabel\")"], failures);
        RequireAll(source.Read("app/services/toastService.ts"), ["text1: title", "text2: mapMessagesToDescription(normalizedMessages)"], failures);
        RequireAll(source.Read("app/locales/en.json"), ["\"login\": \"Login\"", "\"register\": \"Register\"", "\"loginFailed\": \"Login failed\"", "\"repeatPassword\": \"Repeat password\"", "\"accentLabel\": \"Tutorial\"", "\"title\": \"Your Arenas\"", "\"primaryActionLabel\": \"Define Arena\"", "\"logout\": \"Logout\""], failures);
        return failures;
    }

    internal static IReadOnlyList<string> ValidateBrowserSource(string browserDirectory)
    {
        var failures = new List<string>();
        var forbiddenToastBody = string.Concat("Unauthor", "ized");
        foreach (var file in Directory.EnumerateFiles(browserDirectory, "*.cs", SearchOption.AllDirectories)
                     .Where(file => !file.EndsWith("Tests.cs", StringComparison.Ordinal)))
        {
            var content = File.ReadAllText(file);
            var isCatalogFile = file.Contains($"{Path.DirectorySeparatorChar}Locators{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
            if (!isCatalogFile && content.Contains(".Locator(", StringComparison.Ordinal))
            {
                failures.Add("Raw DOM fallbacks must remain in Browser/Locators.");
            }

            if (content.Contains(forbiddenToastBody, StringComparison.Ordinal))
            {
                failures.Add("The wrong-password toast body must remain dynamic.");
            }
        }

        return failures;
    }

    private static void RequireAll(string source, IReadOnlyList<string> required, ICollection<string> failures)
    {
        foreach (var value in required.Where(value => !source.Contains(value, StringComparison.Ordinal)))
        {
            failures.Add("Pinned source locator evidence is missing.");
        }
    }

    private static IReadOnlyList<LgymWebSurfaceContract> CreateExpectedContracts() =>
        Array.AsReadOnly<LgymWebSurfaceContract>(
        [
        new(LgymWebSurface.Preload, "/", nameof(PreloadPage), ReadOnly("Login", "Register"), ReadOnly(), null, null, true, null),
        new(LgymWebSurface.Registration, "/Register", nameof(RegistrationPage), ReadOnly("Register"), ReadOnly("Username", "Email", "Password", "Repeat password"), "/Login", null, true, null),
        new(LgymWebSurface.Login, "/Login", nameof(LoginPage), ReadOnly("Login"), ReadOnly("Username", "Password"), "/Start", null, true, null),
        new(LgymWebSurface.WrongPasswordToast, "/Login", nameof(WrongPasswordToastComponent), ReadOnly(), ReadOnly(), null, new("Login failed", LgymWebDynamicLocator.ToastBody), false, DeferredIssue),
        new(LgymWebSurface.ActiveTutorial, "/Start", nameof(ActiveTutorialComponent), ReadOnly("Tutorial", "Your Arenas", "Define Arena"), ReadOnly(), null, null, false, DeferredIssue),
        new(LgymWebSurface.ProfileLogout, "/Start", nameof(ProfileLogoutComponent), ReadOnly("Profile", "Logout"), ReadOnly(), "/Login", null, false, DeferredIssue)
        ]);

    private static IReadOnlyList<string> ReadOnly(params string[] values) => Array.AsReadOnly(values);

    private static bool Matches(LgymWebSurfaceContract expected, LgymWebSurfaceContract actual) =>
        expected.Route == actual.Route &&
        expected.Component == actual.Component &&
        expected.ResultRoute == actual.ResultRoute &&
        expected.Toast == actual.Toast &&
        expected.LiveResolvable == actual.LiveResolvable &&
        expected.DeferredTo == actual.DeferredTo &&
        expected.Text.SequenceEqual(actual.Text) &&
        expected.OrderedInputs.SequenceEqual(actual.OrderedInputs);

    private static void RequireInOrder(string source, IReadOnlyList<string> required, ICollection<string> failures)
    {
        var current = 0;
        foreach (var value in required)
        {
            current = source.IndexOf(value, current, StringComparison.Ordinal);
            if (current < 0)
            {
                failures.Add("Pinned source input evidence is missing or reordered.");
                return;
            }

            current += value.Length;
        }
    }

    private static void RequireCount(string source, string token, int expectedCount, ICollection<string> failures)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += token.Length;
        }

        if (count != expectedCount)
        {
            failures.Add("Pinned source input count changed.");
        }
    }

    private sealed class ArchivedSource(string root)
    {
        internal string Read(string relativePath) => File.ReadAllText(Path.Combine(root, relativePath));
    }
}
