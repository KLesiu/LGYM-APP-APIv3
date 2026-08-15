using LgymApi.E2ETests.Configuration;
using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.Browser.Locators;

[TestFixture]
[Category("LocatorContract")]
public sealed class LgymWebLocatorCatalogTests
{
    [Test]
    public void Locator_catalog_covers_exactly_the_six_issue_434_surfaces()
    {
        // Given
        var catalog = LgymWebLocatorCatalog.Surfaces;

        // When
        var validation = LgymWebLocatorCatalog.ValidateCatalog(catalog);

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(validation, Is.Empty);
            Assert.That(catalog.Select(surface => surface.Surface), Is.Unique);
            Assert.That(catalog, Has.Count.EqualTo(6));
            Assert.That(catalog, Is.All.Matches<LgymWebSurfaceContract>(surface =>
                surface.LiveResolvable && surface.DeferredTo is null));
            Assert.That(catalog.Single(surface => surface.Surface == LgymWebSurface.ProfileLogout)
                .ResultRoute, Is.EqualTo("/"));
            Assert.That(catalog.SelectMany(surface => surface.TestIds), Is.EquivalentTo(
            [
                LgymWebTestIds.PreloadScreen, LgymWebTestIds.PreloadLogin, LgymWebTestIds.PreloadRegister,
                LgymWebTestIds.LoginUsername, LgymWebTestIds.LoginPassword, LgymWebTestIds.LoginSubmit,
                LgymWebTestIds.RegisterUsername, LgymWebTestIds.RegisterEmail, LgymWebTestIds.RegisterPassword,
                LgymWebTestIds.RegisterConfirmPassword, LgymWebTestIds.RegisterSubmit, LgymWebTestIds.HomeDashboard,
                LgymWebTestIds.TutorialModal, LgymWebTestIds.TutorialTitle, LgymWebTestIds.TutorialPrimaryAction,
                LgymWebTestIds.HomeMenuToggle, LgymWebTestIds.HomeMenuProfile, LgymWebTestIds.ProfileLogout,
                LgymWebTestIds.ToastErrorTitle, LgymWebTestIds.ToastErrorBody
            ]));
            Assert.That(catalog.Single(surface => surface.Surface == LgymWebSurface.WrongPasswordToast)
                .Toast!.Title, Is.EqualTo("Login failed"));
            Assert.That(catalog.Single(surface => surface.Surface == LgymWebSurface.WrongPasswordToast)
                .Toast!.Body, Is.EqualTo(LgymWebDynamicLocator.ToastBody));
        });

        var seventhSurface = catalog.Append(catalog[0] with { Surface = (LgymWebSurface)999 });

        Assert.That(LgymWebLocatorCatalog.ValidateCatalog(seventhSurface), Is.Not.Empty);
    }

    [Test]
    public async Task Locator_catalog_matches_the_archived_pinned_routes_and_text()
    {
        // Given
        var options = E2EConfiguration.Load(TestContext.CurrentContext.TestDirectory, RepositoryRoot.Find());
        var sourcePath = ResolveSourcePath();
        await using var lease = PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            RepositoryRoot.Find(),
            options.Runtime.PrivateRunRoot,
            TimeSpan.FromSeconds(options.Timeouts.ProcessShutdownSeconds)));
        var stager = new PinnedWebSourceStager(ApiRepositoryStateReader.ResolveGitExecutable());
        var stage = await stager.StageAsync(new PinnedWebSourceRequest(
            sourcePath,
            options.WebSource.CommitSha,
            lease,
            TimeSpan.FromSeconds(options.Timeouts.WebStartupSeconds),
            TimeSpan.FromSeconds(options.Timeouts.ProcessShutdownSeconds)));
        var routeFixture = Path.Combine(lease.RunDirectory, "locator-contract-route");
        var forwardedIdFixture = Path.Combine(lease.RunDirectory, "locator-contract-forwarded-id");
        var textInputIdFixture = Path.Combine(lease.RunDirectory, "locator-contract-text-input-id");
        var toastIdFixture = Path.Combine(lease.RunDirectory, "locator-contract-toast-id");
        CopyDirectory(stage.SourceDirectory, routeFixture);
        CopyDirectory(stage.SourceDirectory, forwardedIdFixture);
        CopyDirectory(stage.SourceDirectory, textInputIdFixture);
        CopyDirectory(stage.SourceDirectory, toastIdFixture);

        // When
        var baseline = LgymWebLocatorCatalog.ValidateArchivedSource(stage.SourceDirectory);
        var registerRoute = Path.Combine(routeFixture, "app", "Register.tsx");
        await File.WriteAllTextAsync(
            registerRoute,
            (await File.ReadAllTextAsync(registerRoute))
                .Replace("router.push(\"/Login\");", "router.push(\"/changed\");", StringComparison.Ordinal));
        var routeDrift = LgymWebLocatorCatalog.ValidateArchivedSource(routeFixture);
        await File.WriteAllTextAsync(
            Path.Combine(forwardedIdFixture, "app", "components", "elements", "CustomButton.tsx"),
            (await File.ReadAllTextAsync(Path.Combine(
                forwardedIdFixture,
                "app",
                "components",
                "elements",
                "CustomButton.tsx")))
                .Replace("testID={props.testID}", "testID={undefined}", StringComparison.Ordinal));
        var forwardedIdDrift = LgymWebLocatorCatalog.ValidateArchivedSource(forwardedIdFixture);
        await File.WriteAllTextAsync(
            Path.Combine(textInputIdFixture, "app", "Login.tsx"),
            (await File.ReadAllTextAsync(Path.Combine(textInputIdFixture, "app", "Login.tsx")))
                .Replace("testID=\"auth.login.username\"", "testID=\"removed\"", StringComparison.Ordinal));
        var textInputIdDrift = LgymWebLocatorCatalog.ValidateArchivedSource(textInputIdFixture);
        await File.WriteAllTextAsync(
            Path.Combine(toastIdFixture, "helpers", "toastConfig.tsx"),
            (await File.ReadAllTextAsync(Path.Combine(toastIdFixture, "helpers", "toastConfig.tsx")))
                .Replace("text2Props={{ testID: \"toast.error.body\" }}", "text2Props={{}}", StringComparison.Ordinal));
        var toastIdDrift = LgymWebLocatorCatalog.ValidateArchivedSource(toastIdFixture);

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(baseline, Is.Empty);
            Assert.That(routeDrift, Is.Not.Empty);
            Assert.That(forwardedIdDrift, Is.EqualTo(["Pinned source test ID evidence is missing."]));
            Assert.That(textInputIdDrift, Is.EqualTo(["Pinned source test ID evidence is missing."]));
            Assert.That(toastIdDrift, Is.EqualTo(["Pinned source test ID evidence is missing."]));
        });
    }

    [Test]
    public void DOM_fallbacks_are_centralized_and_no_Unauthorized_body_is_hard_coded()
    {
        // Given
        var browserRoot = Path.Combine(RepositoryRoot.Find(), "LgymApi.E2ETests", "Browser");
        var maliciousRoot = Path.Combine(Path.GetTempPath(), "lgym-locator-contract-" + Path.GetRandomFileName());
        Directory.CreateDirectory(maliciousRoot);
        var outsideCatalog = Path.Combine(maliciousRoot, "OutsideCatalog.cs");
        var hardCodedBody = Path.Combine(maliciousRoot, "Locators", "BadLocator.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(hardCodedBody)!);
        File.WriteAllText(outsideCatalog, "page.Loc" + "ator(\"input\");");
        File.WriteAllText(hardCodedBody, "const string body = \"Unauthorized\";");

        try
        {
            // When
            var maintainedSource = LgymWebLocatorCatalog.ValidateBrowserSource(browserRoot);
            var maliciousSource = LgymWebLocatorCatalog.ValidateBrowserSource(maliciousRoot);

            // Then
            Assert.Multiple(() =>
            {
                Assert.That(maintainedSource, Is.Empty);
                Assert.That(maliciousSource, Has.Count.EqualTo(2));
            });
        }
        finally
        {
            Directory.Delete(maliciousRoot, recursive: true);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath);
        }
    }

    private static string ResolveSourcePath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("LGYM_E2E__WebSource__SourcePath");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        var repositoryRoot = RepositoryRoot.Find();
        var sourcePath = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            "..",
            "LGYM-APP-OFFICIAL",
            "LGYM-APP-MOBILE"));
        if (!Directory.Exists(sourcePath))
        {
            throw new InvalidOperationException("Locator contract source checkout is required.");
        }

        return sourcePath;
    }
}
