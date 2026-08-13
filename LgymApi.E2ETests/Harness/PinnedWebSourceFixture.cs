using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace LgymApi.E2ETests.Harness;

internal sealed class PinnedWebSourceFixture : IAsyncDisposable
{
    private readonly string _fixtureRoot;

    private PinnedWebSourceFixture(string fixtureRoot, string gitExecutable, string sourcePath, string ownerRoot)
    {
        _fixtureRoot = fixtureRoot;
        GitExecutable = gitExecutable;
        SourcePath = sourcePath;
        OwnerRoot = ownerRoot;
    }

    internal string GitExecutable { get; }

    internal string SourcePath { get; }

    internal string OwnerRoot { get; }

    internal string PinnedCommit { get; private set; } = string.Empty;

    internal static async Task<PinnedWebSourceFixture> CreateAsync()
    {
        var fixtureRoot = Directory.CreateTempSubdirectory("lgym-e2e-pinned-source-").FullName;
        var sourcePath = Path.Combine(fixtureRoot, "source");
        var ownerRoot = Path.Combine(fixtureRoot, "owner");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(ownerRoot);
        var fixture = new PinnedWebSourceFixture(
            fixtureRoot,
            ApiRepositoryStateReader.ResolveGitExecutable(),
            sourcePath,
            ownerRoot);

        await fixture.RunGitAsync(["init"]);
        Directory.CreateDirectory(Path.Combine(sourcePath, "nested"));
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "app.txt"), "pinned-content\n");
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "nested", "value.txt"), "nested-pinned\n");
        await fixture.RunGitAsync(["add", "."]);
        await fixture.RunGitAsync(["commit", "-m", "pinned"]);
        fixture.PinnedCommit = (await fixture.RunGitAsync(["rev-parse", "HEAD"])).Trim();

        await File.WriteAllTextAsync(Path.Combine(sourcePath, "app.txt"), "current-content\n");
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "current-only.txt"), "current-only\n");
        await fixture.RunGitAsync(["add", "."]);
        await fixture.RunGitAsync(["commit", "-m", "current"]);
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "app.txt"), "dirty-content\n");
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "untracked.txt"), "untracked\n");
        return fixture;
    }

    internal PrivateRunDirectoryLease CreateLease() =>
        PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            OwnerRoot,
            ".e2e-private/runs",
            TimeSpan.FromSeconds(5)));

    internal async Task<string> ReadHeadAsync() =>
        (await RunGitAsync(["rev-parse", "HEAD"])).Trim();

    internal async Task<string> ReadPinnedBlobIdAsync() =>
        (await RunGitAsync(["rev-parse", $"{PinnedCommit}:app.txt"])).Trim();

    internal async Task<string> CreateBareRepositoryAsync()
    {
        var barePath = Path.Combine(_fixtureRoot, "bare.git");
        await RunGitAsync(["init", "--bare", barePath]);
        return barePath;
    }

    internal async Task<string> ReadStatusFingerprintAsync()
    {
        var bytes = await RunGitBytesAsync(["status", "--porcelain=v1", "-z", "--untracked-files=all"]);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public ValueTask DisposeAsync()
    {
        foreach (var path in Directory.EnumerateFiles(_fixtureRoot, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        Directory.Delete(_fixtureRoot, recursive: true);
        return ValueTask.CompletedTask;
    }

    private async Task<string> RunGitAsync(IReadOnlyList<string> arguments) =>
        Encoding.UTF8.GetString(await RunGitBytesAsync(arguments));

    private async Task<byte[]> RunGitBytesAsync(IReadOnlyList<string> arguments)
    {
        using var process = Process.Start(CreateStartInfo(arguments))
            ?? throw new InvalidOperationException("Synthetic Git fixture could not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var outputTask = ReadAllBytesAsync(process.StandardOutput.BaseStream, timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Synthetic Git fixture failed: {error}");
        }

        return output;
    }

    private ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(GitExecutable)
        {
            WorkingDirectory = SourcePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("--no-optional-locks");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        CopyRequiredWindowsEnvironment(startInfo.Environment);
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = "NUL";
        startInfo.Environment["GIT_AUTHOR_NAME"] = "LGYM E2E";
        startInfo.Environment["GIT_AUTHOR_EMAIL"] = "e2e@example.invalid";
        startInfo.Environment["GIT_COMMITTER_NAME"] = "LGYM E2E";
        startInfo.Environment["GIT_COMMITTER_EMAIL"] = "e2e@example.invalid";
        return startInfo;
    }

    private static void CopyRequiredWindowsEnvironment(IDictionary<string, string?> environment)
    {
        foreach (var name in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP" })
        {
            environment[name] = Environment.GetEnvironmentVariable(name);
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        await stream.CopyToAsync(output, cancellationToken);
        return output.ToArray();
    }
}
