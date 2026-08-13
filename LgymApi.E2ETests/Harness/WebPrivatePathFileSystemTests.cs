namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class WebPrivatePathFileSystemTests
{
    [Test]
    public async Task Private_web_root_rejects_reparse_before_writing_outside_its_run()
    {
        // Given
        var lease = CreateLease();
        var outsideDirectory = Directory.CreateTempSubdirectory("lgym-e2e-private-outside-").FullName;
        var outsideSentinel = Path.Combine(outsideDirectory, "outside.marker");
        var webSource = Path.Combine(lease.RunDirectory, "web-source");
        File.WriteAllText(outsideSentinel, "outside");
        Directory.CreateSymbolicLink(webSource, outsideDirectory);

        try
        {
            // When
            var exception = Assert.Throws<InvalidOperationException>(() => lease.ResolveWebOwnedPath("web-source"));

            // Then
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
                Assert.That(File.Exists(outsideSentinel), Is.True);
            });
        }
        finally
        {
            await lease.DisposeAsync();
            Assert.That(File.Exists(outsideSentinel), Is.True);
            Directory.Delete(outsideDirectory, recursive: true);
        }
    }

    [Test]
    public async Task Private_cache_root_rejects_reparse_before_writing_outside_its_owner()
    {
        // Given
        var repositoryRoot = Directory.CreateTempSubdirectory("lgym-e2e-private-cache-root-").FullName;
        var lease = CreateLease(repositoryRoot);
        var outsideDirectory = Directory.CreateTempSubdirectory("lgym-e2e-private-cache-outside-").FullName;
        var outsideSentinel = Path.Combine(outsideDirectory, "outside.marker");
        var cachePath = Path.Combine(repositoryRoot, ".e2e-private", "npm-cache");
        File.WriteAllText(outsideSentinel, "outside");
        Directory.CreateSymbolicLink(cachePath, outsideDirectory);

        try
        {
            // When
            var exception = Assert.Throws<InvalidOperationException>(() =>
                lease.ResolveCacheOwnedPath(".e2e-private/npm-cache"));

            // Then
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
                Assert.That(File.Exists(outsideSentinel), Is.True);
            });
        }
        finally
        {
            if (Directory.Exists(cachePath))
            {
                Directory.Delete(cachePath);
            }

            await lease.DisposeAsync();
            Assert.That(File.Exists(outsideSentinel), Is.True);
            Directory.Delete(outsideDirectory, recursive: true);
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Test]
    public async Task Private_cleanup_removes_only_lease_run_after_rejected_traversal_and_double_dispose()
    {
        // Given
        var repositoryRoot = RepositoryRoot.Find();
        var outsideDirectory = Path.Combine(
            repositoryRoot,
            ".e2e-private",
            "runs",
            $"task-1-private-outside-{Guid.NewGuid():N}");
        var outsideSentinel = Path.Combine(outsideDirectory, "outside.marker");
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(outsideSentinel, "outside");
        var lease = CreateLease(repositoryRoot);
        var ownedWebDirectory = lease.ResolveWebOwnedPath("web-runtime");
        Directory.CreateDirectory(ownedWebDirectory);

        try
        {
            // When
            var exception = Assert.Throws<InvalidOperationException>(() => lease.ResolveWebOwnedPath("../web-runtime"));
            await lease.DisposeAsync();
            await lease.DisposeAsync();

            // Then
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
                Assert.That(Directory.Exists(lease.RunDirectory), Is.False);
                Assert.That(File.Exists(outsideSentinel), Is.True);
            });
        }
        finally
        {
            if (Directory.Exists(lease.RunDirectory))
            {
                await lease.DisposeAsync();
            }

            Directory.Delete(outsideDirectory, recursive: true);
        }
    }

    [Test]
    public async Task Private_cleanup_preserves_shared_cache_owned_by_the_harness()
    {
        // Given
        var repositoryRoot = Directory.CreateTempSubdirectory("lgym-e2e-private-cache-owner-").FullName;
        var lease = CreateLease(repositoryRoot);
        var cacheDirectory = lease.ResolveCacheOwnedPath(".e2e-private/browsers");
        var cacheSentinel = Path.Combine(cacheDirectory, "browser.marker");
        Directory.CreateDirectory(cacheDirectory);
        File.WriteAllText(cacheSentinel, "cache");

        try
        {
            // When
            await lease.DisposeAsync();

            // Then
            Assert.Multiple(() =>
            {
                Assert.That(Directory.Exists(lease.RunDirectory), Is.False);
                Assert.That(File.Exists(cacheSentinel), Is.True);
            });
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Test]
    public async Task Private_cleanup_cancellation_is_bounded_and_retryable()
    {
        // Given
        var cleaner = new NeverCompletingCleaner();
        var lease = PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            RepositoryRoot.Find(),
            ".e2e-private/runs",
            TimeSpan.FromMilliseconds(50)), cleaner);

        // When
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await lease.DisposeAsync());

        // Then
        Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.CleanupMessage));
        cleaner.Complete();
        await lease.DisposeAsync();
        Assert.That(Directory.Exists(lease.RunDirectory), Is.False);
    }

    [Test]
    public async Task Private_cleanup_retries_a_sharing_violation_within_its_single_deadline()
    {
        var cleaner = new SharingViolationThenDeleteCleaner();
        var lease = PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            RepositoryRoot.Find(), ".e2e-private/runs", TimeSpan.FromSeconds(1)), cleaner);

        await lease.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(cleaner.Attempts, Is.EqualTo(2));
            Assert.That(Directory.Exists(lease.RunDirectory), Is.False);
        });
    }

    [TestCase(nameof(PrivateRunCleanupStage.Enumeration))]
    [TestCase(nameof(PrivateRunCleanupStage.Attributes))]
    [TestCase(nameof(PrivateRunCleanupStage.EntryDelete))]
    [TestCase(nameof(PrivateRunCleanupStage.ParentDelete))]
    public async Task Private_cleanup_classifies_filesystem_failure_stage(string stageName)
    {
        var stage = Enum.Parse<PrivateRunCleanupStage>(stageName);
        var cleaner = new FileSystemRunDirectoryCleaner(new FailingRunDirectoryFileSystem(stage));
        var lease = PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            RepositoryRoot.Find(), ".e2e-private/runs", TimeSpan.FromSeconds(1)), cleaner);

        var exception = Assert.ThrowsAsync<PrivateRunCleanupException>(async () => await lease.DisposeAsync());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Stage, Is.EqualTo(stage));
            Assert.That(lease.CleanupStage, Is.EqualTo(stage));
        });
    }

    [Test]
    public async Task Private_cleanup_retries_only_a_transient_entry_delete_sharing_violation()
    {
        var fileSystem = new TransientEntryDeleteFileSystem();
        var lease = PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            RepositoryRoot.Find(), ".e2e-private/runs", TimeSpan.FromSeconds(1)),
            new FileSystemRunDirectoryCleaner(fileSystem));

        await lease.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(fileSystem.EntryDeleteAttempts, Is.EqualTo(2));
            Assert.That(lease.CleanupStage, Is.EqualTo(PrivateRunCleanupStage.Unknown));
        });
    }

    [Test]
    public async Task Private_cleanup_preserves_entry_delete_stage_when_its_retry_deadline_expires()
    {
        var lease = PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            RepositoryRoot.Find(), ".e2e-private/runs", TimeSpan.FromMilliseconds(50)),
            new FileSystemRunDirectoryCleaner(new PersistentEntryDeleteSharingFileSystem()));

        var exception = Assert.ThrowsAsync<PrivateRunCleanupException>(async () => await lease.DisposeAsync());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Stage, Is.EqualTo(PrivateRunCleanupStage.EntryDelete));
            Assert.That(lease.CleanupStage, Is.EqualTo(PrivateRunCleanupStage.EntryDelete));
        });
    }

    private static PrivateRunDirectoryLease CreateLease(string? repositoryRoot = null) =>
        PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            repositoryRoot ?? RepositoryRoot.Find(),
            ".e2e-private/runs",
            TimeSpan.FromSeconds(1)));
}

