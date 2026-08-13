namespace LgymApi.E2ETests.Harness;

internal sealed record PinnedWebSourceRequest(
    string SourcePath,
    string CommitSha,
    PrivateRunDirectoryLease RunLease,
    TimeSpan ExecutionTimeout,
    TimeSpan ShutdownTimeout,
    bool DisposeRunLeaseOnFailure = true);

internal sealed record PinnedWebSourceReceipt(
    string PinnedCommitSha,
    bool SourceStatePreserved,
    int ManifestEntryCount,
    string ManifestSha256,
    bool ManifestMatched,
    bool TemporaryArchiveRemoved)
{
    public override string ToString() => "<pinned-web-source-receipt>";
}

internal sealed record PinnedWebSourceStage(
    string SourceDirectory,
    PinnedWebSourceReceipt Receipt)
{
    public override string ToString() => "<pinned-web-source-stage>";
}

internal enum GitObjectFormat
{
    Sha1,
    Sha256
}

internal sealed record GitTreeManifest(
    IReadOnlyDictionary<string, string> Entries,
    string Sha256);

internal sealed record ExternalGitWorktreeState(
    string HeadSha,
    string StatusSha256,
    int StatusRecordCount);

internal sealed record ExternalGitWorktree(
    string SourcePath,
    string PinnedCommitSha,
    GitObjectFormat ObjectFormat,
    ExternalGitWorktreeState InitialState);

internal sealed record ExternalGitCommandResult<T>(int ExitCode, T Output);

internal sealed record ExternalGitCommandTimeouts(TimeSpan Execution, TimeSpan Shutdown);

internal interface IExternalGitCommandRunner
{
    Task<ExternalGitCommandResult<T>> RunAsync<T>(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        Func<Stream, CancellationToken, Task<T>> readStandardOutput,
        ExternalGitCommandTimeouts timeouts,
        CancellationToken cancellationToken = default);
}

internal interface IPinnedWebSourceStagingArtifactCleaner
{
    void DeleteIfPresent(string path);
}
