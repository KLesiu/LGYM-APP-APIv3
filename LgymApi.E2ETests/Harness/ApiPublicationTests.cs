using System.Security.Cryptography;
using System.Text.Json;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class ApiPublicationTests
{
    private ApiPublicationFixture _fixture = null!;

    [OneTimeSetUp]
    public async Task PublishFreshApi()
    {
        _fixture = await ApiPublicationFixture.CreateAsync();
    }

    [OneTimeTearDown]
    public void CleanDisposableCopies() => _fixture.Dispose();

    [Test]
    public async Task Fresh_API_publication_records_required_artifacts_and_hash_bound_repository_evidence()
    {
        var publication = _fixture.Publication;
        var receipt = publication.Receipt;
        var expectedHash = ComputeHash(publication.DllPath);
        var gitExecutable = ApiRepositoryStateReader.ResolveGitExecutable();
        var expectedRepositoryState = await new ApiRepositoryStateReader(
            new ExternalProcessRunner(),
            gitExecutable).ReadAsync(
                _fixture.RepositoryRoot,
                new ApiRepositoryStateTimeouts(
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(_fixture.Options.Timeouts.ProcessShutdownSeconds)));
        var launchRequest = _fixture.CreateLaunchRequest(publication);

        publication.ValidateBeforeLaunch(launchRequest);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(publication.DllPath), Is.True);
            Assert.That(File.Exists(publication.DependenciesPath), Is.True);
            Assert.That(File.Exists(publication.RuntimeConfigurationPath), Is.True);
            Assert.That(File.Exists(_fixture.StaleMarkerPath), Is.False);
            Assert.That(receipt.CommandName, Is.EqualTo("publish"));
            Assert.That(receipt.DllSha256, Is.EqualTo(expectedHash));
            Assert.That(receipt.ApiRepositoryHeadSha, Is.EqualTo(expectedRepositoryState.HeadSha));
            Assert.That(receipt.RepositoryIsDirty, Is.EqualTo(expectedRepositoryState.IsDirty));
            Assert.That(receipt.CompletedAtUtc.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(receipt.CompletedAtUtc, Is.LessThanOrEqualTo(DateTimeOffset.UtcNow));
            Assert.That(receipt.Process.ExitCode, Is.Zero);
            Assert.That(receipt.ToString(), Does.Not.Contain(_fixture.RepositoryRoot));
            Assert.That(receipt.ToString(), Does.Not.Contain(publication.PublicationDirectory));
            Assert.That(publication.ToString(), Is.EqualTo("<verified-api-publication>"));
        });

        WriteSanitizedEvidence(receipt, expectedHash == receipt.DllSha256);
    }

    [Test]
    public void API_publication_command_uses_absolute_dotnet_publish_and_configured_timeout()
    {
        var layout = ApiPublicationLayout.Resolve(
            _fixture.RepositoryRoot,
            _fixture.Options.Api.PublishedDllPath);

        var request = ApiPublisher.CreatePublishRequest(
            _fixture.DotNetExecutable,
            layout,
            _fixture.Options);

        Assert.Multiple(() =>
        {
            Assert.That(Path.IsPathFullyQualified(request.FileName), Is.True);
            Assert.That(Path.GetFileName(request.FileName), Is.EqualTo("dotnet.exe").IgnoreCase);
            Assert.That(
                request.Arguments,
                Is.EqualTo(new[]
                {
                    "publish",
                    Path.Combine("LgymApi.Api", "LgymApi.Api.csproj"),
                    "--configuration",
                    "Release",
                    "--output",
                    Path.Combine(".e2e-private", "published-api"),
                    "--disable-build-servers"
                }));
            Assert.That(request.Arguments, Does.Not.Contain("run"));
            Assert.That(request.Arguments.Any(argument =>
                argument.StartsWith("--launch-profile", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(request.ExecutionTimeout, Is.EqualTo(TimeSpan.FromSeconds(300)));
            Assert.That(request.ShutdownTimeout, Is.EqualTo(TimeSpan.FromSeconds(15)));
        });
    }

    [TestCase(ApiPublicationLayout.DllFileName)]
    [TestCase(ApiPublicationLayout.DependenciesFileName)]
    [TestCase(ApiPublicationLayout.RuntimeConfigurationFileName)]
    public void API_publication_launch_validation_rejects_missing_artifacts_before_start(string artifactName)
    {
        var disposablePublication = _fixture.CopyPublication();
        var launchRequest = _fixture.CreateLaunchRequest(disposablePublication);
        File.Delete(Path.Combine(disposablePublication.PublicationDirectory, artifactName));
        var processStartReached = false;

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            disposablePublication.ValidateBeforeLaunch(launchRequest);
            processStartReached = true;
        });

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ApiPublication.RequiredArtifactMessage));
            Assert.That(processStartReached, Is.False);
            Assert.That(exception.Message, Does.Not.Contain(disposablePublication.PublicationDirectory));
        });
    }

    [Test]
    public void API_publication_launch_validation_rejects_mutated_DLL_before_start()
    {
        var disposablePublication = _fixture.CopyPublication();
        var launchRequest = _fixture.CreateLaunchRequest(disposablePublication);
        using (var dll = new FileStream(disposablePublication.DllPath, FileMode.Append, FileAccess.Write, FileShare.None))
        {
            dll.WriteByte(0x5A);
        }

        var processStartReached = false;
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            disposablePublication.ValidateBeforeLaunch(launchRequest);
            processStartReached = true;
        });

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ApiPublication.IntegrityMessage));
            Assert.That(processStartReached, Is.False);
            Assert.That(exception.Message, Does.Not.Contain(disposablePublication.PublicationDirectory));
        });
    }

    [TestCase("relative-executable")]
    [TestCase("run")]
    [TestCase("launch-profile")]
    public void API_publication_launch_validation_rejects_unsafe_process_commands_before_start(string invalidCase)
    {
        var publication = _fixture.Publication;
        var validRequest = _fixture.CreateLaunchRequest(publication);
        var request = invalidCase switch
        {
            "relative-executable" => CopyRequest(validRequest, fileName: "dotnet.exe"),
            "run" => CopyRequest(validRequest, arguments: ["run", publication.DllPath]),
            "launch-profile" => CopyRequest(validRequest, arguments: [publication.DllPath, "--launch-profile"]),
            _ => throw new InvalidOperationException("Unknown test case.")
        };
        var processStartReached = false;

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            publication.ValidateBeforeLaunch(request);
            processStartReached = true;
        });

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ApiPublication.LaunchCommandMessage));
            Assert.That(processStartReached, Is.False);
            Assert.That(exception.Message, Does.Not.Contain(publication.PublicationDirectory));
        });
    }

    private static ExternalProcessRequest CopyRequest(
        ExternalProcessRequest source,
        string? fileName = null,
        IReadOnlyList<string>? arguments = null) =>
        new()
        {
            FileName = fileName ?? source.FileName,
            Arguments = arguments ?? source.Arguments,
            WorkingDirectory = source.WorkingDirectory,
            ExecutionTimeout = source.ExecutionTimeout,
            ShutdownTimeout = source.ShutdownTimeout
        };

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void WriteSanitizedEvidence(ApiPublicationReceipt receipt, bool hashMatches)
    {
        var evidenceDirectory = Path.Combine(
            RepositoryRoot.Find(),
            "LgymApi.E2ETests",
            "TestResults",
            "issue-433-task3");
        Directory.CreateDirectory(evidenceDirectory);
        var evidence = new
        {
            requiredArtifactCount = 3,
            requiredArtifactsPresent = true,
            receipt.CommandName,
            receipt.DllSha256,
            hashMatches,
            receipt.CompletedAtUtc,
            receipt.ApiRepositoryHeadSha,
            receipt.RepositoryIsDirty,
            process = new
            {
                receipt.Process.ExitCode,
                receipt.Process.StandardOutputWasTruncated,
                receipt.Process.StandardErrorWasTruncated,
                rawOutputRetained = false
            },
            privatePathRetained = false
        };
        File.WriteAllText(
            Path.Combine(evidenceDirectory, "task-3-issue-433-e2e-api-host.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }
}
