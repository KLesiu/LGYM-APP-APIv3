using System.Security.Cryptography;
using System.Diagnostics;
using DotNet.Testcontainers.Containers;
using LgymApi.E2ETests.Configuration;
using LgymApi.E2ETests.Lifecycle;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace LgymApi.E2ETests.Harness;

public sealed class PostgreSqlContainerLease : IAsyncDisposable
{
    private const int PostgreSqlPort = 5432;
    private const string PostgreSqlUsername = "postgres";
    private readonly IPostgreSqlContainerLeaseOperations _operations;
    private readonly TimeSpan _cleanupTimeout;
    private readonly ScenarioResourceIdentity _identity;
    private readonly SemaphoreSlim _cleanupGate = new(1, 1);
    private readonly object _cleanupStateLock = new();
    private PostgreSqlCleanupAttempt? _cleanupAttempt;
    private PostgreSqlCleanupState _cleanupState;

    private const string CleanupFailureMessage = "Testcontainers PostgreSQL cleanup failed.";
    private const string CleanupTimeoutMessage = "Testcontainers PostgreSQL cleanup exceeded the configured shutdown timeout.";

    private PostgreSqlContainerLease(
        IPostgreSqlContainerLeaseOperations operations,
        TimeSpan cleanupTimeout,
        ScenarioResourceIdentity identity)
    {
        _operations = operations;
        _cleanupTimeout = cleanupTimeout;
        _identity = identity;
    }

    internal string ConnectionString => _operations.ConnectionString;

    internal bool IsRunning => _operations.IsRunning;

    internal PostgreSqlCleanupReceipt CleanupReceipt { get; private set; } = new("container-cleanup", false, TimeSpan.Zero);

    public static Task<PostgreSqlContainerLease> StartAsync(CancellationToken cancellationToken = default) =>
        StartAsync(null, cancellationToken);

    internal static async Task<PostgreSqlContainerLease> StartAsync(
        Func<PostgreSqlContainer, CancellationToken, Task>? startupCallback,
        CancellationToken cancellationToken = default)
    {
        var options = E2EConfiguration.Load(TestContext.CurrentContext.TestDirectory, RepositoryRoot.Find());
        using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupTimeout.CancelAfter(TimeSpan.FromSeconds(options.Timeouts.ContainerStartupSeconds));
        await DockerContainerProbe.EnsureAvailableAsync(startupTimeout.Token, cancellationToken);

        var containerName = $"{options.Database.NamePrefix}-{CreateRandomValue()}";
        var builder = new PostgreSqlBuilder(options.Database.Image)
            .WithDatabase($"{options.Database.NamePrefix}_{CreateRandomValue()}")
            .WithName(containerName)
            .WithUsername(PostgreSqlUsername)
            .WithPassword(CreateRandomValue())
            .WithCleanUp(true)
            .WithLogger(NullLogger.Instance);
        if (startupCallback is not null)
        {
            builder = builder.WithStartupCallback(startupCallback);
        }

        var container = builder.Build();

        return await StartAsync(
            new TestcontainersPostgreSqlContainerLeaseOperations(container, containerName),
            TimeSpan.FromSeconds(options.Timeouts.ProcessShutdownSeconds),
            startupTimeout.Token);
    }

