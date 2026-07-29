using FluentAssertions;
using LgymApi.Application.Identity.Contracts.Sessions;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Adapters;
using LgymApi.Application.Notifications.Repositories;
using LgymApi.Application.Repositories;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using LgymApi.UnitTests.Fakes;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PushInstallationSessionDisassociationAdapterTests
{
    [Test]
    public async Task StageDisassociateAsync_ForwardsTheSessionExactlyOnceWithoutCommitting()
    {
        var lifecycleService = Substitute.For<IPushInstallationLifecycleService>();
        var adapter = new PushInstallationSessionDisassociationAdapter(lifecycleService);
        var accountId = Id<AccountReference>.New();
        var sessionId = Id<AccountSessionReference>.New();
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;

        await adapter.StageDisassociateAsync(accountId, sessionId, cancellationToken);

        await lifecycleService.Received(1).StageDisassociateForSessionAsync(
            sessionId,
            cancellationToken);
    }

    [Test]
    public async Task AddNotificationsModule_RegistersAccountSessionDisassociationPortExactlyOnceAsScoped()
    {
        var services = new ServiceCollection();
        var pushInstallationRepository = new ConfigurablePushInstallationRepository();

        services.AddNotificationsModule();

        var productionRepositoryRegistration = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPushInstallationRepository))
            .Should()
            .ContainSingle()
            .Which;
        productionRepositoryRegistration.Lifetime.Should().Be(ServiceLifetime.Scoped);
        productionRepositoryRegistration.ImplementationType!.Name.Should().Be("PushInstallationRepository");

        services.AddScoped<IPushInstallationRepository>(_ => pushInstallationRepository);
        services.AddScoped(_ => Substitute.For<IUnitOfWork>());

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(IAccountSessionDisassociationPort))
            .ToArray();

        registrations.Should().ContainSingle();
        registrations[0].Lifetime.Should().Be(ServiceLifetime.Scoped);
        registrations[0].ImplementationType.Should().Be(typeof(PushInstallationSessionDisassociationAdapter));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IAccountSessionDisassociationPort>();
        adapter
            .Should()
            .BeOfType<PushInstallationSessionDisassociationAdapter>();

        var accountId = Id<AccountReference>.New();
        var sessionId = Id<AccountSessionReference>.New();
        using var cancellationSource = new CancellationTokenSource();

        await adapter.StageDisassociateAsync(accountId, sessionId, cancellationSource.Token);

        pushInstallationRepository.Calls
            .Where(call =>
                call.Method == nameof(IPushInstallationRepository.DisassociateForSessionAsync)
                && call.Argument is ValueTuple<Id<AccountSessionReference>, DateTimeOffset> arguments
                && arguments.Item1 == sessionId
                && call.CancellationToken == cancellationSource.Token)
            .Should()
            .ContainSingle();
    }
}
