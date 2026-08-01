using FluentAssertions;
using LgymApi.Application.Coaching.Invitations.Accept;
using LgymApi.Application.Coaching.Invitations.ListPaginated;
using LgymApi.Application.Pagination;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class CoachingInvitationSliceIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task Accept_ExpiresMatchingEmailInvitationWithoutBindingCreatingLinkOrEnqueuingCommand()
    {
        var trainer = await SeedUserAsync("invitation-expiry-trainer", "invitation-expiry-trainer@example.test", "password123");
        var trainee = await SeedUserAsync("invitation-expiry-trainee", "invitation-expiry-trainee@example.test", "password123");
        var invitationId = Id<TrainerInvitation>.New();

        using (var writeScope = Factory.Services.CreateScope())
        {
            var database = writeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TrainerInvitations.Add(new TrainerInvitation
            {
                Id = invitationId,
                TrainerId = trainer.Id,
                InviteeEmail = " INVITATION-EXPIRY-TRAINEE@EXAMPLE.TEST ",
                Code = "SLICE-EXPIRED",
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
    public async Task ListPaginated_EnrichesIdentityBeforeApplyingTheDefaultCreatedAtPage()
    {
        var trainer = await SeedUserAsync("invitation-slice-trainer", "invitation-slice-trainer@example.test", "password123");
        var trainee = await SeedUserAsync("Invitation slice trainee", "invitation-slice-trainee@example.test", "password123");
        var oldestId = new Id<TrainerInvitation>(new Guid("10000000-0000-0000-0000-000000000001"));
        var newestId = new Id<TrainerInvitation>(new Guid("20000000-0000-0000-0000-000000000001"));
        var createdAt = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);

        using (var scope = Factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.UserRoles.Add(new UserRole { UserId = trainer.Id, RoleId = RoleSeedDataConfiguration.TrainerRoleSeedId });
            database.TrainerInvitations.AddRange(
                new TrainerInvitation
                {
                    Id = oldestId,
                    TrainerId = trainer.Id,
                    TraineeId = trainee.Id,
                    InviteeEmail = trainee.Email,
                    Code = "SLICE-OLDEST",
                    Status = TrainerInvitationStatus.Pending,
                    ExpiresAt = createdAt.AddDays(7),
                    CreatedAt = createdAt,
                    UpdatedAt = createdAt
                },
                new TrainerInvitation
                {
                    Id = newestId,
                    TrainerId = trainer.Id,
                    InviteeEmail = "email-only@example.test",
                    Code = "SLICE-NEWEST",
                    Status = TrainerInvitationStatus.Pending,
                    ExpiresAt = createdAt.AddDays(8),
                    CreatedAt = createdAt.AddDays(1),
                    UpdatedAt = createdAt.AddDays(1)
                });
            await database.SaveChangesAsync();
        }

        using var readScope = Factory.Services.CreateScope();
        var useCase = readScope.ServiceProvider.GetRequiredService<IListPaginatedInvitationsUseCase>();

        var result = await useCase.ExecuteAsync(new ListPaginatedInvitationsQuery(
            trainer.Id,
            new FilterInput { Page = 1, PageSize = 20 }));

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.Select(item => item.Id).Should().Equal(oldestId, newestId);
        result.Value.Items[0].TraineeName.Should().Be(trainee.Name);
        result.Value.Items[0].TraineeEmail.Should().Be(trainee.Email);
        result.Value.Items[1].TraineeId.Should().BeNull();
        result.Value.Items[1].TraineeName.Should().BeNull();
        result.Value.Items[1].TraineeEmail.Should().BeNull();
    }
}
