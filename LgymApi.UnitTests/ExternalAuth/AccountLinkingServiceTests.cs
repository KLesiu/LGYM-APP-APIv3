using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.ExternalAuth;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using LgymApi.TestUtils;
using LgymApi.TestUtils.Fakes;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class AccountLinkingServiceTests
{
    private IGoogleTokenValidator _googleTokenValidator = null!;
    private IUserRepository _userRepository = null!;
    private IUserExternalLoginRepository _userExternalLoginRepository = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private AccountLinkingService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _googleTokenValidator = Substitute.For<IGoogleTokenValidator>();
        _userRepository = Substitute.For<IUserRepository>();
        _userExternalLoginRepository = Substitute.For<IUserExternalLoginRepository>();
        _unitOfWork = new FakeUnitOfWork();

        _service = new AccountLinkingService(
            _googleTokenValidator,
            _userRepository,
            _userExternalLoginRepository,
            _unitOfWork);
    }

    [Test]
    public async Task LinkGoogle_InvalidToken_ReturnsInvalidUserError()
    {
        _googleTokenValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GoogleTokenPayload?>(null));

        var result = await _service.LinkGoogleAsync(Id<User>.New(), "token", null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidUserError>();
        result.Error!.Message.Should().Be(Messages.GoogleTokenInvalid);
        _unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task LinkGoogle_UnverifiedEmail_ReturnsInvalidUserError()
    {
        _googleTokenValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GoogleTokenPayload("sub123", "test@example.com", false, "Test User", null));

        var result = await _service.LinkGoogleAsync(Id<User>.New(), "token", null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidUserError>();
        result.Error!.Message.Should().Be(Messages.GoogleEmailNotVerified);
        _unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task LinkGoogle_AlreadyLinkedForCurrentUser_ReturnsConflict()
    {
        var user = CreateUser();
        var existingLogin = new UserExternalLogin
        {
            Id = Id<UserExternalLogin>.New(),
            UserId = user.Id,
            Provider = AuthConstants.ExternalProviders.Google,
            ProviderKey = "sub-existing",
            ProviderEmail = "test@example.com"
        };

        _googleTokenValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GoogleTokenPayload("sub123", "test@example.com", true, "Test User", null));
        _userRepository.FindByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _userExternalLoginRepository.FindByUserAndProviderAsync(user.Id, AuthConstants.ExternalProviders.Google, Arg.Any<CancellationToken>())
            .Returns(existingLogin);

        var result = await _service.LinkGoogleAsync(user.Id, "token", null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ConflictError>();
        result.Error!.Message.Should().Be(Messages.GoogleAccountAlreadyLinked);
        await _userExternalLoginRepository.DidNotReceiveWithAnyArgs().FindByProviderAsync(default!, default!, default);
        await _userExternalLoginRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        _unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task LinkGoogle_AlreadyLinkedToAnotherUser_ReturnsConflict()
    {
        var user = CreateUser();
        var anotherUser = CreateUser();
        var existingLogin = new UserExternalLogin
        {
            Id = Id<UserExternalLogin>.New(),
            UserId = anotherUser.Id,
            Provider = AuthConstants.ExternalProviders.Google,
            ProviderKey = "sub123",
            ProviderEmail = "linked@example.com",
            User = anotherUser
        };

        _googleTokenValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GoogleTokenPayload("sub123", "test@example.com", true, "Test User", null));
        _userRepository.FindByIdAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
            .Returns(user);
        _userExternalLoginRepository.FindByUserAndProviderAsync(user.Id, AuthConstants.ExternalProviders.Google, Arg.Any<CancellationToken>())
            .Returns((UserExternalLogin?)null);
        _userExternalLoginRepository.FindByProviderAsync(AuthConstants.ExternalProviders.Google, "sub123", Arg.Any<CancellationToken>())
            .Returns(existingLogin);

        var result = await _service.LinkGoogleAsync(user.Id, "token", null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ConflictError>();
        await _userExternalLoginRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        _unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task LinkGoogle_Success_AddsExternalLoginAndSaves()
    {
        var user = CreateUser();
        GoogleTokenPayload? payload = new GoogleTokenPayload("sub123", "test@example.com", true, "Test User", null);

        _googleTokenValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(payload);
        _userRepository.FindByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _userExternalLoginRepository.FindByUserAndProviderAsync(user.Id, AuthConstants.ExternalProviders.Google, Arg.Any<CancellationToken>())
            .Returns((UserExternalLogin?)null);
        _userExternalLoginRepository.FindByProviderAsync(AuthConstants.ExternalProviders.Google, payload.Subject, Arg.Any<CancellationToken>())
            .Returns((UserExternalLogin?)null);

        var result = await _service.LinkGoogleAsync(user.Id, "token", null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _userExternalLoginRepository.Received(1).AddAsync(
            Arg.Is<UserExternalLogin>(x =>
                x.UserId == user.Id &&
                x.Provider == AuthConstants.ExternalProviders.Google &&
                x.ProviderKey == payload.Subject &&
                x.ProviderEmail == payload.Email),
            Arg.Any<CancellationToken>());
        _unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task UnlinkGoogle_UserMissing_ReturnsUserNotFoundAndDoesNotQueryGoogleLogin()
    {
        _userRepository.FindByIdAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await _service.UnlinkGoogleAsync(Id<User>.New(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UserNotFoundError>();
        result.Error!.Message.Should().Be(Messages.DidntFind);
        await _userExternalLoginRepository.DidNotReceiveWithAnyArgs().FindActiveGoogleByUserIdAsync(default!, default);
        await _userExternalLoginRepository.DidNotReceiveWithAnyArgs().MarkGoogleDeletedAsync(default!, default);
        await _googleTokenValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default, default);
        _unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task UnlinkGoogle_NoActiveLink_ReturnsNotFoundAndDoesNotSave()
    {
        var user = CreateUser();

        _userRepository.FindByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _userExternalLoginRepository.FindActiveGoogleByUserIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns((UserExternalLogin?)null);

        var result = await _service.UnlinkGoogleAsync(user.Id, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>();
        result.Error!.Message.Should().Be(Messages.DidntFind);
        await _userRepository.Received(1).FindByIdAsync(user.Id, Arg.Any<CancellationToken>());
        await _userExternalLoginRepository.Received(1).FindActiveGoogleByUserIdAsync(user.Id, Arg.Any<CancellationToken>());
        await _userExternalLoginRepository.DidNotReceiveWithAnyArgs().MarkGoogleDeletedAsync(default!, default);
        await _googleTokenValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default, default);
        _unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    [Test]
    public async Task UnlinkGoogle_Success_MarksGoogleDeletedAndSaves()
    {
        var user = CreateUser();
        var googleLogin = new UserExternalLogin
        {
            Id = Id<UserExternalLogin>.New(),
            UserId = user.Id,
            Provider = AuthConstants.ExternalProviders.Google,
            ProviderKey = "sub123",
            ProviderEmail = "google@example.com"
        };

        _userRepository.FindByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _userExternalLoginRepository.FindActiveGoogleByUserIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(googleLogin);

        var result = await _service.UnlinkGoogleAsync(user.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _userRepository.Received(1).FindByIdAsync(user.Id, Arg.Any<CancellationToken>());
        await _userExternalLoginRepository.Received(1).FindActiveGoogleByUserIdAsync(user.Id, Arg.Any<CancellationToken>());
        await _userExternalLoginRepository.Received(1).MarkGoogleDeletedAsync(user.Id, Arg.Any<CancellationToken>());
        await _googleTokenValidator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default, default);
        _unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task GetExternalLogins_ReturnsAllForUser()
    {
        var user = CreateUser();
        var logins = new List<UserExternalLogin>
        {
            new()
            {
                Id = Id<UserExternalLogin>.New(),
                UserId = user.Id,
                Provider = "facebook",
                ProviderEmail = "fb@example.com",
                ProviderKey = "fb-1"
            },
            new()
            {
                Id = Id<UserExternalLogin>.New(),
                UserId = user.Id,
                Provider = AuthConstants.ExternalProviders.Google,
                ProviderEmail = "google@example.com",
                ProviderKey = "google-1"
            }
        };

        _userRepository.FindByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _userExternalLoginRepository.GetByUserIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(logins);

        var result = await _service.GetExternalLoginsAsync(user.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].Should().BeEquivalentTo(new ExternalLoginInfo(AuthConstants.ExternalProviders.Google, "google@example.com"));
        result.Value[1].Should().BeEquivalentTo(new ExternalLoginInfo("facebook", "fb@example.com"));
    }

    private static User CreateUser()
    {
        return new User
        {
            Id = Id<User>.New(),
            Name = "Test User",
            Email = new Email("test@example.com"),
            ProfileRank = "Rookie",
            PreferredTimeZone = "UTC",
            IsDeleted = false,
            IsBlocked = false,
            LegacyHash = string.Empty,
            LegacySalt = string.Empty
        };
    }
}
