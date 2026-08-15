using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Identity.Profile;
using LgymApi.Application.Identity.Ranking;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.UnitTests.Fakes;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class UserServiceProfileFailureTests
{
    private IUnitOfWork _unitOfWork = null!;
    private ConfigurableUserRepository _userRepository = null!;
    private IAccountPushInstallationCleanupPort _accountPushInstallationCleanupPort = null!;
    private UserProfileService _profileService = null!;

    [SetUp]
    public void SetUp()
    {
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _userRepository = new ConfigurableUserRepository();
        _accountPushInstallationCleanupPort = Substitute.For<IAccountPushInstallationCleanupPort>();
        _profileService = new UserProfileService(
            _userRepository,
            new ConfigurableRoleRepository(),
            Substitute.For<LgymApi.Application.Services.IRankService>(),
            _unitOfWork,
            _accountPushInstallationCleanupPort,
            new LgymApi.Application.Options.AppDefaultsOptions(),
            Substitute.For<LgymApi.Application.Features.Tutorial.ITutorialService>(),
            Substitute.For<IMapper>());
    }

    [Test]
    public async Task CheckTokenAsync_ReturnsUserNotFoundWithoutCommit_WhenCurrentUserIsMissing()
    {
        var result = await _profileService.CheckTokenAsync(null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UserNotFoundError>();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteAccountAsync_ReturnsUserNotFoundWithoutCommit_WhenCurrentUserIsMissing()
    {
        var result = await _profileService.DeleteAccountAsync(null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<UserNotFoundError>();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteAccountAsync_LeavesUserUnchangedWithoutCommit_WhenPushCleanupStagingFails()
    {
        var user = new User { Id = Id<User>.New(), Name = "profile-failure", Email = "profile-failure@example.com" };
        var stagingFailure = new InvalidOperationException("Push installation cleanup staging failed.");
        _accountPushInstallationCleanupPort.StageRemoveForAccountAsync(
                user.Id.Rebind<LgymApi.Identity.Contracts.AccountReference>(),
                CancellationToken.None)
            .Returns(Task.FromException(stagingFailure));

        var action = () => _profileService.DeleteAccountAsync(user);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .Where(exception => ReferenceEquals(exception, stagingFailure));
        user.IsDeleted.Should().BeFalse();
        user.Name.Should().Be("profile-failure");
        user.Email.Value.Should().Be("profile-failure@example.com");
        _userRepository.Calls.Should().NotContain(call => call.Method == nameof(IUserRepository.UpdateAsync));
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

}
