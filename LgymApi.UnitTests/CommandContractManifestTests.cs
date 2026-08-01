using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Identity.Contracts.BackgroundCommands;
using LgymApi.Application.Nutrition.Contracts.BackgroundCommands;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands;
using LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration;
using LgymApi.BackgroundWorker.Runtime;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using NUnit.Framework;
using System.Text.Json;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CommandContractManifestTests
{
    [Test]
    public void LegacyCommandManifest_ContainsExactlyFifteenCommands()
    {
        // Given
        var manifest = LegacyCommandContractManifest.All;

        // When
        var commandCount = manifest.Count;

        // Then
        commandCount.Should().Be(15);
    }

    [Test]
    public void LegacyCommandManifest_HasExactUniqueMembershipAliasesAndHandlerCardinality()
    {
        var action = () => LegacyCommandContractManifest.Validate(LegacyCommandContractManifest.All);

        action.Should().NotThrow();
        LegacyCommandContractManifest.All.Select(contract => contract.CommandType)
            .Should().Equal(LegacyCommandContractManifest.ExpectedRuntimeTypes);
        LegacyCommandContractManifest.All.Select(contract => contract.CanonicalId)
            .Should().OnlyHaveUniqueItems();
        LegacyCommandContractManifest.All.Select(contract => contract.FutureClrNameReadAlias)
            .Should().Equal(LegacyCommandContractManifest.ExpectedFutureClrNameReadAliases)
            .And.OnlyHaveUniqueItems();
        LegacyCommandContractManifest.All.Sum(contract => contract.HandlerTypeFullNames.Count)
            .Should().Be(16);
    }

    [TestCaseSource(nameof(InvalidManifestCases))]
    public void LegacyCommandManifest_RejectsInvalidCompatibilityFixture(
        IReadOnlyList<LegacyCommandContract> manifest,
        string expectedMessage)
    {
        var action = () => LegacyCommandContractManifest.Validate(manifest);

        action.Should().Throw<InvalidOperationException>().WithMessage(expectedMessage);
    }

    [TestCaseSource(typeof(LegacyCommandContractManifest), nameof(LegacyCommandContractManifest.CommandCases))]
    public void LegacyCommand_UsesItsFixedCanonicalId(LegacyCommandContract contract)
    {
        // Given
        var commandType = contract.CommandType;

        // When
        var persistedId = CommandContractRegistry.CreateDefault()
            .DescribeForWrite(commandType)
            .CanonicalId;

        // Then
        persistedId.Should().Be(contract.CanonicalId);
    }

    [TestCaseSource(typeof(LegacyCommandContractManifest), nameof(LegacyCommandContractManifest.CommandCases))]
    public void LegacyCommand_SerializesToItsGoldenPayload(LegacyCommandContract contract)
    {
        // Given
        var command = contract.Command;

        // When
        var payloadJson = JsonSerializer.Serialize(command, contract.CommandType, SharedSerializationOptions.Current);

        // Then
        payloadJson.Should().Be(contract.PayloadJson);
    }

    [Test]
    public void InvitationAcceptedCommand_UsesTheManualGoldenCorrelationInput()
    {
        // Given
        var contract = LegacyCommandContractManifest.InvitationAccepted;

        // When
        var correlationInput = $"{contract.CanonicalId}|{JsonSerializer.Serialize(contract.Command, contract.CommandType, SharedSerializationOptions.Current)}";

        // Then
        correlationInput.Should().Be("LgymApi.BackgroundWorker.Common.Commands.InvitationAcceptedCommand|{\"invitationId\":\"00000000-0000-0000-0000-000000000005\"}");
    }

    private static IEnumerable<TestCaseData> InvalidManifestCases()
    {
        var manifest = LegacyCommandContractManifest.All.ToArray();

        yield return new TestCaseData(
                manifest[..^1],
                "The durable command manifest must contain exactly 15 rows.")
            .SetName("Missing_command_row_is_rejected");

        yield return new TestCaseData(
                manifest.Append(manifest[0] with
                {
                    Name = "UnexpectedCommand",
                    CanonicalId = "Unexpected.Command",
                    FutureClrNameReadAlias = "Unexpected.Application.Command"
                }).ToArray(),
                "The durable command manifest must contain exactly 15 rows.")
            .SetName("Extra_command_row_is_rejected");

        yield return new TestCaseData(
                ReplaceLast(manifest, manifest[^1] with { CanonicalId = manifest[0].CanonicalId }),
                "Canonical command IDs must be unique.")
            .SetName("Duplicate_canonical_id_is_rejected");

        yield return new TestCaseData(
                ReplaceLast(manifest, manifest[^1] with { CanonicalId = "Unexpected.Canonical.Command" }),
                "Canonical command IDs must exactly match the fixed 15-command membership.")
            .SetName("Altered_canonical_id_is_rejected");

        yield return new TestCaseData(
                ReplaceLast(manifest, manifest[^1] with { CanonicalId = manifest[^1].FutureClrNameReadAlias }),
                "Canonical command IDs and future read aliases must not overlap.")
            .SetName("Alias_write_id_is_rejected");

        yield return new TestCaseData(
                ReplaceLast(manifest, manifest[^1] with { FutureClrNameReadAlias = manifest[0].FutureClrNameReadAlias }),
                "Future CLR-name read aliases must be unique.")
            .SetName("Duplicate_future_alias_is_rejected");

        yield return new TestCaseData(
                ReplaceLast(manifest, manifest[^1] with { FutureClrNameReadAlias = string.Empty }),
                "Future CLR-name read aliases must exactly match the fixed 15-command membership.")
            .SetName("Missing_future_alias_is_rejected");

        yield return new TestCaseData(
                ReplaceLast(manifest, manifest[^1] with { CommandType = manifest[0].CommandType }),
                "Runtime command types must be unique.")
            .SetName("Duplicate_runtime_type_is_rejected");

        yield return new TestCaseData(
                ReplaceLast(manifest, manifest[^1] with
                {
                    CommandType = typeof(UnexpectedCommand),
                    Command = new UnexpectedCommand(),
                    FutureClrNameReadAlias = typeof(UnexpectedCommand).FullName!
                }),
                "Runtime command types must exactly match the fixed 15-command membership.")
            .SetName("Altered_runtime_type_is_rejected");

        yield return new TestCaseData(
                ReplaceLast(manifest, manifest[^1] with { PayloadJson = "{\"unexpectedSchemaField\":true}" }),
                "Every command fixture must serialize to its fixed golden payload.")
            .SetName("Altered_payload_schema_is_rejected");

        yield return new TestCaseData(
                ReplaceLast(manifest, manifest[^1] with { HandlerTypeFullNames = [] }),
                "Every command except TrainingCompletedCommand must declare exactly one handler.")
            .SetName("Wrong_handler_cardinality_is_rejected");

        yield return new TestCaseData(
                ReplaceLast(manifest, manifest[^1] with { HandlerTypeFullNames = ["Unexpected.Handler"] }),
                "Handler metadata mismatch for 'LgymApi.BackgroundWorker.Common.Commands.TrainerRelationshipEndedInAppNotificationCommand'. Expected [LgymApi.BackgroundWorker.Actions.TrainerRelationshipEndedInAppNotificationCommandHandler]; actual [Unexpected.Handler].")
            .SetName("Same_cardinality_wrong_handler_name_is_rejected");
    }

    private static LegacyCommandContract[] ReplaceLast(
        IReadOnlyList<LegacyCommandContract> manifest,
        LegacyCommandContract replacement) =>
        manifest.Take(manifest.Count - 1).Append(replacement).ToArray();

    private sealed class UnexpectedCommand : IActionCommand;
}

