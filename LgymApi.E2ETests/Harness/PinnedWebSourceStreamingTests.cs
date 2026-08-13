using System.Security.Cryptography;
using System.Text;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class PinnedWebSourceStreamingTests
{
    private const string Pin = "1111111111111111111111111111111111111111";
    private const string Head = "2222222222222222222222222222222222222222";

    [Test]
    public async Task PinnedWebSource_status_fingerprint_streams_beyond_process_tail_limit()
    {
        // Given
        var sourcePath = Directory.CreateTempSubdirectory("lgym-e2e-stream-status-").FullName;
        var statusBytes = CreateLargeStatus(out var recordCount);
        var git = new ScriptedGitRunner(arguments => GetInspectorOutput(arguments, sourcePath, statusBytes));
        var inspector = new ExternalGitWorktreeInspector(git);

        try
        {
            // When
            var worktree = await inspector.InspectAsync(
                sourcePath,
                Pin,
                new ExternalGitCommandTimeouts(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)),
                CancellationToken.None);

            // Then
            Assert.Multiple(() =>
            {
                Assert.That(statusBytes.Length, Is.GreaterThan(ExternalProcessOutput.MaximumTailBytes));
                Assert.That(worktree.InitialState.StatusRecordCount, Is.EqualTo(recordCount));
                Assert.That(
                    worktree.InitialState.StatusSha256,
                    Is.EqualTo(Convert.ToHexString(SHA256.HashData(statusBytes)).ToLowerInvariant()));
            });
        }
        finally
        {
            Directory.Delete(sourcePath, recursive: true);
        }
    }

    [Test]
    public async Task PinnedWebSource_tree_manifest_streams_beyond_process_tail_limit_for_sha256()
    {
        // Given
        var treeBytes = CreateLargeTree(out var entryCount);
        var git = new ScriptedGitRunner(_ => treeBytes);
        var reader = new GitTreeManifestReader(git);
        var worktree = new ExternalGitWorktree(
            Path.GetTempPath(),
            new string('1', 64),
            GitObjectFormat.Sha256,
            new ExternalGitWorktreeState(new string('2', 64), "status", 0));

        // When
        var manifest = await reader.ReadAsync(
            worktree,
            new ExternalGitCommandTimeouts(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)),
            CancellationToken.None);

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(treeBytes.Length, Is.GreaterThan(ExternalProcessOutput.MaximumTailBytes));
            Assert.That(manifest.Entries, Has.Count.EqualTo(entryCount));
            Assert.That(
                manifest.Sha256,
                Is.EqualTo(Convert.ToHexString(SHA256.HashData(treeBytes)).ToLowerInvariant()));
        });
    }

    [TestCase("120000 blob 1111111111111111111111111111111111111111\tlink\0")]
    [TestCase("160000 commit 1111111111111111111111111111111111111111\tsubmodule\0")]
    [TestCase("100644 tree 1111111111111111111111111111111111111111\tnot-a-blob\0")]
    public void PinnedWebSource_tree_manifest_rejects_link_gitlink_and_non_blob(string record)
    {
        // Given
        var git = new ScriptedGitRunner(_ => Encoding.UTF8.GetBytes(record.Replace("\\0", "\0")));
        var reader = new GitTreeManifestReader(git);
        var worktree = new ExternalGitWorktree(
            Path.GetTempPath(),
            Pin,
            GitObjectFormat.Sha1,
            new ExternalGitWorktreeState(Head, "status", 0));

        // When
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.ReadAsync(
                worktree,
                new ExternalGitCommandTimeouts(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)),
                CancellationToken.None));

        // Then
        Assert.That(exception!.Message, Is.EqualTo(GitTreeManifestReader.TreeValidationMessage));
    }

    private static byte[] GetInspectorOutput(
        IReadOnlyList<string> arguments,
        string sourcePath,
        byte[] statusBytes)
    {
        if (arguments[0] == "status")
        {
            return statusBytes;
        }

        if (arguments.Contains("--show-toplevel"))
        {
            return Encoding.UTF8.GetBytes($"{sourcePath}\n");
        }

        if (arguments.Contains("--is-bare-repository"))
        {
            return "false\n"u8.ToArray();
        }

        if (arguments.Contains("--show-object-format"))
        {
            return "sha1\n"u8.ToArray();
        }

        return Encoding.ASCII.GetBytes(arguments.Contains("--verify") ? $"{Pin}\n" : $"{Head}\n");
    }

    private static byte[] CreateLargeStatus(out int recordCount)
    {
        using var stream = new MemoryStream();
        recordCount = 5000;
        for (var index = 0; index < recordCount; index++)
        {
            stream.Write(Encoding.UTF8.GetBytes($"?? untracked-{index:D5}.txt\0"));
        }

        return stream.ToArray();
    }

    private static byte[] CreateLargeTree(out int entryCount)
    {
        using var stream = new MemoryStream();
        entryCount = 2000;
        var objectId = new string('a', 64);
        for (var index = 0; index < entryCount; index++)
        {
            stream.Write(Encoding.UTF8.GetBytes($"100644 blob {objectId}\tfiles/file-{index:D5}.txt\0"));
        }

        return stream.ToArray();
    }

    private sealed class ScriptedGitRunner(Func<IReadOnlyList<string>, byte[]> getOutput)
        : IExternalGitCommandRunner
    {
        public async Task<ExternalGitCommandResult<T>> RunAsync<T>(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            Func<Stream, CancellationToken, Task<T>> readStandardOutput,
            ExternalGitCommandTimeouts timeouts,
            CancellationToken cancellationToken = default)
        {
            await using var stream = new MemoryStream(getOutput(arguments));
            return new ExternalGitCommandResult<T>(0, await readStandardOutput(stream, cancellationToken));
        }
    }
}
