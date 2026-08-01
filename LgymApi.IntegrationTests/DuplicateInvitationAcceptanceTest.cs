using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LgymApi.Application.Coaching.Invitations.Accept;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using LgymApi.IntegrationTests;
using LgymApi.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

public sealed partial class TrainerEmailInvitationTests
{
    [Test]
    public async Task AcceptInvitationTwice_SendsSingleEmail()
    {
        // Arrange
        var trainer = await SeedDuplicateAcceptanceTrainerAsync();
        var trainee = await SeedUserAsync();
        var invitation = await SeedDuplicateAcceptanceInvitationAsync(trainer.Id, trainee.Email.Value, status: TrainerInvitationStatus.Pending);
        using var scope = Factory.Services.CreateScope();
        var acceptInvitation = scope.ServiceProvider.GetRequiredService<IAcceptInvitationUseCase>();

        // Act
        await acceptInvitation.ExecuteAsync(new AcceptInvitationCommand(trainee.Id, invitation));
        await ProcessPendingCommandsAsync();
        await acceptInvitation.ExecuteAsync(new AcceptInvitationCommand(trainee.Id, invitation));
        await ProcessPendingCommandsAsync();

        Id<NotificationMessage> notificationId;
        using (var verifyScope = Factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var correlationId = invitation.Rebind<CorrelationScope>();
            var notifications = await db.NotificationMessages
                .Where(x => x.CorrelationId == correlationId)
                .ToListAsync();

            notifications.Should().ContainSingle();
            notificationId = notifications.Single().Id;
        }

        using (var processScope = Factory.Services.CreateScope())
        {
            var handler = processScope.ServiceProvider.GetRequiredService<IEmailJobHandler>();
            await handler.ProcessAsync(notificationId.ToString());
        }

        // Act again: a duplicate dispatch of the same notification must not send a second email.
        using (var processScope2 = Factory.Services.CreateScope())
        {
            var handler = processScope2.ServiceProvider.GetRequiredService<IEmailJobHandler>();
            await handler.ProcessAsync(notificationId.ToString());
        }

        // Assert
        Factory.EmailSender.SentMessages.Should().ContainSingle();
    }

    [Test]
    public async Task AcceptInvitationTwice_CrashBeforeSendRecoversAndSendsSingleEmail()
    {
        // Arrange
        var trainer = await SeedDuplicateAcceptanceTrainerAsync();
        var trainee = await SeedUserAsync();
        var invitation = await SeedDuplicateAcceptanceInvitationAsync(trainer.Id, trainee.Email.Value, status: TrainerInvitationStatus.Pending);
        using (var scope = Factory.Services.CreateScope())
        {
            var acceptInvitation = scope.ServiceProvider.GetRequiredService<IAcceptInvitationUseCase>();
            await acceptInvitation.ExecuteAsync(new AcceptInvitationCommand(trainee.Id, invitation));
        }

        await ProcessPendingCommandsAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var acceptInvitation = scope.ServiceProvider.GetRequiredService<IAcceptInvitationUseCase>();
            await acceptInvitation.ExecuteAsync(new AcceptInvitationCommand(trainee.Id, invitation));
        }

        await ProcessPendingCommandsAsync();

        Id<NotificationMessage> notificationId;
        using (var verifyScope = Factory.Services.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var correlationId = invitation.Rebind<CorrelationScope>();
            var notifications = await db.NotificationMessages
                .Where(x => x.CorrelationId == correlationId)
                .ToListAsync();

            notifications.Should().ContainSingle();
            notificationId = notifications.Single().Id;
        }

        // Simulate the first ProcessAsync claiming the notification (Pending -> Sending) and then
        // crashing before the SMTP send completed. The notification is left stranded in Sending
        // with DeliveredAt still null and an already-expired send lease.
        using (var crashScope = Factory.Services.CreateScope())
        {
            var db = crashScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notification = await db.NotificationMessages.FirstAsync(x => x.Id == notificationId);
            notification.Status = EmailNotificationStatus.Sending;
            notification.DeliveredAt = null;
            notification.LastAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        // Act: a subsequent dispatcher reclaims the stranded Sending notification (lease expired,
        // never delivered) and sends it exactly once.
        using (var processScope = Factory.Services.CreateScope())
        {
            var handler = processScope.ServiceProvider.GetRequiredService<IEmailJobHandler>();
            await handler.ProcessAsync(notificationId.ToString());
        }

        // Assert
        Factory.EmailSender.SentMessages.Should().ContainSingle();
    }

    private async Task<User> SeedDuplicateAcceptanceTrainerAsync(string name = "trainer", string email = "trainer@example.com")
    {
        var trainer = await SeedUserAsync(name: name, email: email, password: "password123");
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var alreadyLinked = await db.UserRoles.AnyAsync(ur => ur.UserId == trainer.Id && ur.RoleId == RoleSeedDataConfiguration.TrainerRoleSeedId);
        if (!alreadyLinked)
        {
            db.UserRoles.Add(new UserRole { UserId = trainer.Id, RoleId = RoleSeedDataConfiguration.TrainerRoleSeedId });
        }

        var trainerToUpdate = await db.Users.FirstAsync(u => u.Id == trainer.Id);
        trainerToUpdate.PreferredLanguage = "en-US";
        await db.SaveChangesAsync();
        return trainer;
    }

    private async Task<Id<TrainerInvitation>> SeedDuplicateAcceptanceInvitationAsync(
        Id<User> trainerId,
        string inviteeEmail,
        TrainerInvitationStatus status,
        string code = "TESTCODE0001",
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? respondedAt = null)
    {
        var invitationId = Id<TrainerInvitation>.New();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TrainerInvitations.Add(new TrainerInvitation
        {
            Id = invitationId,
            TrainerId = trainerId,
            InviteeEmail = inviteeEmail,
            TraineeId = null,
            Code = code,
            Status = status,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(7),
            RespondedAt = respondedAt
        });
        await db.SaveChangesAsync();
        return invitationId;
    }
}
