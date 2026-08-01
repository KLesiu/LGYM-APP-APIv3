using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Globalization;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Data.SeedData;
using LgymApi.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class CoachingContractCompatibilityTests : IntegrationTestBase
{
    [TestCase("/api/invitations/not-a-guid?code=unused")]
    [TestCase("/api/invitations/00000000-0000-0000-0000-000000000000")]
    [TestCase("/api/invitations/00000000-0000-0000-0000-000000000001?code=unused")]
    [TestCase("/api/invitations/00000000-0000-0000-0000-000000000001?code=")]
    [TestCase("/api/invitations/00000000-0000-0000-0000-000000000001?code=%20")]
    public async Task PublicInvitationStatus_ReturnsBareNotFoundForMissingOrMalformedInput(string route)
    {
        ClearAuthorizationHeader();

        var response = await Client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("title").GetString().Should().Be("Not Found");
        body.RootElement.GetProperty("status").GetInt32().Should().Be((int)HttpStatusCode.NotFound);
        body.RootElement.TryGetProperty("msg", out _).Should().BeFalse();
    }

    [Test]
    public async Task TrainerInvitationResponses_PreserveLegacyIdAndMessageFields()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var trainer = await SeedTrainerAsync("coaching-contract-trainer", "coaching-contract-trainer@example.com");
            var trainee = await SeedUserAsync("coaching-contract-trainee", "coaching-contract-trainee@example.com", "password123");
            SetAuthorizationHeader(trainer.Id);
            SetIdempotencyKey("coaching-contract-invitation");

            HttpResponseMessage createResponse;
            try
            {
                createResponse = await Client.PostAsJsonAsync("/api/trainer/invitations", new { traineeId = trainee.Id.ToString() });
            }
            finally
            {
                ClearIdempotencyKey();
            }

            createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            using var invitation = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            invitation.RootElement.GetProperty("_id").GetString().Should().NotBeNullOrWhiteSpace();
            invitation.RootElement.TryGetProperty("id", out _).Should().BeFalse();

            var revokeResponse = await Client.PostAsync($"/api/trainer/invitations/{invitation.RootElement.GetProperty("_id").GetString()}/revoke", null);

            revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            using var message = JsonDocument.Parse(await revokeResponse.Content.ReadAsStringAsync());
            message.RootElement.GetProperty("msg").GetString().Should().Be(Messages.Updated);
            message.RootElement.TryGetProperty("message", out _).Should().BeFalse();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Test]
    public async Task TrainerInvitation_MalformedTraineeId_ReturnsValidationBadRequest()
    {
        var trainer = await SeedTrainerAsync("coaching-contract-invalid-trainer", "coaching-contract-invalid-trainer@example.com");
        SetAuthorizationHeader(trainer.Id);
        SetIdempotencyKey("coaching-contract-invalid-invitation");

        HttpResponseMessage response;
        try
        {
            response = await Client.PostAsJsonAsync("/api/trainer/invitations", new { traineeId = "not-a-guid" });
        }
        finally
        {
            ClearIdempotencyKey();
        }

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.TryGetProperty("errors", out _).Should().BeTrue();
        body.RootElement.TryGetProperty("msg", out _).Should().BeFalse();
    }

    [Test]
    public async Task TraineeInvitation_MalformedId_ReturnsLocalizedLegacyMessage()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var trainee = await SeedUserAsync("coaching-contract-invalid", "coaching-contract-invalid@example.com", "password123");
            SetAuthorizationHeader(trainee.Id);

            var response = await Client.PostAsync("/api/trainee/invitations/not-a-guid/accept", null);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            body.RootElement.GetProperty("msg").GetString().Should().Be(Messages.FieldRequired);
            body.RootElement.TryGetProperty("message", out _).Should().BeFalse();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private async Task<User> SeedTrainerAsync(string name, string email)
    {
        var trainer = await SeedUserAsync(name, email, "password123");
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserRoles.Add(new UserRole { UserId = trainer.Id, RoleId = RoleSeedDataConfiguration.TrainerRoleSeedId });
        await db.SaveChangesAsync();
        return trainer;
    }
}
