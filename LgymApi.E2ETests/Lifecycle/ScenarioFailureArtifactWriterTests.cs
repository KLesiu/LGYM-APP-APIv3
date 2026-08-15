using System.Text.Json;
using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.Lifecycle;

[TestFixture]
[Category("Lifecycle")]
public sealed class ScenarioFailureArtifactWriterTests
{
    [Test]
    public async Task Artifact_policy_writes_one_fixed_schema_bounded_json_under_the_exact_case_directory()
    {
        await using var fixture = new ArtifactFixture();
        var scenario = fixture.Run.CreateScenario("artifact-policy");
        var writer = CreateWriter();

        await writer.WriteAsync(scenario, CreateReceipt(), CancellationToken.None);

        var artifactPath = Path.Combine(scenario.ArtifactDirectory, ScenarioFailureArtifactWriter.FileName);
        var content = await File.ReadAllTextAsync(artifactPath);
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var expectedProperties = new[]
        {
            "caseId", "failureCategory", "apiHeadSha", "apiRepositoryDirty", "acquiredCategories",
            "cleanupCategories", "cleanupFailureCount", "databaseIdentityDistinct", "previousResourcesAbsent",
            "browserStorageEmpty", "databaseAbsent", "apiAbsent", "expoAbsent", "scenarioPathsAbsent"
        };

        Assert.Multiple(() =>
        {
            Assert.That(new FileInfo(artifactPath).Length, Is.LessThanOrEqualTo(ScenarioFailureArtifactWriter.MaximumArtifactBytes));
            Assert.That(root.EnumerateObject().Select(property => property.Name).Order(), Is.EqualTo(expectedProperties.Order()));
            Assert.That(root.GetProperty("caseId").GetString(), Is.EqualTo("artifact-policy"));
            Assert.That(root.GetProperty("failureCategory").GetString(), Is.EqualTo(ScenarioFailureArtifactWriter.FailureCategory));
            Assert.That(root.GetProperty("apiHeadSha").GetString(), Is.EqualTo(new string('a', 40)));
            Assert.That(root.GetProperty("apiRepositoryDirty").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("cleanupFailureCount").GetInt32(), Is.EqualTo(0));
            Assert.That(content, Does.Not.Contain("C:\\private-path-canary"));
            Assert.That(content, Does.Not.Contain("secret-canary"));
            Assert.That(content, Does.Not.Contain("ProcessId"));
            Assert.That(content, Does.Not.Contain("storageState"));
        });
    }

    [Test]
    public async Task Artifact_policy_rejects_path_and_case_collisions_without_writing_outside_the_owned_case_directory()
    {
        await using var fixture = new ArtifactFixture();
        var scenario = fixture.Run.CreateScenario("artifact-case");
        var traversal = Assert.Throws<InvalidOperationException>(() => fixture.Run.CreateScenario("../outside"));
        var collision = Assert.Throws<InvalidOperationException>(() => fixture.Run.CreateScenario("ARTIFACT-CASE"));

        await CreateWriter().WriteAsync(scenario, CreateReceipt(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(traversal!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
            Assert.That(collision!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
            Assert.That(File.Exists(Path.Combine(scenario.ArtifactDirectory, ScenarioFailureArtifactWriter.FileName)), Is.True);
            Assert.That(Directory.Exists(Path.Combine(fixture.Root, "outside")), Is.False);
        });
    }

    [Test]
    public async Task Artifact_policy_rejects_a_reparse_artifact_directory_without_touching_its_foreign_target()
    {
        await using var fixture = new ArtifactFixture();
        var scenario = fixture.Run.CreateScenario("artifact-reparse");
        var foreignDirectory = Path.Combine(fixture.Root, "foreign-artifacts");
        var foreignMarker = Path.Combine(foreignDirectory, "marker.txt");
        Directory.CreateDirectory(foreignDirectory);
        File.WriteAllText(foreignMarker, "foreign");
        Directory.Delete(scenario.ArtifactDirectory);
        Directory.CreateSymbolicLink(scenario.ArtifactDirectory, foreignDirectory);

        try
        {
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await CreateWriter().WriteAsync(scenario, CreateReceipt(), CancellationToken.None));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
                Assert.That(File.Exists(foreignMarker), Is.True);
                Assert.That(File.Exists(Path.Combine(foreignDirectory, ScenarioFailureArtifactWriter.FileName)), Is.False);
            });
        }
        finally
        {
            Directory.Delete(scenario.ArtifactDirectory);
            Directory.Delete(foreignDirectory, recursive: true);
        }
    }

    [Test]
    public async Task Artifact_policy_rejects_oversize_and_atomic_destination_failures_without_partial_output()
    {
        await using var fixture = new ArtifactFixture();
        var oversizeScenario = fixture.Run.CreateScenario("artifact-oversize");
        var oversize = new ScenarioFailureArtifactWriter(CreatePublicationReceipt(), maximumArtifactBytes: 1);

        var oversizeException = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await oversize.WriteAsync(oversizeScenario, CreateReceipt(), CancellationToken.None));

        var atomicScenario = fixture.Run.CreateScenario("artifact-atomic");
        var destinationPath = Path.Combine(atomicScenario.ArtifactDirectory, ScenarioFailureArtifactWriter.FileName);
        File.WriteAllText(destinationPath, "sentinel");
        Assert.ThrowsAsync<IOException>(async () =>
            await CreateWriter().WriteAsync(atomicScenario, CreateReceipt(), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(oversizeException!.Message, Is.EqualTo("E2E scenario failure artifact exceeds its byte limit."));
            Assert.That(Directory.GetFiles(oversizeScenario.ArtifactDirectory), Is.Empty);
            Assert.That(File.ReadAllText(destinationPath), Is.EqualTo("sentinel"));
            Assert.That(Directory.GetFiles(atomicScenario.ArtifactDirectory, "*.tmp"), Is.Empty);
        });
    }

    private static ScenarioFailureArtifactWriter CreateWriter() => new(CreatePublicationReceipt());

    private static ApiPublicationReceipt CreatePublicationReceipt() => new(
        "publish",
        new string('b', 64),
        DateTimeOffset.UnixEpoch,
        new string('a', 40),
        true,
        new ApiPublicationProcessReceipt(0, false, false));

    private static ScenarioLifecycleReceipt CreateReceipt() => new(
        ["scenario-paths", "postgresql", "external-api-host", "expo", "browser-run", "browser-scenario"],
        ["browser-scenario", "browser-run", "expo", "external-api-host", "scenario-paths"],
        0,
        true,
        true,
        true,
        true,
        true,
        true,
        true);

    private sealed class ArtifactFixture : IAsyncDisposable
    {
        internal ArtifactFixture()
        {
            Root = Directory.CreateTempSubdirectory("lgym-e2e-failure-artifact-").FullName;
            Run = LifecycleRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
                Root,
                ".e2e-private/runs",
                TimeSpan.FromSeconds(2)));
        }

        internal string Root { get; }

        internal LifecycleRunDirectoryLease Run { get; }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
