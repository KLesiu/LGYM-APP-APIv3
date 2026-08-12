using LgymApi.E2ETests.Configuration;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class ApiPublicationSafetyTests
{
    [Test]
    public async Task API_publication_repeated_misleading_success_cleans_partial_output()
    {
        var repositoryRoot = RepositoryRoot.Find();
        const string relativeDllPath = ".e2e-private/task3-interrupted/LgymApi.Api.dll";
        var publicationDirectory = Path.Combine(repositoryRoot, ".e2e-private", "task3-interrupted");
        var options = new E2EOptions
        {
            Api = new E2EApiOptions { PublishedDllPath = relativeDllPath },
            Timeouts = new E2ETimeoutsOptions
            {
                ApiPublishSeconds = 30,
                ProcessShutdownSeconds = 5
            }
        };
        var publisher = new ApiPublisher(runPublication: SimulateMisleadingSuccessAsync);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await publisher.PublishAsync(options));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(ApiPublication.RequiredArtifactMessage));
                Assert.That(Directory.Exists(publicationDirectory), Is.False);
                Assert.That(exception.Message, Does.Not.Contain(publicationDirectory));
            });
        }

        Task<ExternalProcessResult> SimulateMisleadingSuccessAsync(
            ExternalProcessRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(publicationDirectory);
            File.WriteAllText(Path.Combine(publicationDirectory, "partial.marker"), "partial");
            return Task.FromResult(new ExternalProcessResult(
                0,
                new ExternalProcessOutput("success", WasTruncated: false),
                new ExternalProcessOutput(string.Empty, WasTruncated: false)));
        }
    }

    [Test]
    public async Task API_publication_rejects_traversal_before_cleaning_the_destination()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var sentinelDirectory = Path.Combine(repositoryRoot, ".e2e-private", "task3-path-sentinel");
        var sentinelPath = Path.Combine(sentinelDirectory, "keep.marker");
        Directory.CreateDirectory(sentinelDirectory);
        File.WriteAllText(sentinelPath, "keep");
        var options = new E2EOptions
        {
            Api = new E2EApiOptions
            {
                PublishedDllPath = ".e2e-private/published-api/../task3-path-sentinel/LgymApi.Api.dll"
            },
            Timeouts = new E2ETimeoutsOptions
            {
                ApiPublishSeconds = 1,
                ProcessShutdownSeconds = 1
            }
        };

        try
        {
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await new ApiPublisher().PublishAsync(options));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(ApiPublicationLayout.PathValidationMessage));
                Assert.That(File.Exists(sentinelPath), Is.True);
                Assert.That(exception.Message, Does.Not.Contain(sentinelDirectory));
            });
        }
        finally
        {
            Directory.Delete(sentinelDirectory, recursive: true);
        }
    }

    [Test]
    public void API_publication_rejects_rooted_and_symlinked_private_paths()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var privateRoot = Path.Combine(repositoryRoot, ".e2e-private");
        var linkPath = Path.Combine(privateRoot, "task3-link");
        Directory.CreateDirectory(privateRoot);

        try
        {
            Directory.CreateSymbolicLink(linkPath, repositoryRoot);
            var rootedException = Assert.Throws<InvalidOperationException>(() =>
                ApiPublicationLayout.Resolve(repositoryRoot, Path.Combine(repositoryRoot, ApiPublicationLayout.DllFileName)));
            var linkException = Assert.Throws<InvalidOperationException>(() =>
                ApiPublicationLayout.Resolve(
                    repositoryRoot,
                    ".e2e-private/task3-link/LgymApi.Api.dll"));

            Assert.Multiple(() =>
            {
                Assert.That(rootedException!.Message, Is.EqualTo(ApiPublicationLayout.PathValidationMessage));
                Assert.That(linkException!.Message, Is.EqualTo(ApiPublicationLayout.PathValidationMessage));
                Assert.That(linkException.Message, Does.Not.Contain(linkPath));
            });
        }
        finally
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath);
            }
        }
    }

    [Test]
    public void API_publication_dotnet_resolution_uses_required_priority_and_sanitized_failure()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var candidateRoot = Path.Combine(repositoryRoot, ".e2e-private", "resolver-fixture");
        var hostCandidate = Path.Combine(candidateRoot, "host", "dotnet.exe");
        var dotnetRoot = Path.Combine(candidateRoot, "root");
        var rootCandidate = Path.Combine(dotnetRoot, "dotnet.exe");
        var programFiles = Path.Combine(candidateRoot, "program-files");
        var programFilesCandidate = Path.Combine(programFiles, "dotnet", "dotnet.exe");
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            hostCandidate,
            rootCandidate,
            programFilesCandidate
        };
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["DOTNET_HOST_PATH"] = hostCandidate,
            ["DOTNET_ROOT"] = dotnetRoot,
            ["ProgramFiles"] = programFiles
        };

        var hostResult = Resolve();
        values["DOTNET_HOST_PATH"] = "relative-dotnet.exe";
        var rootResult = Resolve();
        values["DOTNET_ROOT"] = null;
        var programFilesResult = Resolve();
        existing.Clear();
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve());

        Assert.Multiple(() =>
        {
            Assert.That(hostResult, Is.EqualTo(hostCandidate));
            Assert.That(rootResult, Is.EqualTo(rootCandidate));
            Assert.That(programFilesResult, Is.EqualTo(programFilesCandidate));
            Assert.That(exception!.Message, Is.EqualTo(DotNetExecutableResolver.PrerequisiteMessage));
            Assert.That(exception.Message, Does.Not.Contain(candidateRoot));
        });

        string Resolve() => DotNetExecutableResolver.Resolve(
            name => values.GetValueOrDefault(name),
            path => existing.Contains(path));
    }
}
