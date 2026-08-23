using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.AdultConfirmation;
using LgymApi.TestUtils.Fakes;
using LgymApi.UnitTests.Fakes;
using Microsoft.Extensions.Options;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class AdultConfirmationServiceTests
{
    [Test]
    public async Task ConfirmAsync_FirstConfirmation_PersistsServerVersionAndTimestamp()
    {
        var user = CreateUser();
        var repository = new ConfigurableUserRepository
        {
            FindById = (_, _) => Task.FromResult<User?>(user)
        };
        var unitOfWork = new FakeUnitOfWork();
        var service = new AdultConfirmationService(
            repository,
            unitOfWork,
            Options.Create(new AgeGateOptions { ConfirmationVersion = "18plus-v1" }));

        var result = await service.ConfirmAsync(user.Id.Rebind<AccountReference>(), true);

        result.IsSuccess.Should().BeTrue();
        user.AdultConfirmedAt.Should().NotBeNull();
        user.AdultConfirmationVersion.Should().Be("18plus-v1");
        repository.Calls.Should().NotContain(call => call.Method == nameof(repository.UpdateAsync));
        unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task ConfirmAsync_RepeatedConfirmation_PreservesTimestampWithoutSavingAgain()
    {
        var originalTimestamp = DateTimeOffset.UtcNow.AddDays(-1);
        var user = CreateUser();
        user.AdultConfirmedAt = originalTimestamp;
        user.AdultConfirmationVersion = "18plus-v1";
        var repository = new ConfigurableUserRepository
        {
            FindById = (_, _) => Task.FromResult<User?>(user)
        };
        var unitOfWork = new FakeUnitOfWork();
        var service = new AdultConfirmationService(repository, unitOfWork, Options.Create(new AgeGateOptions()));

        var result = await service.ConfirmAsync(user.Id.Rebind<AccountReference>(), true);

        result.IsSuccess.Should().BeTrue();
        user.AdultConfirmedAt.Should().Be(originalTimestamp);
        unitOfWork.SaveChangesCalls.Should().Be(0);
    }

    private static User CreateUser() => new()
    {
        Id = Id<User>.New(),
        Name = "adult-confirmation-user",
        Email = new Email("adult-confirmation@example.com"),
        ProfileRank = "Junior 1",
        PreferredLanguage = "en-US",
        PreferredTimeZone = "UTC"
    };
}
