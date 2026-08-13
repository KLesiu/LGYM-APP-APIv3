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
            Assert.That(catalog.Count(surface => surface.LiveResolvable), Is.EqualTo(3));
            Assert.That(catalog.Where(surface => !surface.LiveResolvable)
                .Select(surface => surface.DeferredTo), Is.All.EqualTo("#435"));
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
        var textFixture = Path.Combine(lease.RunDirectory, "locator-contract-text");
        var inputFixture = Path.Combine(lease.RunDirectory, "locator-contract-input");
        CopyDirectory(stage.SourceDirectory, routeFixture);
        CopyDirectory(stage.SourceDirectory, textFixture);
        CopyDirectory(stage.SourceDirectory, inputFixture);

        // When
        var baseline = LgymWebLocatorCatalog.ValidateArchivedSource(stage.SourceDirectory);
        var registerRoute = Path.Combine(routeFixture, "app", "Register.tsx");
        await File.WriteAllTextAsync(
            registerRoute,
            (await File.ReadAllTextAsync(registerRoute))
                .Replace("router.push(\"Login\");", "router.push(\"/changed\");", StringComparison.Ordinal));
        var routeDrift = LgymWebLocatorCatalog.ValidateArchivedSource(routeFixture);
        await File.WriteAllTextAsync(
            Path.Combine(textFixture, "app", "locales", "en.json"),
            (await File.ReadAllTextAsync(Path.Combine(textFixture, "app", "locales", "en.json")))
                .Replace("\"Login failed\"", "\"Changed title\"", StringComparison.Ordinal));
        var textDrift = LgymWebLocatorCatalog.ValidateArchivedSource(textFixture);
        await File.WriteAllTextAsync(
            Path.Combine(inputFixture, "app", "Register.tsx"),
            (await File.ReadAllTextAsync(Path.Combine(inputFixture, "app", "Register.tsx")))
                .Replace("<TextInput", "<RemovedInput", StringComparison.Ordinal));
        var inputOrderDrift = LgymWebLocatorCatalog.ValidateArchivedSource(inputFixture);

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(baseline, Is.Empty);
            Assert.That(routeDrift, Is.Not.Empty);
            Assert.That(textDrift, Is.Not.Empty);
            Assert.That(inputOrderDrift, Is.Not.Empty);
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
