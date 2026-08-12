using System.Diagnostics;
using System.ComponentModel;
using System.Reflection;

namespace LgymApi.E2ETests.Harness;

internal sealed class ProcessParentIdReader
{
    internal const string PrerequisiteMessage = "Windows process identity inspection is unavailable for the E2E harness.";
    private readonly Type? _valueType;
    private readonly bool _canRead;
    private readonly Func<Process, object?>? _getter;
    private readonly Func<int, Exception?>? _snapshotFailure;
    private int _snapshotCount;

    internal ProcessParentIdReader(
        Type? valueType,
        bool canRead,
        Func<Process, object?>? getter,
        Func<int, Exception?>? snapshotFailure = null)
    {
        _valueType = valueType;
        _canRead = canRead;
        _getter = getter;
        _snapshotFailure = snapshotFailure;
    }

    internal static ProcessParentIdReader CreateRuntime(
        Func<int, Exception?>? snapshotFailure = null)
    {
        var property = typeof(Process).GetProperty(
            "ParentProcessId",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return new ProcessParentIdReader(
            property?.PropertyType,
            property?.CanRead == true,
            property is null ? null : process => property.GetValue(process),
            snapshotFailure);
    }

    internal void ValidateContract()
    {
        if (_valueType != typeof(int) || !_canRead || _getter is null)
        {
            throw new PlatformNotSupportedException(PrerequisiteMessage);
        }

        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            if (_getter(currentProcess) is not int)
            {
                throw new PlatformNotSupportedException(PrerequisiteMessage);
            }
        }
        catch (PlatformNotSupportedException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new PlatformNotSupportedException(PrerequisiteMessage);
        }
    }

    internal void BeginSnapshot()
    {
        if (_snapshotFailure is null)
        {
            return;
        }

        var failure = _snapshotFailure(Interlocked.Increment(ref _snapshotCount));
        if (failure is not null)
        {
            throw new ProcessTreeInspectionException(failure);
        }
    }

    internal bool TryRead(Process process, out int parentProcessId)
    {
        try
        {
            var value = _getter!(process);
            if (value is int parsed)
            {
                parentProcessId = parsed;
                return true;
            }

            throw new ProcessTreeInspectionException();
        }
        catch (TargetInvocationException exception) when (
            exception.InnerException is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            parentProcessId = default;
            return false;
        }
        catch (ProcessTreeInspectionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProcessTreeInspectionException(exception);
        }
    }
}

internal sealed class ProcessTreeInspectionException(Exception? innerException = null)
    : Exception("Process tree inspection failed.", innerException);