public static class LegacyCommandContractManifest
{
    public static IReadOnlyList<string> ExpectedCanonicalIds { get; } =
    [
        "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
        "LgymApi.BackgroundWorker.Common.Commands.TrainingCompletedCommand",
        "LgymApi.BackgroundWorker.Common.Commands.InvitationCreatedCommand",
        "LgymApi.BackgroundWorker.Common.Commands.InvitationAcceptedCommand",
        "LgymApi.BackgroundWorker.Common.Commands.InvitationRevokedCommand",
        "LgymApi.BackgroundWorker.Common.Commands.DietPlanUpdatedInAppNotificationCommand",
        "LgymApi.BackgroundWorker.Common.Commands.TraineeNoteUpdatedInAppNotificationCommand",
        "LgymApi.BackgroundWorker.Common.Commands.ReportSubmissionCreatedInAppNotificationCommand",
        "LgymApi.BackgroundWorker.Common.Commands.ReportSubmissionAcceptedProgressCommand",
        "LgymApi.BackgroundWorker.Common.Commands.ReportRequestCreatedInAppNotificationCommand",
        "LgymApi.BackgroundWorker.Common.Commands.ReportFeedbackAddedInAppNotificationCommand",
        "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationAcceptedInAppNotificationCommand",
        "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationCreatedInAppNotificationCommand",
        "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationRejectedInAppNotificationCommand",
        "LgymApi.BackgroundWorker.Common.Commands.TrainerRelationshipEndedInAppNotificationCommand"
    ];

    public static IReadOnlyList<Type> ExpectedRuntimeTypes { get; } =
    [
        typeof(UserRegisteredCommand),
        typeof(TrainingCompletedCommand),
        typeof(InvitationCreatedCommand),
        typeof(InvitationAcceptedCommand),
        typeof(InvitationRevokedCommand),
        typeof(DietPlanUpdatedInAppNotificationCommand),
        typeof(TraineeNoteUpdatedInAppNotificationCommand),
        typeof(ReportSubmissionCreatedInAppNotificationCommand),
        typeof(ReportSubmissionAcceptedProgressCommand),
        typeof(ReportRequestCreatedInAppNotificationCommand),
        typeof(ReportFeedbackAddedInAppNotificationCommand),
        typeof(TrainerInvitationAcceptedInAppNotificationCommand),
        typeof(TrainerInvitationCreatedInAppNotificationCommand),
        typeof(TrainerInvitationRejectedInAppNotificationCommand),
        typeof(TrainerRelationshipEndedInAppNotificationCommand)
    ];

