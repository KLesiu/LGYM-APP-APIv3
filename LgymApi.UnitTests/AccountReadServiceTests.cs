using FluentAssertions;
using LgymApi.Identity;
using LgymApi.Application.Identity.Access;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Persistence;
using LgymApi.UnitTests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class AccountReadServiceTests
{
    private ConfigurableUserRepository _userRepository = null!;
    private AccountReadService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _userRepository = new ConfigurableUserRepository();
        _service = new AccountReadService(_userRepository, BuildMapper());
    }

    [Test]
    public void AccountReadMappingProfile_MapsEveryAccountFact()
    {
        var account = CreateUser();
        var mapper = BuildMapper();

        var result = mapper.Map<User, AccountReadModel>(account, mapper.CreateContext());

        result.Should().Be(new AccountReadModel(
            account.Id,
            account.Name,
            account.Email.Value,
            account.Avatar,
            account.PreferredLanguage,
            account.PreferredTimeZone));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsImmutableAccountFacts_WhenActiveAccountExists()
    {
        var account = CreateUser();
        _userRepository.FindById = (id, cancellationToken) => Task.FromResult<User?>(account);

        var result = await _service.GetByIdAsync(account.Id);

        result.Should().Be(new AccountReadModel(
            account.Id,
            account.Name,
            account.Email.Value,
            account.Avatar,
            account.PreferredLanguage,
            account.PreferredTimeZone));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenAccountIsMissing()
    {
        var accountId = Id<User>.New();
        _userRepository.FindById = (_, _) => Task.FromResult<User?>(null);

        var result = await _service.GetByIdAsync(accountId);

        result.Should().BeNull();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenAccountIsDeleted()
    {
        var account = CreateUser();
        account.IsDeleted = true;
        _userRepository.FindById = (_, _) => Task.FromResult<User?>(account);

        var result = await _service.GetByIdAsync(account.Id);

        result.Should().BeNull();
    }

    [Test]
    public async Task GetByEmailAsync_NormalizesEmailOnceBeforeLookup()
    {
        var account = CreateUser();
        _userRepository.FindByEmail = (_, _) => Task.FromResult<User?>(account);

        var result = await _service.GetByEmailAsync("  ACCOUNT@EXAMPLE.COM  ");

        result!.Email.Should().Be("account@example.com");
        _userRepository.Calls
            .Where(call =>
                call.Method == nameof(IUserRepository.FindByEmailAsync)
                && call.Argument is Email email
                && email.Value == "account@example.com"
                && call.CancellationToken == CancellationToken.None)
            .Should()
            .ContainSingle();
    }

    [Test]
    public async Task GetByEmailAsync_ReturnsNull_WhenAccountIsDeleted()
    {
        var account = CreateUser();
        account.IsDeleted = true;
        _userRepository.FindByEmail = (_, _) => Task.FromResult<User?>(account);

        var result = await _service.GetByEmailAsync(account.Email.Value);

        result.Should().BeNull();
    }

    [Test]
    public async Task GetByIdsAsync_UsesOneBatchReadAndPreservesOrderDuplicatesAndUnavailableAccountAbsence()
    {
        var firstAccount = CreateUser();
        var deletedAccount = CreateUser();
        deletedAccount.IsDeleted = true;
        var secondAccount = CreateUser();
        var missingAccountId = Id<User>.New();
        var accounts = new Dictionary<Id<User>, User>
        {
            [firstAccount.Id] = firstAccount,
            [deletedAccount.Id] = deletedAccount,
            [secondAccount.Id] = secondAccount
        };
        _userRepository.GetByIds = (_, _) => Task.FromResult(accounts.Values.ToList());

        var result = await _service.GetByIdsAsync([
            secondAccount.Id,
            missingAccountId,
            deletedAccount.Id,
            firstAccount.Id,
            secondAccount.Id
        ]);

        result.Select(account => account.Id).Should().Equal(
            secondAccount.Id,
            firstAccount.Id,
            secondAccount.Id);
        result.Select(account => account.Name).Should().Equal(
            secondAccount.Name,
            firstAccount.Name,
            secondAccount.Name);
        _userRepository.Calls
            .Where(call =>
                call.Method == nameof(IUserRepository.GetByIdsAsync)
                && call.Argument is IReadOnlyCollection<Id<User>> ids
                && ids.SequenceEqual(new[]
                {
                    secondAccount.Id,
                    missingAccountId,
                    deletedAccount.Id,
                    firstAccount.Id,
                    secondAccount.Id
                })
                && call.CancellationToken == CancellationToken.None)
            .Should()
            .ContainSingle();
        _userRepository.Calls.Should().NotContain(call => call.Method == nameof(IUserRepository.FindByIdAsync));
    }

    [Test]
    public async Task GetByIdsAsync_PropagatesCancellationToken()
    {
        var accountId = Id<User>.New();
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        cancellationSource.Cancel();
        _userRepository.GetByIds = (_, _) => Task.FromCanceled<List<User>>(cancellationToken);

        var action = async () => await _service.GetByIdsAsync([accountId], cancellationToken);

        await action.Should().ThrowAsync<TaskCanceledException>();
        _userRepository.Calls
            .Where(call =>
                call.Method == nameof(IUserRepository.GetByIdsAsync)
                && call.Argument is IReadOnlyCollection<Id<User>> ids
                && ids.SequenceEqual(new[] { accountId })
                && call.CancellationToken == cancellationToken)
            .Should()
            .ContainSingle();
    }

    [Test]
    public void AddIdentityModule_RegistersAccountReadServiceExactlyOnceAndResolvesIt()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
        services.AddIdentityModule();
        services.AddScoped<IUserRepository>(_ => new ConfigurableUserRepository());
        services.AddScoped<IIdentityPersistenceContext, IdentityPersistenceContextStub>();

        services.Count(descriptor => descriptor.ServiceType == typeof(IAccountReadService)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IIdentityPersistenceContext)).Should().Be(1);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IAccountReadService>()
            .Should()
            .ContainSingle()
            .Which
            .Should()
            .BeOfType<AccountReadService>();
        scope.ServiceProvider.GetRequiredService<IIdentityPersistenceContext>()
            .Should()
            .BeOfType<IdentityPersistenceContextStub>();
    }

    private static User CreateUser() => new()
    {
        Id = Id<User>.New(),
        Name = "Account",
        Email = "account@example.com",
        Avatar = "avatar.png",
        PreferredLanguage = "pl-PL",
        PreferredTimeZone = "Europe/Warsaw"
    };

    private static IMapper BuildMapper()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMapper>();
    }

    private sealed class IdentityPersistenceContextStub : IIdentityPersistenceContext
    {
        public DbSet<User> Users => null!;
        public DbSet<Role> Roles => null!;
        public DbSet<UserRole> UserRoles => null!;
        public DbSet<RoleClaim> RoleClaims => null!;
        public DbSet<PasswordResetToken> PasswordResetTokens => null!;
        public DbSet<UserExternalLogin> UserExternalLogins => null!;
        public DbSet<UserSession> UserSessions => null!;
        public DbSet<UserTutorialProgress> UserTutorialProgresses => null!;
        public DbSet<UserTutorialStepProgress> UserTutorialStepProgresses => null!;
        public string? ProviderName => null;
    }
}
