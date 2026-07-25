using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.PasswordReset;
using LgymApi.Application.Features.PasswordReset.Contracts;
using LgymApi.Application.Models;
using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Services;
using LgymApi.TestUtils.Fakes;
using FluentAssertions;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PasswordResetServiceTests
{
    [Test]
    public async Task RequestPasswordReset_WithValidEmail_CreatesTokenAndSchedulesEmail()
    {
        var user = CreateTestUser("test@example.com");
        var userRepository = new FakeUserRepository { ExistingByEmail = user };
        var tokenRepository = new FakePasswordResetTokenRepository();
        var emailScheduler = new FakePasswordRecoveryEmailScheduler();
        var unitOfWork = new FakeUnitOfWork();
        var service = CreateService(userRepository, tokenRepository, emailScheduler, unitOfWork);

        using var cancellationSource = new CancellationTokenSource();
        var result = await service.RequestPasswordResetAsync("test@example.com", "en", cancellationSource.Token);

        result.IsSuccess.Should().BeTrue();
        tokenRepository.Added.Should().HaveCount(1);
        tokenRepository.Added[0].UserId.Should().Be(user.Id);
        tokenRepository.Added[0].IsUsed.Should().BeFalse();
        (tokenRepository.Added[0].ExpiresAt > DateTimeOffset.UtcNow).Should().BeTrue();
        (tokenRepository.Added[0].ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(31)).Should().BeTrue();
        emailScheduler.ScheduledRequests.Should().ContainSingle();
        var scheduledRequest = emailScheduler.ScheduledRequests[0];
        scheduledRequest.UserId.Should().Be(user.Id);
        scheduledRequest.TokenId.Should().Be(tokenRepository.Added[0].Id);
        scheduledRequest.UserName.Should().Be(user.Name);
        scheduledRequest.RecipientEmail.Should().Be("test@example.com");
        scheduledRequest.ResetToken.Should().HaveLength(64);
        scheduledRequest.ResetUrl.Should().BeEmpty();
        scheduledRequest.CultureName.Should().Be("en");
        emailScheduler.CancellationToken.Should().Be(cancellationSource.Token);
        typeof(PasswordResetServiceDependencies).GetConstructors().Should().ContainSingle()
            .Which.GetParameters()[4].ParameterType.Should().Be(typeof(IPasswordRecoveryEmailScheduler));
        unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task RequestPasswordReset_WithValidEmail_LeavesResetUrlEmpty()
    {
        var userRepository = new FakeUserRepository { ExistingByEmail = CreateTestUser("reset-url@example.com") };
        var tokenRepository = new FakePasswordResetTokenRepository();
        var emailScheduler = new FakePasswordRecoveryEmailScheduler();
        var unitOfWork = new FakeUnitOfWork();
        var service = CreateService(userRepository, tokenRepository, emailScheduler, unitOfWork);

        var result = await service.RequestPasswordResetAsync("reset-url@example.com", "en-US", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        emailScheduler.ScheduledRequests.Should().ContainSingle();
        emailScheduler.ScheduledRequests[0].ResetUrl.Should().BeEmpty();
    }

    [Test]
    public async Task RequestPasswordReset_WithNonexistentEmail_ReturnsSuccessWithoutCreatingToken()
    {
        var userRepository = new FakeUserRepository { ExistingByEmail = null };
        var tokenRepository = new FakePasswordResetTokenRepository();
        var emailScheduler = new FakePasswordRecoveryEmailScheduler();
        var unitOfWork = new FakeUnitOfWork();
        var service = CreateService(userRepository, tokenRepository, emailScheduler, unitOfWork);

        var result = await service.RequestPasswordResetAsync("nonexistent@example.com", "en", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tokenRepository.Added.Should().BeEmpty();
        emailScheduler.ScheduledRequests.Should().BeEmpty();
        unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task RequestPasswordReset_WithDeletedUser_ReturnsSuccessWithoutCreatingToken()
    {
        var user = CreateTestUser("deleted@example.com");
        user.IsDeleted = true;
        var userRepository = new FakeUserRepository { ExistingByEmail = user };
        var tokenRepository = new FakePasswordResetTokenRepository();
        var emailScheduler = new FakePasswordRecoveryEmailScheduler();
        var unitOfWork = new FakeUnitOfWork();
        var service = CreateService(userRepository, tokenRepository, emailScheduler, unitOfWork);

        var result = await service.RequestPasswordResetAsync("deleted@example.com", "en", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tokenRepository.Added.Should().BeEmpty();
        emailScheduler.ScheduledRequests.Should().BeEmpty();
        unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task RequestPasswordReset_WithExistingActiveToken_InvalidatesOldTokenAndCreatesNew()
    {
        var user = CreateTestUser("test@example.com");
        var oldToken = new PasswordResetToken
        {
            Id = Id<PasswordResetToken>.New(),
            UserId = user.Id,
            TokenHash = "oldtokenhash",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20),
            IsUsed = false
        };
        var userRepository = new FakeUserRepository { ExistingByEmail = user };
        var tokenRepository = new FakePasswordResetTokenRepository { ExistingActiveForUser = [oldToken] };
        var emailScheduler = new FakePasswordRecoveryEmailScheduler();
        var unitOfWork = new FakeUnitOfWork();
        var service = CreateService(userRepository, tokenRepository, emailScheduler, unitOfWork);

        var result = await service.RequestPasswordResetAsync("test@example.com", "en", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        oldToken.IsUsed.Should().BeTrue();
        tokenRepository.Updated.Should().Contain(oldToken);
        tokenRepository.Added.Should().HaveCount(1);
        unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task RequestPasswordReset_NormalizesEmail_BeforeLookup()
    {
        var user = CreateTestUser("test@example.com");
        var userRepository = new FakeUserRepository { ExistingByEmail = user };
        var tokenRepository = new FakePasswordResetTokenRepository();
        var emailScheduler = new FakePasswordRecoveryEmailScheduler();
        var unitOfWork = new FakeUnitOfWork();
        var service = CreateService(userRepository, tokenRepository, emailScheduler, unitOfWork);

        await service.RequestPasswordResetAsync("  TEST@EXAMPLE.COM  ", "en", CancellationToken.None);

        userRepository.LastEmailLookup.Should().Be("test@example.com");
    }

    [Test]
    public async Task RequestPasswordReset_StoresHashedToken_NotPlaintext()
    {
        var user = CreateTestUser("test@example.com");
        var userRepository = new FakeUserRepository { ExistingByEmail = user };
        var tokenRepository = new FakePasswordResetTokenRepository();
        var emailScheduler = new FakePasswordRecoveryEmailScheduler();
        var unitOfWork = new FakeUnitOfWork();
        var service = CreateService(userRepository, tokenRepository, emailScheduler, unitOfWork);

        await service.RequestPasswordResetAsync("test@example.com", "en", CancellationToken.None);

        var addedToken = tokenRepository.Added[0];
        var scheduledRequest = emailScheduler.ScheduledRequests[0];

        addedToken.TokenHash.Should().NotBe(scheduledRequest.ResetToken);
        addedToken.TokenHash.Should().HaveLength(64); // SHA-256 hex output
        scheduledRequest.ResetToken.Should().HaveLength(64); // 32 bytes as hex
    }

    [Test]
    public async Task ResetPassword_WithValidToken_UpdatesPasswordAndMarksTokenUsed()
    {
        var user = CreateTestUser("test@example.com");
        var plainTextToken = "ABC123DEF456";
        var tokenHash = ComputeSha256Hex(plainTextToken);
        var resetToken = new PasswordResetToken
        {
            Id = Id<PasswordResetToken>.New(),
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20),
            IsUsed = false
        };
        var userRepository = new FakeUserRepository { ExistingById = user };
        var tokenRepository = new FakePasswordResetTokenRepository { ExistingByTokenHash = resetToken };
        var unitOfWork = new FakeUnitOfWork();
        var sessionStore = new FakeUserSessionStore();
        var service = CreateService(userRepository, tokenRepository, unitOfWork: unitOfWork, sessionStore: sessionStore);

        var result = await service.ResetPasswordAsync(plainTextToken, "newPassword123", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.LegacyHash.Should().NotBeEmpty();
        user.LegacySalt.Should().NotBeEmpty();
        resetToken.IsUsed.Should().BeTrue();
        tokenRepository.Updated.Should().Contain(resetToken);
        userRepository.Updated.Should().Contain(user);
        unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task ResetPassword_WithInvalidToken_ReturnsFailure()
    {
        var tokenRepository = new FakePasswordResetTokenRepository { ExistingByTokenHash = null };
        var userRepository = new FakeUserRepository();
        var unitOfWork = new FakeUnitOfWork();
        var service = CreateService(userRepository, tokenRepository, unitOfWork: unitOfWork);

        var result = await service.ResetPasswordAsync("invalidtoken", "newPassword123", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task ResetPassword_WithExpiredToken_ReturnsFailure()
    {
        var user = CreateTestUser("test@example.com");
        var plainTextToken = "ABC123DEF456";
        var tokenHash = ComputeSha256Hex(plainTextToken);
        var expiredToken = new PasswordResetToken
        {
            Id = Id<PasswordResetToken>.New(),
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5), // Expired 5 minutes ago
            IsUsed = false
        };
        var tokenRepository = new FakePasswordResetTokenRepository { ExistingByTokenHash = null }; // FindActiveByTokenHashAsync filters expired tokens
        var userRepository = new FakeUserRepository { ExistingById = user };
        var unitOfWork = new FakeUnitOfWork();
        var service = CreateService(userRepository, tokenRepository, unitOfWork: unitOfWork);

        var result = await service.ResetPasswordAsync(plainTextToken, "newPassword123", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task ResetPassword_WithUsedToken_ReturnsFailure()
    {
        var user = CreateTestUser("test@example.com");
        var plainTextToken = "ABC123DEF456";
        var tokenHash = ComputeSha256Hex(plainTextToken);
        var usedToken = new PasswordResetToken
        {
            Id = Id<PasswordResetToken>.New(),
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20),
            IsUsed = true
        };
        var tokenRepository = new FakePasswordResetTokenRepository { ExistingByTokenHash = null }; // FindActiveByTokenHashAsync filters used tokens
        var userRepository = new FakeUserRepository { ExistingById = user };
        var unitOfWork = new FakeUnitOfWork();
        var service = CreateService(userRepository, tokenRepository, unitOfWork: unitOfWork);

        var result = await service.ResetPasswordAsync(plainTextToken, "newPassword123", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task ResetPassword_WithDeletedUser_ReturnsFailure()
    {
        var user = CreateTestUser("test@example.com");
        user.IsDeleted = true;
        var plainTextToken = "ABC123DEF456";
        var tokenHash = ComputeSha256Hex(plainTextToken);
        var resetToken = new PasswordResetToken
        {
            Id = Id<PasswordResetToken>.New(),
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20),
            IsUsed = false
        };
        var userRepository = new FakeUserRepository { ExistingById = user };
        var tokenRepository = new FakePasswordResetTokenRepository { ExistingByTokenHash = resetToken };
        var unitOfWork = new FakeUnitOfWork();
        var service = CreateService(userRepository, tokenRepository, unitOfWork: unitOfWork);

        var result = await service.ResetPasswordAsync(plainTextToken, "newPassword123", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task ResetPassword_OnSuccess_RevokesAllUserSessions()
    {
        var user = CreateTestUser("test@example.com");
        var plainTextToken = "ABC123DEF456";
        var tokenHash = ComputeSha256Hex(plainTextToken);
        var resetToken = new PasswordResetToken
        {
            Id = Id<PasswordResetToken>.New(),
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20),
            IsUsed = false
        };
        var userRepository = new FakeUserRepository { ExistingById = user };
        var tokenRepository = new FakePasswordResetTokenRepository { ExistingByTokenHash = resetToken };
        var unitOfWork = new FakeUnitOfWork();
        var sessionStore = new FakeUserSessionStore();
        var service = CreateService(userRepository, tokenRepository, unitOfWork: unitOfWork, sessionStore: sessionStore);

        await service.ResetPasswordAsync(plainTextToken, "newPassword123", CancellationToken.None);

        sessionStore.RevokedAllUserIds.Should().Contain(user.Id);
    }

    private static User CreateTestUser(string email)
    {
        return new User
        {
            Id = Id<User>.New(),
            Name = "Test User",
            Email = new Email(email),
            ProfileRank = "Rookie",
            IsDeleted = false,
            LegacyHash = string.Empty,
            LegacySalt = string.Empty
        };
    }

    private static string ComputeSha256Hex(string value)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    }

    private static PasswordResetService CreateService(
        FakeUserRepository? userRepository = null,
        FakePasswordResetTokenRepository? tokenRepository = null,
        FakePasswordRecoveryEmailScheduler? emailScheduler = null,
        FakeUnitOfWork? unitOfWork = null,
        FakeUserSessionStore? sessionStore = null)
    {
        var actualTokenRepository = tokenRepository ?? new FakePasswordResetTokenRepository();

        var dependencies = new PasswordResetServiceDependencies(
            userRepository ?? new FakeUserRepository(),
            actualTokenRepository,
            new PasswordResetTokenGenerationService(actualTokenRepository),
            new LegacyPasswordService(),
            emailScheduler ?? new FakePasswordRecoveryEmailScheduler(),
            sessionStore ?? new FakeUserSessionStore(),
            unitOfWork ?? new FakeUnitOfWork());

        return new PasswordResetService(dependencies);
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public User? ExistingByEmail { get; set; }
        public User? ExistingById { get; set; }
        public string? LastEmailLookup { get; set; }
        public List<User> Updated { get; } = new();

        public Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken = default)
        {
            LastEmailLookup = email.Value;
            return Task.FromResult(ExistingByEmail);
        }

        public Task<User?> FindByIdAsync(Id<User> id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingById);
        }

        public Task UpdateAsync(User user, CancellationToken cancellationToken = default)
        {
            Updated.Add(user);
            return Task.CompletedTask;
        }

        public Task AddAsync(User user, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<User?> FindByIdIncludingDeletedAsync(Id<User> id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<User?> FindByIdWithRolesAsync(Id<User> id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<User?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<User?> FindByNameOrEmailAsync(string name, string email, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<UserRankingEntry>> GetRankingAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Pagination<UserResult>> GetUsersPaginatedAsync(FilterInput filterInput, bool includeDeleted, CancellationToken cancellationToken = default)
            => Task.FromResult(new Pagination<UserResult>());
    }

    private sealed class FakePasswordResetTokenRepository : IPasswordResetTokenRepository
    {
        public List<PasswordResetToken> Added { get; } = new();
        public List<PasswordResetToken> Updated { get; } = new();
        public PasswordResetToken? ExistingByTokenHash { get; set; }
        public List<PasswordResetToken> ExistingActiveForUser { get; set; } = new();

        public Task AddAsync(PasswordResetToken token, CancellationToken cancellationToken = default)
        {
            Added.Add(token);
            return Task.CompletedTask;
        }

        public Task<PasswordResetToken?> FindActiveByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingByTokenHash);
        }

        public Task<List<PasswordResetToken>> GetActiveForUserAsync(Id<User> userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingActiveForUser);
        }

        public Task UpdateAsync(PasswordResetToken token, CancellationToken cancellationToken = default)
        {
            Updated.Add(token);
            return Task.CompletedTask;
        }

        public Task<bool> TokenHashExistsAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class FakePasswordRecoveryEmailScheduler : IPasswordRecoveryEmailScheduler
    {
        public List<PasswordRecoveryEmailRequest> ScheduledRequests { get; } = new();
        public CancellationToken CancellationToken { get; private set; }

        public Task ScheduleAsync(PasswordRecoveryEmailRequest request, CancellationToken cancellationToken = default)
        {
            ScheduledRequests.Add(request);
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls += 1;
            return Task.FromResult(1);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void DetachEntity<TEntity>(TEntity entity) where TEntity : class
            => throw new NotSupportedException();
    }
}
