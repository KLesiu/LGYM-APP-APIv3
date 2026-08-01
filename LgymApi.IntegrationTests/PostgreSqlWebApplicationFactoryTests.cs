using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
[Category("PostgreSql")]
public sealed class PostgreSqlWebApplicationFactoryTests
{
    [Test]
    public void RemoveAppDbContextRegistrations_PreservesUnrelatedEntityFrameworkServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>();
        services.AddSingleton(typeof(IModelCacheKeyFactory), _ => null!);

        PostgreSqlWebApplicationFactory.RemoveAppDbContextRegistrations(services);

        Assert.Multiple(() =>
        {
            Assert.That(services.Any(descriptor => descriptor.ServiceType == typeof(IModelCacheKeyFactory)), Is.True);
            Assert.That(services.Any(descriptor => descriptor.ServiceType == typeof(AppDbContext)), Is.False);
            Assert.That(services.Any(descriptor => descriptor.ServiceType == typeof(DbContextOptions)), Is.False);
            Assert.That(services.Any(descriptor => descriptor.ServiceType == typeof(DbContextOptions<AppDbContext>)), Is.False);
            Assert.That(services.Any(descriptor => descriptor.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>)), Is.False);
        });
    }

    [Test]
    public async Task CompleteInitializationAsync_WhenInitializationFails_PreservesRootException()
    {
        var rootException = new FactoryInitializationProbeException("root-sentinel");
        var probe = new FactoryLifecycleProbe();

        var action = async () => await PostgreSqlWebApplicationFactory.CompleteInitializationAsync(
            probe,
            (_, _) => Task.FromException(rootException),
            target => target.CleanupAsync(),
            CancellationToken.None);

        var exception = await action.Should().ThrowExactlyAsync<FactoryInitializationProbeException>();

        exception.Which.Should().BeSameAs(rootException);
        probe.CleanupAttempts.Should().Be(1);
    }

    [Test]
    public async Task CompleteInitializationAsync_WhenInitializationAndCleanupFail_PreservesBothExceptionsWithRedactedContext()
    {
        var rootException = new FactoryInitializationProbeException("root-sentinel");
        var cleanupException = new FactoryCleanupProbeException("cleanup-sentinel");
        var probe = new FactoryLifecycleProbe(cleanupException);

        var action = async () => await PostgreSqlWebApplicationFactory.CompleteInitializationAsync(
            probe,
            (_, _) => Task.FromException(rootException),
            target => target.CleanupAsync(),
            CancellationToken.None);

        var exception = await action.Should().ThrowExactlyAsync<PostgreSqlFactoryInitializationException>();

        exception.Which.InnerException.Should().BeSameAs(rootException);
        exception.Which.CleanupException.Should().BeSameAs(cleanupException);
        exception.Which.Message.Should().Contain("connection value is redacted");
        exception.Which.Message.Should().NotContain("root-sentinel");
        exception.Which.Message.Should().NotContain("cleanup-sentinel");
        probe.CleanupAttempts.Should().Be(1);
    }

    [Test]
    public async Task CompleteInitializationAsync_WhenCanceled_PreservesCancellationException()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var cancellationException = new OperationCanceledException(cancellationSource.Token);
        var probe = new FactoryLifecycleProbe();

        var action = async () => await PostgreSqlWebApplicationFactory.CompleteInitializationAsync(
            probe,
            (_, _) => Task.FromException(cancellationException),
            target => target.CleanupAsync(),
            cancellationSource.Token);

        var exception = await action.Should().ThrowExactlyAsync<OperationCanceledException>();

        exception.Which.Should().BeSameAs(cancellationException);
        exception.Which.CancellationToken.Should().Be(cancellationSource.Token);
        probe.CleanupAttempts.Should().Be(1);
    }

    [Test]
    public async Task CompleteInitializationAsync_WhenCanceledAndCleanupFails_PreservesCancellationAndCleanupExceptions()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var cancellationException = new OperationCanceledException(cancellationSource.Token);
        var cleanupException = new FactoryCleanupProbeException("cleanup-sentinel");
        var probe = new FactoryLifecycleProbe(cleanupException);

        var action = async () => await PostgreSqlWebApplicationFactory.CompleteInitializationAsync(
            probe,
            (_, _) => Task.FromException(cancellationException),
            target => target.CleanupAsync(),
            cancellationSource.Token);

        var exception = await action.Should().ThrowExactlyAsync<PostgreSqlFactoryInitializationCanceledException>();

        exception.Which.InnerException.Should().BeSameAs(cancellationException);
        exception.Which.CleanupException.Should().BeSameAs(cleanupException);
        exception.Which.CancellationToken.Should().Be(cancellationSource.Token);
        exception.Which.Message.Should().Contain("connection value is redacted");
        exception.Which.Message.Should().NotContain("cleanup-sentinel");
        probe.CleanupAttempts.Should().Be(1);
    }

    [Test]
    public async Task CompleteDisposalAsync_WhenBaseDisposalFails_PreservesBaseException()
    {
        var baseException = new FactoryBaseDisposalProbeException("base-sentinel");

        var action = async () => await PostgreSqlWebApplicationFactory.CompleteDisposalAsync(
            () => ValueTask.FromException(baseException),
            static () => ValueTask.CompletedTask);

        var exception = await action.Should().ThrowExactlyAsync<FactoryBaseDisposalProbeException>();

        exception.Which.Should().BeSameAs(baseException);
    }

    [Test]
    public async Task CompleteDisposalAsync_WhenLeaseDisposalFails_PreservesLeaseException()
    {
        var leaseException = new FactoryLeaseDisposalProbeException("lease-sentinel");

        var action = async () => await PostgreSqlWebApplicationFactory.CompleteDisposalAsync(
            static () => ValueTask.CompletedTask,
            () => ValueTask.FromException(leaseException));

        var exception = await action.Should().ThrowExactlyAsync<FactoryLeaseDisposalProbeException>();

        exception.Which.Should().BeSameAs(leaseException);
    }

    [Test]
    public async Task CompleteDisposalAsync_WhenBaseAndLeaseDisposalFail_PreservesBothExceptionsWithRedactedContext()
    {
        var baseException = new FactoryBaseDisposalProbeException("base-sentinel");
        var leaseException = new FactoryLeaseDisposalProbeException("lease-sentinel");

        var action = async () => await PostgreSqlWebApplicationFactory.CompleteDisposalAsync(
            () => ValueTask.FromException(baseException),
            () => ValueTask.FromException(leaseException));

        var exception = await action.Should().ThrowExactlyAsync<PostgreSqlFactoryDisposalException>();

        exception.Which.InnerException.Should().BeSameAs(baseException);
        exception.Which.LeaseCleanupException.Should().BeSameAs(leaseException);
        exception.Which.Message.Should().Contain("connection value is redacted");
        exception.Which.Message.Should().NotContain("base-sentinel");
        exception.Which.Message.Should().NotContain("lease-sentinel");
    }

    private sealed class FactoryLifecycleProbe
    {
        private readonly Exception? _cleanupException;

        public FactoryLifecycleProbe(Exception? cleanupException = null)
        {
            _cleanupException = cleanupException;
        }

        public int CleanupAttempts { get; private set; }

        public ValueTask CleanupAsync()
        {
            CleanupAttempts++;
            return _cleanupException == null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(_cleanupException);
        }
    }

    private sealed class FactoryInitializationProbeException(string message) : Exception(message);

    private sealed class FactoryCleanupProbeException(string message) : Exception(message);

    private sealed class FactoryBaseDisposalProbeException(string message) : Exception(message);

    private sealed class FactoryLeaseDisposalProbeException(string message) : Exception(message);
}
