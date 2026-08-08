using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Identity.Contracts.BackgroundCommands;
using LgymApi.Application.Identity.Contracts.Authentication;
using LgymApi.Application.Identity.Contracts.Registration;
using LgymApi.Application.Identity.Authentication;
using LgymApi.Application.Identity.Registration;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Notifications;
using LgymApi.Application.Options;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.Services;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using LgymApi.UnitTests.Fakes;
using LgymApi.TestUtils.Fakes;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class UserServiceAuthTests
{
    private ICommandDispatcher _commandDispatcher = null!;
    private ILegacyPasswordService _passwordService = null!;
    private ConfigurableRoleRepository _roleRepository = null!;
    private ConfigurableTokenService _tokenService = null!;
    private ITutorialService _tutorialService = null!;
    private IUnitOfWork _unitOfWork = null!;
    private ConfigurableUserRepository _userRepository = null!;
    private FakeUserSessionStore _userSessionStore = null!;
    private IRankService _rankService = null!;
    private IUserCredentialLoginService _credentialLoginService = null!;
    private IUserRegistrationService _registrationService = null!;

    [SetUp]
    public void SetUp()
    {
        _commandDispatcher = Substitute.For<ICommandDispatcher>();
        _passwordService = Substitute.For<ILegacyPasswordService>();
        _roleRepository = new ConfigurableRoleRepository();
        _tokenService = new ConfigurableTokenService();
        _tutorialService = Substitute.For<ITutorialService>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _userRepository = new ConfigurableUserRepository();
        _userSessionStore = new FakeUserSessionStore();
        _rankService = Substitute.For<IRankService>();
        _passwordService.Create(Arg.Any<string>()).Returns(("hash", "salt", 25000, 512, "sha256"));
        _registrationService = new UserRegistrationService(
            _userRepository,
            _roleRepository,
            _passwordService,
            _commandDispatcher,
            _unitOfWork,
            NullLogger<UserRegistrationService>.Instance,
            new AppDefaultsOptions { PreferredLanguage = "en-US", PreferredTimeZone = "UTC" },
            _tutorialService);
        _credentialLoginService = new UserCredentialLoginService(
            _userRepository,
            _roleRepository,
            _passwordService,
            _rankService,
            _userSessionStore,
            _tokenService,
            _unitOfWork,
            new AppDefaultsOptions { PreferredLanguage = "en-US", PreferredTimeZone = "UTC" },
            _tutorialService,
            BuildMapper());
    }

    [Test]
    public async Task RegisterAsync_AssignsDefaultRoleEnqueuesCommandAndCommits_WhenInputIsValid()
    {
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var defaultRole = new Role { Id = Id<Role>.New(), Name = AuthConstants.Roles.User };
        User? addedUser = null;
        _userRepository.FindByNameOrEmail = (_, _, _) => Task.FromResult<User?>(null);
        _userRepository.Add = (user, _) =>
        {
            addedUser = user;
            return Task.CompletedTask;
        };
        _roleRepository.GetByNames = (_, _) => Task.FromResult<List<Role>>([defaultRole]);
        _roleRepository.AddUserRoles = (_, _, _) => Task.CompletedTask;
        _commandDispatcher.EnqueueAsync(Arg.Any<UserRegisteredCommand>()).Returns(Task.CompletedTask);
        _tutorialService.InitializeOnboardingTutorialAsync(Arg.Any<Id<User>>(), cancellationToken).Returns(Result<Unit, AppError>.Success(Unit.Value));
        _unitOfWork.SaveChangesAsync(cancellationToken).Returns(Task.FromResult(1));

        var result = await _registrationService.RegisterAsync(new RegisterUserInput("new-user", " NEW@example.com ", "password123", "password123", null, null), cancellationToken);

        result.IsSuccess.Should().BeTrue();
        addedUser.Should().NotBeNull();
        addedUser!.Email.Value.Should().Be("new@example.com");
        addedUser.PreferredLanguage.Should().Be("en-US");
        addedUser.PreferredTimeZone.Should().Be("UTC");
        addedUser.IsVisibleInRanking.Should().BeTrue();
        _roleRepository.Calls
            .Where(call =>
                call.Method == nameof(IRoleRepository.AddUserRolesAsync)
                && call.Argument is ValueTuple<Id<User>, IReadOnlyCollection<Id<Role>>> arguments
                && arguments.Item1 == addedUser.Id
                && arguments.Item2.Single() == defaultRole.Id
                && call.CancellationToken == cancellationToken)
            .Should()
            .ContainSingle();
        await _commandDispatcher.Received(1).EnqueueAsync(Arg.Is<UserRegisteredCommand>(command => command.UserId == addedUser.Id));
        await _tutorialService.Received(1).InitializeOnboardingTutorialAsync(addedUser.Id, cancellationToken);
        await _unitOfWork.Received(1).SaveChangesAsync(cancellationToken);
        Received.InOrder(() =>
        {
            _unitOfWork.SaveChangesAsync(cancellationToken);
            _tutorialService.InitializeOnboardingTutorialAsync(addedUser.Id, cancellationToken);
        });
    }

    [Test]
    public async Task RegisterAsync_WhenTutorialInitializationThrows_ReturnsSuccessAfterUserSave()
    {
        var defaultRole = new Role { Id = Id<Role>.New(), Name = AuthConstants.Roles.User };
        _userRepository.FindByNameOrEmail = (_, _, _) => Task.FromResult<User?>(null);
        _userRepository.Add = (_, _) => Task.CompletedTask;
        _roleRepository.GetByNames = (_, _) => Task.FromResult<List<Role>>([defaultRole]);
        _roleRepository.AddUserRoles = (_, _, _) => Task.CompletedTask;
        _commandDispatcher.EnqueueAsync(Arg.Any<UserRegisteredCommand>()).Returns(Task.CompletedTask);
        _tutorialService.InitializeOnboardingTutorialAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Result<Unit, AppError>>(new InvalidOperationException("tutorial failed")));

        var result = await _registrationService.RegisterAsync(
            new RegisterUserInput("new-user", "new@example.com", "password123", "password123", true, null));

        result.IsSuccess.Should().BeTrue();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _tutorialService.Received(1).InitializeOnboardingTutorialAsync(result.Value, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RegisterTrainerAsync_AssignsTrainerRolesAndHidesRanking()
    {
        var roles = new List<Role>
        {
            new() { Id = Id<Role>.New(), Name = AuthConstants.Roles.User },
            new() { Id = Id<Role>.New(), Name = AuthConstants.Roles.Trainer }
        };
        User? addedUser = null;
        _userRepository.FindByNameOrEmail = (_, _, _) => Task.FromResult<User?>(null);
        _userRepository.Add = (user, _) =>
        {
            addedUser = user;
            return Task.CompletedTask;
        };
        _roleRepository.GetByNames = (_, _) => Task.FromResult(roles);
        _roleRepository.AddUserRoles = (_, _, _) => Task.CompletedTask;
        _commandDispatcher.EnqueueAsync(Arg.Any<UserRegisteredCommand>()).Returns(Task.CompletedTask);
        _tutorialService.InitializeOnboardingTutorialAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>()).Returns(Result<Unit, AppError>.Success(Unit.Value));

        var result = await _registrationService.RegisterTrainerAsync(new RegisterUserInput("trainer", "trainer@example.com", "password123", "password123", true, "pl-PL"));

        result.IsSuccess.Should().BeTrue();
        addedUser.Should().NotBeNull();
        addedUser!.IsVisibleInRanking.Should().BeFalse();
        addedUser.PreferredLanguage.Should().Be("en-US");
        _roleRepository.Calls
            .Where(call =>
                call.Method == nameof(IRoleRepository.AddUserRolesAsync)
                && call.Argument is ValueTuple<Id<User>, IReadOnlyCollection<Id<Role>>> arguments
                && arguments.Item1 == addedUser.Id
                && arguments.Item2.SequenceEqual(roles.Select(role => role.Id)))
            .Should()
            .ContainSingle();
    }

    [Test]
    public async Task RegisterAsync_ReturnsInternalServerErrorWithoutCommit_WhenDefaultRoleIsMissing()
    {
        _userRepository.FindByNameOrEmail = (_, _, _) => Task.FromResult<User?>(null);
        _userRepository.Add = (_, _) => Task.CompletedTask;
        _roleRepository.GetByNames = (_, _) => Task.FromResult(new List<Role>());

        var result = await _registrationService.RegisterAsync(new RegisterUserInput("new-user", "new@example.com", "password123", "password123", true, null));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InternalServerError>();
        await _commandDispatcher.DidNotReceive().EnqueueAsync(Arg.Any<UserRegisteredCommand>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoginAsync_CreatesSessionProjectsClaimsAndCommits_WhenCredentialsAreValid()
    {
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var user = CreateUser("login-user", "login@example.com");
        user.PreferredTimeZone = string.Empty;
        var session = new UserSession { Id = Id<UserSession>.New(), UserId = user.Id, Jti = "session-jti" };
        var roles = new List<string> { AuthConstants.Roles.User };
        var permissionClaims = new List<string> { AuthConstants.Permissions.AdminAccess };
        _userRepository.FindByName = (_, _) => Task.FromResult<User?>(user);
        _passwordService.Verify("password123", user.LegacyHash!, user.LegacySalt!, user.LegacyIterations, user.LegacyKeyLength, user.LegacyDigest).Returns(true);
        _roleRepository.GetRoleNamesByUserId = (_, _) => Task.FromResult(roles);
        _roleRepository.GetPermissionClaimsByUserId = (_, _) => Task.FromResult(permissionClaims);
        _userSessionStore.CreateSessionHandler = (_, _, _) => Task.FromResult(session);
        _tokenService.Create = (_, _, _, _, _) => "issued-token";
        _tutorialService.HasActiveTutorialsAsync(user.Id, cancellationToken).Returns(true);
        _unitOfWork.SaveChangesAsync(cancellationToken).Returns(Task.FromResult(1));
        _rankService.GetNextRank(user.ProfileRank).Returns(new RankDefinition { Name = "Senior 1", NeedElo = 1500 });

        var result = await _credentialLoginService.LoginAsync(user.Name, "password123", cancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Token.Should().Be("issued-token");
        result.Value.PermissionClaims.Should().Equal(permissionClaims);
        result.Value.User.Roles.Should().Equal(roles);
        result.Value.User.PermissionClaims.Should().Equal(permissionClaims);
        result.Value.User.PreferredTimeZone.Should().Be("UTC");
        result.Value.User.Elo.Should().Be(1000);
        result.Value.User.NextRank.Should().BeEquivalentTo(new RankInfo { Name = "Senior 1", NeedElo = 1500 });
        result.Value.User.CreatedAt.Should().Be(user.CreatedAt.UtcDateTime);
        result.Value.User.UpdatedAt.Should().Be(user.UpdatedAt.UtcDateTime);
        result.Value.User.IsDeleted.Should().Be(user.IsDeleted);
        result.Value.User.IsVisibleInRanking.Should().Be(user.IsVisibleInRanking);
        result.Value.User.HasActiveTutorials.Should().BeTrue();
        _userSessionStore.Calls
            .Where(call =>
                call.Method == nameof(IUserSessionStore.CreateSessionAsync)
                && call.Argument is ValueTuple<Id<User>, DateTimeOffset> arguments
                && arguments.Item1 == user.Id
                && call.CancellationToken == cancellationToken)
            .Should()
            .ContainSingle();
        await _unitOfWork.Received(1).SaveChangesAsync(cancellationToken);
    }

    [Test]
    public async Task LoginTrainerAsync_ReturnsUnauthorizedWithoutSessionOrCommit_WhenTrainerRoleIsMissing()
    {
        var user = CreateUser("user", "user@example.com");
        _userRepository.FindByName = (_, _) => Task.FromResult<User?>(user);
        _passwordService.Verify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>()).Returns(true);
        _roleRepository.GetRoleNamesByUserId = (_, _) => Task.FromResult<List<string>>([AuthConstants.Roles.User]);

        var result = await _credentialLoginService.LoginTrainerAsync(user.Name, "password123");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UserUnauthorizedError>();
        _userSessionStore.Calls.Should().NotContain(call => call.Method == nameof(IUserSessionStore.CreateSessionAsync));
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static User CreateUser(string name, string email) => new()
    {
        Id = Id<User>.New(),
        Name = name,
        Email = email,
        ProfileRank = "Junior 1",
        LegacyHash = "hash",
        LegacySalt = "salt",
        LegacyIterations = 25000,
        LegacyKeyLength = 512,
        LegacyDigest = "sha256"
    };

    private static IMapper BuildMapper()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMapper>();
    }
}
