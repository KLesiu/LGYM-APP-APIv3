namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class WebPrivatePathPolicyTests
{
    [Test]
    public async Task Private_API_runtime_artifact_accepts_canonical_api_root()
    {
        // Given
        var lease = PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            RepositoryRoot.Find(),
            ".e2e-private/runs",
            TimeSpan.FromSeconds(1)));
        var apiArtifact = Path.Combine(lease.RunDirectory, "api", "appsettings.e2e.json");

        try
        {
            // When
            var action = () => lease.EnsureSafeRuntimeArtifact(apiArtifact);

            // Then
            Assert.DoesNotThrow(action);
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    [Test]
    public async Task Private_API_runtime_artifact_rejects_web_root()
    {
        // Given
        var lease = CreateLease();
        var webArtifact = Path.Combine(lease.RunDirectory, "web-runtime", "app.json");

        try
        {
            // When
            var exception = Assert.Throws<InvalidOperationException>(() =>
                lease.EnsureSafeRuntimeArtifact(webArtifact));

            // Then
            Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    [TestCase("web-source")]
    [TestCase("web-runtime")]
    public async Task Private_web_run_root_resolves_canonically_when_allowlisted(string relativeRoot)
    {
        // Given
        var lease = CreateLease();

        try
        {
            // When
            var resolvedPath = lease.ResolveWebOwnedPath(relativeRoot);

            // Then
            Assert.That(resolvedPath, Is.EqualTo(Path.GetFullPath(Path.Combine(lease.RunDirectory, relativeRoot))));
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    [TestCase(".e2e-private/npm-cache")]
    [TestCase(".e2e-private/browsers")]
    public async Task Private_cache_root_resolves_canonically_when_allowlisted(string relativeRoot)
    {
        // Given
        var repositoryRoot = RepositoryRoot.Find();
        var lease = CreateLease(repositoryRoot);

        try
        {
            // When
            var resolvedPath = lease.ResolveCacheOwnedPath(relativeRoot);

            // Then
            Assert.That(resolvedPath, Is.EqualTo(Path.GetFullPath(Path.Combine(repositoryRoot, relativeRoot))));
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    [TestCase("other")]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("../web-source")]
    [TestCase("web-source-sibling")]
    [TestCase("WEB-SOURCE")]
    [TestCase(".e2e-private/npm-cache")]
    public async Task Private_web_run_root_rejects_non_allowlisted_path_variants(string relativeRoot)
    {
        // Given
        var lease = CreateLease();

        try
        {
            // When
            var exception = Assert.Throws<InvalidOperationException>(() => lease.ResolveWebOwnedPath(relativeRoot));

            // Then
            Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    [TestCase("other")]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase(".e2e-private/../npm-cache")]
    [TestCase(".e2e-private/npm-cache-sibling")]
    [TestCase(".e2e-private/NPM-CACHE")]
    [TestCase("web-source")]
    public async Task Private_cache_root_rejects_non_allowlisted_path_variants(string relativeRoot)
    {
        // Given
        var lease = CreateLease();

        try
        {
            // When
            var exception = Assert.Throws<InvalidOperationException>(() => lease.ResolveCacheOwnedPath(relativeRoot));

            // Then
            Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    [Test]
    public async Task Private_owned_path_factories_reject_absolute_paths()
    {
        // Given
        var lease = CreateLease();
        var absolutePath = Path.Combine(lease.RunDirectory, "web-source");

        try
        {
            // When
            var webException = Assert.Throws<InvalidOperationException>(() => lease.ResolveWebOwnedPath(absolutePath));
            var cacheException = Assert.Throws<InvalidOperationException>(() => lease.ResolveCacheOwnedPath(absolutePath));

            // Then
            Assert.Multiple(() =>
            {
                Assert.That(webException!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
                Assert.That(cacheException!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
            });
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    private static PrivateRunDirectoryLease CreateLease(string? repositoryRoot = null) =>
        PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            repositoryRoot ?? RepositoryRoot.Find(),
            ".e2e-private/runs",
            TimeSpan.FromSeconds(1)));
}