    public static IReadOnlyList<string> ExpectedFutureClrNameReadAliases { get; } =
    [
        "LgymApi.Application.Identity.Contracts.BackgroundCommands.UserRegisteredCommand",
        "LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands.TrainingCompletedCommand",
        "LgymApi.Application.Coaching.Contracts.BackgroundCommands.InvitationCreatedCommand",
        "LgymApi.Application.Coaching.Contracts.BackgroundCommands.InvitationAcceptedCommand",
        "LgymApi.Application.Coaching.Contracts.BackgroundCommands.InvitationRevokedCommand",
        "LgymApi.Application.Nutrition.Contracts.BackgroundCommands.DietPlanUpdatedInAppNotificationCommand",
        "LgymApi.Application.Coaching.Contracts.BackgroundCommands.TraineeNoteUpdatedInAppNotificationCommand",
        "LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportSubmissionCreatedInAppNotificationCommand",
        "LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportSubmissionAcceptedProgressCommand",
        "LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportRequestCreatedInAppNotificationCommand",
        "LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportFeedbackAddedInAppNotificationCommand",
        "LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerInvitationAcceptedInAppNotificationCommand",
        "LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerInvitationCreatedInAppNotificationCommand",
        "LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerInvitationRejectedInAppNotificationCommand",
        "LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerRelationshipEndedInAppNotificationCommand"
    ];

    public static IReadOnlyDictionary<Type, IReadOnlyList<string>> ExpectedHandlersByRuntimeType { get; } =
        new Dictionary<Type, IReadOnlyList<string>>
        {
            [typeof(UserRegisteredCommand)] = ["LgymApi.BackgroundWorker.Actions.SendRegistrationEmailHandler"],
            [typeof(TrainingCompletedCommand)] =
            [
                "LgymApi.BackgroundWorker.Actions.TrainingCompletedEmailCommandHandler",
                "LgymApi.BackgroundWorker.Actions.UpdateTrainingMainRecordsHandler"
            ],
            [typeof(InvitationCreatedCommand)] = ["LgymApi.BackgroundWorker.Actions.SendInvitationEmailHandler"],
            [typeof(InvitationAcceptedCommand)] = ["LgymApi.BackgroundWorker.Actions.InvitationAcceptedEmailHandler"],
            [typeof(InvitationRevokedCommand)] = ["LgymApi.BackgroundWorker.Actions.InvitationRevokedEmailHandler"],
            [typeof(DietPlanUpdatedInAppNotificationCommand)] = ["LgymApi.BackgroundWorker.Actions.DietPlanUpdatedInAppNotificationCommandHandler"],
            [typeof(TraineeNoteUpdatedInAppNotificationCommand)] = ["LgymApi.BackgroundWorker.Actions.TraineeNoteUpdatedInAppNotificationCommandHandler"],
            [typeof(ReportSubmissionCreatedInAppNotificationCommand)] = ["LgymApi.BackgroundWorker.Actions.ReportSubmissionCreatedInAppNotificationCommandHandler"],
            [typeof(ReportSubmissionAcceptedProgressCommand)] = ["LgymApi.BackgroundWorker.Actions.ReportSubmissionAcceptedProgressCommandHandler"],
            [typeof(ReportRequestCreatedInAppNotificationCommand)] = ["LgymApi.BackgroundWorker.Actions.ReportRequestCreatedInAppNotificationCommandHandler"],
            [typeof(ReportFeedbackAddedInAppNotificationCommand)] = ["LgymApi.BackgroundWorker.Actions.ReportFeedbackAddedInAppNotificationCommandHandler"],
            [typeof(TrainerInvitationAcceptedInAppNotificationCommand)] = ["LgymApi.BackgroundWorker.Actions.TrainerInvitationAcceptedInAppNotificationCommandHandler"],
            [typeof(TrainerInvitationCreatedInAppNotificationCommand)] = ["LgymApi.BackgroundWorker.Actions.TrainerInvitationCreatedInAppNotificationCommandHandler"],
            [typeof(TrainerInvitationRejectedInAppNotificationCommand)] = ["LgymApi.BackgroundWorker.Actions.TrainerInvitationRejectedInAppNotificationCommandHandler"],
            [typeof(TrainerRelationshipEndedInAppNotificationCommand)] = ["LgymApi.BackgroundWorker.Actions.TrainerRelationshipEndedInAppNotificationCommandHandler"]
        };