internal sealed class FailingRunDirectoryFileSystem(PrivateRunCleanupStage stage) : IRunDirectoryFileSystem
{
    public bool DirectoryExists(string path) => true;
    public string[] GetEntries(string path) => stage == PrivateRunCleanupStage.Enumeration ? throw Sharing() : ["entry"];
    public FileAttributes GetAttributes(string path) => stage == PrivateRunCleanupStage.Attributes ? throw Sharing() : FileAttributes.Normal;
    public void DeleteDirectory(string path)
    {
        if (stage is PrivateRunCleanupStage.EntryDelete or PrivateRunCleanupStage.ParentDelete)
        {
            throw Sharing();
        }
    }
    public void DeleteFile(string path)
    {
        if (stage == PrivateRunCleanupStage.EntryDelete)
        {
            throw Sharing();
        }
    }
    private static IOException Sharing() => new("failure", unchecked((int)0x80070021));
}

internal sealed class TransientEntryDeleteFileSystem : IRunDirectoryFileSystem
{
    internal int EntryDeleteAttempts { get; private set; }
    private bool _entriesReturned;

    public bool DirectoryExists(string path) => true;
    public string[] GetEntries(string path)
    {
        if (_entriesReturned)
        {
            return [];
        }

        _entriesReturned = true;
        return ["entry"];
    }
    public FileAttributes GetAttributes(string path) => FileAttributes.Normal;
    public void DeleteDirectory(string path) { }
    public void DeleteFile(string path)
    {
        EntryDeleteAttempts++;
        if (EntryDeleteAttempts == 1)
        {
            throw new IOException("sharing", unchecked((int)0x80070020));
        }
    }
}

internal sealed class PersistentEntryDeleteSharingFileSystem : IRunDirectoryFileSystem
{
    private bool _entriesReturned;

    public bool DirectoryExists(string path) => true;
    public string[] GetEntries(string path)
    {
        if (_entriesReturned)
        {
            return [];
        }

        _entriesReturned = true;
        return ["entry"];
    }
    public FileAttributes GetAttributes(string path) => FileAttributes.Normal;
    public void DeleteDirectory(string path) { }
    public void DeleteFile(string path) => throw new IOException("sharing", unchecked((int)0x80070020));
}

internal sealed class SharingViolationThenDeleteCleaner : IRunDirectoryCleaner
{
    private const int SharingViolationHResult = unchecked((int)0x80070020);

    internal int Attempts { get; private set; }

    public Task DeleteAsync(string runDirectory, CancellationToken cancellationToken)
    {
        Attempts++;
        if (Attempts == 1)
        {
            throw new IOException("sharing", SharingViolationHResult);
        }

        Directory.Delete(runDirectory, recursive: true);
        return Task.CompletedTask;
    }
}
