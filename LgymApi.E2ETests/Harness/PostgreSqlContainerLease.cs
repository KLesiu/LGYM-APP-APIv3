using System.Security.Cryptography;
using System.Diagnostics;
using DotNet.Testcontainers.Containers;
using LgymApi.E2ETests.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace LgymApi.E2ETests.Harness;

public sealed class PostgreSqlContainerLease : IAsyncDisposable
{
    private const int PostgreSqlPort = 5432;
    private const string PostgreSqlUsername = "postgres";
    private readonly PostgreSqlContainer _container;
    private readonly string _containerId;
    private readonly string _connectionString;
    private readonly TimeSpan _cleanupTimeout;
    private int _disposed;

    private PostgreSqlContainerLease(
        PostgreSqlContainer container,
        string containerId,
        string connectionString,
        TimeSpan cleanupTimeout)
    {
        _container = container;
        _containerId = containerId;
        _connectionString = connectionString;
        _cleanupTimeout = cleanupTimeout;
    }

    internal string ConnectionString => _connectionString;

    internal bool IsRunning => _container.State == TestcontainersStates.Running;

    internal PostgreSqlCleanupReceipt CleanupReceipt { get; private set; } = new("container-cleanup", false, TimeSpan.Zero);

    public static async Task<PostgreSqlContainerLease> StartAsync(CancellationToken cancellationToken = default)
    {
        var options = E2EConfiguration.Load(TestContext.CurrentContext.TestDirectory, RepositoryRoot.Find());
        using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupTimeout.CancelAfter(TimeSpan.FromSeconds(options.Timeouts.ContainerStartupSeconds));
        await DockerContainerProbe.EnsureAvailableAsync(startupTimeout.Token, cancellationToken);

        var container = new PostgreSqlBuilder(options.Database.Image)
            .WithDatabase($"{options.Database.NamePrefix}_{CreateRandomValue()}")
            .WithName($"{options.Database.NamePrefix}-{CreateRandomValue()}")
            .WithUsername(PostgreSqlUsername)
            .WithPassword(CreateRandomValue())
            .WithCleanUp(true)
            .WithLogger(NullLogger.Instance)
            .Build();

        try
        {
            await container.StartAsync(startupTimeout.Token);

            var containerId = container.Id;
            if (string.IsNullOrWhiteSpace(containerId))
            {
                throw new InvalidOperationException("Testcontainers started PostgreSQL without a container ID.");
            }

            return new PostgreSqlContainerLease(
                container,
                containerId,
                container.GetConnectionString(),
                TimeSpan.FromSeconds(options.Timeouts.ProcessShutdownSeconds));
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var wasRemoved = false;
        try
        {
            using var timeout = new CancellationTokenSource(_cleanupTimeout);
            await _container.DisposeAsync().AsTask().WaitAsync(timeout.Token);
        }
        finally
        {
            wasRemoved = await DockerContainerProbe.WaitUntilAbsentAsync(_containerId, _cleanupTimeout);
            CleanupReceipt = new PostgreSqlCleanupReceipt(
                "container-cleanup",
                wasRemoved,
                Stopwatch.GetElapsedTime(startedAt));
        }

        if (!wasRemoved)
        {
            throw new InvalidOperationException("Testcontainers PostgreSQL cleanup exceeded the configured shutdown timeout.");
        }
    }

    private static string CreateRandomValue() => RandomNumberGenerator.GetHexString(16).ToLowerInvariant();

    internal Task<bool> ConfirmAbsentAsync() => DockerContainerProbe.WaitUntilAbsentAsync(_containerId, _cleanupTimeout);

    public override string ToString() => "<postgresql-container-lease>";

}

internal sealed record PostgreSqlCleanupReceipt(string Category, bool ContainerAbsent, TimeSpan Duration);
