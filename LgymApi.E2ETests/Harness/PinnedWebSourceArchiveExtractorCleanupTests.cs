namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class PinnedWebSourceArchiveExtractorCleanupTests
{
    [Test]
    public async Task PinnedWebSource_archive_rejection_preserves_existing_destination_and_outside_sentinel()
    {
        using var fixture = new ExistingDestinationFixture();
        var destinationSentinel = Path.Combine(fixture.DestinationPath, "destination.marker");
        var outsideSentinel = Path.Combine(fixture.Root, "outside.marker");
        Directory.CreateDirectory(fixture.DestinationPath);
        File.WriteAllText(destinationSentinel, "destination");
        File.WriteAllText(outsideSentinel, "outside");

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await PinnedWebSourceArchiveExtractor.ExtractAsync(
                fixture.ArchivePath,
                fixture.DestinationPath,
                new GitTreeManifest(new Dictionary<string, string>(), "fixture-manifest-hash"),
                GitObjectFormat.Sha1));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(PinnedWebSourceArchiveExtractor.ArchiveValidationMessage));
            Assert.That(File.ReadAllText(destinationSentinel), Is.EqualTo("destination"));
            Assert.That(File.ReadAllText(outsideSentinel), Is.EqualTo("outside"));
        });
    }

    private sealed class ExistingDestinationFixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("lgym-e2e-existing-destination-").FullName;

        internal string ArchivePath => Path.Combine(Root, "source.tar");

        internal string DestinationPath => Path.Combine(Root, "destination");

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
