using LgymApi.Domain.Security;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Application.Repositories;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
[Category("PostgreSql")]
public sealed class PostgreSqlDatabaseLeaseTests
{
    [Test]
    public async Task CreateAsync_WithTwoSimultaneousFactories_UsesDistinctDatabasesAndDropsBothAfterDisposal()
    {
        var databaseNames = await CreateAndDisposeTwoFactoriesAsync();

        databaseNames.First.Should().NotBe(databaseNames.Second);
        (await PostgreSqlDatabaseLease.DatabaseExistsAsync(databaseNames.First)).Should().BeFalse();
        (await PostgreSqlDatabaseLease.DatabaseExistsAsync(databaseNames.Second)).Should().BeFalse();
    }

    [Test]
    public async Task DisposeAsync_WhenTestBodyThrows_DropsTheLeasedDatabase()
    {
        var databaseName = string.Empty;

        try
        {
            await using (var factory = await PostgreSqlWebApplicationFactory.CreateAsync())
            {
                databaseName = factory.DatabaseName;
                throw new InvalidOperationException("Simulated test-body failure");
            }
        }
        catch (InvalidOperationException exception) when (exception.Message == "Simulated test-body failure")
        {
        }

        (await PostgreSqlDatabaseLease.DatabaseExistsAsync(databaseName)).Should().BeFalse();
    }

