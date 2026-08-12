using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace LgymApi.E2ETests.Harness;

internal static class WindowsProcessTree
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);
    private static readonly ProcessParentIdReader DefaultParentProcessIdReader =
        ProcessParentIdReader.CreateRuntime();

    internal static void EnsureSupported(ProcessParentIdReader? parentProcessIdReader = null) =>
        (parentProcessIdReader ?? DefaultParentProcessIdReader).ValidateContract();

    internal static IReadOnlyList<ProcessIdentity> Capture(
        Process root,
        CancellationToken cancellationToken,
        ProcessParentIdReader? parentProcessIdReader = null)
    {
        var reader = parentProcessIdReader ?? DefaultParentProcessIdReader;
        EnsureSupported(reader);
        var rootIdentity = new ProcessIdentity(root.Id, root.StartTime.ToUniversalTime());
        return CaptureFromKnownRoots([rootIdentity], cancellationToken, reader);
    }

    internal static IReadOnlyList<ProcessIdentity> CaptureFromKnownRoots(
        IReadOnlyList<ProcessIdentity> knownRoots,
        CancellationToken cancellationToken,
        ProcessParentIdReader? parentProcessIdReader = null)
    {
        var reader = parentProcessIdReader ?? DefaultParentProcessIdReader;
        EnsureSupported(reader);
        var snapshot = CaptureSnapshot(cancellationToken, reader);
        var snapshots = snapshot.Processes;
        var identities = knownRoots
            .DistinctBy(identity => (identity.ProcessId, identity.StartTimeUtc))
            .ToList();
        var currentIdentities = snapshots.ToDictionary(
            snapshot => snapshot.ProcessId,
            snapshot => snapshot.StartTimeUtc);
        var pending = new Queue<ProcessIdentity>();
        var seen = identities
            .Select(identity => (identity.ProcessId, identity.StartTimeUtc))
            .ToHashSet();
        foreach (var identity in identities)
        {
            pending.Enqueue(identity);
        }

        while (pending.TryDequeue(out var parent))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (currentIdentities.TryGetValue(parent.ProcessId, out var currentStartTime) &&
                currentStartTime != parent.StartTimeUtc)
            {
                throw new ProcessTreeInspectionException();
            }

            if (!currentIdentities.ContainsKey(parent.ProcessId))
            {
                continue;
            }

            foreach (var child in snapshots.Where(candidate =>
                         candidate.ParentProcessId == parent.ProcessId &&
                         candidate.StartTimeUtc >= parent.StartTimeUtc))
            {
                if (!seen.Add((child.ProcessId, child.StartTimeUtc)))
                {
                    continue;
                }

                var identity = new ProcessIdentity(child.ProcessId, child.StartTimeUtc);
                identities.Add(identity);
                pending.Enqueue(identity);
            }
        }

        return identities;
    }

    internal static bool AllAbsentOrReused(IReadOnlyList<ProcessIdentity> identities) =>
        identities.All(IsAbsentOrReused);

    internal static async Task WaitUntilAllAbsentOrReusedAsync(
        IReadOnlyList<ProcessIdentity> identities,
        CancellationToken cancellationToken)
    {
        while (!AllAbsentOrReused(identities))
        {
            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    internal static void TerminateKnownIdentities(IReadOnlyList<ProcessIdentity> identities)
    {
        foreach (var identity in identities)
        {
            try
            {
                using var process = Process.GetProcessById(identity.ProcessId);
                if (process.StartTime.ToUniversalTime() == identity.StartTimeUtc)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException or Win32Exception)
            {
                continue;
            }
        }
    }

    private static ProcessSnapshotBatch CaptureSnapshot(
        CancellationToken cancellationToken,
        ProcessParentIdReader parentProcessIdReader)
    {
        parentProcessIdReader.BeginSnapshot();
        var processes = Process.GetProcesses();
        var snapshots = new List<ProcessSnapshot>(processes.Length);
        var observedProcessIds = new HashSet<int>();
        try
        {
            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                observedProcessIds.Add(process.Id);
                if (TryReadSnapshot(process, parentProcessIdReader, out var snapshot))
                {
                    snapshots.Add(snapshot);
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return new ProcessSnapshotBatch(snapshots, observedProcessIds);
    }

    private static bool TryReadSnapshot(
        Process process,
        ProcessParentIdReader parentProcessIdReader,
        out ProcessSnapshot snapshot)
    {
        try
        {
            if (!parentProcessIdReader.TryRead(process, out var parentProcessId))
            {
                snapshot = default;
                return false;
            }

            snapshot = new ProcessSnapshot(
                process.Id,
                parentProcessId,
                process.StartTime.ToUniversalTime());
            return true;
        }
        catch (Exception exception) when (IsUnavailableProcess(exception))
        {
            snapshot = default;
            return false;
        }
    }

    private static bool IsAbsentOrReused(ProcessIdentity identity)
    {
        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            return process.StartTime.ToUniversalTime() != identity.StartTimeUtc;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static bool IsUnavailableProcess(Exception exception) =>
        exception is InvalidOperationException or Win32Exception or NotSupportedException ||
        exception is TargetInvocationException
        {
            InnerException: InvalidOperationException or Win32Exception or NotSupportedException
        };

    private readonly record struct ProcessSnapshot(
        int ProcessId,
        int ParentProcessId,
        DateTime StartTimeUtc);

    private sealed record ProcessSnapshotBatch(
        IReadOnlyList<ProcessSnapshot> Processes,
        IReadOnlySet<int> ObservedProcessIds);
}
