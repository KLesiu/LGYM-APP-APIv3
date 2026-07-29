using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.Identity.Profile;
using LgymApi.Application.Identity.Ranking;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.UnitTests.Fakes;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class UserServiceProfileFailureTests
{
    private IUnitOfWork _unitOfWork = null!;
    private ConfigurableUserRepository _userRepository = null!;
    private UserProfileService _profileService = null!;

    [SetUp]
    public void SetUp()
    {
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _userRepository = new ConfigurableUserRepository();
        _profileService = new UserProfileService(new UserProfileServiceDependencies(
            _userRepository,
            new ConfigurableRoleRepository(),
            Substitute.For<LgymApi.Application.Services.IRankService>(),
            _unitOfWork,
            new LgymApi.Application.Options.AppDefaultsOptions(),
            Substitute.For<LgymApi.Application.Features.Tutorial.ITutorialService>(),
            Substitute.For<IMapper>()));
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

}
