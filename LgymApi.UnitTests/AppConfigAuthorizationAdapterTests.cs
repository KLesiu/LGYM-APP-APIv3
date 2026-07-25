using FluentAssertions;
using LgymApi.Application.Identity.Adapters;
using LgymApi.Application.Identity;
using LgymApi.Application.Platform.ReferenceData.AppConfig.Contracts;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class AppConfigAuthorizationAdapterTests
{
    private IUserRepository _userRepository = null!;
    private IRoleRepository _roleRepository = null!;
    private AppConfigAuthorizationAdapter _adapter = null!;

    [SetUp]
    public void SetUp()
    {
        _userRepository = Substitute.For<IUserRepository>();
        _roleRepository = Substitute.For<IRoleRepository>();
        _adapter = new AppConfigAuthorizationAdapter(_userRepository, _roleRepository);
    }

    [Test]
    public async Task CanManageAppConfigAsync_EmptyUserId_ReturnsFalseWithoutRepositoryCalls()
    {
        var result = await _adapter.CanManageAppConfigAsync(Id<User>.Empty);

        result.Should().BeFalse();
        await _userRepository.DidNotReceiveWithAnyArgs().FindByIdAsync(default, default);
        await _roleRepository.DidNotReceiveWithAnyArgs().UserHasPermissionAsync(default, default!, default);
    }

    [Test]
    public async Task CanManageAppConfigAsync_MissingUser_ReturnsFalseWithoutPermissionCheck()
    {
        var userId = Id<User>.New();
        _userRepository.FindByIdAsync(userId, CancellationToken.None).Returns(Task.FromResult<User?>(null));

        var result = await _adapter.CanManageAppConfigAsync(userId);

        result.Should().BeFalse();
        await _userRepository.Received(1).FindByIdAsync(userId, CancellationToken.None);
        await _roleRepository.DidNotReceiveWithAnyArgs().UserHasPermissionAsync(default, default!, default);
    }

    [Test]
    public async Task CanManageAppConfigAsync_UserLacksManageAppConfigPermission_ReturnsFalse()
    {
        var userId = Id<User>.New();
        _userRepository.FindByIdAsync(userId, CancellationToken.None).Returns(Task.FromResult<User?>(new User()));
        _roleRepository.UserHasPermissionAsync(userId, AuthConstants.Permissions.ManageAppConfig, CancellationToken.None)
            .Returns(Task.FromResult(false));

        var result = await _adapter.CanManageAppConfigAsync(userId);

        result.Should().BeFalse();
        await _roleRepository.Received(1)
            .UserHasPermissionAsync(userId, AuthConstants.Permissions.ManageAppConfig, CancellationToken.None);
    }

    [Test]
    public async Task CanManageAppConfigAsync_UserHasManageAppConfigPermission_ReturnsTrue()
    {
        var userId = Id<User>.New();
        _userRepository.FindByIdAsync(userId, CancellationToken.None).Returns(Task.FromResult<User?>(new User()));
        _roleRepository.UserHasPermissionAsync(userId, AuthConstants.Permissions.ManageAppConfig, CancellationToken.None)
            .Returns(Task.FromResult(true));

        var result = await _adapter.CanManageAppConfigAsync(userId);

        result.Should().BeTrue();
        await _roleRepository.Received(1)
            .UserHasPermissionAsync(userId, AuthConstants.Permissions.ManageAppConfig, CancellationToken.None);
    }

    [Test]
    public async Task CanManageAppConfigAsync_AllowedUser_ForwardsCancellationTokenToBothRepositories()
    {
        var userId = Id<User>.New();
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        cancellationTokenSource.Cancel();
        _userRepository.FindByIdAsync(userId, cancellationToken).Returns(Task.FromResult<User?>(new User()));
        _roleRepository.UserHasPermissionAsync(userId, AuthConstants.Permissions.ManageAppConfig, cancellationToken)
            .Returns(Task.FromResult(true));

        var result = await _adapter.CanManageAppConfigAsync(userId, cancellationToken);

        result.Should().BeTrue();
        await _userRepository.Received(1).FindByIdAsync(userId, cancellationToken);
        await _roleRepository.Received(1)
            .UserHasPermissionAsync(userId, AuthConstants.Permissions.ManageAppConfig, cancellationToken);
    }

    [Test]
    public async Task CanManageAppConfigAsync_UserRepositoryThrows_PropagatesException()
    {
        var expectedException = new InvalidOperationException("User lookup failed.");
        _userRepository.FindByIdAsync(Arg.Any<Id<User>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<User?>(expectedException));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _adapter.CanManageAppConfigAsync(Id<User>.New()));

        exception.Should().BeSameAs(expectedException);
        await _roleRepository.DidNotReceiveWithAnyArgs().UserHasPermissionAsync(default, default!, default);
    }

    [Test]
    public async Task CanManageAppConfigAsync_RoleRepositoryThrows_PropagatesException()
    {
        var userId = Id<User>.New();
        var expectedException = new InvalidOperationException("Permission lookup failed.");
        _userRepository.FindByIdAsync(userId, CancellationToken.None).Returns(Task.FromResult<User?>(new User()));
        _roleRepository.UserHasPermissionAsync(userId, AuthConstants.Permissions.ManageAppConfig, CancellationToken.None)
            .Returns(Task.FromException<bool>(expectedException));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _adapter.CanManageAppConfigAsync(userId));

        exception.Should().BeSameAs(expectedException);
    }

    [Test]
    public async Task AddIdentityModule_RegistersAppConfigAuthorizationPortExactlyOnceAsScopedAndExecutesIt()
    {
        var services = new ServiceCollection();
        services.AddIdentityModule();
        services.AddScoped<IUserRepository>(_ => _userRepository);
        services.AddScoped<IRoleRepository>(_ => _roleRepository);

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(IAppConfigAuthorizationPort))
            .ToArray();

        Assert.That(registrations, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(registrations[0].Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
            Assert.That(registrations[0].ImplementationType, Is.EqualTo(typeof(AppConfigAuthorizationAdapter)));
        });

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        using var scope = serviceProvider.CreateScope();
        var port = scope.ServiceProvider.GetRequiredService<IAppConfigAuthorizationPort>();
        var userId = Id<User>.New();
        _userRepository.FindByIdAsync(userId, CancellationToken.None).Returns(Task.FromResult<User?>(new User()));
        _roleRepository.UserHasPermissionAsync(userId, AuthConstants.Permissions.ManageAppConfig, CancellationToken.None)
            .Returns(Task.FromResult(true));

        var result = await port.CanManageAppConfigAsync(userId);

        result.Should().BeTrue();

        _roleRepository.UserHasPermissionAsync(userId, AuthConstants.Permissions.ManageAppConfig, CancellationToken.None)
            .Returns(Task.FromResult(false));

        var denied = await port.CanManageAppConfigAsync(userId);

        denied.Should().BeFalse();

        var expectedException = new InvalidOperationException("Permission lookup failed.");
        _roleRepository.UserHasPermissionAsync(userId, AuthConstants.Permissions.ManageAppConfig, CancellationToken.None)
            .Returns(Task.FromException<bool>(expectedException));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await port.CanManageAppConfigAsync(userId));

        exception.Should().BeSameAs(expectedException);
    }
}
