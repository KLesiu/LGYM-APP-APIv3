using FluentAssertions;
using LgymApi.Application.Coaching.Invitations.Accept;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
[Category("PostgreSql")]
internal sealed class PostgreSqlCoachingPersistenceRepositoryTests : PostgreSqlIntegrationTestBase
{
    [Test]
    public async Task Accept_ExpiredEmailInvitationLeavesTraineeUnboundWithoutLinkOrCommand()
    {
        var suffix = Id<PostgreSqlCoachingPersistenceRepositoryTests>.New().ToString().Replace("-", string.Empty, StringComparison.Ordinal);
        var trainer = await SeedUserAsync($"coaching-expired-trainer-{suffix}", $"coaching-expired-trainer-{suffix}@example.test");
        var trainee = await SeedUserAsync($"coaching-expired-trainee-{suffix}", $"coaching-expired-trainee-{suffix}@example.test");
        var invitationId = Id<TrainerInvitation>.New();

        using (var writeScope = Factory.Services.CreateScope())
        {
            var database = writeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TrainerInvitations.Add(new TrainerInvitation
            {
                Id = invitationId,
                TrainerId = trainer.Id,
                InviteeEmail = $" COACHING-EXPIRED-TRAINEE-{suffix}@EXAMPLE.TEST ",
                Code = $"expired-{suffix}",
                Status = TrainerInvitationStatus.Pending,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
            await database.SaveChangesAsync();
        }

        using (var actionScope = Factory.Services.CreateScope())
        {
            var useCase = actionScope.ServiceProvider.GetRequiredService<IAcceptInvitationUseCase>();

            var result = await useCase.ExecuteAsync(new AcceptInvitationCommand(trainee.Id, invitationId));

            result.IsFailure.Should().BeTrue();
        }

        using var verificationScope = Factory.Services.CreateScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invitation = await verificationDatabase.TrainerInvitations.SingleAsync(candidate => candidate.Id == invitationId);

        invitation.Status.Should().Be(TrainerInvitationStatus.Expired);
        invitation.RespondedAt.Should().NotBeNull();
        invitation.TraineeId.Should().BeNull();
        (await verificationDatabase.TrainerTraineeLinks.AnyAsync(candidate => candidate.TraineeId == trainee.Id)).Should().BeFalse();
        (await verificationDatabase.CommandEnvelopes.CountAsync(candidate => candidate.PayloadJson.Contains(invitationId.ToString()))).Should().Be(0);
    }

    [Test]
    public async Task FactReader_ReturnsCompleteUnpagedCoachingFacts()
    {
        var suffix = Id<PostgreSqlCoachingPersistenceRepositoryTests>.New().ToString().Replace("-", string.Empty, StringComparison.Ordinal);
        var trainer = await SeedUserAsync($"coaching-facts-trainer-{suffix}", $"coaching-facts-trainer-{suffix}@example.test");
        var trainee = await SeedUserAsync($"coaching-facts-trainee-{suffix}", $"coaching-facts-trainee-{suffix}@example.test");
        var now = DateTimeOffset.UtcNow;
        var latestInvitation = new TrainerInvitation
        {
            Id = Id<TrainerInvitation>.New(),
            TrainerId = trainer.Id,
            TraineeId = trainee.Id,
            InviteeEmail = trainee.Email,
            Code = $"latest-{suffix}",
            Status = TrainerInvitationStatus.Pending,
            ExpiresAt = now.AddDays(3),
            CreatedAt = now,
            UpdatedAt = now
        };
        var emailInvitation = new TrainerInvitation
        {
            Id = Id<TrainerInvitation>.New(),
            TrainerId = trainer.Id,
            InviteeEmail = $"email-{suffix}@example.test",
            Code = $"email-{suffix}",
            Status = TrainerInvitationStatus.Pending,
            ExpiresAt = now.AddDays(2),
            CreatedAt = now.AddMinutes(-1),
            UpdatedAt = now.AddMinutes(-1)
        };

        using (var writeScope = Factory.Services.CreateScope())
        {
            var database = writeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id,
                CreatedAt = now.AddDays(-2),
                UpdatedAt = now.AddDays(-2)
            });
            database.TrainerInvitations.AddRange(latestInvitation, emailInvitation);
            await database.SaveChangesAsync();
        }

        using var readScope = Factory.Services.CreateScope();
        var reader = readScope.ServiceProvider.GetRequiredService<ICoachingFactReader>();

        var invitationFacts = await reader.GetInvitationFactsAsync(trainer.Id);
        var dashboardFacts = await reader.GetDashboardFactsAsync(trainer.Id);

        invitationFacts.Should().Contain(fact => fact.Id == latestInvitation.Id && fact.TraineeId == trainee.Id);
        invitationFacts.Should().Contain(fact => fact.Id == emailInvitation.Id && !fact.TraineeId.HasValue);
        dashboardFacts.Should().ContainSingle(fact => fact.TraineeId == trainee.Id);
        dashboardFacts.Single().LatestInvitation!.Id.Should().Be(latestInvitation.Id);
        dashboardFacts.Single().ActiveLink.Should().NotBeNull();
    }
}
