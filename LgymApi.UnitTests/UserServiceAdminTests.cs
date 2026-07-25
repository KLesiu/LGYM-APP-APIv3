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
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class UserServiceAdminTests
{
    private IRoleRepository _roleRepository = null!;
    private IUnitOfWork _unitOfWork = null!;
    private IUserRepository _userRepository = null!;
    private UserAdminAccessService _adminAccessService = null!;
    private UserRoleAdministrationService _roleAdministrationService = null!;

    [SetUp]
    public void SetUp()
    {
        _roleRepository = Substitute.For<IRoleRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _userRepository = Substitute.For<IUserRepository>();
        _adminAccessService = new UserAdminAccessService(_roleRepository);
        _roleAdministrationService = new UserRoleAdministrationService(_userRepository, _roleRepository, _unitOfWork);
    }

    [Test]
    public async Task IsAdminAsync_ReturnsFalseWithoutRepositoryCall_WhenUserIdIsEmpty()
    {
        var result = await _adminAccessService.IsAdminAsync(Id<User>.Empty);

        result.Should().BeFalse();
        await _roleRepository.DidNotReceive().UserHasPermissionAsync(Arg.Any<Id<User>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task IsAdminAsync_ForwardsPermissionCheckAndCancellationToken()
    {
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var userId = Id<User>.New();
        _roleRepository.UserHasPermissionAsync(userId, AuthConstants.Permissions.AdminAccess, cancellationToken).Returns(true);

        var result = await _adminAccessService.IsAdminAsync(userId, cancellationToken);

        result.Should().BeTrue();
        await _roleRepository.Received(1).UserHasPermissionAsync(userId, AuthConstants.Permissions.AdminAccess, cancellationToken);
    }

    [Test]
    public async Task UpdateUserRolesAsync_ReplacesNormalizedRolesAndCommitsExactlyOnce()
    {
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var user = new User { Id = Id<User>.New(), Name = "user", Email = "user@example.com" };
        var trainerRole = new Role { Id = Id<Role>.New(), Name = AuthConstants.Roles.Trainer };
        _userRepository.FindByIdAsync(user.Id, cancellationToken).Returns(user);
        _roleRepository.GetByNamesAsync(Arg.Is<IReadOnlyCollection<string>>(roles => roles.Single() == AuthConstants.Roles.Trainer), cancellationToken).Returns([trainerRole]);
        _roleRepository.ReplaceUserRolesAsync(user.Id, Arg.Any<IReadOnlyCollection<Id<Role>>>(), cancellationToken).Returns(Task.CompletedTask);

        var result = await _roleAdministrationService.UpdateUserRolesAsync(user.Id, [" Trainer ", "trainer", ""], cancellationToken);

        result.IsSuccess.Should().BeTrue();
        await _roleRepository.Received(1).GetByNamesAsync(
            Arg.Is<IReadOnlyCollection<string>>(roles => roles.Count == 1 && roles.Single() == AuthConstants.Roles.Trainer),
            cancellationToken);
        await _roleRepository.Received(1).ReplaceUserRolesAsync(user.Id, Arg.Is<IReadOnlyCollection<Id<Role>>>(roleIds => roleIds.Single() == trainerRole.Id), cancellationToken);
        await _unitOfWork.Received(1).SaveChangesAsync(cancellationToken);
    }

    [Test]
    public async Task UpdateUserRolesAsync_ReturnsUserNotFoundWithoutCommit_WhenTargetUserIsMissing()
    {
        var userId = Id<User>.New();
        _userRepository.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _roleAdministrationService.UpdateUserRolesAsync(userId, [AuthConstants.Roles.User]);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UserNotFoundError>();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateUserRolesAsync_ReturnsInvalidUserErrorWithoutCommit_WhenRoleIsMissing()
    {
        var user = new User { Id = Id<User>.New(), Name = "user", Email = "user@example.com" };
        _userRepository.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _roleRepository.GetByNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns([]);

        var result = await _roleAdministrationService.UpdateUserRolesAsync(user.Id, ["missing"]);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidUserError>();
        await _roleRepository.DidNotReceive().ReplaceUserRolesAsync(Arg.Any<Id<User>>(), Arg.Any<IReadOnlyCollection<Id<Role>>>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateUserRolesAsync_ReturnsInvalidUserErrorWithoutRepositoryCallsOrCommit_WhenTargetUserIdIsEmpty()
    {
        var result = await _roleAdministrationService.UpdateUserRolesAsync(Id<User>.Empty, [AuthConstants.Roles.User]);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidUserError>();
        await _userRepository.DidNotReceive().FindByIdAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>());
        await _roleRepository.DidNotReceive().GetByNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

}
