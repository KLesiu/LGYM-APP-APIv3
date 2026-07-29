using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Administration;
using LgymApi.Application.Identity.Administration;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.UnitTests.Fakes;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class UserServiceAdminTests
{
    private ConfigurableRoleRepository _roleRepository = null!;
    private IUnitOfWork _unitOfWork = null!;
    private ConfigurableUserRepository _userRepository = null!;
    private UserAdminAccessService _adminAccessService = null!;
    private UserRoleAdministrationService _roleAdministrationService = null!;

    [SetUp]
    public void SetUp()
    {
        _roleRepository = new ConfigurableRoleRepository();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _userRepository = new ConfigurableUserRepository();
        _adminAccessService = new UserAdminAccessService(_roleRepository);
        _roleAdministrationService = new UserRoleAdministrationService(_userRepository, _roleRepository, _unitOfWork);
    }

    [Test]
    public async Task IsAdminAsync_ReturnsFalseWithoutRepositoryCall_WhenUserIdIsEmpty()
    {
        var result = await _adminAccessService.IsAdminAsync(Id<User>.Empty);

        result.Should().BeFalse();
        _roleRepository.Calls.Should().NotContain(call => call.Method == nameof(IRoleRepository.UserHasPermissionAsync));
    }

    [Test]
    public async Task IsAdminAsync_ForwardsPermissionCheckAndCancellationToken()
    {
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var userId = Id<User>.New();
        _roleRepository.UserHasPermission = (_, _, _) => Task.FromResult(true);

        var result = await _adminAccessService.IsAdminAsync(userId, cancellationToken);

        result.Should().BeTrue();
        _roleRepository.Calls
            .Where(call =>
                call.Method == nameof(IRoleRepository.UserHasPermissionAsync)
                && call.Argument is ValueTuple<Id<User>, string> arguments
                && arguments.Item1 == userId
                && arguments.Item2 == AuthConstants.Permissions.AdminAccess
                && call.CancellationToken == cancellationToken)
            .Should()
            .ContainSingle();
    }

    [Test]
    public async Task UpdateUserRolesAsync_ReplacesNormalizedRolesAndCommitsExactlyOnce()
    {
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var user = new User { Id = Id<User>.New(), Name = "user", Email = "user@example.com" };
        var trainerRole = new Role { Id = Id<Role>.New(), Name = AuthConstants.Roles.Trainer };
        _userRepository.FindById = (_, _) => Task.FromResult<User?>(user);
        _roleRepository.GetByNames = (_, _) => Task.FromResult<List<Role>>([trainerRole]);
        _roleRepository.ReplaceUserRoles = (_, _, _) => Task.CompletedTask;

        var result = await _roleAdministrationService.UpdateUserRolesAsync(user.Id, [" Trainer ", "trainer", ""], cancellationToken);

        result.IsSuccess.Should().BeTrue();
        _roleRepository.Calls
            .Where(call =>
                call.Method == nameof(IRoleRepository.GetByNamesAsync)
                && call.Argument is IReadOnlyCollection<string> roles
                && roles.Count == 1
                && roles.Single() == AuthConstants.Roles.Trainer
                && call.CancellationToken == cancellationToken)
            .Should()
            .ContainSingle();
        _roleRepository.Calls
            .Where(call =>
                call.Method == nameof(IRoleRepository.ReplaceUserRolesAsync)
                && call.Argument is ValueTuple<Id<User>, IReadOnlyCollection<Id<Role>>> arguments
                && arguments.Item1 == user.Id
                && arguments.Item2.Single() == trainerRole.Id
                && call.CancellationToken == cancellationToken)
            .Should()
            .ContainSingle();
        await _unitOfWork.Received(1).SaveChangesAsync(cancellationToken);
    }

    [Test]
    public async Task UpdateUserRolesAsync_ReturnsUserNotFoundWithoutCommit_WhenTargetUserIsMissing()
    {
        var userId = Id<User>.New();
        _userRepository.FindById = (_, _) => Task.FromResult<User?>(null);

        var result = await _roleAdministrationService.UpdateUserRolesAsync(userId, [AuthConstants.Roles.User]);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UserNotFoundError>();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateUserRolesAsync_ReturnsInvalidUserErrorWithoutCommit_WhenRoleIsMissing()
    {
        var user = new User { Id = Id<User>.New(), Name = "user", Email = "user@example.com" };
        _userRepository.FindById = (_, _) => Task.FromResult<User?>(user);
        _roleRepository.GetByNames = (_, _) => Task.FromResult<List<Role>>([]);

        var result = await _roleAdministrationService.UpdateUserRolesAsync(user.Id, ["missing"]);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidUserError>();
        _roleRepository.Calls.Should().NotContain(call => call.Method == nameof(IRoleRepository.ReplaceUserRolesAsync));
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateUserRolesAsync_ReturnsInvalidUserErrorWithoutRepositoryCallsOrCommit_WhenTargetUserIdIsEmpty()
    {
        var result = await _roleAdministrationService.UpdateUserRolesAsync(Id<User>.Empty, [AuthConstants.Roles.User]);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidUserError>();
        _userRepository.Calls.Should().NotContain(call => call.Method == nameof(IUserRepository.FindByIdAsync));
        _roleRepository.Calls.Should().NotContain(call => call.Method == nameof(IRoleRepository.GetByNamesAsync));
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

}