    internal static async Task<PostgreSqlContainerLease> StartAsync(
        IPostgreSqlContainerLeaseOperations operations,
        TimeSpan cleanupTimeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await operations.StartAsync(cancellationToken);
            return new PostgreSqlContainerLease(operations, cleanupTimeout, ScenarioResourceIdentity.Create());
        }
        catch
        {
            var failedLease = new PostgreSqlContainerLease(operations, cleanupTimeout, ScenarioResourceIdentity.Create());
            try
            {
                await failedLease.DisposeAsync();
            }
            catch (InvalidOperationException cleanupFailure)
            {
                throw new InvalidOperationException(cleanupFailure.Message);
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var cleanupDeadline = new CancellationTokenSource(_cleanupTimeout);
        PostgreSqlCleanupAttempt cleanupAttempt;
        try
        {
            await _cleanupGate.WaitAsync(cleanupDeadline.Token);
            try
            {
                cleanupAttempt = GetOrStartCleanupAttempt();
            }
            finally
            {
                _cleanupGate.Release();
            }

            var outcome = await cleanupAttempt.Completion.WaitAsync(cleanupDeadline.Token);
            if (!outcome.Succeeded)
            {
                throw new InvalidOperationException(outcome.FailureMessage);
            }

            CleanupReceipt = outcome.Receipt!;
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException(CleanupTimeoutMessage);
        }
    }

    private static string CreateRandomValue() => RandomNumberGenerator.GetHexString(16).ToLowerInvariant();

    private PostgreSqlCleanupAttempt GetOrStartCleanupAttempt()
    {
        lock (_cleanupStateLock)
        {
            if (_cleanupState is PostgreSqlCleanupState.InFlight or PostgreSqlCleanupState.Succeeded)
            {
                return _cleanupAttempt!;
            }

            var cleanupAttempt = new PostgreSqlCleanupAttempt(CaptureRawDisposalTask());
            _cleanupAttempt = cleanupAttempt;
            _cleanupState = PostgreSqlCleanupState.InFlight;
            cleanupAttempt.Completion = CompleteCleanupAttemptAsync(cleanupAttempt);
            return cleanupAttempt;
        }
    }

    private Task CaptureRawDisposalTask()
    {
        try
        {
            return _operations.DisposeAsync();
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }

    private async Task<PostgreSqlCleanupOutcome> CompleteCleanupAttemptAsync(PostgreSqlCleanupAttempt cleanupAttempt)
    {
        var startedAt = Stopwatch.GetTimestamp();
        Exception? disposalFailure = null;
        try
        {
            await cleanupAttempt.RawDisposal;
        }
        catch (Exception exception)
        {
            disposalFailure = exception;
        }

        var wasRemoved = false;
        try
        {
            using var absenceDeadline = new CancellationTokenSource(_cleanupTimeout);
            wasRemoved = await _operations.WaitUntilAbsentAsync(absenceDeadline.Token);
        }
        catch (Exception)
        {
            wasRemoved = false;
        }

        var outcome = disposalFailure is not null
            ? PostgreSqlCleanupOutcome.Failed(CleanupFailureMessage)
            : !wasRemoved
                ? PostgreSqlCleanupOutcome.Failed(CleanupTimeoutMessage)
                : PostgreSqlCleanupOutcome.Success(new PostgreSqlCleanupReceipt(
                    "container-cleanup",
                    true,
                    Stopwatch.GetElapsedTime(startedAt)));

        lock (_cleanupStateLock)
        {
            if (ReferenceEquals(_cleanupAttempt, cleanupAttempt))
            {
                _cleanupState = outcome.Succeeded
                    ? PostgreSqlCleanupState.Succeeded
                    : PostgreSqlCleanupState.Failed;
            }
        }

        return outcome;
    }

    internal async Task<bool> ConfirmAbsentAsync()
    {
        using var deadline = new CancellationTokenSource(_cleanupTimeout);
        try
        {
            return await _operations.WaitUntilAbsentAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    internal ScenarioResourceObservation CreateObservation() =>
        new(_identity, ConfirmAbsentAsync);

    public override string ToString() => "<postgresql-container-lease>";

}

internal interface IPostgreSqlContainerLeaseOperations
{
    string ConnectionString { get; }

    bool IsRunning { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task DisposeAsync();

    Task<bool> WaitUntilAbsentAsync(CancellationToken cancellationToken);
}

internal sealed class TestcontainersPostgreSqlContainerLeaseOperations(
    PostgreSqlContainer container,
    string containerLocator)
    : IPostgreSqlContainerLeaseOperations
{
    public string ConnectionString => container.GetConnectionString();

    public bool IsRunning => container.State == TestcontainersStates.Running;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await container.StartAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(container.Id))
        {
            throw new InvalidOperationException("Testcontainers started PostgreSQL without a container ID.");
        }
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    public Task<bool> WaitUntilAbsentAsync(CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(containerLocator)
            ? Task.FromResult(false)
            : DockerContainerProbe.WaitUntilAbsentAsync(containerLocator, cancellationToken);
    }
}

internal sealed record PostgreSqlCleanupReceipt(string Category, bool ContainerAbsent, TimeSpan Duration);

internal enum PostgreSqlCleanupState
{
    Idle,
    InFlight,
    Succeeded,
    Failed
}

internal sealed class PostgreSqlCleanupAttempt(Task rawDisposal)
{
    internal Task RawDisposal { get; } = rawDisposal;

    internal Task<PostgreSqlCleanupOutcome> Completion { get; set; } = null!;
}

internal sealed record PostgreSqlCleanupOutcome(bool Succeeded, PostgreSqlCleanupReceipt? Receipt, string FailureMessage)
{
    internal static PostgreSqlCleanupOutcome Success(PostgreSqlCleanupReceipt receipt) => new(true, receipt, string.Empty);

    internal static PostgreSqlCleanupOutcome Failed(string failureMessage) => new(false, null, failureMessage);
}
