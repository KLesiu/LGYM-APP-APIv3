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

internal sealed record LgymWebToast(
    string Title,
    string TitleTestId,
    LgymWebDynamicLocator Body,
    string BodyTestId);

internal sealed record LgymWebSurfaceContract(
    LgymWebSurface Surface,
    string Route,
    string Component,
    IReadOnlyList<string> RuntimeText,
    IReadOnlyList<string> TestIds,
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
    internal ILocator Screen => Page.GetByTestId(LgymWebTestIds.PreloadScreen);

    internal ILocator Login => Page.GetByTestId(LgymWebTestIds.PreloadLogin);

    internal ILocator Register => Page.GetByTestId(LgymWebTestIds.PreloadRegister);
}

internal sealed class RegistrationPage(IPage page, LgymWebSurfaceContract contract) : LgymWebPageComponent(page, contract)
{
    internal ILocator Username => Page.GetByTestId(LgymWebTestIds.RegisterUsername);

    internal ILocator Email => Page.GetByTestId(LgymWebTestIds.RegisterEmail);

    internal ILocator Password => Page.GetByTestId(LgymWebTestIds.RegisterPassword);

    internal ILocator ConfirmPassword => Page.GetByTestId(LgymWebTestIds.RegisterConfirmPassword);

    internal IReadOnlyList<ILocator> Inputs => [Username, Email, Password, ConfirmPassword];

    internal ILocator Submit => Page.GetByTestId(LgymWebTestIds.RegisterSubmit);
}

internal sealed class LoginPage(IPage page, LgymWebSurfaceContract contract) : LgymWebPageComponent(page, contract)
{
    internal ILocator Username => Page.GetByTestId(LgymWebTestIds.LoginUsername);

    internal ILocator Password => Page.GetByTestId(LgymWebTestIds.LoginPassword);

    internal ILocator Dashboard => Page.GetByTestId(LgymWebTestIds.HomeDashboard);

    internal ILocator Submit => Page.GetByTestId(LgymWebTestIds.LoginSubmit);
}

internal sealed class WrongPasswordToastComponent(IPage page, LgymWebSurfaceContract contract) : LgymWebPageComponent(page, contract)
{
    internal ILocator Title => Page.GetByTestId(Contract.Toast!.TitleTestId);

    internal ILocator DynamicBody => Page.GetByTestId(Contract.Toast!.BodyTestId);
}

internal sealed class ActiveTutorialComponent(IPage page, LgymWebSurfaceContract contract) : LgymWebPageComponent(page, contract)
{
    internal ILocator Modal => Page.GetByTestId(LgymWebTestIds.TutorialModal);

    internal ILocator CurrentTitle => Page.GetByTestId(LgymWebTestIds.TutorialTitle);

    internal ILocator NextTitle => Page.GetByTestId(LgymWebTestIds.TutorialTitle);

    internal ILocator PrimaryAction => Page.GetByTestId(LgymWebTestIds.TutorialPrimaryAction);
}

internal sealed class ProfileLogoutComponent(IPage page, LgymWebSurfaceContract contract) : LgymWebPageComponent(page, contract)
{
    internal ILocator MenuToggle => Page.GetByTestId(LgymWebTestIds.HomeMenuToggle);

    internal ILocator Profile => Page.GetByTestId(LgymWebTestIds.HomeMenuProfile);

    internal ILocator Logout => Page.GetByTestId(LgymWebTestIds.ProfileLogout);
}