    [Test]
    public async Task CreateAsync_UsesNpgsqlMigrationsAndSeedsDefaultRoles()
    {
        await using var factory = await PostgreSqlWebApplicationFactory.CreateAsync();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        database.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
        (await database.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        scope.ServiceProvider.GetServices<IEmailSender>().Should().ContainSingle()
            .Which.Should().BeSameAs(factory.EmailSender);
        scope.ServiceProvider.GetServices<IPushProviderSender>().Should().ContainSingle()
            .Which.Should().BeSameAs(factory.PushSender);

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await using (var transaction = await unitOfWork.BeginTransactionAsync())
        {
            await transaction.RollbackAsync();
        }

        var roleNames = await database.Roles.Select(role => role.Name).ToListAsync();
        roleNames.Should().Contain(
            AuthConstants.Roles.User,
            AuthConstants.Roles.Admin,
            AuthConstants.Roles.Tester,
            AuthConstants.Roles.Trainer);
    }

    [Test]
    public async Task CreateAsync_WhenConnectionIsMissing_ThrowsActionableSkipWithoutSecret()
    {
        using var environment = new EnvironmentVariableScope(value: null);

        var action = async () => await PostgreSqlDatabaseLease.CreateAsync();

        var exception = await action.Should().ThrowAsync<IgnoreException>();

        exception.Which.Message.Should().Contain("LGYM_TEST_POSTGRES");
        exception.Which.Message.Should().Contain("admin PostgreSQL connection");
        exception.Which.Message.Should().NotContain("Password");
    }

    [Test]
    public async Task CreateAsync_WhenConnectionIsMalformed_ThrowsRedactedActionableError()
    {
        const string password = "must-not-appear";
        using var environment = new EnvironmentVariableScope($"Host=127.0.0.1;Port=not-a-port;Password={password}");

        var action = async () => await PostgreSqlDatabaseLease.CreateAsync();

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();

        exception.Which.Message.Should().Contain("LGYM_TEST_POSTGRES");
        exception.Which.Message.Should().Contain("could not be parsed");
        exception.Which.Message.Should().NotContain(password);
    }

    [Test]
    public async Task CreateAsync_WhenNpgsqlFails_RetainsRedactedMessageAndExactInnerCause()
    {
        const string password = "must-not-appear";
        var npgsqlException = new NpgsqlException("Forced create failure.");
        var operations = new ControllableDatabaseLeaseOperations
        {
            CreateException = npgsqlException
        };

        var action = async () => await PostgreSqlDatabaseLease.CreateWithRedactedNpgsqlAsync(
            $"Host=test;Password={password}",
            operations);

        var exception = await action.Should().ThrowExactlyAsync<InvalidOperationException>();

        exception.Which.Message.Should().Be(
            "Could not create an isolated PostgreSQL test database from LGYM_TEST_POSTGRES. Verify the admin connection and CREATE DATABASE permission. The connection value is redacted.");
        exception.Which.InnerException.Should().BeSameAs(npgsqlException);
        exception.Which.ToString().Should().NotContain(password);
        operations.DropAttempts.Should().Be(1);
        operations.DatabaseNames.Should().BeEmpty();
    }

    [Test]
    public async Task CreateAsync_WhenLeaseConstructionFailsAfterDatabaseCreation_DropsDatabase()
    {
        var operations = new ControllableDatabaseLeaseOperations
        {
            FailConnectionStringBuild = true
        };

        var action = async () => await PostgreSqlDatabaseLease.CreateAsync("Host=test", operations);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Forced lease construction failure.");
        operations.DatabaseNames.Should().BeEmpty();
        operations.DropAttempts.Should().Be(1);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CreateAsync_WhenCreateThrows_ReconcilesGeneratedNameAndPreservesOriginalError(
        bool databaseCreatedBeforeFailure)
    {
        var createException = new InvalidOperationException("Forced lease creation failure.");
        var operations = new ControllableDatabaseLeaseOperations
        {
            CreateException = createException,
            CreateDatabaseBeforeFailure = databaseCreatedBeforeFailure
        };

        var action = async () => await PostgreSqlDatabaseLease.CreateAsync("Host=test", operations);

        var exception = await action.Should().ThrowExactlyAsync<InvalidOperationException>();

        exception.Which.Should().BeSameAs(createException);
        operations.DropAttempts.Should().Be(1);
        operations.DatabaseNames.Should().BeEmpty();
    }

    [Test]
    public async Task DisposeAsync_WhenFirstDropFails_RetriesOnNextDispose()
    {
        var operations = new ControllableDatabaseLeaseOperations
        {
            RemainingDropFailures = 1
        };
        var lease = await PostgreSqlDatabaseLease.CreateAsync("Host=test", operations);

        var firstDispose = async () => await lease.DisposeAsync();

        await firstDispose.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Forced drop failure.");
        operations.DatabaseNames.Should().ContainSingle(lease.DatabaseName);

        await lease.DisposeAsync();
        operations.DatabaseNames.Should().BeEmpty();
        operations.DropAttempts.Should().Be(2);

        await lease.DisposeAsync();
        operations.DropAttempts.Should().Be(2);
    }

    [Test]
    public async Task DisposeAsync_WhenNpgsqlFails_RetainsRedactedMessageAndExactInnerCauseThenRetries()
    {
        const string password = "must-not-appear";
        var npgsqlException = new NpgsqlException("Forced drop failure.");
        var operations = new ControllableDatabaseLeaseOperations
        {
            NpgsqlDropException = npgsqlException,
            RemainingNpgsqlDropFailures = 1
        };
        var lease = await PostgreSqlDatabaseLease.CreateAsync(
            $"Host=test;Password={password}",
            operations);

        var firstDispose = async () => await lease.DisposeAsync();

        var exception = await firstDispose.Should().ThrowExactlyAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be(
            $"Could not remove isolated PostgreSQL test database '{lease.DatabaseName}'. The connection value is redacted.");
        exception.Which.InnerException.Should().BeSameAs(npgsqlException);
        exception.Which.ToString().Should().NotContain(password);
        operations.DatabaseNames.Should().ContainSingle(lease.DatabaseName);

        await lease.DisposeAsync();
        operations.DropAttempts.Should().Be(2);
        operations.DatabaseNames.Should().BeEmpty();
    }

    [Test]
    public async Task CreateAsync_WhenAmbiguousCreateAndCompensationFail_CorrelatesBothErrorsWithoutSecret()
    {
        const string password = "must-not-appear";
        var operations = new ControllableDatabaseLeaseOperations
        {
            CreateException = new InvalidOperationException("Forced lease creation failure."),
            CreateDatabaseBeforeFailure = true,
            RemainingDropFailures = 1
        };

        var action = async () => await PostgreSqlDatabaseLease.CreateAsync(
            $"Host=test;Password={password}",
            operations);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();

        exception.Which.Message.Should().Contain("acquisition and compensating cleanup also failed");
        exception.Which.InnerException.Should().BeOfType<AggregateException>()
            .Which.InnerExceptions.Select(error => error.Message)
            .Should().Equal("Forced lease creation failure.", "Forced drop failure.");
        exception.Which.ToString().Should().NotContain(password);
    }

    [Test]
    public async Task CreateAllOrDisposeSuccessfulAsync_WhenSiblingCreationFails_DropsSuccessfulSiblingDatabaseOnce()
    {
        var operations = new ControllableDatabaseLeaseOperations();
        var lease = await PostgreSqlDatabaseLease.CreateAsync("Host=test", operations);
        var successfulFactory = new LeaseBackedTestFactory(lease);
        var successfulCreation = Task.FromResult(successfulFactory);
        var creationException = new InvalidOperationException("Forced sibling creation failure.");

        async Task<LeaseBackedTestFactory> FailAfterSuccessfulSiblingAsync()
        {
            await successfulCreation;
            throw creationException;
        }

        var action = async () => await ConcurrentFactoryCreation.CreateAllOrDisposeSuccessfulAsync(
            () => successfulCreation,
            FailAfterSuccessfulSiblingAsync);

        try
        {
            var exception = await action.Should().ThrowExactlyAsync<InvalidOperationException>();

            exception.Which.Should().BeSameAs(creationException);
            successfulFactory.DisposalAttempts.Should().Be(1);
            operations.DatabaseNames.Should().BeEmpty();
        }
        finally
        {
            if (operations.DatabaseNames.Count != 0)
            {
                await lease.DisposeAsync();
            }
        }
    }

    [Test]
    public async Task CreateAllOrDisposeSuccessfulAsync_WhenSiblingCreationAndCleanupFail_ExposesBothErrors()
    {
        var creationException = new InvalidOperationException("Forced sibling creation failure.");
        var cleanupException = new InvalidOperationException("Forced sibling cleanup failure.");
        var successfulFactory = new FailingDisposalTestFactory(cleanupException);
        var successfulCreation = Task.FromResult(successfulFactory);

        async Task<FailingDisposalTestFactory> FailAfterSuccessfulSiblingAsync()
        {
            await successfulCreation;
            throw creationException;
        }

        var action = async () => await ConcurrentFactoryCreation.CreateAllOrDisposeSuccessfulAsync(
            () => successfulCreation,
            FailAfterSuccessfulSiblingAsync);

        var exception = await action.Should().ThrowExactlyAsync<AggregateException>();

        exception.Which.InnerExceptions.Should().Equal(creationException, cleanupException);
        successfulFactory.DisposalAttempts.Should().Be(1);
    }

    private static async Task<(string First, string Second)> CreateAndDisposeTwoFactoriesAsync()
    {
        var factories = await ConcurrentFactoryCreation.CreateAllOrDisposeSuccessfulAsync(
            static () => PostgreSqlWebApplicationFactory.CreateAsync(),
            static () => PostgreSqlWebApplicationFactory.CreateAsync());

        await using var first = factories[0];
        await using var second = factories[1];

        first.DatabaseName.Should().NotBe(second.DatabaseName);
        return (first.DatabaseName, second.DatabaseName);
    }

    private sealed class LeaseBackedTestFactory(PostgreSqlDatabaseLease lease) : IAsyncDisposable
    {
        public int DisposalAttempts { get; private set; }

        public async ValueTask DisposeAsync()
        {
            DisposalAttempts++;
            await lease.DisposeAsync();
        }
    }

    private sealed class FailingDisposalTestFactory(Exception cleanupException) : IAsyncDisposable
    {
        public int DisposalAttempts { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposalAttempts++;
            return ValueTask.FromException(cleanupException);
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private const string VariableName = "LGYM_TEST_POSTGRES";
        private readonly string? _originalValue = Environment.GetEnvironmentVariable(VariableName);

        public EnvironmentVariableScope(string? value)
        {
            Environment.SetEnvironmentVariable(VariableName, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(VariableName, _originalValue);
        }
    }

    private sealed class ControllableDatabaseLeaseOperations : IPostgreSqlDatabaseLeaseOperations
    {
        public HashSet<string> DatabaseNames { get; } = [];

        public bool FailConnectionStringBuild { get; init; }

        public Exception? CreateException { get; init; }

        public bool CreateDatabaseBeforeFailure { get; init; }

        public int RemainingDropFailures { get; set; }

        public NpgsqlException? NpgsqlDropException { get; init; }

        public int RemainingNpgsqlDropFailures { get; set; }

        public int DropAttempts { get; private set; }

        public Task CreateDatabaseAsync(
            string adminConnectionString,
            string databaseName,
            CancellationToken cancellationToken)
        {
            if (CreateException != null)
            {
                if (CreateDatabaseBeforeFailure)
                {
                    DatabaseNames.Add(databaseName);
                }

                throw CreateException;
            }

            DatabaseNames.Add(databaseName);
            return Task.CompletedTask;
        }

        public Task DropDatabaseAsync(
            string adminConnectionString,
            string databaseName,
            CancellationToken cancellationToken)
        {
            DropAttempts++;
            if (RemainingNpgsqlDropFailures > 0)
            {
                RemainingNpgsqlDropFailures--;
                throw NpgsqlDropException!;
            }

            if (RemainingDropFailures > 0)
            {
                RemainingDropFailures--;
                throw new InvalidOperationException("Forced drop failure.");
            }

            DatabaseNames.Remove(databaseName);
            return Task.CompletedTask;
        }

        public string BuildDatabaseConnectionString(string adminConnectionString, string databaseName)
        {
            if (FailConnectionStringBuild)
            {
                throw new InvalidOperationException("Forced lease construction failure.");
            }

            return $"Host=test;Database={databaseName}";
        }
    }
}
