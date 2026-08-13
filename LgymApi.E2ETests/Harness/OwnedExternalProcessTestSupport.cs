namespace LgymApi.E2ETests.Harness;

internal static class OwnedExternalProcessTestSupport
{
    internal static CancellationTokenSource CreateDeadline() =>
        new(TimeSpan.FromSeconds(10));

    internal static async Task DisposeAsync(
        OwnedExternalProcessLease lease,
        CancellationToken cancellationToken) =>
        await lease.DisposeAsync().AsTask().WaitAsync(cancellationToken);

    internal static async Task DisposeIfNeededAsync(
        OwnedExternalProcessLease lease,
        CancellationToken cancellationToken)
    {
        if (lease.CleanupReceipt is null)
        {
            await DisposeAsync(lease, cancellationToken);
        }
    }

    internal static async Task<TException> CaptureAsync<TException>(
        Func<Task> action,
        CancellationToken cancellationToken)
        where TException : Exception
    {
        try
        {
            await action().WaitAsync(cancellationToken);
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new AssertionException($"Expected an exception of type {typeof(TException).Name}.");
    }

    internal static void AssertIdentityFacts(
        ProcessIdentity rootIdentity,
        IReadOnlyList<ProcessIdentity> identities,
        bool expectAbsent)
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                identities.Any(identity =>
                    identity.ProcessId == rootIdentity.ProcessId &&
                    identity.StartTimeUtc == rootIdentity.StartTimeUtc),
                Is.True);
            Assert.That(
                identities.Select(identity => (identity.ProcessId, identity.StartTimeUtc)).Distinct().Count(),
                Is.EqualTo(identities.Count));
            if (expectAbsent)
            {
                Assert.That(WindowsProcessTree.AllAbsentOrReused(identities), Is.True);
            }
        });
    }
}
