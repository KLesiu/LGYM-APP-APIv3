using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class PinnedWebSourceArchiveSafetyTests
{
    [TestCase("../outside.marker")]
    [TestCase("/rooted.txt")]
    [TestCase("C:/rooted.txt")]
    [TestCase("safe/file.txt:stream")]
    [TestCase(".git/config")]
    public async Task PinnedWebSource_archive_rejects_unsafe_file_name_before_outside_write(string entryName)
    {
        // Given
        using var fixture = new MaliciousArchiveFixture();
        var outsideSentinel = fixture.CreateOutsideSentinel();
        fixture.WriteRegularEntries((entryName, "hostile"));
        var manifest = CreateManifest((entryName, "hostile"));

        // When
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await PinnedWebSourceArchiveExtractor.ExtractAsync(
                fixture.ArchivePath,
                fixture.DestinationPath,
                manifest,
                GitObjectFormat.Sha1));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(PinnedWebSourceArchiveExtractor.ArchiveValidationMessage));
            Assert.That(File.ReadAllText(outsideSentinel), Is.EqualTo("outside"));
            Assert.That(Directory.Exists(fixture.DestinationPath), Is.False);
        });
    }

    [Test]
    public async Task PinnedWebSource_archive_rejects_case_collision_before_extraction()
    {
        // Given
        using var fixture = new MaliciousArchiveFixture();
        fixture.WriteRegularEntries(("safe/File.txt", "one"), ("safe/file.txt", "two"));
        var manifest = CreateManifest(("safe/File.txt", "one"), ("safe/file.txt", "two"));

        // When
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await PinnedWebSourceArchiveExtractor.ExtractAsync(
                fixture.ArchivePath,
                fixture.DestinationPath,
                manifest,
                GitObjectFormat.Sha1));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(PinnedWebSourceArchiveExtractor.ArchiveValidationMessage));
            Assert.That(Directory.Exists(fixture.DestinationPath), Is.False);
        });
    }

    [Test]
    public async Task PinnedWebSource_archive_rejects_link_before_extraction()
    {
        // Given
        using var fixture = new MaliciousArchiveFixture();
        fixture.WriteSymbolicLink("safe/link", "../../outside.marker");
        var manifest = CreateManifest(("safe/link", "ignored"));

        // When
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await PinnedWebSourceArchiveExtractor.ExtractAsync(
                fixture.ArchivePath,
                fixture.DestinationPath,
                manifest,
                GitObjectFormat.Sha1));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(PinnedWebSourceArchiveExtractor.ArchiveValidationMessage));
            Assert.That(Directory.Exists(fixture.DestinationPath), Is.False);
        });
    }

    [Test]
    public async Task PinnedWebSource_archive_rejects_duplicate_file_before_extraction()
    {
        // Given
        using var fixture = new MaliciousArchiveFixture();
        fixture.WriteRegularEntries(("safe/file.txt", "content"), ("safe/file.txt", "content"));
        var manifest = CreateManifest(("safe/file.txt", "content"));

        // When
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await PinnedWebSourceArchiveExtractor.ExtractAsync(
                fixture.ArchivePath,
                fixture.DestinationPath,
                manifest,
                GitObjectFormat.Sha1));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(PinnedWebSourceArchiveExtractor.ArchiveValidationMessage));
            Assert.That(Directory.Exists(fixture.DestinationPath), Is.False);
        });
    }

    [TestCase("different-content", "safe/file.txt")]
    [TestCase("content", "safe/extra.txt")]
    public async Task PinnedWebSource_archive_rejects_blob_or_path_outside_manifest(
        string archiveContent,
        string archivePath)
    {
        // Given
        using var fixture = new MaliciousArchiveFixture();
        fixture.WriteRegularEntries((archivePath, archiveContent));
        var manifest = CreateManifest(("safe/file.txt", "content"));

        // When
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await PinnedWebSourceArchiveExtractor.ExtractAsync(
                fixture.ArchivePath,
                fixture.DestinationPath,
                manifest,
                GitObjectFormat.Sha1));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(PinnedWebSourceArchiveExtractor.ArchiveValidationMessage));
            Assert.That(Directory.Exists(fixture.DestinationPath), Is.False);
        });
    }

    [Test]
    public async Task PinnedWebSource_archive_rejects_existing_destination_reparse_before_outside_write()
    {
        // Given
        using var fixture = new MaliciousArchiveFixture();
        var outsideSentinel = fixture.CreateOutsideSentinel();
        fixture.WriteRegularEntries(("safe/file.txt", "content"));
        fixture.CreateDestinationLink();
        var manifest = CreateManifest(("safe/file.txt", "content"));

        // When
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await PinnedWebSourceArchiveExtractor.ExtractAsync(
                fixture.ArchivePath,
                fixture.DestinationPath,
                manifest,
                GitObjectFormat.Sha1));

        // Then
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(PinnedWebSourceArchiveExtractor.ArchiveValidationMessage));
                Assert.That(File.ReadAllText(outsideSentinel), Is.EqualTo("outside"));
            });
    }

    private static GitTreeManifest CreateManifest(params (string Path, string Content)[] files)
    {
        var entries = files.ToDictionary(
            file => file.Path,
            file => ComputeBlobId(file.Content),
            StringComparer.Ordinal);
        return new GitTreeManifest(entries, "fixture-manifest-hash");
    }

    private static string ComputeBlobId(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var header = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(header);
        hash.AppendData(bytes);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private sealed class MaliciousArchiveFixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("lgym-e2e-malicious-tar-").FullName;

        internal string ArchivePath => Path.Combine(_root, "source.tar");

        internal string DestinationPath => Path.Combine(_root, "destination");

        internal string CreateOutsideSentinel()
        {
            var path = Path.Combine(_root, "outside.marker");
            File.WriteAllText(path, "outside");
            return path;
        }

        internal void CreateDestinationLink() => Directory.CreateSymbolicLink(DestinationPath, _root);

        internal void WriteRegularEntries(params (string Path, string Content)[] entries)
        {
            using var archive = File.Create(ArchivePath);
            using var writer = new TarWriter(archive, leaveOpen: false);
            foreach (var (path, content) in entries)
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, path)
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content))
                });
            }
        }

        internal void WriteSymbolicLink(string path, string target)
        {
            using var archive = File.Create(ArchivePath);
            using var writer = new TarWriter(archive, leaveOpen: false);
            writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, path) { LinkName = target });
        }

        public void Dispose()
        {
            if (Directory.Exists(DestinationPath) &&
                (File.GetAttributes(DestinationPath) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(DestinationPath);
            }

            Directory.Delete(_root, recursive: true);
        }
    }
}
