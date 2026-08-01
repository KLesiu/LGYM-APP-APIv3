using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using LgymApi.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed partial class TrainerEmailInvitationTests : IntegrationTestBase
{
    [SetUp]
    public void ResetEmailCapture()
    {
        Factory.EmailSender.Reset();
    }

    [Test]
    public async Task CreateInvitationByEmail_WhenEmailNotInSystem_ReturnsPendingInvitationWithEmptyTraineeId()
    {
        var trainer = await SeedTrainerAsync("trainer-email-new", "trainer-email-new@example.com");
        SetAuthorizationHeader(trainer.Id);

        var response = await Client.PostAsJsonAsync("/api/trainer/invitations/by-email", new
        {
            email = "new-unknown@example.com",
            preferredLanguage = "en-US",
            preferredTimeZone = "UTC"
        });

        await ProcessPendingCommandsAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TrainerInvitationResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("Pending");
        body.TraineeId.Should().BeEmpty();
        body.Code.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task CreateInvitationByEmail_WhenEmailBelongsToExistingUser_ReturnsPendingInvitationWithTraineeId()
    {
        var trainer = await SeedTrainerAsync("trainer-email-existing", "trainer-email-existing@example.com");
        var existingUser = await SeedUserAsync("existing-user", "existing-user@example.com", "password123");
        SetAuthorizationHeader(trainer.Id);

        var response = await Client.PostAsJsonAsync("/api/trainer/invitations/by-email", new
        {
            email = "existing-user@example.com",
            preferredLanguage = "en-US",
            preferredTimeZone = "UTC"
        });

        await ProcessPendingCommandsAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TrainerInvitationResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("Pending");
        body.TraineeId.Should().Be(existingUser.Id.ToString());
    }

    [Test]
    public async Task CreateInvitationByEmail_WhenPendingInvitationExistsForEmail_ReturnsConflict()
    {
        var trainer = await SeedTrainerAsync("trainer-email-pending", "trainer-email-pending@example.com");
        await SeedInvitationAsync(trainer.Id, "pending-dup@example.com", status: TrainerInvitationStatus.Pending, code: "TESTCODE0001");
        SetAuthorizationHeader(trainer.Id);

        var response = await Client.PostAsJsonAsync("/api/trainer/invitations/by-email", new
        {
            email = "pending-dup@example.com",
            preferredLanguage = "en-US",
            preferredTimeZone = "UTC"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var responseBody = await response.Content.ReadAsStringAsync();

        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var expectedMessage = typeof(Messages).GetProperty("InvitationPendingForEmail")!.GetValue(null) as string;
            responseBody.Should().Contain(expectedMessage!);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Test]
    public async Task CreateInvitationByEmail_WhenEmailAlreadyBelongsToTrainerTrainee_ReturnsConflict()
    {
        var trainer = await SeedTrainerAsync("trainer-email-linked", "trainer-email-linked@example.com");
        var trainee = await SeedUserAsync("linked-trainee", "linked-trainee@example.com", "password123");

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TrainerTraineeLinks.Add(new TrainerTraineeLink
            {
                Id = Id<TrainerTraineeLink>.New(),
                TrainerId = trainer.Id,
                TraineeId = trainee.Id
            });
            await db.SaveChangesAsync();
        }

        SetAuthorizationHeader(trainer.Id);

        var response = await Client.PostAsJsonAsync("/api/trainer/invitations/by-email", new
        {
            email = trainee.Email.Value,
            preferredLanguage = "en-US",
            preferredTimeZone = "UTC"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var responseBody = await response.Content.ReadAsStringAsync();

        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var expectedMessage = typeof(Messages).GetProperty("EmailAlreadyYourTrainee")!.GetValue(null) as string;
            responseBody.Should().Contain(expectedMessage!);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Test]
    public async Task CreateInvitationByEmail_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await Client.PostAsJsonAsync("/api/trainer/invitations/by-email", new
        {
            email = "valid@example.com",
            preferredLanguage = "en-US",
            preferredTimeZone = "UTC"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task CreateInvitationByEmail_WithInvalidEmailFormat_ReturnsBadRequest()
    {
        var trainer = await SeedTrainerAsync("trainer-email-invalid", "trainer-email-invalid@example.com");
        SetAuthorizationHeader(trainer.Id);

        var response = await Client.PostAsJsonAsync("/api/trainer/invitations/by-email", new
        {
            email = "not-an-email",
            preferredLanguage = "en-US",
            preferredTimeZone = "UTC"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task RevokeInvitation_WhenTrainerRevokesPendingInvitation_ReturnsOkAndMarksInvitationRevoked()
    {
        var trainer = await SeedTrainerAsync("trainer-revoke-ok", "trainer-revoke-ok@example.com");
        var invitationId = await SeedInvitationAsync(trainer.Id, "revoke-me@example.com", status: TrainerInvitationStatus.Pending, code: "REVOKE000001");
        SetAuthorizationHeader(trainer.Id);

        var response = await Client.PostAsync($"/api/trainer/invitations/{invitationId}/revoke", null);

        await ProcessPendingCommandsAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invitation = await db.TrainerInvitations.FirstAsync(i => i.Id == invitationId);
        invitation.Status.Should().Be(TrainerInvitationStatus.Revoked);
        invitation.RespondedAt.Should().NotBeNull();
    }

    [Test]
    public async Task RevokeInvitation_WhenInvitationAlreadyAccepted_ReturnsBadRequest()
    {
        var trainer = await SeedTrainerAsync("trainer-revoke-accepted", "trainer-revoke-accepted@example.com");
        var invitationId = await SeedInvitationAsync(trainer.Id, "accepted@example.com", status: TrainerInvitationStatus.Accepted, code: "ACCEPTED001");
        SetAuthorizationHeader(trainer.Id);

        var response = await Client.PostAsync($"/api/trainer/invitations/{invitationId}/revoke", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task RevokeInvitation_WhenInvitationBelongsToAnotherTrainer_ReturnsNotFound()
    {
        var trainerA = await SeedTrainerAsync("trainer-revoke-a", "trainer-revoke-a@example.com");
        var trainerB = await SeedTrainerAsync("trainer-revoke-b", "trainer-revoke-b@example.com");
        var invitationId = await SeedInvitationAsync(trainerB.Id, "other-trainer@example.com", status: TrainerInvitationStatus.Pending, code: "OTHERTRN001");
        SetAuthorizationHeader(trainerA.Id);

        var response = await Client.PostAsync($"/api/trainer/invitations/{invitationId}/revoke", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetInvitationStatus_WithValidCodeAndUnknownUser_ReturnsPendingAndUserDoesNotExist()
    {
        var trainer = await SeedTrainerAsync("trainer-public-ghost", "trainer-public-ghost@example.com");
        var invitationId = await SeedInvitationAsync(trainer.Id, "ghost@example.com", status: TrainerInvitationStatus.Pending, code: "CODE001VALID");

        var response = await Client.GetAsync($"/api/invitations/{invitationId}?code=CODE001VALID");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["status", "userExists"]);
        body.RootElement.GetProperty("status").GetString().Should().Be("Pending");
        body.RootElement.GetProperty("userExists").GetBoolean().Should().BeFalse();
    }

    [Test]
    public async Task GetInvitationStatus_WithBoundTrainee_ReturnsUserExistsWithoutInviteeEmailAccount()
    {
        var trainer = await SeedTrainerAsync("trainer-public-bound", "trainer-public-bound@example.com");
        var trainee = await SeedUserAsync("bound-user", "bound-user@example.com", "password123");
        var invitationId = await SeedInvitationAsync(
            trainer.Id,
            "unresolved-bound-email@example.com",
            status: TrainerInvitationStatus.Accepted,
            code: "BOUND001CODE",
            traineeId: trainee.Id);

        var response = await Client.GetAsync($"/api/invitations/{invitationId}?code=BOUND001CODE");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PublicInvitationStatusResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("Accepted");
        body.UserExists.Should().BeTrue();
    }

    [Test]
    public async Task GetInvitationStatus_WithValidCodeAndExistingUser_ReturnsPendingAndUserExists()
    {
        var trainer = await SeedTrainerAsync("trainer-public-existing", "trainer-public-existing@example.com");
        await SeedUserAsync("exists-user", "exists-user@example.com", "password123");
        var invitationId = await SeedInvitationAsync(trainer.Id, "exists-user@example.com", status: TrainerInvitationStatus.Pending, code: "CODE002VALID");

        var response = await Client.GetAsync($"/api/invitations/{invitationId}?code=CODE002VALID");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PublicInvitationStatusResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("Pending");
        body.UserExists.Should().BeTrue();
    }

    [Test]
    public async Task GetInvitationStatus_WithInvalidCode_ReturnsNotFound()
    {
        var trainer = await SeedTrainerAsync("trainer-public-invalid-code", "trainer-public-invalid-code@example.com");
        var invitationId = await SeedInvitationAsync(trainer.Id, "wrong-code@example.com", status: TrainerInvitationStatus.Pending, code: "REALCODE001");

        var response = await Client.GetAsync($"/api/invitations/{invitationId}?code=WRONGCODE");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("title").GetString().Should().Be("Not Found");
        body.RootElement.GetProperty("status").GetInt32().Should().Be((int)HttpStatusCode.NotFound);
        body.RootElement.TryGetProperty("msg", out _).Should().BeFalse();
    }

    [Test]
    public async Task GetInvitationStatus_WhenInvitationRevoked_ReturnsRevokedStatus()
    {
        var trainer = await SeedTrainerAsync("trainer-public-revoked", "trainer-public-revoked@example.com");
        var invitationId = await SeedInvitationAsync(trainer.Id, "revoked@example.com", status: TrainerInvitationStatus.Revoked, code: "REVOKECODE1");

        var response = await Client.GetAsync($"/api/invitations/{invitationId}?code=REVOKECODE1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PublicInvitationStatusResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("Revoked");
    }

    [Test]
    public async Task AcceptInvitation_WhenEmailMatchesInvitation_SetsTraineeIdAndCreatesLink()
    {
        var trainer = await SeedTrainerAsync("trainer-accept-email", "trainer-accept-email@example.com");
        var trainee = await SeedUserAsync("accept-email-user", "accept-email-user@example.com", "password123");
        var invitationId = await SeedInvitationAsync(trainer.Id, "accept-email-user@example.com", status: TrainerInvitationStatus.Pending, code: "ACCEPT001");
        SetAuthorizationHeader(trainee.Id);

        var response = await Client.PostAsync($"/api/trainee/invitations/{invitationId}/accept", null);

        await ProcessPendingCommandsAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.TrainerTraineeLinks.FirstOrDefaultAsync(x => x.TrainerId == trainer.Id && x.TraineeId == trainee.Id);
        link.Should().NotBeNull();

        var invitation = await db.TrainerInvitations.FirstAsync(i => i.Id == invitationId);
        invitation.TraineeId.Should().Be(trainee.Id);
        invitation.Status.Should().Be(TrainerInvitationStatus.Accepted);
    }

    [Test]
    public async Task AcceptInvitation_WhenEmailDoesNotMatchCurrentUser_ReturnsNotFound()
    {
        var trainer = await SeedTrainerAsync("trainer-accept-mismatch", "trainer-accept-mismatch@example.com");
        var wrongUser = await SeedUserAsync("wrong-user", "wrong-user@example.com", "password123");
        var invitationId = await SeedInvitationAsync(trainer.Id, "different@example.com", status: TrainerInvitationStatus.Pending, code: "NOMATCHE01");
        SetAuthorizationHeader(wrongUser.Id);

        var response = await Client.PostAsync($"/api/trainee/invitations/{invitationId}/accept", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<User> SeedTrainerAsync(string name, string email, string preferredLanguage = "en-US")
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
        trainerToUpdate.PreferredLanguage = preferredLanguage;
        await db.SaveChangesAsync();
        return trainer;
    }

    private async Task<Id<TrainerInvitation>> SeedInvitationAsync(
        Id<User> trainerId,
        string inviteeEmail,
        TrainerInvitationStatus status,
        string code,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? respondedAt = null,
        Id<User>? traineeId = null)
    {
        var invitationId = Id<TrainerInvitation>.New();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TrainerInvitations.Add(new TrainerInvitation
        {
            Id = invitationId,
            TrainerId = trainerId,
            InviteeEmail = inviteeEmail,
            TraineeId = traineeId,
            Code = code,
            Status = status,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(7),
            RespondedAt = respondedAt
        });
        await db.SaveChangesAsync();
        return invitationId;
    }

    private sealed class TrainerInvitationResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("traineeId")]
        public string TraineeId { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }

    private sealed class PublicInvitationStatusResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("userExists")]
        public bool UserExists { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }
}
