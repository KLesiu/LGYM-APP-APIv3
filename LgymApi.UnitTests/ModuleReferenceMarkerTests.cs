using FluentAssertions;
using System.Text.Json;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Notifications.Contracts;
using LgymApi.Platform.Contracts;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ModuleReferenceMarkerTests
{
    private sealed class SourceScope;

    private static Id<T> ParseTestId<T>(string uuid)
    {
        if (!Id<T>.TryParse(uuid, out Id<T> id))
        {
            throw new ArgumentException($"Invalid UUID: {uuid}", nameof(uuid));
        }

        return id;
    }

    [Test]
    public void Rebind_PreservesValueWithinEachApprovedMarkerScope()
    {
        Id<SourceScope> source = ParseTestId<SourceScope>("00000000-0000-0000-0000-000000000004");

        var actor = source.Rebind<ActorReference>();
        var account = source.Rebind<AccountReference>();
        var accountSession = source.Rebind<AccountSessionReference>();
        var role = source.Rebind<RoleReference>();
        var plan = source.Rebind<PlanReference>();
        var planDay = source.Rebind<PlanDayReference>();
        var planExercise = source.Rebind<PlanExerciseReference>();
        var notification = source.Rebind<NotificationReference>();
        var pushInstallation = source.Rebind<PushInstallationReference>();

        Assert.Multiple(() =>
        {
            actor.Should().Be(source.Rebind<ActorReference>());
            account.Should().Be(source.Rebind<AccountReference>());
            accountSession.Should().Be(source.Rebind<AccountSessionReference>());
            role.Should().Be(source.Rebind<RoleReference>());
            plan.Should().Be(source.Rebind<PlanReference>());
            planDay.Should().Be(source.Rebind<PlanDayReference>());
            planExercise.Should().Be(source.Rebind<PlanExerciseReference>());
            notification.Should().Be(source.Rebind<NotificationReference>());
            pushInstallation.Should().Be(source.Rebind<PushInstallationReference>());
            account.Equals((object)plan).Should().BeFalse();
        });
    }

    [Test]
    public void Rebind_PreservesTargetScopeHashAndJsonSerialization()
    {
        Id<SourceScope> source = ParseTestId<SourceScope>("00000000-0000-0000-0000-000000000005");
        Id<AccountReference> account = source.Rebind<AccountReference>();

        var json = JsonSerializer.Serialize(account, SharedSerializationOptions.Current);
        var roundTrip = JsonSerializer.Deserialize<Id<AccountReference>>(json, SharedSerializationOptions.Current);

        Assert.Multiple(() =>
        {
            account.GetHashCode().Should().Be(source.Rebind<AccountReference>().GetHashCode());
            json.Should().Be("\"00000000-0000-0000-0000-000000000005\"");
            roundTrip.Should().Be(account);
        });
    }
}
