using System.Security.Cryptography;
using System.Text.Json;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class RuntimeConfigurationLeaseTests
{
    [Test]
    public async Task RuntimeConfiguration_creates_exact_private_E2E_config_and_removes_only_its_run()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var staleDirectory = Path.Combine(repositoryRoot, ".e2e-private", "runs", "task4-stale");
        var staleMarker = Path.Combine(staleDirectory, "partial.marker");
        Directory.CreateDirectory(staleDirectory);
        File.WriteAllText(staleMarker, "partial");
        RuntimeConfigurationLease? lease = null;

        try
        {
            lease = await RuntimeConfigurationLease.CreateAsync(CreateRequest(repositoryRoot));
            using var configurationStream = File.OpenRead(lease.ConfigurationPath);
            using var document = JsonDocument.Parse(configurationStream);
            var root = document.RootElement;

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(lease.ConfigurationPath), Is.True);
                Assert.That(Directory.Exists(lease.RunDirectory), Is.True);
                Assert.That(File.Exists(staleMarker), Is.True);
                Assert.That(root.GetProperty("ConnectionStrings").GetProperty("Postgres").ValueKind, Is.EqualTo(JsonValueKind.String));
                Assert.That(root.GetProperty("Jwt").GetProperty("SigningKey").GetString()!.Length, Is.GreaterThanOrEqualTo(32));
                Assert.That(root.GetProperty("Cors").GetProperty("AllowedOrigins").EnumerateArray().Single().GetString(), Is.EqualTo("http://localhost:8083"));
                Assert.That(root.GetProperty("PhotoStorage").GetProperty("Provider").GetString(), Is.EqualTo("Local"));
                Assert.That(root.GetProperty("Email").GetProperty("Enabled").GetBoolean(), Is.False);
                Assert.That(root.GetProperty("PushNotifications").GetProperty("SendEnabled").GetBoolean(), Is.False);
                Assert.That(root.GetProperty("Logging").GetProperty("LogLevel").GetProperty("Microsoft.Hosting.Lifetime").GetString(), Is.EqualTo("Information"));
                Assert.That(lease.ToString(), Is.EqualTo("<runtime-configuration-lease>"));
            });
        }
        finally
        {
            if (lease is not null)
            {
                await lease.DisposeAsync();
                await lease.DisposeAsync();
                Assert.That(Directory.Exists(lease.RunDirectory), Is.False);
            }

            Assert.That(File.Exists(staleMarker), Is.True);
            Directory.Delete(staleDirectory, recursive: true);
        }
    }

    [Test]
    public async Task RuntimeConfiguration_uses_structurally_valid_synthetic_Cloudflare_R2_data()
    {
        await using var lease = await RuntimeConfigurationLease.CreateAsync(CreateRequest(
            RepositoryRoot.Find(),
            ApiRuntimeConfigurationProfile.SyntheticCloudflareR2));
        using var configurationStream = File.OpenRead(lease.ConfigurationPath);
        using var document = JsonDocument.Parse(configurationStream);
        var storage = document.RootElement.GetProperty("PhotoStorage");

        Assert.Multiple(() =>
        {
            Assert.That(storage.GetProperty("Provider").GetString(), Is.EqualTo("CloudflareR2"));
            Assert.That(storage.GetProperty("BucketName").GetString(), Is.Not.Empty);
            Assert.That(storage.GetProperty("Endpoint").GetString(), Does.StartWith("https://"));
            Assert.That(storage.GetProperty("AccessKeyId").GetString(), Is.Not.Empty);
            Assert.That(storage.GetProperty("SecretAccessKey").GetString(), Is.Not.Empty);
        });
    }

    [TestCase(".e2e-private/../runs")]
    [TestCase("..\\runs")]
    public void RuntimeConfiguration_rejects_unsafe_private_destinations(string privateRunRoot)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PrivateRunDirectoryLease.Create(
            new PrivateRunDirectoryRequest(RepositoryRoot.Find(), privateRunRoot, TimeSpan.FromSeconds(1))));

        Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
    }

    [Test]
    public async Task RuntimeConfiguration_rejects_noncanonical_private_root_without_touching_foreign_state()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var foreignDirectory = Path.Combine(repositoryRoot, ".e2e-private", "not-runs");
        var foreignMarker = Path.Combine(foreignDirectory, "foreign.marker");
        Directory.CreateDirectory(foreignDirectory);
        File.WriteAllText(foreignMarker, "foreign");
        PrivateRunDirectoryLease? lease = null;

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => lease = PrivateRunDirectoryLease.Create(
                new PrivateRunDirectoryRequest(repositoryRoot, ".e2e-private/not-runs", TimeSpan.FromSeconds(1))));
            Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
        }
        finally
        {
            if (lease is not null)
            {
                await lease.DisposeAsync();
            }

            Assert.That(File.Exists(foreignMarker), Is.True);
            Directory.Delete(foreignDirectory, recursive: true);
        }
    }

    [Test]
    public async Task RuntimeConfiguration_rejects_api_reparse_before_writing_outside_its_run()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var foreignDirectory = Path.Combine(repositoryRoot, ".e2e-private", "task4-api-race");
        Directory.CreateDirectory(foreignDirectory);
        var writer = new ApiReparseFileWriter(foreignDirectory);
        var infrastructure = new RuntimeConfigurationInfrastructure(writer, new FileSystemRunDirectoryCleaner());

        try
        {
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await RuntimeConfigurationLease.CreateAsync(
                CreateRequest(repositoryRoot), infrastructure));
            Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
            Assert.That(File.Exists(writer.ForeignConfigurationPath), Is.False);
        }
        finally
        {
            Directory.Delete(foreignDirectory, recursive: true);
        }
    }

    [Test]
    public void RuntimeConfiguration_rejects_rooted_and_reparse_private_destinations()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var linkPath = Path.Combine(repositoryRoot, ".e2e-private", "task4-link");
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);

        try
        {
            Directory.CreateSymbolicLink(linkPath, repositoryRoot);
            var rooted = Assert.Throws<InvalidOperationException>(() => PrivateRunDirectoryLease.Create(
                new PrivateRunDirectoryRequest(repositoryRoot, Path.Combine(repositoryRoot, "runs"), TimeSpan.FromSeconds(1))));
            var linked = Assert.Throws<InvalidOperationException>(() => PrivateRunDirectoryLease.Create(
                new PrivateRunDirectoryRequest(repositoryRoot, ".e2e-private/task4-link/runs", TimeSpan.FromSeconds(1))));
            Assert.Multiple(() =>
            {
                Assert.That(rooted!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
                Assert.That(linked!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
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
    public async Task RuntimeConfiguration_cleans_after_injected_write_failure_and_cancellation()
    {
        var writer = new FailingFileWriter();
        var infrastructure = new RuntimeConfigurationInfrastructure(writer, new FileSystemRunDirectoryCleaner());
        var failure = Assert.ThrowsAsync<IOException>(async () => await RuntimeConfigurationLease.CreateAsync(
            CreateRequest(RepositoryRoot.Find()), infrastructure));
        Assert.That(Directory.Exists(writer.RunDirectory!), Is.False);

        using var cancellation = new CancellationTokenSource();
        var cancellingWriter = new CancellingFileWriter(cancellation);
        var cancelledInfrastructure = new RuntimeConfigurationInfrastructure(cancellingWriter, new FileSystemRunDirectoryCleaner());
        Assert.ThrowsAsync<OperationCanceledException>(async () => await RuntimeConfigurationLease.CreateAsync(
            CreateRequest(RepositoryRoot.Find()), cancelledInfrastructure, cancellation.Token));
        Assert.That(Directory.Exists(cancellingWriter.RunDirectory!), Is.False);
        Assert.That(failure, Is.Not.Null);
    }

    [Test]
    public async Task RuntimeConfiguration_retries_cleanup_after_an_injected_cleanup_fault()
    {
        var cleaner = new FailOnceCleaner();
        var infrastructure = new RuntimeConfigurationInfrastructure(new AtomicRuntimeConfigurationFileWriter(), cleaner);
        var lease = await RuntimeConfigurationLease.CreateAsync(CreateRequest(RepositoryRoot.Find()), infrastructure);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await lease.DisposeAsync());
        await lease.DisposeAsync();

        Assert.That(Directory.Exists(lease.RunDirectory), Is.False);
    }

    [Test]
    public async Task RuntimeConfiguration_times_out_hung_cleanup_without_marking_the_lease_clean()
    {
        var cleaner = new NeverCompletingCleaner();
        var infrastructure = new RuntimeConfigurationInfrastructure(new AtomicRuntimeConfigurationFileWriter(), cleaner);
        var lease = await RuntimeConfigurationLease.CreateAsync(CreateRequest(RepositoryRoot.Find()), infrastructure);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await lease.DisposeAsync());
        Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.CleanupMessage));
        cleaner.Complete();
        await lease.DisposeAsync();
        Assert.That(Directory.Exists(lease.RunDirectory), Is.False);
    }

    [Test]
    public async Task FileSystemRunDirectoryCleaner_pre_canceled_cleanup_does_not_start_physical_delete()
    {
        var directory = Directory.CreateTempSubdirectory("lgym-e2e-cleaner-").FullName;
        File.WriteAllText(Path.Combine(directory, "retained.marker"), "retained");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await new FileSystemRunDirectoryCleaner().DeleteAsync(directory, cancellation.Token));
            Assert.That(Directory.Exists(directory), Is.True);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static RuntimeConfigurationRequest CreateRequest(
        string repositoryRoot,
        ApiRuntimeConfigurationProfile profile = ApiRuntimeConfigurationProfile.E2E) => new(
        new PrivateRunDirectoryRequest(repositoryRoot, ".e2e-private/runs", TimeSpan.FromSeconds(1)),
        new ApiRuntimeDatabase($"Host=localhost;Database=e2e-canary-db-{RandomNumberGenerator.GetHexString(16, true)};Password=e2e-canary-db-{RandomNumberGenerator.GetHexString(16, true)}"),
        profile);

}
