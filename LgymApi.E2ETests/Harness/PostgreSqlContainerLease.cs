using System.Security.Cryptography;
using DotNet.Testcontainers.Containers;
using LgymApi.E2ETests.Configuration;
using Testcontainers.PostgreSql;

namespace LgymApi.E2ETests.Harness;

public sealed class PostgreSqlContainerLease : IAsyncDisposable
{
    private const int PostgreSqlPort = 5432;
    private const string PostgreSqlUsername = "postgres";
    private readonly PostgreSqlContainer _container;
    private readonly TimeSpan _cleanupTimeout;
    private int _disposed;

    private PostgreSqlContainerLease(PostgreSqlContainer container, string containerId, int mappedPort, TimeSpan cleanupTimeout)
    {
        _container = container;
        ContainerId = containerId;
        MappedPort = mappedPort;
        _cleanupTimeout = cleanupTimeout;
    }

    public string ContainerId { get; }

    public int MappedPort { get; }

    public bool IsRunning => _container.State == TestcontainersStates.Running;

    public bool WasRemoved { get; private set; }

    public static async Task<PostgreSqlContainerLease> StartAsync(CancellationToken cancellationToken = default)
    {
        var options = E2EConfiguration.Load(TestContext.CurrentContext.TestDirectory, RepositoryRoot.Find());
        using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupTimeout.CancelAfter(TimeSpan.FromSeconds(options.Timeouts.ContainerStartupSeconds));
        await DockerContainerProbe.EnsureAvailableAsync(startupTimeout.Token, cancellationToken);

        var container = new PostgreSqlBuilder("postgres:17.10-alpine3.24")
            .WithDatabase($"{options.Database.NamePrefix}_{CreateRandomValue()}")
            .WithUsername(PostgreSqlUsername)
            .WithPassword(CreateRandomValue())
            .WithCleanUp(true)
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
                container.GetMappedPublicPort(PostgreSqlPort),
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

        try
        {
            await _container.DisposeAsync();
        }
        finally
        {
            WasRemoved = await DockerContainerProbe.WaitUntilAbsentAsync(ContainerId, _cleanupTimeout);
        }

        if (!WasRemoved)
        {
            throw new InvalidOperationException("Testcontainers PostgreSQL cleanup exceeded the configured shutdown timeout.");
        }
    }

    private static string CreateRandomValue() => RandomNumberGenerator.GetHexString(16).ToLowerInvariant();

}
