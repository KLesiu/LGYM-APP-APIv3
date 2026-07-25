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
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ExternalAuthServiceTests
{
    private IGoogleTokenValidator _googleTokenValidator = null!;
    private IUserExternalLoginRepository _userExternalLoginRepository = null!;
    private IGoogleUserRegistrar _googleUserRegistrar = null!;
    private ILoginResultBuilder _loginResultBuilder = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private ExternalAuthService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _googleTokenValidator = Substitute.For<IGoogleTokenValidator>();
        _userExternalLoginRepository = Substitute.For<IUserExternalLoginRepository>();
        _googleUserRegistrar = Substitute.For<IGoogleUserRegistrar>();
        _loginResultBuilder = Substitute.For<ILoginResultBuilder>();
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
        _googleTokenValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GoogleTokenPayload?>(null));

        var result = await _service.GoogleSignInAsync("invalid-token", null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UserUnauthorizedError>();
        _unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task GoogleSignIn_UnverifiedEmail_ReturnsUnauthorized()
    {
        _googleTokenValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GoogleTokenPayload("sub123", "test@example.com", false, "Test User", null));

        var result = await _service.GoogleSignInAsync("token", null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UserUnauthorizedError>();
        await _userExternalLoginRepository.DidNotReceiveWithAnyArgs().FindByProviderAsync(default!, default!, default);
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

        _googleTokenValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GoogleTokenPayload("sub123", "test@example.com", true, "Test User", null));
        _userExternalLoginRepository.FindByProviderAsync(AuthConstants.ExternalProviders.Google, "sub123", Arg.Any<CancellationToken>())
            .Returns(externalLogin);
        _loginResultBuilder.BuildAsync(existingUser, existingUser.PreferredTimeZone, Arg.Any<CancellationToken>())
            .Returns(Result.Success<LoginResult, AppError>(CreateLoginResult()));

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

        _googleTokenValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GoogleTokenPayload("sub123", "test@example.com", true, "Test User", null));
        _userExternalLoginRepository.FindByProviderAsync(AuthConstants.ExternalProviders.Google, "sub123", Arg.Any<CancellationToken>())
            .Returns(externalLogin);

        var result = await _service.GoogleSignInAsync("token", null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ForbiddenError>();
        await _loginResultBuilder.DidNotReceiveWithAnyArgs().BuildAsync(default!, default!, default);
        _unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task GoogleSignIn_NewUser_NoConflict_CreatesAndReturnsLoginResult()
    {
        var createdUser = CreateUser(preferredTimeZone: "UTC");

        _googleTokenValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GoogleTokenPayload("sub123", "test@example.com", true, "Test User", null));
        _userExternalLoginRepository.FindByProviderAsync(AuthConstants.ExternalProviders.Google, "sub123", Arg.Any<CancellationToken>())
            .Returns((UserExternalLogin?)null);
        _googleUserRegistrar.RegisterAsync(Arg.Any<GoogleTokenPayload>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<User, AppError>(createdUser));
        _loginResultBuilder.BuildAsync(createdUser, createdUser.PreferredTimeZone, Arg.Any<CancellationToken>())
            .Returns(Result.Success<LoginResult, AppError>(CreateLoginResult()));

        var result = await _service.GoogleSignInAsync("token", null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Token.Should().Be("jwt-token");
        _unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task GoogleSignIn_EmailCollision_NoLink_ReturnsConflict()
    {
        _googleTokenValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GoogleTokenPayload("sub123", "test@example.com", true, "Test User", null));
        _userExternalLoginRepository.FindByProviderAsync(AuthConstants.ExternalProviders.Google, "sub123", Arg.Any<CancellationToken>())
            .Returns((UserExternalLogin?)null);
        _googleUserRegistrar.RegisterAsync(Arg.Any<GoogleTokenPayload>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<User, AppError>(new ConflictError("email conflict")));

        var result = await _service.GoogleSignInAsync("token", null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ConflictError>();
        await _loginResultBuilder.DidNotReceiveWithAnyArgs().BuildAsync(default!, default!, default);
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