internal static class LgymWebTestIds
{
    internal const string PreloadScreen = "preload.screen";
    internal const string PreloadLogin = "preload.login";
    internal const string PreloadRegister = "preload.register";
    internal const string LoginUsername = "auth.login.username";
    internal const string LoginPassword = "auth.login.password";
    internal const string LoginSubmit = "auth.login.submit";
    internal const string RegisterUsername = "auth.register.username";
    internal const string RegisterEmail = "auth.register.email";
    internal const string RegisterPassword = "auth.register.password";
    internal const string RegisterConfirmPassword = "auth.register.confirm-password";
    internal const string RegisterSubmit = "auth.register.submit";
    internal const string HomeDashboard = "home.dashboard";
    internal const string TutorialModal = "tutorial.modal";
    internal const string TutorialTitle = "tutorial.title";
    internal const string TutorialPrimaryAction = "tutorial.primary-action";
    internal const string HomeMenuToggle = "home.menu.toggle";
    internal const string HomeMenuProfile = "home.menu.profile";
    internal const string ProfileLogout = "profile.logout";
    internal const string ToastErrorTitle = "toast.error.title";
    internal const string ToastErrorBody = "toast.error.body";
}

internal static class LgymWebLocatorCatalog
{
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

            if (!contract.LiveResolvable || contract.DeferredTo is not null)
            {
                failures.Add($"{contract.Surface} must be live and have no deferral.");
            }
        }

        return failures;
    }

    internal static IReadOnlyList<string> ValidateArchivedSource(string sourceDirectory)
    {
        var source = new ArchivedSource(sourceDirectory);
        var failures = new List<string>();
        RequireAll(source.Read("app/index.tsx"), ["router.push(\"/Login\")", "router.push(\"/Register\")"], failures);
        RequireAll(source.Read("app/Login.tsx"), ["router.push(\"/Start\")"], failures);
        RequireAll(source.Read("app/Register.tsx"), ["router.push(\"/Login\")"], failures);
        RequireAll(source.Read("app/components/home/profile/MainProfileInfo.tsx"), ["router.push(\"/\")"], failures);
        RequireAll(source.Read("app/services/toastService.ts"), ["text1: title", "text2: mapMessagesToDescription(normalizedMessages)"], failures);
        RequireTestIds(source.Read("app/index.tsx"),
            [LgymWebTestIds.PreloadScreen, LgymWebTestIds.PreloadLogin, LgymWebTestIds.PreloadRegister], failures);
        RequireTestIds(source.Read("app/Login.tsx"),
            [LgymWebTestIds.LoginUsername, LgymWebTestIds.LoginPassword, LgymWebTestIds.LoginSubmit], failures);
        RequireTestIds(source.Read("app/Register.tsx"),
            [LgymWebTestIds.RegisterUsername, LgymWebTestIds.RegisterEmail, LgymWebTestIds.RegisterPassword,
                LgymWebTestIds.RegisterConfirmPassword, LgymWebTestIds.RegisterSubmit], failures);
        RequireTestIds(source.Read("app/components/elements/CustomButton.tsx"), ["testID={props.testID}"], failures);
        RequireTestIds(source.Read("app/components/home/start/Start.tsx"), [LgymWebTestIds.HomeDashboard], failures);
        RequireTestIds(source.Read("app/components/onboarding/ContextualHelpModal.tsx"),
            [LgymWebTestIds.TutorialModal, LgymWebTestIds.TutorialTitle, LgymWebTestIds.TutorialPrimaryAction], failures);
        RequireTestIds(source.Read("app/components/layout/Menu.tsx"),
            ["testID={`home.menu.${item.screenId.toLowerCase()}`}", LgymWebTestIds.HomeMenuToggle, "screenId: \"PROFILE\""], failures);
        RequireTestIds(source.Read("app/components/home/profile/MainProfileInfo.tsx"), [LgymWebTestIds.ProfileLogout], failures);
        RequireTestIds(source.Read("helpers/toastConfig.tsx"), [LgymWebTestIds.ToastErrorTitle, LgymWebTestIds.ToastErrorBody], failures);
        return failures;
    }

    internal static IReadOnlyList<string> ValidateBrowserSource(string browserDirectory)
    {
        var failures = new List<string>();
        var forbiddenToastBody = string.Concat("Unauthor", "ized");
        var textLocator = string.Concat("GetBy", "Text(");
        foreach (var file in Directory.EnumerateFiles(browserDirectory, "*.cs", SearchOption.AllDirectories)
                     .Where(file => !file.EndsWith("Tests.cs", StringComparison.Ordinal)))
        {
            var content = File.ReadAllText(file);
            var isCatalogFile = file.Contains($"{Path.DirectorySeparatorChar}Locators{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
            if (!isCatalogFile && content.Contains(".Locator(", StringComparison.Ordinal))
            {
                failures.Add("Raw DOM fallbacks must remain in Browser/Locators.");
            }

            if (isCatalogFile && content.Contains(textLocator, StringComparison.Ordinal))
            {
                failures.Add("App-owned controls must use test ID locators.");
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

    private static void RequireTestIds(string source, IReadOnlyList<string> required, ICollection<string> failures)
    {
        foreach (var value in required.Where(value => !source.Contains(value, StringComparison.Ordinal)))
        {
            failures.Add("Pinned source test ID evidence is missing.");
        }
    }

    private static IReadOnlyList<LgymWebSurfaceContract> CreateExpectedContracts() =>
        Array.AsReadOnly<LgymWebSurfaceContract>(
        [
        new(LgymWebSurface.Preload, "/", nameof(PreloadPage), ReadOnly("Login", "Register"),
            ReadOnly(LgymWebTestIds.PreloadScreen, LgymWebTestIds.PreloadLogin, LgymWebTestIds.PreloadRegister), null, null, true, null),
        new(LgymWebSurface.Registration, "/Register", nameof(RegistrationPage), ReadOnly("Register"),
            ReadOnly(LgymWebTestIds.RegisterUsername, LgymWebTestIds.RegisterEmail, LgymWebTestIds.RegisterPassword,
                LgymWebTestIds.RegisterConfirmPassword, LgymWebTestIds.RegisterSubmit), "/Login", null, true, null),
        new(LgymWebSurface.Login, "/Login", nameof(LoginPage), ReadOnly("Login"),
            ReadOnly(LgymWebTestIds.LoginUsername, LgymWebTestIds.LoginPassword, LgymWebTestIds.LoginSubmit,
                LgymWebTestIds.HomeDashboard), "/Start", null, true, null),
        new(LgymWebSurface.WrongPasswordToast, "/Login", nameof(WrongPasswordToastComponent), ReadOnly("Login failed"),
            ReadOnly(LgymWebTestIds.ToastErrorTitle, LgymWebTestIds.ToastErrorBody), null,
            new("Login failed", LgymWebTestIds.ToastErrorTitle, LgymWebDynamicLocator.ToastBody, LgymWebTestIds.ToastErrorBody), true, null),
        new(LgymWebSurface.ActiveTutorial, "/Start", nameof(ActiveTutorialComponent), ReadOnly("Tutorial", "Your Arenas", "Define Arena"),
            ReadOnly(LgymWebTestIds.TutorialModal, LgymWebTestIds.TutorialTitle, LgymWebTestIds.TutorialPrimaryAction), null, null, true, null),
        new(LgymWebSurface.ProfileLogout, "/Start", nameof(ProfileLogoutComponent), ReadOnly("Profile", "Logout"),
            ReadOnly(LgymWebTestIds.HomeMenuToggle, LgymWebTestIds.HomeMenuProfile, LgymWebTestIds.ProfileLogout), "/", null, true, null)
        ]);

    private static IReadOnlyList<string> ReadOnly(params string[] values) => Array.AsReadOnly(values);

    private static bool Matches(LgymWebSurfaceContract expected, LgymWebSurfaceContract actual) =>
        expected.Route == actual.Route &&
        expected.Component == actual.Component &&
        expected.ResultRoute == actual.ResultRoute &&
        expected.Toast == actual.Toast &&
        expected.LiveResolvable == actual.LiveResolvable &&
        expected.DeferredTo == actual.DeferredTo &&
        expected.RuntimeText.SequenceEqual(actual.RuntimeText) &&
        expected.TestIds.SequenceEqual(actual.TestIds);

    private sealed class ArchivedSource(string root)
    {
        internal string Read(string relativePath) => File.ReadAllText(Path.Combine(root, relativePath));
    }
}
