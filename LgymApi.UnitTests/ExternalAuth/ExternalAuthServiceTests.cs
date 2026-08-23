using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.ExternalAuth;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.TestUtils.Fakes;
using LgymApi.UnitTests.Fakes;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ExternalAuthServiceTests
{
    private ConfigurableGoogleTokenValidator _googleTokenValidator = null!;
    private ConfigurableUserExternalLoginRepository _userExternalLoginRepository = null!;
    private ConfigurableGoogleUserRegistrar _googleUserRegistrar = null!;
    private ConfigurableLoginResultBuilder _loginResultBuilder = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private ExternalAuthService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _googleTokenValidator = new ConfigurableGoogleTokenValidator();
        _userExternalLoginRepository = new ConfigurableUserExternalLoginRepository();
        _googleUserRegistrar = new ConfigurableGoogleUserRegistrar();
        _loginResultBuilder = new ConfigurableLoginResultBuilder();
        _unitOfWork = new FakeUnitOfWork();

        _service = new ExternalAuthService(
            _googleTokenValidator,
            _userExternalLoginRepository,
            _googleUserRegistrar,
            _loginResultBuilder,
            _unitOfWork);
    }

    [Test]
    public async Task GoogleSignIn_InvalidToken_ReturnsUnauthorized()
    {
        _googleTokenValidator.Validate = (_, _, _) => Task.FromResult<GoogleTokenPayload?>(null);

        var result = await _service.GoogleSignInAsync("invalid-token", null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UserUnauthorizedError>();
        _unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task GoogleSignIn_UnverifiedEmail_ReturnsUnauthorized()
    {
        _googleTokenValidator.Validate = (_, _, _) => Task.FromResult<GoogleTokenPayload?>(new GoogleTokenPayload("sub123", "test@example.com", false, "Test User", null));

        var result = await _service.GoogleSignInAsync("token", null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UserUnauthorizedError>();
        _userExternalLoginRepository.Calls.Should().NotContain(call => call.Method == nameof(IUserExternalLoginRepository.FindByProviderAsync));
        _unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task GoogleSignIn_ExistingLink_ReturnsLoginResult()
    {
        var existingUser = CreateUser(preferredTimeZone: "Europe/Warsaw");
        var externalLogin = new UserExternalLogin
        {
            Id = Id<UserExternalLogin>.New(),
            UserId = existingUser.Id,
            Provider = AuthConstants.ExternalProviders.Google,
            ProviderKey = "sub123",
            ProviderEmail = "test@example.com",
            User = existingUser
        };

        _googleTokenValidator.Validate = (_, _, _) => Task.FromResult<GoogleTokenPayload?>(new GoogleTokenPayload("sub123", "test@example.com", true, "Test User", null));
        _userExternalLoginRepository.FindByProvider = (_, _, _) => Task.FromResult<UserExternalLogin?>(externalLogin);
        _loginResultBuilder.Build = (_, _, _) => Task.FromResult(Result.Success<LoginResult, AppError>(CreateLoginResult()));

        var result = await _service.GoogleSignInAsync("token", null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Token.Should().Be("jwt-token");
        _unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task GoogleSignIn_ExistingLink_BlockedUser_ReturnsError()
    {
        var blockedUser = CreateUser();
        blockedUser.IsBlocked = true;
        var externalLogin = new UserExternalLogin
        {
            Id = Id<UserExternalLogin>.New(),
            UserId = blockedUser.Id,
            Provider = AuthConstants.ExternalProviders.Google,
            ProviderKey = "sub123",
            ProviderEmail = "test@example.com",
            User = blockedUser
        };

        _googleTokenValidator.Validate = (_, _, _) => Task.FromResult<GoogleTokenPayload?>(new GoogleTokenPayload("sub123", "test@example.com", true, "Test User", null));
        _userExternalLoginRepository.FindByProvider = (_, _, _) => Task.FromResult<UserExternalLogin?>(externalLogin);

        var result = await _service.GoogleSignInAsync("token", null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ForbiddenError>();
        _loginResultBuilder.Calls.Should().BeEmpty();
        _unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task GoogleSignIn_NewUser_NoConflict_CreatesAndReturnsLoginResult()
    {
        var createdUser = CreateUser(preferredTimeZone: "UTC");

        _googleTokenValidator.Validate = (_, _, _) => Task.FromResult<GoogleTokenPayload?>(new GoogleTokenPayload("sub123", "test@example.com", true, "Test User", null));
        _userExternalLoginRepository.FindByProvider = (_, _, _) => Task.FromResult<UserExternalLogin?>(null);
        _googleUserRegistrar.Register = (_, _) => Task.FromResult(Result.Success<User, AppError>(createdUser));
        _loginResultBuilder.Build = (_, _, _) => Task.FromResult(Result.Success<LoginResult, AppError>(CreateLoginResult()));

        var result = await _service.GoogleSignInAsync("token", null, true, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Token.Should().Be("jwt-token");
        _unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task GoogleSignIn_NewUserWithoutAdultConfirmation_DoesNotCreateAccountOrBinding()
    {
        _googleTokenValidator.Validate = (_, _, _) => Task.FromResult<GoogleTokenPayload?>(
            new GoogleTokenPayload("sub123", "test@example.com", true, "Test User", null));
        _userExternalLoginRepository.FindByProvider = (_, _, _) => Task.FromResult<UserExternalLogin?>(null);

        var result = await _service.GoogleSignInAsync("token", null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<AdultConfirmationRequiredForRegistrationError>();
        _googleUserRegistrar.Calls.Should().BeEmpty();
        _loginResultBuilder.Calls.Should().BeEmpty();
        _unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task GoogleSignIn_EmailCollision_NoLink_ReturnsConflict()
    {
        _googleTokenValidator.Validate = (_, _, _) => Task.FromResult<GoogleTokenPayload?>(new GoogleTokenPayload("sub123", "test@example.com", true, "Test User", null));
        _userExternalLoginRepository.FindByProvider = (_, _, _) => Task.FromResult<UserExternalLogin?>(null);
        _googleUserRegistrar.Register = (_, _) => Task.FromResult(Result.Failure<User, AppError>(new ConflictError("email conflict")));

        var result = await _service.GoogleSignInAsync("token", null, true, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ConflictError>();
        _loginResultBuilder.Calls.Should().BeEmpty();
        _unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    private static User CreateUser(string preferredTimeZone = "UTC")
    {
        return new User
        {
            Id = Id<User>.New(),
            Name = "Test User",
            Email = new Email("test@example.com"),
            ProfileRank = "Rookie",
            PreferredTimeZone = preferredTimeZone,
            IsDeleted = false,
            IsBlocked = false,
            LegacyHash = string.Empty,
            LegacySalt = string.Empty
        };
    }

    private static LoginResult CreateLoginResult()
    {
        return new LoginResult
        {
            Token = "jwt-token",
            User = new UserInfoResult
            {
                Id = Id<User>.New(),
                Name = "Test User",
                Email = "test@example.com",
                ProfileRank = "Rookie",
                PreferredTimeZone = "UTC",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Elo = 1000,
                IsDeleted = false,
                IsVisibleInRanking = true,
                Roles = new List<string>(),
                PermissionClaims = new List<string>(),
                HasActiveTutorials = false
            },
            PermissionClaims = new List<string>()
        };
    }
}
