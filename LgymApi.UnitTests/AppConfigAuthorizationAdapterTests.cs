using FluentAssertions;
using LgymApi.Application.Identity.Adapters;
using LgymApi.Identity;
using LgymApi.Application.Platform.ReferenceData.AppConfig.Contracts;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Platform.Contracts;
using LgymApi.UnitTests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class AppConfigAuthorizationAdapterTests
{
    private ConfigurableUserRepository _userRepository = null!;
    private ConfigurableRoleRepository _roleRepository = null!;
    private AppConfigAuthorizationAdapter _adapter = null!;

    [SetUp]
    public void SetUp()
    {
        _userRepository = new ConfigurableUserRepository();
        _roleRepository = new ConfigurableRoleRepository();
        _adapter = new AppConfigAuthorizationAdapter(_userRepository, _roleRepository);
    }

    [Test]
    public async Task CanManageAppConfigAsync_EmptyUserId_ReturnsFalseWithoutRepositoryCalls()
    {
        var result = await _adapter.CanManageAppConfigAsync(Id<ActorReference>.Empty);

        result.Should().BeFalse();
        _userRepository.Calls.Should().NotContain(call => call.Method == nameof(IUserRepository.FindByIdAsync));
        _roleRepository.Calls.Should().NotContain(call => call.Method == nameof(IRoleRepository.UserHasPermissionAsync));
    }

    [Test]
    public async Task CanManageAppConfigAsync_MissingUser_ReturnsFalseWithoutPermissionCheck()
    {
        var actorId = Id<ActorReference>.New();
        var userId = actorId.Rebind<User>();
        _userRepository.FindById = (_, _) => Task.FromResult<User?>(null);

        var result = await _adapter.CanManageAppConfigAsync(actorId);

        result.Should().BeFalse();
        _userRepository.Calls
            .Where(call =>
                call.Method == nameof(IUserRepository.FindByIdAsync)
                && call.Argument is Id<User> id
                && id == userId
                && call.CancellationToken == CancellationToken.None)
            .Should()
            .ContainSingle();
        _roleRepository.Calls.Should().NotContain(call => call.Method == nameof(IRoleRepository.UserHasPermissionAsync));
    }

    [Test]
    public async Task CanManageAppConfigAsync_UserLacksManageAppConfigPermission_ReturnsFalse()
    {
        var actorId = Id<ActorReference>.New();
        var userId = actorId.Rebind<User>();
        _userRepository.FindById = (_, _) => Task.FromResult<User?>(new User());
        _roleRepository.UserHasPermission = (_, _, _) => Task.FromResult(false);

        var result = await _adapter.CanManageAppConfigAsync(actorId);

        result.Should().BeFalse();
        _roleRepository.Calls
            .Where(call =>
                call.Method == nameof(IRoleRepository.UserHasPermissionAsync)
                && call.Argument is ValueTuple<Id<User>, string> arguments
                && arguments.Item1 == userId
                && arguments.Item2 == AuthConstants.Permissions.ManageAppConfig
                && call.CancellationToken == CancellationToken.None)
            .Should()
            .ContainSingle();
    }

    [Test]
    public async Task CanManageAppConfigAsync_UserHasManageAppConfigPermission_ReturnsTrue()
    {
        var actorId = Id<ActorReference>.New();
        var userId = actorId.Rebind<User>();
        _userRepository.FindById = (_, _) => Task.FromResult<User?>(new User());
        _roleRepository.UserHasPermission = (_, _, _) => Task.FromResult(true);

        var result = await _adapter.CanManageAppConfigAsync(actorId);

        result.Should().BeTrue();
        _roleRepository.Calls
            .Where(call =>
                call.Method == nameof(IRoleRepository.UserHasPermissionAsync)
                && call.Argument is ValueTuple<Id<User>, string> arguments
                && arguments.Item1 == userId
                && arguments.Item2 == AuthConstants.Permissions.ManageAppConfig
                && call.CancellationToken == CancellationToken.None)
            .Should()
            .ContainSingle();
    }

    [Test]
    public async Task CanManageAppConfigAsync_AllowedUser_ForwardsCancellationTokenToBothRepositories()
    {
        var actorId = Id<ActorReference>.New();
        var userId = actorId.Rebind<User>();
        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        cancellationTokenSource.Cancel();
        _userRepository.FindById = (_, _) => Task.FromResult<User?>(new User());
        _roleRepository.UserHasPermission = (_, _, _) => Task.FromResult(true);

        var result = await _adapter.CanManageAppConfigAsync(actorId, cancellationToken);

        result.Should().BeTrue();
        _userRepository.Calls
            .Where(call =>
                call.Method == nameof(IUserRepository.FindByIdAsync)
                && call.Argument is Id<User> id
                && id == userId
                && call.CancellationToken == cancellationToken)
            .Should()
            .ContainSingle();
        _roleRepository.Calls
            .Where(call =>
                call.Method == nameof(IRoleRepository.UserHasPermissionAsync)
                && call.Argument is ValueTuple<Id<User>, string> arguments
                && arguments.Item1 == userId
                && arguments.Item2 == AuthConstants.Permissions.ManageAppConfig
                && call.CancellationToken == cancellationToken)
            .Should()
            .ContainSingle();
    }

    [Test]
    public async Task CanManageAppConfigAsync_UserRepositoryThrows_PropagatesException()
    {
        var expectedException = new InvalidOperationException("User lookup failed.");
        _userRepository.FindById = (_, _) => Task.FromException<User?>(expectedException);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _adapter.CanManageAppConfigAsync(Id<ActorReference>.New()));

        exception.Should().BeSameAs(expectedException);
        _roleRepository.Calls.Should().NotContain(call => call.Method == nameof(IRoleRepository.UserHasPermissionAsync));
    }

    [Test]
    public async Task CanManageAppConfigAsync_RoleRepositoryThrows_PropagatesException()
    {
        var actorId = Id<ActorReference>.New();
        var userId = actorId.Rebind<User>();
        var expectedException = new InvalidOperationException("Permission lookup failed.");
        _userRepository.FindById = (_, _) => Task.FromResult<User?>(new User());
        _roleRepository.UserHasPermission = (_, _, _) => Task.FromException<bool>(expectedException);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _adapter.CanManageAppConfigAsync(actorId));

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
        var actorId = Id<ActorReference>.New();
        var userId = actorId.Rebind<User>();
        _userRepository.FindById = (_, _) => Task.FromResult<User?>(new User());
        _roleRepository.UserHasPermission = (_, _, _) => Task.FromResult(true);

        var result = await port.CanManageAppConfigAsync(actorId);

        result.Should().BeTrue();

        _roleRepository.UserHasPermission = (_, _, _) => Task.FromResult(false);

        var denied = await port.CanManageAppConfigAsync(actorId);

        denied.Should().BeFalse();

        var expectedException = new InvalidOperationException("Permission lookup failed.");
        _roleRepository.UserHasPermission = (_, _, _) => Task.FromException<bool>(expectedException);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await port.CanManageAppConfigAsync(actorId));

        exception.Should().BeSameAs(expectedException);
    }
}
