using System.Text.RegularExpressions;
using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.Lifecycle;

[TestFixture]
[Category("Lifecycle")]
public sealed class LifecycleRunDirectoryLeaseTests
{
    [Test]
    public async Task LifecycleRunDirectory_creates_one_canonical_root_with_safe_scoped_children()
    {
        var run = LifecycleRunDirectoryLease.Create(CreateRequest());
        try
        {
            var scenario = run.CreateScenario("login-journey");
            var api = scenario.CreateApiComponent();
            var webRuntime = scenario.CreateWebRuntimeComponent();
            var browserRuntime = scenario.CreateBrowserRuntimeComponent();
            var sourceSentinel = Path.Combine(run.RunDirectory, "web-source", "source.marker");
            var artifactMarker = Path.Combine(scenario.ArtifactDirectory, "result.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(sourceSentinel)!);
            File.WriteAllText(sourceSentinel, "source");
            File.WriteAllText(artifactMarker, "artifact");

            Assert.Multiple(() =>
            {
                Assert.That(run.RunId, Does.Match(new Regex("^[a-z0-9][a-z0-9-]{0,63}$")));
                Assert.That(run.RunDirectory, Is.EqualTo(Path.Combine(RepositoryRoot.Find(), ".e2e-private", "runs", run.RunId)));
                Assert.That(scenario.ScenarioDirectory, Is.EqualTo(Path.Combine(run.RunDirectory, "scenarios", "login-journey")));
                Assert.That(scenario.ArtifactDirectory, Is.EqualTo(Path.Combine(run.RunDirectory, "artifacts", "login-journey")));
                Assert.That(api.ComponentDirectory, Is.EqualTo(Path.Combine(scenario.ScenarioDirectory, "api")));
                Assert.That(webRuntime.ComponentDirectory, Is.EqualTo(Path.Combine(scenario.ScenarioDirectory, "web-runtime")));
                Assert.That(browserRuntime.ComponentDirectory, Is.EqualTo(Path.Combine(scenario.ScenarioDirectory, "browser-runtime")));
            });

            await api.DisposeAsync();

            Assert.Multiple(() =>
            {
                Assert.That(Directory.Exists(run.RunDirectory), Is.True);
                Assert.That(Directory.Exists(api.ComponentDirectory), Is.False);
                Assert.That(Directory.Exists(webRuntime.ComponentDirectory), Is.True);
                Assert.That(Directory.Exists(browserRuntime.ComponentDirectory), Is.True);
                Assert.That(File.Exists(sourceSentinel), Is.True);
                Assert.That(File.Exists(artifactMarker), Is.True);
            });

            await scenario.DisposeAsync();

            Assert.Multiple(() =>
            {
                Assert.That(Directory.Exists(scenario.ScenarioDirectory), Is.False);
                Assert.That(Directory.Exists(run.RunDirectory), Is.True);
                Assert.That(File.Exists(sourceSentinel), Is.True);
                Assert.That(File.Exists(artifactMarker), Is.True);
            });

            await run.FinalizeSuccessAsync();
            Assert.That(Directory.Exists(run.RunDirectory), Is.False);
        }
        finally
        {
            await run.DisposeAsync();
        }
    }

    [Test]
    public async Task LifecycleRunDirectory_failure_finalization_retains_only_bounded_artifacts()
    {
        var run = LifecycleRunDirectoryLease.Create(CreateRequest());

        try
        {
            var scenario = run.CreateScenario("registration");
            var api = scenario.CreateApiComponent();
            var artifactMarker = Path.Combine(scenario.ArtifactDirectory, "failure.txt");
            var nonArtifactMarker = Path.Combine(run.RunDirectory, "web-source", "source.marker");
            File.WriteAllText(artifactMarker, "artifact");
            Directory.CreateDirectory(Path.GetDirectoryName(nonArtifactMarker)!);
            File.WriteAllText(nonArtifactMarker, "source");

            await run.FinalizeFailureAsync();

            Assert.Multiple(() =>
            {
                Assert.That(Directory.Exists(run.RunDirectory), Is.True);
                Assert.That(Directory.GetFileSystemEntries(run.RunDirectory), Is.EqualTo(new[] { Path.Combine(run.RunDirectory, "artifacts") }));
                Assert.That(File.Exists(artifactMarker), Is.True);
                Assert.That(Directory.Exists(scenario.ScenarioDirectory), Is.False);
                Assert.That(Directory.Exists(api.ComponentDirectory), Is.False);
                Assert.That(File.Exists(nonArtifactMarker), Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(run.RunDirectory))
            {
                Directory.Delete(run.RunDirectory, recursive: true);
            }
        }
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("-leading")]
    [TestCase("UPPERCASE")]
    [TestCase("case/child")]
    [TestCase("../outside")]
    public async Task LifecycleRunDirectory_rejects_noncanonical_case_ids_before_creating_paths(string caseId)
    {
        await using var run = LifecycleRunDirectoryLease.Create(CreateRequest());

        var exception = Assert.Throws<InvalidOperationException>(() => run.CreateScenario(caseId));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
            Assert.That(Directory.Exists(Path.Combine(run.RunDirectory, "scenarios", caseId.Replace('/', Path.DirectorySeparatorChar))), Is.False);
            Assert.That(Directory.Exists(Path.Combine(run.RunDirectory, "artifacts", caseId.Replace('/', Path.DirectorySeparatorChar))), Is.False);
        });
    }

    [Test]
    public async Task LifecycleRunDirectory_rejects_duplicate_and_case_colliding_case_ids_before_creating_paths()
    {
        await using var run = LifecycleRunDirectoryLease.Create(CreateRequest());
        var scenario = run.CreateScenario("login");

        var duplicate = Assert.Throws<InvalidOperationException>(() => run.CreateScenario("login"));
        var caseCollision = Assert.Throws<InvalidOperationException>(() => run.CreateScenario("LOGIN"));

        Assert.Multiple(() =>
        {
            Assert.That(duplicate!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
            Assert.That(caseCollision!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
            Assert.That(Directory.Exists(scenario.ScenarioDirectory), Is.True);
            Assert.That(Directory.GetDirectories(Path.Combine(run.RunDirectory, "scenarios")), Is.EqualTo(new[] { scenario.ScenarioDirectory }));
        });
    }

    [Test]
    public async Task LifecycleRunDirectory_rejects_reparse_children_without_touching_the_foreign_target()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var foreignDirectory = Path.Combine(repositoryRoot, ".e2e-private", "lifecycle-foreign");
        var foreignMarker = Path.Combine(foreignDirectory, "foreign.marker");
        Directory.CreateDirectory(foreignDirectory);
        File.WriteAllText(foreignMarker, "foreign");
        await using var run = LifecycleRunDirectoryLease.Create(CreateRequest(repositoryRoot));
        var scenariosDirectory = Path.Combine(run.RunDirectory, "scenarios");
        Directory.CreateSymbolicLink(scenariosDirectory, foreignDirectory);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => run.CreateScenario("safe-case"));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
                Assert.That(File.Exists(foreignMarker), Is.True);
                Assert.That(Directory.Exists(Path.Combine(foreignDirectory, "safe-case")), Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(scenariosDirectory))
            {
                Directory.Delete(scenariosDirectory);
            }

            if (Directory.Exists(foreignDirectory))
            {
                Directory.Delete(foreignDirectory, recursive: true);
            }
        }
    }