    public static IReadOnlyList<LegacyCommandContract> All { get; } =
    [
        new(
            "UserRegistered",
            typeof(UserRegisteredCommand),
            new UserRegisteredCommand { UserId = ParseId<User>("00000000-0000-0000-0000-000000000001") },
            "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            "LgymApi.Application.Identity.Contracts.BackgroundCommands.UserRegisteredCommand",
            "{\"userId\":\"00000000-0000-0000-0000-000000000001\"}",
            "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand|{\"userId\":\"00000000-0000-0000-0000-000000000001\"}",
            "18093edd-559e-65ad-14dd-7f51988efa23",
            ["LgymApi.BackgroundWorker.Actions.SendRegistrationEmailHandler"]),
        new(
            "TrainingCompleted",
            typeof(TrainingCompletedCommand),
            new TrainingCompletedCommand
            {
                UserId = ParseId<User>("00000000-0000-0000-0000-000000000002"),
                TrainingId = ParseId<Training>("00000000-0000-0000-0000-000000000003")
            },
            "LgymApi.BackgroundWorker.Common.Commands.TrainingCompletedCommand",
            "LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands.TrainingCompletedCommand",
            "{\"userId\":\"00000000-0000-0000-0000-000000000002\",\"trainingId\":\"00000000-0000-0000-0000-000000000003\"}",
            "LgymApi.BackgroundWorker.Common.Commands.TrainingCompletedCommand|{\"userId\":\"00000000-0000-0000-0000-000000000002\",\"trainingId\":\"00000000-0000-0000-0000-000000000003\"}",
            "47796be5-388e-c49c-0188-e245ea4ce8fb",
            [
                "LgymApi.BackgroundWorker.Actions.TrainingCompletedEmailCommandHandler",
                "LgymApi.BackgroundWorker.Actions.UpdateTrainingMainRecordsHandler"
            ]),
        new(
            "InvitationCreated",
            typeof(InvitationCreatedCommand),
            new InvitationCreatedCommand { InvitationId = ParseId<TrainerInvitation>("00000000-0000-0000-0000-000000000004") },
            "LgymApi.BackgroundWorker.Common.Commands.InvitationCreatedCommand",
            "LgymApi.Application.Coaching.Contracts.BackgroundCommands.InvitationCreatedCommand",
            "{\"invitationId\":\"00000000-0000-0000-0000-000000000004\"}",
            "LgymApi.BackgroundWorker.Common.Commands.InvitationCreatedCommand|{\"invitationId\":\"00000000-0000-0000-0000-000000000004\"}",
            "ef847f37-79bc-7d75-d82b-f540115428fe",
            ["LgymApi.BackgroundWorker.Actions.SendInvitationEmailHandler"]),
        new(
            "InvitationAccepted",
            typeof(InvitationAcceptedCommand),
            new InvitationAcceptedCommand { InvitationId = ParseId<TrainerInvitation>("00000000-0000-0000-0000-000000000005") },
            "LgymApi.BackgroundWorker.Common.Commands.InvitationAcceptedCommand",
            "LgymApi.Application.Coaching.Contracts.BackgroundCommands.InvitationAcceptedCommand",
            "{\"invitationId\":\"00000000-0000-0000-0000-000000000005\"}",
            "LgymApi.BackgroundWorker.Common.Commands.InvitationAcceptedCommand|{\"invitationId\":\"00000000-0000-0000-0000-000000000005\"}",
            "d8e6ec90-24c3-06e8-9fe7-17f9da4a3fd6",
            ["LgymApi.BackgroundWorker.Actions.InvitationAcceptedEmailHandler"]),
        new(
            "InvitationRevoked",
            typeof(InvitationRevokedCommand),
            new InvitationRevokedCommand { InvitationId = ParseId<TrainerInvitation>("00000000-0000-0000-0000-000000000006") },
            "LgymApi.BackgroundWorker.Common.Commands.InvitationRevokedCommand",
            "LgymApi.Application.Coaching.Contracts.BackgroundCommands.InvitationRevokedCommand",
            "{\"invitationId\":\"00000000-0000-0000-0000-000000000006\"}",
            "LgymApi.BackgroundWorker.Common.Commands.InvitationRevokedCommand|{\"invitationId\":\"00000000-0000-0000-0000-000000000006\"}",
            "975467ee-65cd-a154-8c92-12f5acabe3be",
            ["LgymApi.BackgroundWorker.Actions.InvitationRevokedEmailHandler"]),
        new(
            "DietPlanUpdatedInAppNotification",
            typeof(DietPlanUpdatedInAppNotificationCommand),
            new DietPlanUpdatedInAppNotificationCommand
            {
                DietPlanId = ParseId<DietPlan>("00000000-0000-0000-0000-000000000007"),
                TraineeId = ParseId<User>("00000000-0000-0000-0000-000000000008"),
                TrainerId = ParseId<User>("00000000-0000-0000-0000-000000000009"),
                DietPlanName = "Strength cycle",
                TriggeredAt = new DateTimeOffset(2026, 7, 18, 12, 34, 56, TimeSpan.Zero)
            },
            "LgymApi.BackgroundWorker.Common.Commands.DietPlanUpdatedInAppNotificationCommand",
            "LgymApi.Application.Nutrition.Contracts.BackgroundCommands.DietPlanUpdatedInAppNotificationCommand",
            "{\"dietPlanId\":\"00000000-0000-0000-0000-000000000007\",\"traineeId\":\"00000000-0000-0000-0000-000000000008\",\"trainerId\":\"00000000-0000-0000-0000-000000000009\",\"dietPlanName\":\"Strength cycle\",\"triggeredAt\":\"2026-07-18T12:34:56+00:00\"}",
            "LgymApi.BackgroundWorker.Common.Commands.DietPlanUpdatedInAppNotificationCommand|{\"dietPlanId\":\"00000000-0000-0000-0000-000000000007\",\"traineeId\":\"00000000-0000-0000-0000-000000000008\",\"trainerId\":\"00000000-0000-0000-0000-000000000009\",\"dietPlanName\":\"Strength cycle\",\"triggeredAt\":\"2026-07-18T12:34:56+00:00\"}",
            "c4c06376-8bea-aee9-778f-d38ba32c64f4",
            ["LgymApi.BackgroundWorker.Actions.DietPlanUpdatedInAppNotificationCommandHandler"]),
        new(
            "TraineeNoteUpdatedInAppNotification",
            typeof(TraineeNoteUpdatedInAppNotificationCommand),
            new TraineeNoteUpdatedInAppNotificationCommand
            {
                TraineeNoteId = ParseId<TraineeNote>("00000000-0000-0000-0000-000000000010"),
                TraineeId = ParseId<User>("00000000-0000-0000-0000-000000000011"),
                TrainerId = ParseId<User>("00000000-0000-0000-0000-000000000012"),
                NoteTitle = "Weekly check-in",
                TriggeredAt = new DateTimeOffset(2026, 7, 18, 12, 34, 57, TimeSpan.Zero)
            },
            "LgymApi.BackgroundWorker.Common.Commands.TraineeNoteUpdatedInAppNotificationCommand",
            "LgymApi.Application.Coaching.Contracts.BackgroundCommands.TraineeNoteUpdatedInAppNotificationCommand",
            "{\"traineeNoteId\":\"00000000-0000-0000-0000-000000000010\",\"traineeId\":\"00000000-0000-0000-0000-000000000011\",\"trainerId\":\"00000000-0000-0000-0000-000000000012\",\"noteTitle\":\"Weekly check-in\",\"triggeredAt\":\"2026-07-18T12:34:57+00:00\"}",
            "LgymApi.BackgroundWorker.Common.Commands.TraineeNoteUpdatedInAppNotificationCommand|{\"traineeNoteId\":\"00000000-0000-0000-0000-000000000010\",\"traineeId\":\"00000000-0000-0000-0000-000000000011\",\"trainerId\":\"00000000-0000-0000-0000-000000000012\",\"noteTitle\":\"Weekly check-in\",\"triggeredAt\":\"2026-07-18T12:34:57+00:00\"}",
            "a8eacf8f-09dd-1bf9-5b82-ee58d15e40d2",
            ["LgymApi.BackgroundWorker.Actions.TraineeNoteUpdatedInAppNotificationCommandHandler"]),
        new(
            "ReportSubmissionCreatedInAppNotification",
            typeof(ReportSubmissionCreatedInAppNotificationCommand),
            new ReportSubmissionCreatedInAppNotificationCommand
            {
                SubmissionId = ParseId<ReportSubmission>("00000000-0000-0000-0000-000000000013"),
                TrainerId = ParseId<AccountReference>("00000000-0000-0000-0000-000000000014"),
                TraineeId = ParseId<AccountReference>("00000000-0000-0000-0000-000000000015"),
                TemplateName = "Progress review"
            },
            "LgymApi.BackgroundWorker.Common.Commands.ReportSubmissionCreatedInAppNotificationCommand",
            "LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportSubmissionCreatedInAppNotificationCommand",
            "{\"submissionId\":\"00000000-0000-0000-0000-000000000013\",\"trainerId\":\"00000000-0000-0000-0000-000000000014\",\"traineeId\":\"00000000-0000-0000-0000-000000000015\",\"templateName\":\"Progress review\"}",
            "LgymApi.BackgroundWorker.Common.Commands.ReportSubmissionCreatedInAppNotificationCommand|{\"submissionId\":\"00000000-0000-0000-0000-000000000013\",\"trainerId\":\"00000000-0000-0000-0000-000000000014\",\"traineeId\":\"00000000-0000-0000-0000-000000000015\",\"templateName\":\"Progress review\"}",
            "2b262cf9-4ff2-5b6f-b692-a9fb262b094c",
            ["LgymApi.BackgroundWorker.Actions.ReportSubmissionCreatedInAppNotificationCommandHandler"]),
        new(
            "ReportSubmissionAcceptedProgress",
            typeof(ReportSubmissionAcceptedProgressCommand),
            new ReportSubmissionAcceptedProgressCommand
            {
                Event = new ReportSubmissionAcceptedProgressPayload(
                    1,
                    "00000000-0000-0000-0000-000000000033",
                    "00000000-0000-0000-0000-000000000034",
                    "00000000-0000-0000-0000-000000000035",
                    "00000000-0000-0000-0000-000000000036",
                    ParseId<AccountReference>("00000000-0000-0000-0000-000000000037"),
                    new DateTimeOffset(2026, 7, 20, 8, 30, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 7, 20, 8, 31, 0, TimeSpan.Zero),
                    [new ReportSubmissionAcceptedProgressMeasurement(BodyParts.Chest, 101.5, MeasurementUnits.Centimeters)])
            },
            "LgymApi.BackgroundWorker.Common.Commands.ReportSubmissionAcceptedProgressCommand",
            "LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportSubmissionAcceptedProgressCommand",
            "{\"event\":{\"schemaVersion\":1,\"eventId\":\"00000000-0000-0000-0000-000000000033\",\"reportSubmissionId\":\"00000000-0000-0000-0000-000000000034\",\"correlationId\":\"00000000-0000-0000-0000-000000000035\",\"causationId\":\"00000000-0000-0000-0000-000000000036\",\"traineeId\":\"00000000-0000-0000-0000-000000000037\",\"observedAt\":\"2026-07-20T08:30:00+00:00\",\"acceptedAt\":\"2026-07-20T08:31:00+00:00\",\"measurements\":[{\"bodyPart\":\"Chest\",\"value\":101.5,\"unit\":\"Centimeters\"}]}}",
            "LgymApi.BackgroundWorker.Common.Commands.ReportSubmissionAcceptedProgressCommand|{\"event\":{\"schemaVersion\":1,\"eventId\":\"00000000-0000-0000-0000-000000000033\",\"reportSubmissionId\":\"00000000-0000-0000-0000-000000000034\",\"correlationId\":\"00000000-0000-0000-0000-000000000035\",\"causationId\":\"00000000-0000-0000-0000-000000000036\",\"traineeId\":\"00000000-0000-0000-0000-000000000037\",\"observedAt\":\"2026-07-20T08:30:00+00:00\",\"acceptedAt\":\"2026-07-20T08:31:00+00:00\",\"measurements\":[{\"bodyPart\":\"Chest\",\"value\":101.5,\"unit\":\"Centimeters\"}]}}",
            "0f45be81-891d-1caa-f843-d1f8308f2da7",
            ["LgymApi.BackgroundWorker.Actions.ReportSubmissionAcceptedProgressCommandHandler"]),
        new(
            "ReportRequestCreatedInAppNotification",
            typeof(ReportRequestCreatedInAppNotificationCommand),
            new ReportRequestCreatedInAppNotificationCommand
            {
                RequestId = ParseId<ReportRequest>("00000000-0000-0000-0000-000000000016"),
                TraineeId = ParseId<AccountReference>("00000000-0000-0000-0000-000000000017"),
                TrainerId = ParseId<AccountReference>("00000000-0000-0000-0000-000000000018"),
                TemplateName = "Weekly check-in"
            },
            "LgymApi.BackgroundWorker.Common.Commands.ReportRequestCreatedInAppNotificationCommand",
            "LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportRequestCreatedInAppNotificationCommand",
            "{\"requestId\":\"00000000-0000-0000-0000-000000000016\",\"traineeId\":\"00000000-0000-0000-0000-000000000017\",\"trainerId\":\"00000000-0000-0000-0000-000000000018\",\"templateName\":\"Weekly check-in\"}",
            "LgymApi.BackgroundWorker.Common.Commands.ReportRequestCreatedInAppNotificationCommand|{\"requestId\":\"00000000-0000-0000-0000-000000000016\",\"traineeId\":\"00000000-0000-0000-0000-000000000017\",\"trainerId\":\"00000000-0000-0000-0000-000000000018\",\"templateName\":\"Weekly check-in\"}",
            "f1848bbf-6a7c-e680-e74f-c0f7f71aaf92",
            ["LgymApi.BackgroundWorker.Actions.ReportRequestCreatedInAppNotificationCommandHandler"]),
        new(
            "ReportFeedbackAddedInAppNotification",
            typeof(ReportFeedbackAddedInAppNotificationCommand),
            new ReportFeedbackAddedInAppNotificationCommand
            {
                SubmissionId = ParseId<ReportSubmission>("00000000-0000-0000-0000-000000000019"),
                TraineeId = ParseId<AccountReference>("00000000-0000-0000-0000-000000000020"),
                TrainerId = ParseId<AccountReference>("00000000-0000-0000-0000-000000000021"),
                TemplateName = "Review notes",
                TriggeredAt = new DateTimeOffset(2026, 7, 18, 12, 34, 58, TimeSpan.Zero)
            },
            "LgymApi.BackgroundWorker.Common.Commands.ReportFeedbackAddedInAppNotificationCommand",
            "LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportFeedbackAddedInAppNotificationCommand",
            "{\"submissionId\":\"00000000-0000-0000-0000-000000000019\",\"traineeId\":\"00000000-0000-0000-0000-000000000020\",\"trainerId\":\"00000000-0000-0000-0000-000000000021\",\"templateName\":\"Review notes\",\"triggeredAt\":\"2026-07-18T12:34:58+00:00\"}",
            "LgymApi.BackgroundWorker.Common.Commands.ReportFeedbackAddedInAppNotificationCommand|{\"submissionId\":\"00000000-0000-0000-0000-000000000019\",\"traineeId\":\"00000000-0000-0000-0000-000000000020\",\"trainerId\":\"00000000-0000-0000-0000-000000000021\",\"templateName\":\"Review notes\",\"triggeredAt\":\"2026-07-18T12:34:58+00:00\"}",
            "e858765b-d2f3-054e-14aa-91deb66fc3fa",
            ["LgymApi.BackgroundWorker.Actions.ReportFeedbackAddedInAppNotificationCommandHandler"]),
        new(
            "TrainerInvitationAcceptedInAppNotification",
            typeof(TrainerInvitationAcceptedInAppNotificationCommand),
            new TrainerInvitationAcceptedInAppNotificationCommand
            {
                InvitationId = ParseId<TrainerInvitation>("00000000-0000-0000-0000-000000000022"),
                TrainerId = ParseId<User>("00000000-0000-0000-0000-000000000023"),
                TraineeId = ParseId<User>("00000000-0000-0000-0000-000000000024")
            },
            "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationAcceptedInAppNotificationCommand",
            "LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerInvitationAcceptedInAppNotificationCommand",
            "{\"invitationId\":\"00000000-0000-0000-0000-000000000022\",\"trainerId\":\"00000000-0000-0000-0000-000000000023\",\"traineeId\":\"00000000-0000-0000-0000-000000000024\"}",
            "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationAcceptedInAppNotificationCommand|{\"invitationId\":\"00000000-0000-0000-0000-000000000022\",\"trainerId\":\"00000000-0000-0000-0000-000000000023\",\"traineeId\":\"00000000-0000-0000-0000-000000000024\"}",
            "82a60d7e-c031-225f-4949-b891e8de1aa6",
            ["LgymApi.BackgroundWorker.Actions.TrainerInvitationAcceptedInAppNotificationCommandHandler"]),
        new(
            "TrainerInvitationCreatedInAppNotification",
            typeof(TrainerInvitationCreatedInAppNotificationCommand),
            new TrainerInvitationCreatedInAppNotificationCommand
            {
                InvitationId = ParseId<TrainerInvitation>("00000000-0000-0000-0000-000000000025"),
                TraineeId = ParseId<User>("00000000-0000-0000-0000-000000000026"),
                TrainerId = ParseId<User>("00000000-0000-0000-0000-000000000027")
            },
            "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationCreatedInAppNotificationCommand",
            "LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerInvitationCreatedInAppNotificationCommand",
            "{\"invitationId\":\"00000000-0000-0000-0000-000000000025\",\"traineeId\":\"00000000-0000-0000-0000-000000000026\",\"trainerId\":\"00000000-0000-0000-0000-000000000027\"}",
            "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationCreatedInAppNotificationCommand|{\"invitationId\":\"00000000-0000-0000-0000-000000000025\",\"traineeId\":\"00000000-0000-0000-0000-000000000026\",\"trainerId\":\"00000000-0000-0000-0000-000000000027\"}",
            "624ddee9-0f9c-5736-4de2-687b38cf96ab",
            ["LgymApi.BackgroundWorker.Actions.TrainerInvitationCreatedInAppNotificationCommandHandler"]),
        new(
            "TrainerInvitationRejectedInAppNotification",
            typeof(TrainerInvitationRejectedInAppNotificationCommand),
            new TrainerInvitationRejectedInAppNotificationCommand
            {
                InvitationId = ParseId<TrainerInvitation>("00000000-0000-0000-0000-000000000028"),
                TrainerId = ParseId<User>("00000000-0000-0000-0000-000000000029"),
                TraineeId = ParseId<User>("00000000-0000-0000-0000-000000000030")
            },
            "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationRejectedInAppNotificationCommand",
            "LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerInvitationRejectedInAppNotificationCommand",
            "{\"invitationId\":\"00000000-0000-0000-0000-000000000028\",\"trainerId\":\"00000000-0000-0000-0000-000000000029\",\"traineeId\":\"00000000-0000-0000-0000-000000000030\"}",
            "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationRejectedInAppNotificationCommand|{\"invitationId\":\"00000000-0000-0000-0000-000000000028\",\"trainerId\":\"00000000-0000-0000-0000-000000000029\",\"traineeId\":\"00000000-0000-0000-0000-000000000030\"}",
            "b99d923d-150d-34a8-a778-6aacbefdf2e8",
            ["LgymApi.BackgroundWorker.Actions.TrainerInvitationRejectedInAppNotificationCommandHandler"]),
        new(
            "TrainerRelationshipEndedInAppNotification",
            typeof(TrainerRelationshipEndedInAppNotificationCommand),
            new TrainerRelationshipEndedInAppNotificationCommand
            {
                TrainerId = ParseId<User>("00000000-0000-0000-0000-000000000031"),
                TraineeId = ParseId<User>("00000000-0000-0000-0000-000000000032")
            },
            "LgymApi.BackgroundWorker.Common.Commands.TrainerRelationshipEndedInAppNotificationCommand",
            "LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerRelationshipEndedInAppNotificationCommand",
            "{\"trainerId\":\"00000000-0000-0000-0000-000000000031\",\"traineeId\":\"00000000-0000-0000-0000-000000000032\"}",
            "LgymApi.BackgroundWorker.Common.Commands.TrainerRelationshipEndedInAppNotificationCommand|{\"trainerId\":\"00000000-0000-0000-0000-000000000031\",\"traineeId\":\"00000000-0000-0000-0000-000000000032\"}",
            "d17d13fc-534a-37a8-5f12-6eab966d8517",
            ["LgymApi.BackgroundWorker.Actions.TrainerRelationshipEndedInAppNotificationCommandHandler"])
    ];

