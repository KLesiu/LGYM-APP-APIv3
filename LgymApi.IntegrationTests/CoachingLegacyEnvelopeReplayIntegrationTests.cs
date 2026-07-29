using FluentAssertions;
using LgymApi.BackgroundWorker;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
[Category("PostgreSql")]
public sealed class CoachingLegacyEnvelopeReplayIntegrationTests : PostgreSqlIntegrationTestBase
{
    [Test]
    public async Task CanonicalAndAliasReplays_ProduceOnlyTheEightLegacyChannelsOnce()
    {
        var fixture = await SeedReplayFixtureAsync();
        var replayed = CreateReplayCases(fixture).ToArray();

        await InsertEnvelopePairsAsync(replayed);
        await ProcessPendingCommandsAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var envelopes = await database.CommandEnvelopes
                .Include(envelope => envelope.ExecutionLogs)
                .ToListAsync();
            var emails = await database.NotificationMessages.ToListAsync();
            var inApp = await database.InAppNotifications.ToListAsync();
            var pushes = await database.PushNotificationMessages.ToListAsync();

            envelopes.Should().HaveCount(16);
            envelopes.Should().OnlyContain(envelope => envelope.Status == ActionExecutionStatus.Completed);
            envelopes.Should().OnlyContain(envelope => envelope.ExecutionLogs.Count(log =>
                log.ActionType == ActionExecutionLogType.HandlerExecution
                && log.Status == ActionExecutionStatus.Completed) == 1);

            emails.Should().HaveCount(3);
            emails.Should().OnlyContain(message => message.Channel == NotificationChannel.Email);
            emails.Select(message => message.Type.Value).Should().BeEquivalentTo([
                EmailNotificationTypes.TrainerInvitation.Value,
                EmailNotificationTypes.TrainerInvitationAccepted.Value,
                EmailNotificationTypes.TrainerInvitationRevoked.Value
            ]);
            emails.Select(message => message.CorrelationId).Should().BeEquivalentTo([
                fixture.CreatedInvitationId.Rebind<CorrelationScope>(),
                fixture.AcceptedInvitationId.Rebind<CorrelationScope>(),
                fixture.RevokedInvitationId.Rebind<CorrelationScope>()
            ]);

            inApp.Should().HaveCount(5);
            inApp.Select(notification => notification.DeliveryKey).Should().BeEquivalentTo([
                $"trainer-invitation:{fixture.CreatedInvitationId}:sent",
                $"trainer-invitation:{fixture.AcceptedInvitationId}:accepted",
                $"trainer-invitation:{fixture.RejectedInvitationId}:rejected",
                $"trainer-relationship-ended:{fixture.TrainerId}:{fixture.TraineeId}",
                $"trainee-note:{fixture.NoteId}:{fixture.NoteTriggeredAt:O}"
            ]);
            pushes.Should().HaveCount(5, "each newly-created in-app notification creates one durable push intent");

            fixture.NullTitlePayload.Should().NotContain("\"noteTitle\"");
            envelopes.Where(envelope => envelope.PayloadJson == fixture.NullTitlePayload)
                .Should().HaveCount(2)
                .And.OnlyContain(envelope => envelope.PayloadJson == fixture.NullTitlePayload);
        }

        foreach (var notificationId in await GetEmailNotificationIdsAsync())
        {
            await ProcessEmailAsync(notificationId);
            await ProcessEmailAsync(notificationId);
        }

