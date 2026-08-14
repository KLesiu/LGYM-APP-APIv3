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
    private int _disposed;

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

    public static async Task<PostgreSqlContainerLease> StartAsync(CancellationToken cancellationToken = default)
    {
        var options = E2EConfiguration.Load(TestContext.CurrentContext.TestDirectory, RepositoryRoot.Find());
        using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupTimeout.CancelAfter(TimeSpan.FromSeconds(options.Timeouts.ContainerStartupSeconds));
        await DockerContainerProbe.EnsureAvailableAsync(startupTimeout.Token, cancellationToken);

        var containerName = $"{options.Database.NamePrefix}-{CreateRandomValue()}";
        var container = new PostgreSqlBuilder(options.Database.Image)
            .WithDatabase($"{options.Database.NamePrefix}_{CreateRandomValue()}")
            .WithName(containerName)
            .WithUsername(PostgreSqlUsername)
            .WithPassword(CreateRandomValue())
            .WithCleanUp(true)
            .WithLogger(NullLogger.Instance)
            .Build();

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
            await DisposeStartupFailureAsync(operations, cleanupTimeout);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cleanupGate.WaitAsync();
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            CleanupReceipt = await CleanupAsync(_operations, _cleanupTimeout);
            Interlocked.Exchange(ref _disposed, 1);
        }
        finally
        {
            _cleanupGate.Release();
        }
    }

    private static string CreateRandomValue() => RandomNumberGenerator.GetHexString(16).ToLowerInvariant();

    private static async Task DisposeStartupFailureAsync(
        IPostgreSqlContainerLeaseOperations operations,
        TimeSpan cleanupTimeout)
    {
        _ = await CleanupAsync(operations, cleanupTimeout);
    }

    private static async Task<PostgreSqlCleanupReceipt> CleanupAsync(
        IPostgreSqlContainerLeaseOperations operations,
        TimeSpan cleanupTimeout)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var cleanupDeadline = new CancellationTokenSource(cleanupTimeout);
        var wasRemoved = false;
        Exception? disposalFailure = null;
        try
        {
            await operations.DisposeAsync(cleanupDeadline.Token);
        }
        catch (Exception exception)
        {
            disposalFailure = exception;
        }

        try
        {
            wasRemoved = await operations.WaitUntilAbsentAsync(cleanupDeadline.Token);
        }
        catch (OperationCanceledException)
        {
            wasRemoved = false;
        }

        if (disposalFailure is not null)
        {
            throw new InvalidOperationException("Testcontainers PostgreSQL cleanup failed.", disposalFailure);
        }

        if (!wasRemoved)
        {
            throw new InvalidOperationException("Testcontainers PostgreSQL cleanup exceeded the configured shutdown timeout.");
        }

        return new PostgreSqlCleanupReceipt(
            "container-cleanup",
            wasRemoved,
            Stopwatch.GetElapsedTime(startedAt));
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

    Task DisposeAsync(CancellationToken cancellationToken);

    Task<bool> WaitUntilAbsentAsync(CancellationToken cancellationToken);
}

internal sealed class TestcontainersPostgreSqlContainerLeaseOperations(
    PostgreSqlContainer container,
    string containerLocator)
    : IPostgreSqlContainerLeaseOperations
{
    private Task? _disposal;

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

    public Task DisposeAsync(CancellationToken cancellationToken)
    {
        var disposal = Volatile.Read(ref _disposal);
        if (disposal is null)
        {
            var started = container.DisposeAsync().AsTask();
            disposal = Interlocked.CompareExchange(ref _disposal, started, null) ?? started;
            if (ReferenceEquals(disposal, started))
            {
                _ = started.ContinueWith(
                    completed =>
                    {
                        _ = completed.Exception;
                        if (completed.IsFaulted)
                        {
                            Interlocked.CompareExchange(ref _disposal, null, completed);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        return disposal.WaitAsync(cancellationToken);
    }

    public Task<bool> WaitUntilAbsentAsync(CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(containerLocator)
            ? Task.FromResult(false)
            : DockerContainerProbe.WaitUntilAbsentAsync(containerLocator, cancellationToken);
    }
}

internal sealed record PostgreSqlCleanupReceipt(string Category, bool ContainerAbsent, TimeSpan Duration);
