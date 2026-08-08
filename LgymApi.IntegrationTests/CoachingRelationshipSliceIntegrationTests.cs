using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Application.Coaching.Relationships.DetachFromTrainer;
using LgymApi.Application.Coaching.Relationships.GetCurrentTrainer;
using LgymApi.Application.Coaching.Relationships.UnlinkTrainee;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using LgymApi.IntegrationTests.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class CoachingRelationshipSliceIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task UnlinkTrainee_RemovesOwnedLinkWithoutQueuingRelationshipEndedCommand()
    {
        var trainer = await SeedUserAsync("slice-unlink-trainer", "slice-unlink-trainer@example.test");
        var trainee = await SeedUserAsync("slice-unlink-trainee", "slice-unlink-trainee@example.test");

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.UserRoles.Add(new UserRole
            {
                UserId = trainer.Id,
                RoleId = RoleSeedDataConfiguration.TrainerRoleSeedId
            });
            database.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id
            });
            await database.SaveChangesAsync();
        }

        using (var actionScope = Factory.Services.CreateScope())
        {
            var useCase = actionScope.ServiceProvider.GetRequiredService<IUnlinkTraineeUseCase>();
            var result = await useCase.ExecuteAsync(new UnlinkTraineeCommand(trainer.Id, trainee.Id));
            result.IsSuccess.Should().BeTrue();
        }

        using var verificationScope = Factory.Services.CreateScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verificationDatabase.TrainerTraineeLinks.AnyAsync(link => link.TraineeId == trainee.Id)).Should().BeFalse();
        (await verificationDatabase.CommandEnvelopes.CountAsync(envelope =>
            envelope.CommandTypeFullName.Contains("TrainerRelationshipEndedInAppNotificationCommand"))).Should().Be(0);
    }

    [Test]
    public async Task DetachFromTrainer_RemovesLinkAndQueuesRelationshipEndedCommand()
    {
        var trainer = await SeedUserAsync("slice-detach-trainer", "slice-detach-trainer@example.test");
        var trainee = await SeedUserAsync("slice-detach-trainee", "slice-detach-trainee@example.test");

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id
            });
            await database.SaveChangesAsync();
        }

        using (var actionScope = Factory.Services.CreateScope())
        {
            var useCase = actionScope.ServiceProvider.GetRequiredService<IDetachFromTrainerUseCase>();
            var result = await useCase.ExecuteAsync(new DetachFromTrainerCommand(trainee.Id));
            result.IsSuccess.Should().BeTrue();
        }

        using var verificationScope = Factory.Services.CreateScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verificationDatabase.TrainerTraineeLinks.AnyAsync(link => link.TraineeId == trainee.Id)).Should().BeFalse();
        (await verificationDatabase.CommandEnvelopes.CountAsync(envelope =>
            envelope.CommandTypeFullName.Contains("TrainerRelationshipEndedInAppNotificationCommand"))).Should().Be(1);
    }

    [Test]
    public async Task GetCurrentTrainer_ReturnsIdentityProfileAndExactLinkTimestamp()
    {
        var trainer = await SeedUserAsync("Slice current trainer", "slice-current-trainer@example.test");
        var trainee = await SeedUserAsync("slice-current-trainee", "slice-current-trainee@example.test");
        var linkedAt = new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id,
                CreatedAt = linkedAt,
                UpdatedAt = linkedAt
            });
            await database.SaveChangesAsync();
        }

        using var actionScope = Factory.Services.CreateScope();
        var useCase = actionScope.ServiceProvider.GetRequiredService<IGetCurrentTrainerUseCase>();
        var result = await useCase.ExecuteAsync(new GetCurrentTrainerQuery(trainee.Id));

        result.IsSuccess.Should().BeTrue();
        result.Value.TrainerId.Should().Be(trainer.Id);
        result.Value.Name.Should().Be(trainer.Name);
        result.Value.Email.Should().Be(trainer.Email.Value);
        result.Value.Avatar.Should().Be(trainer.Avatar);
        result.Value.LinkedAt.Should().Be(linkedAt);
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/trainee/trainer", "own", "owner-allow")]
    [AuthorizationEvidence("GET", "/api/trainee/trainer", "own", "no-client-subject")]
    [AuthorizationEvidence("GET", "/api/trainee/trainer", "own", "anonymous-denial")]
    public async Task CurrentTrainerRoute_IsActorScopedAndNonDisclosing()
    {
        var trainer = await SeedUserAsync("http-current-trainer", "http-current-trainer@example.test");
        var trainee = await SeedUserAsync("http-current-trainee", "http-current-trainee@example.test");
        var otherTrainee = await SeedUserAsync("http-current-other", "http-current-other@example.test");
        var linkedAt = new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id,
                CreatedAt = linkedAt,
                UpdatedAt = linkedAt
            });
            await database.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainee.Id);
        using var ownerResponse = await Client.GetAsync("/api/trainee/trainer");
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerProfile = await ownerResponse.Content.ReadFromJsonAsync<TraineeTrainerProfileResponse>();
        ownerProfile.Should().NotBeNull();
        ownerProfile!.TrainerId.Should().Be(trainer.Id.ToString());
        ownerProfile.Name.Should().Be(trainer.Name);
        ownerProfile.Email.Should().Be(trainer.Email.Value);
        ownerProfile.LinkedAt.Should().Be(linkedAt);

        SetAuthorizationHeader(otherTrainee.Id);
        using var otherResponse = await Client.GetAsync("/api/trainee/trainer");
        otherResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var otherResponseText = await otherResponse.Content.ReadAsStringAsync();
        otherResponseText.Should().NotContain(trainer.Id.ToString());
        otherResponseText.Should().NotContain(trainer.Name);
        otherResponseText.Should().NotContain(trainer.Email.Value);

        using (var unlinkScope = Factory.Services.CreateScope())
        {
            var database = unlinkScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var link = await database.TrainerTraineeLinks
                .SingleAsync(candidate => candidate.TrainerId == trainer.Id && candidate.TraineeId == trainee.Id);
            database.TrainerTraineeLinks.Remove(link);
            await database.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainee.Id);
        using var formerResponse = await Client.GetAsync("/api/trainee/trainer");
        formerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var formerResponseText = await formerResponse.Content.ReadAsStringAsync();
        formerResponseText.Should().NotContain(trainer.Id.ToString());
        formerResponseText.Should().NotContain(trainer.Name);
        formerResponseText.Should().NotContain(trainer.Email.Value);

        ClearAuthorizationHeader();
        using var anonymousResponse = await Client.GetAsync("/api/trainee/trainer");
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var anonymousResponseText = await anonymousResponse.Content.ReadAsStringAsync();
        anonymousResponseText.Should().NotContain(trainer.Id.ToString());
        anonymousResponseText.Should().NotContain(trainer.Name);
        anonymousResponseText.Should().NotContain(trainer.Email.Value);
    }

    [Test]
    public async Task GetCurrentTrainer_WhenTrainerAccountIsDeletedReturnsCurrentNotFoundError()
    {
        var trainer = await SeedUserAsync(
            "slice-deleted-trainer",
            "slice-deleted-trainer@example.test",
            isDeleted: true);
        var trainee = await SeedUserAsync("slice-deleted-trainee", "slice-deleted-trainee@example.test");

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id
            });
            await database.SaveChangesAsync();
        }

        using var actionScope = Factory.Services.CreateScope();
        var useCase = actionScope.ServiceProvider.GetRequiredService<IGetCurrentTrainerUseCase>();
        var result = await useCase.ExecuteAsync(new GetCurrentTrainerQuery(trainee.Id));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<TrainerRelationshipNotFoundError>();
    }

    private sealed class TraineeTrainerProfileResponse
    {
        [JsonPropertyName("trainerId")]
        public string TrainerId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("linkedAt")]
        public DateTimeOffset LinkedAt { get; set; }
    }
}
