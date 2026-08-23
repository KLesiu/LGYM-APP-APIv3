using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Identity.Profile;
using LgymApi.Application.Identity.Ranking;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Models;
using LgymApi.Application.Options;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Services;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts.AdultConfirmation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using LgymApi.UnitTests.Fakes;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class UserServiceProfileTests
{
    private ConfigurableRoleRepository _roleRepository = null!;
    private ITutorialService _tutorialService = null!;
    private IUnitOfWork _unitOfWork = null!;
    private ConfigurableUserRepository _userRepository = null!;
    private IRankService _rankService = null!;
    private IAccountPushInstallationCleanupPort _accountPushInstallationCleanupPort = null!;
    private UserProfileService _profileService = null!;

    [SetUp]
    public void SetUp()
    {
        _roleRepository = new ConfigurableRoleRepository();
        _tutorialService = Substitute.For<ITutorialService>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _userRepository = new ConfigurableUserRepository();
        _rankService = Substitute.For<IRankService>();
        _accountPushInstallationCleanupPort = Substitute.For<IAccountPushInstallationCleanupPort>();
        _userRepository.Update = (_, _) => Task.CompletedTask;
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        _profileService = new UserProfileService(
            _userRepository,
            _roleRepository,
            _rankService,
            _unitOfWork,
            _accountPushInstallationCleanupPort,
            new AppDefaultsOptions { PreferredTimeZone = "UTC" },
            _tutorialService,
            BuildMapper(),
            Options.Create(new AgeGateOptions()));
    }

    [Test]
    public async Task CheckTokenAsync_ProjectsRolesClaimsTutorialStateAndDefaultTimeZone()
    {
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var user = CreateUser();
        user.PreferredTimeZone = string.Empty;
        _roleRepository.GetRoleNamesByUserId = (_, _) => Task.FromResult<List<string>>(["User", "Trainer"]);
        _roleRepository.GetPermissionClaimsByUserId = (_, _) => Task.FromResult<List<string>>(["users.read"]);
        _tutorialService.HasActiveTutorialsAsync(user.Id, cancellationToken).Returns(true);
        _rankService.GetNextRank(user.ProfileRank).Returns(new RankDefinition { Name = "Senior 1", NeedElo = 1500 });

        var result = await _profileService.CheckTokenAsync(user, cancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(user.Id);
        result.Value.Name.Should().Be(user.Name);
        result.Value.Email.Should().Be(user.Email);
        result.Value.Avatar.Should().Be(user.Avatar);
        result.Value.ProfileRank.Should().Be(user.ProfileRank);
        result.Value.Roles.Should().Equal("User", "Trainer");
        result.Value.PermissionClaims.Should().Equal("users.read");
        result.Value.HasActiveTutorials.Should().BeTrue();
        result.Value.PreferredTimeZone.Should().Be("UTC");
        result.Value.Elo.Should().Be(1000);
        result.Value.NextRank.Should().BeEquivalentTo(new RankInfo { Name = "Senior 1", NeedElo = 1500 });
        result.Value.CreatedAt.Should().Be(user.CreatedAt.UtcDateTime);
        result.Value.UpdatedAt.Should().Be(user.UpdatedAt.UtcDateTime);
        result.Value.IsDeleted.Should().Be(user.IsDeleted);
        result.Value.IsVisibleInRanking.Should().Be(user.IsVisibleInRanking);
        _roleRepository.Calls
            .Where(call =>
                call.Method == nameof(IRoleRepository.GetRoleNamesByUserIdAsync)
                && call.Argument is Id<User> id
                && id == user.Id
                && call.CancellationToken == cancellationToken)
            .Should()
            .ContainSingle();
    }

    [Test]
    public async Task DeleteAccountAsync_AnonymizesUserAndCommits()
    {
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var user = CreateUser();

        var result = await _profileService.DeleteAccountAsync(user, cancellationToken);

        result.IsSuccess.Should().BeTrue();
        user.IsDeleted.Should().BeTrue();
        user.Name.Should().Be($"anonymized_user_{user.Id}");
        user.Email.Value.Should().Be($"anonymized_{user.Id}@example.com");
        _userRepository.Calls.Should().ContainSingle(call =>
            call.Method == nameof(IUserRepository.UpdateAsync)
            && call.Argument == user
            && call.CancellationToken == cancellationToken);
        await _unitOfWork.Received(1).SaveChangesAsync(cancellationToken);
    }

    [Test]
    public async Task DeleteAccountAsync_StagesPushInstallationRemovalBeforeCommitting()
    {
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        var user = CreateUser();
        var operations = new List<string>();
        _accountPushInstallationCleanupPort.StageRemoveForAccountAsync(
                user.Id.Rebind<LgymApi.Identity.Contracts.AccountReference>(),
                cancellationToken)
            .Returns(_ =>
            {
                operations.Add("cleanup");
                return Task.CompletedTask;
            });
        _userRepository.Update = (_, _) =>
        {
            operations.Add("update");
            return Task.CompletedTask;
        };
        _unitOfWork.SaveChangesAsync(cancellationToken).Returns(_ =>
        {
            operations.Add("commit");
            return Task.FromResult(1);
        });

        await _profileService.DeleteAccountAsync(user, cancellationToken);

        await _accountPushInstallationCleanupPort.Received(1).StageRemoveForAccountAsync(
            user.Id.Rebind<LgymApi.Identity.Contracts.AccountReference>(),
            cancellationToken);
        operations.Should().Equal("cleanup", "update", "commit");
    }

    [Test]
    public async Task UpdateTimeZoneAsync_ReturnsInvalidUserErrorWithoutUpdateOrCommit_WhenTimeZoneIsInvalid()
    {
        var user = CreateUser();

        var result = await _profileService.UpdateTimeZoneAsync(user, "Not/ARealTimeZone");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidUserError>();
        _userRepository.Calls.Should().NotContain(call => call.Method == nameof(IUserRepository.UpdateAsync));
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static User CreateUser() => new()
    {
        Id = Id<User>.New(),
        Name = "user",
        Email = "user@example.com",
        ProfileRank = "Junior 1",
        PreferredTimeZone = "Europe/Warsaw"
    };

    private static IMapper BuildMapper()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMapper>();
    }
}