    public static LegacyCommandContract InvitationAccepted => All.Single(contract => contract.Name == "InvitationAccepted");

    public static IEnumerable<TestCaseData> CommandCases =>
        All.Select(contract => new TestCaseData(contract).SetName($"{contract.Name}_LegacyContract"));

    public static void Validate(IReadOnlyList<LegacyCommandContract> manifest)
    {
        if (manifest.Count != 15)
        {
            throw new InvalidOperationException("The durable command manifest must contain exactly 15 rows.");
        }

        if (manifest.Select(contract => contract.CanonicalId).Distinct(StringComparer.Ordinal).Count() != manifest.Count)
        {
            throw new InvalidOperationException("Canonical command IDs must be unique.");
        }

        if (manifest.Select(contract => contract.FutureClrNameReadAlias).Distinct(StringComparer.Ordinal).Count() != manifest.Count)
        {
            throw new InvalidOperationException("Future CLR-name read aliases must be unique.");
        }

        if (manifest.Select(contract => contract.CanonicalId)
            .Intersect(manifest.Select(contract => contract.FutureClrNameReadAlias), StringComparer.Ordinal)
            .Any())
        {
            throw new InvalidOperationException("Canonical command IDs and future read aliases must not overlap.");
        }

        if (!manifest.Select(contract => contract.CanonicalId)
            .SequenceEqual(ExpectedCanonicalIds, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Canonical command IDs must exactly match the fixed 15-command membership.");
        }

        if (manifest.Select(contract => contract.CommandType).Distinct().Count() != manifest.Count)
        {
            throw new InvalidOperationException("Runtime command types must be unique.");
        }

        if (!manifest.Select(contract => contract.CommandType).ToHashSet().SetEquals(ExpectedRuntimeTypes))
        {
            throw new InvalidOperationException("Runtime command types must exactly match the fixed 15-command membership.");
        }


        if (!manifest.Select(contract => contract.FutureClrNameReadAlias)
            .SequenceEqual(ExpectedFutureClrNameReadAliases, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Future CLR-name read aliases must exactly match the fixed 15-command membership.");
        }

        if (manifest.Any(contract => contract.Command.GetType() != contract.CommandType))
        {
            throw new InvalidOperationException("Every command fixture must match its declared runtime type.");
        }

        if (manifest.Any(contract => !string.Equals(
                JsonSerializer.Serialize(contract.Command, contract.CommandType, SharedSerializationOptions.Current),
                contract.PayloadJson,
                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Every command fixture must serialize to its fixed golden payload.");
        }

        if (manifest.Any(contract =>
                contract.HandlerTypeFullNames.Count != (contract.CommandType == typeof(TrainingCompletedCommand) ? 2 : 1)))
        {
            throw new InvalidOperationException("Every command except TrainingCompletedCommand must declare exactly one handler.");
        }

        if (manifest.Sum(contract => contract.HandlerTypeFullNames.Count) != 16)
        {
            throw new InvalidOperationException("The durable command manifest must declare exactly 16 handlers.");
        }

        foreach (var contract in manifest)
        {
            var expectedHandlers = ExpectedHandlersByRuntimeType[contract.CommandType];
            if (!contract.HandlerTypeFullNames.SequenceEqual(expectedHandlers, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Handler metadata mismatch for '{contract.CanonicalId}'. "
                    + $"Expected [{string.Join(", ", expectedHandlers)}]; "
                    + $"actual [{string.Join(", ", contract.HandlerTypeFullNames)}].");
            }
        }
    }

    public static LegacyCommandContract ResolveForStagedReader(string persistedId) =>
        All.SingleOrDefault(contract =>
            string.Equals(contract.CanonicalId, persistedId, StringComparison.Ordinal)
            || string.Equals(contract.FutureClrNameReadAlias, persistedId, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Unknown durable command identifier '{persistedId}'.");

    public static Type ResolveForLegacyReader(string persistedId) =>
        All.SingleOrDefault(contract =>
            string.Equals(contract.CanonicalId, persistedId, StringComparison.Ordinal))?.CommandType
        ?? throw new InvalidOperationException($"Unknown durable command identifier '{persistedId}'.");

    public static string GetCanonicalWriteIdForStagedWriter(Type commandType) =>
        All.SingleOrDefault(contract => contract.CommandType == commandType)?.CanonicalId
        ?? throw new InvalidOperationException($"Runtime command type '{commandType}' is absent from the durable manifest.");

    public static bool IsCanonicalWriteId(string persistedId) =>
        All.Any(contract => string.Equals(contract.CanonicalId, persistedId, StringComparison.Ordinal));

    private static Id<TEntity> ParseId<TEntity>(string value)
    {
        if (!Id<TEntity>.TryParse(value, out var id))
        {
            throw new InvalidOperationException($"Invalid typed ID fixture '{value}'.");
        }

        return id;
    }
}

public sealed record LegacyCommandContract(
    string Name,
    Type CommandType,
    IActionCommand Command,
    string CanonicalId,
    string FutureClrNameReadAlias,
    string PayloadJson,
    string CorrelationInput,
    string CorrelationId,
    IReadOnlyList<string> HandlerTypeFullNames);