    [Test]
    public async Task LifecycleRunDirectory_retries_success_finalization_after_a_transient_cleanup_failure()
    {
        var cleaner = new FailOnceCleaner();
        var run = LifecycleRunDirectoryLease.Create(CreateRequest(), cleaner);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await run.FinalizeSuccessAsync());
        Assert.That(Directory.Exists(run.RunDirectory), Is.True);

        await run.FinalizeSuccessAsync();

        Assert.That(Directory.Exists(run.RunDirectory), Is.False);
    }

    [Test]
    public async Task LifecycleRunDirectory_retries_component_disposal_after_a_transient_cleanup_failure()
    {
        var cleaner = new FailOnceCleaner();
        await using var run = LifecycleRunDirectoryLease.Create(CreateRequest(), cleaner);
        var component = run.CreateScenario("retry-component").CreateApiComponent();

        Assert.ThrowsAsync<IOException>(async () => await component.DisposeAsync());
        Assert.That(Directory.Exists(component.ComponentDirectory), Is.True);

        await component.DisposeAsync();

        Assert.That(Directory.Exists(component.ComponentDirectory), Is.False);
    }

    [Test]
    public async Task LifecycleRunDirectory_bounds_failure_finalization_and_allows_a_later_retry()
    {
        var cleaner = new NeverCompletingCleaner();
        var run = LifecycleRunDirectoryLease.Create(CreateRequest(cleanupTimeout: TimeSpan.FromMilliseconds(100)), cleaner);
        run.CreateScenario("bounded-failure").CreateApiComponent();

        var failureFinalization = run.FinalizeFailureAsync().AsTask();
        var completed = await Task.WhenAny(failureFinalization, Task.Delay(TimeSpan.FromMilliseconds(300)));

        Assert.That(completed, Is.SameAs(failureFinalization));
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await failureFinalization);
        Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.CleanupMessage));

        cleaner.Complete();
        await run.FinalizeFailureAsync();

        Assert.That(Directory.GetFileSystemEntries(run.RunDirectory), Is.EqualTo(new[] { Path.Combine(run.RunDirectory, "artifacts") }));
        Directory.Delete(run.RunDirectory, recursive: true);
    }

    [Test]
    public async Task LifecycleRunDirectory_rolls_back_partial_scenario_when_artifact_creation_hits_a_reparse_point()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var foreignDirectory = Path.Combine(repositoryRoot, ".e2e-private", "lifecycle-artifact-foreign");
        var foreignMarker = Path.Combine(foreignDirectory, "foreign.marker");
        Directory.CreateDirectory(foreignDirectory);
        File.WriteAllText(foreignMarker, "foreign");
        await using var run = LifecycleRunDirectoryLease.Create(CreateRequest(repositoryRoot));
        var artifactDirectory = Path.Combine(run.RunDirectory, "artifacts");
        Directory.CreateSymbolicLink(artifactDirectory, foreignDirectory);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => run.CreateScenario("partial-case"));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
                Assert.That(Directory.Exists(Path.Combine(run.RunDirectory, "scenarios", "partial-case")), Is.False);
                Assert.That(File.Exists(foreignMarker), Is.True);
                Assert.That(Directory.Exists(Path.Combine(foreignDirectory, "partial-case")), Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(artifactDirectory))
            {
                Directory.Delete(artifactDirectory);
            }

            if (Directory.Exists(foreignDirectory))
            {
                Directory.Delete(foreignDirectory, recursive: true);
            }
        }
    }

    private static PrivateRunDirectoryRequest CreateRequest(
        string? repositoryRoot = null,
        TimeSpan? cleanupTimeout = null) =>
        new(repositoryRoot ?? RepositoryRoot.Find(), ".e2e-private/runs", cleanupTimeout ?? TimeSpan.FromSeconds(2));
}
