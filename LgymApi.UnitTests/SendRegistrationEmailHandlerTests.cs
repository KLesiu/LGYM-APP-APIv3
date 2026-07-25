using FluentAssertions;
using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Application.Repositories;
using LgymApi.Application.Models;
using LgymApi.Application.Pagination;
using LgymApi.Application.Pagination;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.Application.Identity.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Application.Options;
using LgymApi.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class SendRegistrationEmailHandlerTests
{
    private TestUserRepository _testUserRepository = null!;
    private TestEmailScheduler _testScheduler = null!;
    private TestLogger _testLogger = null!;
    private SendRegistrationEmailHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _testUserRepository = new TestUserRepository();
        _testScheduler = new TestEmailScheduler();
        _testLogger = new TestLogger();
        _handler = new SendRegistrationEmailHandler(_testUserRepository, _testScheduler, _testLogger, new AppDefaultsOptions());
    }

    [Test]
    public async Task ExecuteAsync_WithValidCommand_SchedulesWelcomeEmail()
    {
        // Arrange
        var userId = Id<User>.New();
        _testUserRepository.UserToReturn = new User
        {
            Id = userId,
            Name = "JohnDoe",
            Email = "john.doe@example.com",
            PreferredLanguage = "en-US"
        };

        var command = new UserRegisteredCommand
        {
            UserId = userId
        };

        // Act
        await _handler.ExecuteAsync(command);

        // Assert
        _testScheduler.ScheduledPayloads.Should().HaveCount(1);
        var payload = _testScheduler.ScheduledPayloads[0];
        payload.UserId.Should().Be(userId);
        payload.UserName.Should().Be("JohnDoe");
        payload.RecipientEmail.Should().Be("john.doe@example.com");
        payload.CultureName.Should().Be("en-US");
    }

    [Test]
    public async Task ExecuteAsync_WithEmptyEmail_SkipsSchedulingGracefully()
    {
        // Arrange
        var userId = Id<User>.New();
        _testUserRepository.UserToReturn = new User
        {
            Id = userId,
            Name = "TestUser",
            Email = string.Empty,
            PreferredLanguage = "en-US"
        };

        var command = new UserRegisteredCommand
        {
            UserId = userId
        };

        // Act
        await _handler.ExecuteAsync(command);

        // Assert
        _testScheduler.ScheduledPayloads.Should().BeEmpty();
        _testLogger.WarningMessages.Should().HaveCount(1);
        _testLogger.WarningMessages[0].Should().Contain("no recipient email");
    }

    [Test]
    public async Task ExecuteAsync_WithWhitespaceEmail_SkipsSchedulingGracefully()
    {
        // Arrange
        var userId = Id<User>.New();
        _testUserRepository.UserToReturn = new User
        {
            Id = userId,
            Name = "TestUser",
            Email = "   ",
            PreferredLanguage = "en-US"
        };

        var command = new UserRegisteredCommand
        {
            UserId = userId
        };

        // Act
        await _handler.ExecuteAsync(command);

        // Assert
        _testScheduler.ScheduledPayloads.Should().BeEmpty();
        _testLogger.WarningMessages.Should().HaveCount(1);
    }

    [Test]
    public async Task ExecuteAsync_MapsAllUserFieldsToPayload()
    {
        // Arrange
        var userId = Id<User>.New();
        _testUserRepository.UserToReturn = new User
        {
            Id = userId,
            Name = "MariaGarcia",
            Email = "maria.garcia@example.com",
            PreferredLanguage = "es-ES"
        };

        var command = new UserRegisteredCommand
        {
            UserId = userId
        };

        // Act
        await _handler.ExecuteAsync(command);

        // Assert
        var payload = _testScheduler.ScheduledPayloads[0];
        payload.UserId.Should().Be(userId);
        payload.UserName.Should().Be("MariaGarcia");
        payload.RecipientEmail.Should().Be("maria.garcia@example.com");
        payload.CultureName.Should().Be("es-ES");
    }

    [Test]
    public async Task ExecuteAsync_WithCancellationToken_PassesTokenToScheduler()
    {
        // Arrange
        var userId = Id<User>.New();
        _testUserRepository.UserToReturn = new User
        {
            Id = userId,
            Name = "TestUser",
            Email = "test@example.com",
            PreferredLanguage = "en-US"
        };

        var command = new UserRegisteredCommand
        {
            UserId = userId
        };

        using var cts = new CancellationTokenSource();

        // Act
        await _handler.ExecuteAsync(command, cts.Token);

        // Assert
        _testScheduler.ReceivedToken.Should().Be(cts.Token);
    }

    [Test]
    public async Task ExecuteAsync_LogsInformationOnSuccess()
    {
        // Arrange
        var userId = Id<User>.New();
        _testUserRepository.UserToReturn = new User
        {
            Id = userId,
            Name = "TestUser",
            Email = "test@example.com",
            PreferredLanguage = "en-US"
        };

        var command = new UserRegisteredCommand
        {
            UserId = userId
        };

        // Act
        await _handler.ExecuteAsync(command);

        // Assert
        _testLogger.InformationMessages.Should().HaveCount(1);
        _testLogger.InformationMessages[0].Should().Contain("Welcome email scheduled");
    }

    [Test]
    public void Constructor_WithNullUserRepository_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => new SendRegistrationEmailHandler(null!, _testScheduler, _testLogger, new AppDefaultsOptions());
        var ex = action.Should().Throw<ArgumentNullException>().Which;
        ex.ParamName.Should().Be("userRepository");
    }

    [Test]
    public void Constructor_WithNullEmailScheduler_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => new SendRegistrationEmailHandler(_testUserRepository, null!, _testLogger, new AppDefaultsOptions());
        var ex = action.Should().Throw<ArgumentNullException>().Which;
        ex.ParamName.Should().Be("emailScheduler");
    }

    [Test]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => new SendRegistrationEmailHandler(_testUserRepository, _testScheduler, null!, new AppDefaultsOptions());
        var ex = action.Should().Throw<ArgumentNullException>().Which;
        ex.ParamName.Should().Be("logger");
    }

    [Test]
    public async Task ExecuteAsync_WithDifferentCulture_PreservesCultureName()
    {
        // Arrange
        var userId = Id<User>.New();
        _testUserRepository.UserToReturn = new User
        {
            Id = userId,
            Name = "PierreDupont",
            Email = "pierre@example.fr",
            PreferredLanguage = "fr-FR"
        };

        var command = new UserRegisteredCommand
        {
            UserId = userId
        };

        // Act
        await _handler.ExecuteAsync(command);

        // Assert
        var payload = _testScheduler.ScheduledPayloads[0];
        payload.CultureName.Should().Be("fr-FR");
    }

    [Test]
    public async Task ExecuteAsync_WithUserNotFound_SkipsSchedulingGracefully()
    {
        // Arrange
        _testUserRepository.UserToReturn = null;

        var command = new UserRegisteredCommand
        {
            UserId = Id<User>.New()
        };

        // Act
        await _handler.ExecuteAsync(command);

        // Assert
        _testScheduler.ScheduledPayloads.Should().BeEmpty();
        _testLogger.WarningMessages.Should().HaveCount(1);
        _testLogger.WarningMessages[0].Should().Contain("user not found");
    }

    [Test]
    public async Task ExecuteAsync_WithNoPreferredLanguage_DefaultsToEnUs()
    {
        // Arrange
        var userId = Id<User>.New();
        _testUserRepository.UserToReturn = new User
        {
            Id = userId,
            Name = "TestUser",
            Email = "test@example.com",
            PreferredLanguage = null
        };

        var command = new UserRegisteredCommand
        {
            UserId = userId
        };

        // Act
        await _handler.ExecuteAsync(command);

        // Assert
        var payload = _testScheduler.ScheduledPayloads[0];
        payload.CultureName.Should().Be("en-US");
    }

    [Test]
    public async Task ExecuteAsync_WithWhitespacePreferredLanguage_UsesConfiguredDefault()
    {
        var userId = Id<User>.New();
        _testUserRepository.UserToReturn = new User
        {
            Id = userId,
            Name = "TestUser",
            Email = "test@example.com",
            PreferredLanguage = "   "
        };

        var handler = new SendRegistrationEmailHandler(
            _testUserRepository,
            _testScheduler,
            _testLogger,
            new AppDefaultsOptions { PreferredLanguage = "pl-PL", PreferredTimeZone = "Europe/Warsaw" });

        var command = new UserRegisteredCommand { UserId = userId };

        await handler.ExecuteAsync(command);

        var payload = _testScheduler.ScheduledPayloads[0];
        payload.CultureName.Should().Be("pl-PL");
    }

    // Test doubles
    private sealed class TestUserRepository : IUserRepository
    {
        public User? UserToReturn { get; set; }

        public Task<User?> FindByIdAsync(Id<LgymApi.Domain.Entities.User> id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(UserToReturn);
        }

        public Task<User?> FindByIdIncludingDeletedAsync(Id<LgymApi.Domain.Entities.User> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByIdWithRolesAsync(Id<LgymApi.Domain.Entities.User> id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByNameAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> FindByNameOrEmailAsync(string name, string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<UserRankingEntry>> GetRankingAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Pagination<UserResult>> GetUsersPaginatedAsync(FilterInput filterInput, bool includeDeleted, CancellationToken cancellationToken = default) => Task.FromResult(new Pagination<UserResult>());
    }

    private sealed class TestEmailScheduler : IEmailScheduler<WelcomeEmailPayload>
    {
        public List<WelcomeEmailPayload> ScheduledPayloads { get; } = new();
        public CancellationToken ReceivedToken { get; private set; }

        public Task ScheduleAsync(WelcomeEmailPayload payload, CancellationToken cancellationToken = default)
        {
            ScheduledPayloads.Add(payload);
            ReceivedToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class TestLogger : ILogger<SendRegistrationEmailHandler>
    {
        public List<string> WarningMessages { get; } = new();
        public List<string> InformationMessages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (logLevel == LogLevel.Warning)
                WarningMessages.Add(message);
            else if (logLevel == LogLevel.Information)
                InformationMessages.Add(message);
        }
    }
}
