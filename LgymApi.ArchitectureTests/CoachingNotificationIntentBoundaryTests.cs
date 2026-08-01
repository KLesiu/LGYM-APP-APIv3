using System.Reflection;
using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.Domain.Entities;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class CoachingNotificationIntentBoundaryTests
{
    private static readonly Type[] IntentTypes =
    [
        typeof(InvitationCreatedCoachingNotificationIntent),
        typeof(InvitationAcceptedCoachingNotificationIntent),
        typeof(InvitationRejectedCoachingNotificationIntent),
        typeof(InvitationRevokedCoachingNotificationIntent),
        typeof(RelationshipEndedCoachingNotificationIntent),
        typeof(TraineeNoteUpdatedCoachingNotificationIntent)
    ];

    [Test]
    public void CoachingNotificationIntents_ExposeExactlySixFactOnlyRecords()
    {
        var contractsAssembly = typeof(ICoachingNotificationIntentService).Assembly;
        var discovered = contractsAssembly.GetTypes()
            .Where(type => type.Namespace == typeof(CoachingNotificationIntent).Namespace)
            .Where(type => type.IsSealed && type.IsSubclassOf(typeof(CoachingNotificationIntent)))
            .OrderBy(type => type.Name)
            .ToArray();

        discovered.Should().BeEquivalentTo(IntentTypes);
        foreach (var intentType in IntentTypes)
        {
            intentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Should().NotContain(property => property.Name.Contains("Message", StringComparison.Ordinal));
            intentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Should().NotContain(property =>
                    property.Name.Contains("Token", StringComparison.Ordinal) ||
                    property.Name.Contains("Payload", StringComparison.Ordinal) ||
                    property.Name.Contains("Repository", StringComparison.Ordinal));
            intentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.PropertyType.FullName)
                .Should().NotContain(name => name!.Contains("BackgroundWorker", StringComparison.Ordinal) || name.Contains("Common", StringComparison.Ordinal));
            intentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.PropertyType)
                .Should().NotContain(type => type == typeof(NotificationMessage) || type == typeof(PushInstallation) || type == typeof(PushNotificationMessage));
            intentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.PropertyType)
                .Should().NotContain(type => type == typeof(User) || type == typeof(TrainerInvitation) || type == typeof(TraineeNote));
        }
    }

    [Test]
    public void CoachingNotificationReadContract_ExposesOnlyTypedInvitationFacts()
    {
        var method = typeof(ICoachingNotificationReadService).GetMethods(BindingFlags.Public | BindingFlags.Instance).Should().ContainSingle().Subject;

        method.ReturnType.FullName.Should().NotContain("BackgroundWorker");
        method.GetParameters().Select(parameter => parameter.ParameterType).Should().Contain(typeof(LgymApi.Domain.ValueObjects.Id<TrainerInvitation>));
        method.GetParameters().Select(parameter => parameter.ParameterType).Should().NotContain(typeof(TrainerInvitation));
    }
}
