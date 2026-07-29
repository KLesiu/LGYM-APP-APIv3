using FluentAssertions;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.BackgroundWorker.Runtime;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NUnit.Framework;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CoachingBackgroundCommandContractTests
{
    private const string CommandsNamespace = "LgymApi.Application.Coaching.Contracts.BackgroundCommands";

    [TestCaseSource(nameof(CommandCases))]
    public void Command_Has_Exact_Public_Surface_Typed_Ids_And_Golden_Payload(ExpectedCommand expected)
    {
        var commandType = GetCommandType(expected.TypeName);
        var properties = commandType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(property => property.MetadataToken)
            .ToArray();

        Assert.Multiple(() =>
        {
            commandType.Assembly.Should().BeSameAs(typeof(LgymApi.Application.ServiceCollectionExtensions).Assembly);
            commandType.Namespace.Should().Be(CommandsNamespace);
            commandType.IsPublic.Should().BeTrue();
            commandType.IsSealed.Should().BeTrue();
            commandType.GetInterfaces().Should().Equal(typeof(IActionCommand));
            commandType.GetConstructors().Should().ContainSingle(constructor => constructor.GetParameters().Length == 0);
            properties.Select(property => property.Name).Should().Equal(expected.Properties.Select(property => property.Name));
            commandType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Should().BeEmpty();
            commandType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Should().BeEmpty();
        });

        foreach (var expectedProperty in expected.Properties)
        {
            var property = properties.Single(candidate => candidate.Name == expectedProperty.Name);
            property.PropertyType.Should().Be(expectedProperty.Type);
            property.GetMethod!.IsPublic.Should().BeTrue();
            property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Should().Contain(typeof(IsExternalInit));
        }

        var command = JsonSerializer.Deserialize(expected.PayloadJson, commandType, SharedSerializationOptions.Current);

        command.Should().NotBeNull().And.BeOfType(commandType);
        JsonSerializer.Serialize(command, commandType, SharedSerializationOptions.Current).Should().Be(expected.PayloadJson);
    }

    [Test]
    public void TraineeNoteUpdatedInAppNotificationCommand_Preserves_Nullable_NoteTitle_And_Null_Omission()
    {
        var commandType = GetCommandType("TraineeNoteUpdatedInAppNotificationCommand");
        var noteTitle = commandType.GetProperty("NoteTitle", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
        var nullPayload = "{\"traineeNoteId\":\"00000000-0000-0000-0000-000000000010\",\"traineeId\":\"00000000-0000-0000-0000-000000000011\",\"trainerId\":\"00000000-0000-0000-0000-000000000012\",\"triggeredAt\":\"2026-07-18T12:34:57+00:00\"}";

        new NullabilityInfoContext().Create(noteTitle).WriteState.Should().Be(NullabilityState.Nullable);

        var command = JsonSerializer.Deserialize(nullPayload, commandType, SharedSerializationOptions.Current);

        command.Should().NotBeNull();
        noteTitle.GetValue(command).Should().BeNull();
        JsonSerializer.Serialize(command, commandType, SharedSerializationOptions.Current).Should().Be(nullPayload);
    }

    [Test]
    public void CoachingCommands_UseTheFixedLegacyDeliveryChannelMap()
    {
        var registry = CommandContractRegistry.CreateDefault();

        foreach (var expected in DeliveryChannelCases)
        {
            var commandType = GetCommandType(expected.TypeName);
            var contract = registry.Contracts.Single(candidate => candidate.RuntimeType == commandType);
            var handlerType = contract.ExpectedHandlerTypes.Should().ContainSingle().Subject;

            contract.CanonicalId.Should().Be(expected.CanonicalId);
            contract.ReadAlias.Should().Be($"{CommandsNamespace}.{expected.TypeName}");
            handlerType.FullName.Should().Be(expected.HandlerTypeFullName);
            ResolveDeliveryChannel(handlerType).Should().Be(expected.Channel);

            if (expected.EmailPayloadTypeFullName is not null)
            {
                var emailPayloadType = GetEmailPayloadType(handlerType);
                emailPayloadType.FullName.Should().Be(expected.EmailPayloadTypeFullName);
                var payload = (IEmailPayload)Activator.CreateInstance(emailPayloadType)!;
                payload.NotificationType.Value.Should().Be(expected.DeliveryKey);
            }
        }
    }

    private static IEnumerable<TestCaseData> CommandCases()
    {
        yield return Case("InvitationCreatedCommand", "{\"invitationId\":\"00000000-0000-0000-0000-000000000004\"}", new ExpectedProperty("InvitationId", typeof(Id<TrainerInvitation>)));
        yield return Case("InvitationAcceptedCommand", "{\"invitationId\":\"00000000-0000-0000-0000-000000000005\"}", new ExpectedProperty("InvitationId", typeof(Id<TrainerInvitation>)));
        yield return Case("InvitationRevokedCommand", "{\"invitationId\":\"00000000-0000-0000-0000-000000000006\"}", new ExpectedProperty("InvitationId", typeof(Id<TrainerInvitation>)));
        yield return Case("TrainerInvitationCreatedInAppNotificationCommand", "{\"invitationId\":\"00000000-0000-0000-0000-000000000025\",\"traineeId\":\"00000000-0000-0000-0000-000000000026\",\"trainerId\":\"00000000-0000-0000-0000-000000000027\"}", new ExpectedProperty("InvitationId", typeof(Id<TrainerInvitation>)), new ExpectedProperty("TraineeId", typeof(Id<User>)), new ExpectedProperty("TrainerId", typeof(Id<User>)));
        yield return Case("TrainerInvitationAcceptedInAppNotificationCommand", "{\"invitationId\":\"00000000-0000-0000-0000-000000000022\",\"trainerId\":\"00000000-0000-0000-0000-000000000023\",\"traineeId\":\"00000000-0000-0000-0000-000000000024\"}", new ExpectedProperty("InvitationId", typeof(Id<TrainerInvitation>)), new ExpectedProperty("TrainerId", typeof(Id<User>)), new ExpectedProperty("TraineeId", typeof(Id<User>)));
        yield return Case("TrainerInvitationRejectedInAppNotificationCommand", "{\"invitationId\":\"00000000-0000-0000-0000-000000000028\",\"trainerId\":\"00000000-0000-0000-0000-000000000029\",\"traineeId\":\"00000000-0000-0000-0000-000000000030\"}", new ExpectedProperty("InvitationId", typeof(Id<TrainerInvitation>)), new ExpectedProperty("TrainerId", typeof(Id<User>)), new ExpectedProperty("TraineeId", typeof(Id<User>)));
        yield return Case("TrainerRelationshipEndedInAppNotificationCommand", "{\"trainerId\":\"00000000-0000-0000-0000-000000000031\",\"traineeId\":\"00000000-0000-0000-0000-000000000032\"}", new ExpectedProperty("TrainerId", typeof(Id<User>)), new ExpectedProperty("TraineeId", typeof(Id<User>)));
        yield return Case("TraineeNoteUpdatedInAppNotificationCommand", "{\"traineeNoteId\":\"00000000-0000-0000-0000-000000000010\",\"traineeId\":\"00000000-0000-0000-0000-000000000011\",\"trainerId\":\"00000000-0000-0000-0000-000000000012\",\"noteTitle\":\"Weekly check-in\",\"triggeredAt\":\"2026-07-18T12:34:57+00:00\"}", new ExpectedProperty("TraineeNoteId", typeof(Id<TraineeNote>)), new ExpectedProperty("TraineeId", typeof(Id<User>)), new ExpectedProperty("TrainerId", typeof(Id<User>)), new ExpectedProperty("NoteTitle", typeof(string)), new ExpectedProperty("TriggeredAt", typeof(DateTimeOffset)));
    }

    private static readonly DeliveryChannelContract[] DeliveryChannelCases =
    [
        new("InvitationCreatedCommand", "LgymApi.BackgroundWorker.Common.Commands.InvitationCreatedCommand", "LgymApi.BackgroundWorker.Actions.SendInvitationEmailHandler", DeliveryChannel.Email, "LgymApi.BackgroundWorker.Common.Notifications.Models.InvitationEmailPayload", "trainer.invitation.created"),
        new("TrainerInvitationCreatedInAppNotificationCommand", "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationCreatedInAppNotificationCommand", "LgymApi.BackgroundWorker.Actions.TrainerInvitationCreatedInAppNotificationCommandHandler", DeliveryChannel.InApp, null, null),
        new("InvitationAcceptedCommand", "LgymApi.BackgroundWorker.Common.Commands.InvitationAcceptedCommand", "LgymApi.BackgroundWorker.Actions.InvitationAcceptedEmailHandler", DeliveryChannel.Email, "LgymApi.BackgroundWorker.Common.Notifications.Models.InvitationAcceptedEmailPayload", "trainer.invitation.accepted"),
        new("TrainerInvitationAcceptedInAppNotificationCommand", "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationAcceptedInAppNotificationCommand", "LgymApi.BackgroundWorker.Actions.TrainerInvitationAcceptedInAppNotificationCommandHandler", DeliveryChannel.InApp, null, null),
        new("InvitationRevokedCommand", "LgymApi.BackgroundWorker.Common.Commands.InvitationRevokedCommand", "LgymApi.BackgroundWorker.Actions.InvitationRevokedEmailHandler", DeliveryChannel.Email, "LgymApi.BackgroundWorker.Common.Notifications.Models.InvitationRevokedEmailPayload", "trainer.invitation.revoked"),
        new("TrainerInvitationRejectedInAppNotificationCommand", "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationRejectedInAppNotificationCommand", "LgymApi.BackgroundWorker.Actions.TrainerInvitationRejectedInAppNotificationCommandHandler", DeliveryChannel.InApp, null, null),
        new("TrainerRelationshipEndedInAppNotificationCommand", "LgymApi.BackgroundWorker.Common.Commands.TrainerRelationshipEndedInAppNotificationCommand", "LgymApi.BackgroundWorker.Actions.TrainerRelationshipEndedInAppNotificationCommandHandler", DeliveryChannel.InApp, null, null),
        new("TraineeNoteUpdatedInAppNotificationCommand", "LgymApi.BackgroundWorker.Common.Commands.TraineeNoteUpdatedInAppNotificationCommand", "LgymApi.BackgroundWorker.Actions.TraineeNoteUpdatedInAppNotificationCommandHandler", DeliveryChannel.InApp, null, null)
    ];

    private static DeliveryChannel ResolveDeliveryChannel(Type handlerType)
    {
        var constructorParameterTypes = handlerType.GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        if (constructorParameterTypes.Any(type => type.FullName is "LgymApi.Application.Notifications.Contracts.Events.ICoachingEmailNotificationScheduler"
            or "LgymApi.BackgroundWorker.Common.Notifications.IEmailScheduler`1"
            or "LgymApi.Application.Coaching.Contracts.Notifications.IInvitationCreatedEmailPreparationPort"
            or "LgymApi.Application.Coaching.Contracts.Notifications.IInvitationAcceptedEmailPreparationPort"
            or "LgymApi.Application.Coaching.Contracts.Notifications.IInvitationRevokedEmailPreparationPort"))
        {
            return DeliveryChannel.Email;
        }

        if (constructorParameterTypes.Any(type => type.FullName is "LgymApi.Application.Notifications.IInAppNotificationService"
            or "LgymApi.Application.Notifications.Contracts.Events.ICoachingNotificationIntentService"
            or "LgymApi.Application.Coaching.Contracts.Notifications.ITrainerInvitationAcceptedInAppPreparationPort"
            or "LgymApi.Application.Coaching.Contracts.Notifications.ITrainerInvitationRejectedInAppPreparationPort"
            or "LgymApi.Application.Coaching.Contracts.Notifications.ITrainerInvitationCreatedInAppPreparationPort"
            or "LgymApi.Application.Coaching.Contracts.Notifications.ITraineeNoteUpdatedInAppPreparationPort"
            or "LgymApi.Application.Coaching.Contracts.Notifications.IRelationshipEndedPreparationPort"))
        {
            return DeliveryChannel.InApp;
        }

        throw new InvalidOperationException($"Handler '{handlerType.FullName}' has no supported legacy delivery dependency.");
    }

    private static Type GetEmailPayloadType(Type handlerType) => handlerType.FullName switch
    {
        "LgymApi.BackgroundWorker.Actions.SendInvitationEmailHandler" => typeof(InvitationEmailPayload),
        "LgymApi.BackgroundWorker.Actions.InvitationAcceptedEmailHandler" => typeof(InvitationAcceptedEmailPayload),
        "LgymApi.BackgroundWorker.Actions.InvitationRevokedEmailHandler" => typeof(InvitationRevokedEmailPayload),
        _ => throw new InvalidOperationException($"Handler '{handlerType.FullName}' has no legacy email payload mapping.")
    };

    private static TestCaseData Case(string typeName, string payloadJson, params ExpectedProperty[] properties) =>
        new TestCaseData(new ExpectedCommand(typeName, payloadJson, properties)).SetName(typeName);

    private static Type GetCommandType(string typeName)
    {
        var commandType = typeof(LgymApi.Application.ServiceCollectionExtensions).Assembly.GetType($"{CommandsNamespace}.{typeName}");
        commandType.Should().NotBeNull($"{typeName} must be owned by the Coaching Application module");
        return commandType!;
    }

    public sealed record ExpectedCommand(string TypeName, string PayloadJson, ExpectedProperty[] Properties);

    public sealed record ExpectedProperty(string Name, Type Type);

    private sealed record DeliveryChannelContract(
        string TypeName,
        string CanonicalId,
        string HandlerTypeFullName,
        DeliveryChannel Channel,
        string? EmailPayloadTypeFullName,
        string? DeliveryKey);

    private enum DeliveryChannel
    {
        Email,
        InApp
    }
}