        Factory.EmailSender.SentMessages.Should().HaveCount(3);
    }

    [Test]
    public async Task InvalidAliasAndPayload_AreDeadLetteredWithoutCreatingNotifications()
    {
        await InsertEnvelopesAsync([
            Envelope("LgymApi.Application.Coaching.Contracts.BackgroundCommands.UnknownCommand", "{}"),
            Envelope("LgymApi.BackgroundWorker.Common.Commands.InvitationCreatedCommand", "{")
        ]);

        await ProcessPendingCommandsAsync();

        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var envelopes = await database.CommandEnvelopes.ToListAsync();

        envelopes.Should().OnlyContain(envelope => envelope.Status == ActionExecutionStatus.DeadLettered);
        (await database.NotificationMessages.CountAsync()).Should().Be(0);
        (await database.InAppNotifications.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task MissingInvitationFact_CompletesReplayWithoutSchedulingAnEmail()
    {
        var payload = $"{{\"invitationId\":\"{Id<TrainerInvitation>.New()}\"}}";
        await InsertEnvelopePairsAsync([
            new ReplayCase(
                "LgymApi.BackgroundWorker.Common.Commands.InvitationCreatedCommand",
                "LgymApi.Application.Coaching.Contracts.BackgroundCommands.InvitationCreatedCommand",
                payload)
        ]);

        await ProcessPendingCommandsAsync();

        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await database.CommandEnvelopes.ToListAsync()).Should().OnlyContain(envelope =>
            envelope.Status == ActionExecutionStatus.Completed);
        (await database.NotificationMessages.CountAsync()).Should().Be(0);
    }

    private async Task<ReplayFixture> SeedReplayFixtureAsync()
    {
        var trainer = await SeedUserAsync("legacy-replay-trainer", "legacy-replay-trainer@example.test", "password123");
        var trainee = await SeedUserAsync("legacy-replay-trainee", "legacy-replay-trainee@example.test", "password123");
        var createdInvitationId = Id<TrainerInvitation>.New();
        var acceptedInvitationId = Id<TrainerInvitation>.New();
        var revokedInvitationId = Id<TrainerInvitation>.New();
        var rejectedInvitationId = Id<TrainerInvitation>.New();
        var noteId = Id<TraineeNote>.New();
        var noteTriggeredAt = new DateTimeOffset(2026, 7, 23, 12, 34, 56, TimeSpan.Zero);

        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.TrainerInvitations.AddRange(
            Invitation(createdInvitationId, trainer.Id, trainee.Id, trainee.Email.Value, TrainerInvitationStatus.Pending),
            Invitation(acceptedInvitationId, trainer.Id, trainee.Id, trainee.Email.Value, TrainerInvitationStatus.Accepted),
            Invitation(revokedInvitationId, trainer.Id, trainee.Id, trainee.Email.Value, TrainerInvitationStatus.Revoked),
            Invitation(rejectedInvitationId, trainer.Id, trainee.Id, trainee.Email.Value, TrainerInvitationStatus.Rejected));
        database.TraineeNotes.Add(new TraineeNote
        {
            Id = noteId,
            TrainerId = trainer.Id,
            TraineeId = trainee.Id,
            Content = "Replay note",
            VisibleToTrainee = true,
            LastUpdatedByUserId = trainer.Id,
            LastUpdatedAt = noteTriggeredAt
        });
        database.PushInstallations.AddRange(
            Installation(trainer.Id, "legacy-replay-trainer-device"),
            Installation(trainee.Id, "legacy-replay-trainee-device"));
        await database.SaveChangesAsync();

        var nullTitlePayload = $"{{\"traineeNoteId\":\"{noteId}\",\"traineeId\":\"{trainee.Id}\",\"trainerId\":\"{trainer.Id}\",\"triggeredAt\":\"{noteTriggeredAt:O}\"}}";
        return new ReplayFixture(
            trainer.Id,
            trainee.Id,
            createdInvitationId,
            acceptedInvitationId,
            revokedInvitationId,
            rejectedInvitationId,
            noteId,
            noteTriggeredAt,
            nullTitlePayload);
    }

    private static IEnumerable<ReplayCase> CreateReplayCases(ReplayFixture fixture)
    {
        yield return InvitationCase("InvitationCreatedCommand", fixture.CreatedInvitationId);
        yield return InvitationCase("InvitationAcceptedCommand", fixture.AcceptedInvitationId);
        yield return InvitationCase("InvitationRevokedCommand", fixture.RevokedInvitationId);
        yield return InvitationCase("TrainerInvitationCreatedInAppNotificationCommand", fixture.CreatedInvitationId,
            $"\"traineeId\":\"{fixture.TraineeId}\",\"trainerId\":\"{fixture.TrainerId}\"");
        yield return InvitationCase("TrainerInvitationAcceptedInAppNotificationCommand", fixture.AcceptedInvitationId,
            $"\"trainerId\":\"{fixture.TrainerId}\",\"traineeId\":\"{fixture.TraineeId}\"");
        yield return InvitationCase("TrainerInvitationRejectedInAppNotificationCommand", fixture.RejectedInvitationId,
            $"\"trainerId\":\"{fixture.TrainerId}\",\"traineeId\":\"{fixture.TraineeId}\"");
        yield return new ReplayCase(
            "LgymApi.BackgroundWorker.Common.Commands.TrainerRelationshipEndedInAppNotificationCommand",
            "LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerRelationshipEndedInAppNotificationCommand",
            $"{{\"trainerId\":\"{fixture.TrainerId}\",\"traineeId\":\"{fixture.TraineeId}\"}}");
        yield return new ReplayCase(
            "LgymApi.BackgroundWorker.Common.Commands.TraineeNoteUpdatedInAppNotificationCommand",
            "LgymApi.Application.Coaching.Contracts.BackgroundCommands.TraineeNoteUpdatedInAppNotificationCommand",
            fixture.NullTitlePayload);
    }

    private static ReplayCase InvitationCase(string commandName, Id<TrainerInvitation> invitationId, string? remainingProperties = null)
        => new(
            $"LgymApi.BackgroundWorker.Common.Commands.{commandName}",
            $"LgymApi.Application.Coaching.Contracts.BackgroundCommands.{commandName}",
            remainingProperties is null
                ? $"{{\"invitationId\":\"{invitationId}\"}}"
                : $"{{\"invitationId\":\"{invitationId}\",{remainingProperties}}}");

    private async Task InsertEnvelopePairsAsync(IEnumerable<ReplayCase> cases)
    {
        var envelopes = cases.SelectMany(replay => new[]
        {
            Envelope(replay.CanonicalId, replay.PayloadJson),
            Envelope(replay.ApplicationAlias, replay.PayloadJson)
        });
        await InsertEnvelopesAsync(envelopes);
    }

    private async Task InsertEnvelopesAsync(IEnumerable<CommandEnvelope> envelopes)
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.CommandEnvelopes.AddRange(envelopes);
        await database.SaveChangesAsync();
    }

    private async Task<Id<NotificationMessage>[]> GetEmailNotificationIdsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await database.NotificationMessages.Select(message => message.Id).ToArrayAsync();
    }

    private async Task ProcessEmailAsync(Id<NotificationMessage> notificationId)
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<LgymApi.BackgroundWorker.Common.Notifications.IEmailJobHandler>();
        await handler.ProcessAsync(notificationId.ToString());
    }

    private async Task ProcessPendingCommandsAsync()
    {
        for (var pass = 0; pass < 5; pass++)
        {
            using var scope = Factory.Services.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var envelopeIds = await database.CommandEnvelopes
                .Where(envelope => envelope.Status == ActionExecutionStatus.Pending
                    || envelope.Status == ActionExecutionStatus.Failed)
                .OrderBy(envelope => envelope.CreatedAt)
                .Select(envelope => envelope.Id)
                .ToListAsync();

            if (envelopeIds.Count == 0)
            {
                return;
            }

            var orchestrator = scope.ServiceProvider.GetRequiredService<BackgroundActionOrchestratorService>();
            foreach (var envelopeId in envelopeIds)
            {
                try
                {
                    await orchestrator.OrchestrateAsync(envelopeId.ToString());
                }
                catch
                {
                }
            }
        }
    }

    private static CommandEnvelope Envelope(string commandTypeFullName, string payloadJson) => new()
    {
        Id = Id<CommandEnvelope>.New(),
        CorrelationId = Id<CorrelationScope>.New(),
        CommandTypeFullName = commandTypeFullName,
        PayloadJson = payloadJson,
        Status = ActionExecutionStatus.Pending,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static TrainerInvitation Invitation(
        Id<TrainerInvitation> id,
        Id<User> trainerId,
        Id<User> traineeId,
        string inviteeEmail,
        TrainerInvitationStatus status) => new()
        {
            Id = id,
            TrainerId = trainerId,
            TraineeId = traineeId,
            InviteeEmail = inviteeEmail,
            Code = $"REPLAY{id:N}"[..16],
            Status = status,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

    private static PushInstallation Installation(Id<User> userId, string installationId) => new()
    {
        Id = Id<PushInstallation>.New(),
        UserId = userId,
        InstallationId = installationId,
        Platform = "android",
        FcmToken = $"token-{installationId}",
        Environment = "testing",
        PermissionStatus = "authorized",
        LastSeenAt = DateTimeOffset.UtcNow
    };

    private sealed record ReplayCase(string CanonicalId, string ApplicationAlias, string PayloadJson);

    private sealed record ReplayFixture(
        Id<User> TrainerId,
        Id<User> TraineeId,
        Id<TrainerInvitation> CreatedInvitationId,
        Id<TrainerInvitation> AcceptedInvitationId,
        Id<TrainerInvitation> RevokedInvitationId,
        Id<TrainerInvitation> RejectedInvitationId,
        Id<TraineeNote> NoteId,
        DateTimeOffset NoteTriggeredAt,
        string NullTitlePayload);
}
