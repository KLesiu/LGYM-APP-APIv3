using System.Diagnostics;
using System.Security.Cryptography;
using DotNet.Testcontainers.Containers;
using LgymApi.E2ETests.Configuration;
using Testcontainers.PostgreSql;

namespace LgymApi.E2ETests.Harness;

public sealed class PostgreSqlContainerLease : IAsyncDisposable
{
    private const string DockerPrerequisiteMessage = "Docker is unavailable for the E2E PostgreSQL lifecycle. Ensure the Docker daemon is running.";
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

    private static class DockerContainerProbe
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

        public static async Task EnsureAvailableAsync(CancellationToken timeoutToken, CancellationToken callerToken)
        {
            using var process = StartDockerVersion();
            var standardErrorTask = process.StandardError.ReadToEndAsync(timeoutToken);
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(timeoutToken);

            try
            {
                await process.WaitForExitAsync(timeoutToken);
            }
            catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw new InvalidOperationException(DockerPrerequisiteMessage);
            }

            _ = await standardOutputTask;
            _ = await standardErrorTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(DockerPrerequisiteMessage);
            }
        }

        public static async Task<bool> WaitUntilAbsentAsync(string containerId, TimeSpan timeout)
        {
            var deadline = DateTimeOffset.UtcNow.Add(timeout);

            while (true)
            {
                var remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    return false;
                }

                using var inspectionTimeout = new CancellationTokenSource(remaining);
                if (await IsAbsentAsync(containerId, inspectionTimeout.Token))
                {
                    return true;
                }

                remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    return false;
                }

                await Task.Delay(remaining < PollInterval ? remaining : PollInterval);
            }
        }

        private static async Task<bool> IsAbsentAsync(string containerId, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("inspect");
            startInfo.ArgumentList.Add("--type");
            startInfo.ArgumentList.Add("container");
            startInfo.ArgumentList.Add("--format");
            startInfo.ArgumentList.Add("{{.Id}}");
            startInfo.ArgumentList.Add(containerId);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start Docker inspection for PostgreSQL cleanup.");
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw new InvalidOperationException("Docker inspection exceeded the configured shutdown timeout.");
            }

            _ = await standardOutputTask;
            var standardError = await standardErrorTask;
            if (process.ExitCode == 0)
            {
                return false;
            }

            if (standardError.Contains("No such object", StringComparison.OrdinalIgnoreCase) ||
                standardError.Contains("No such container", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            throw new InvalidOperationException($"Docker inspection failed with exit code {process.ExitCode} during PostgreSQL cleanup.");
        }

        private static Process StartDockerVersion()
        {
            var startInfo = new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("version");
            startInfo.ArgumentList.Add("--format");
            startInfo.ArgumentList.Add("{{.Server.Version}}");

            try
            {
                return Process.Start(startInfo)
                    ?? throw new InvalidOperationException(DockerPrerequisiteMessage);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new InvalidOperationException(DockerPrerequisiteMessage);
            }
        }
    }
}
