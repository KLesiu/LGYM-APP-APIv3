using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

/// <summary>
/// T12 Reliability Tests - Replay Detection and Conflict Handling
/// Tests same-key replay safety, same-key-different-body conflicts, and idempotency record persistence.
/// </summary>
[TestFixture]
public sealed class ReliabilityReplayTests : IntegrationTestBase
{
    [Test]
    public async Task Register_SameKeyAndPayload_ReturnsStoredResponseWithoutDuplicateIntent()
    {
        // Arrange
        var request = new
        {
            name = "replay-test-user",
            email = "replay@example.com",
            password = "password123",
            cpassword = "password123",
            isVisibleInRanking = true
        };

        const string idempotencyKey = "test-replay-001";

        // Act - Send identical request twice with same key
        var (first, second) = await SendRepeatedRequestAsync("/api/register", request, idempotencyKey);

        // Assert - Both requests succeed
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert - Responses are identical (replay detection)
        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        secondBody.Should().Be(firstBody, "replay response should match original");

        // Assert - Only one user created
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == "replay@example.com");
        user.Should().NotBeNull("replay should not duplicate user creation");

        // Assert - Only one command envelope created (query by user ID since payload contains UserId not email)
        var envelopeCount = await db.CommandEnvelopes
            .Where(ce => ce.PayloadJson.Contains(user!.Id.ToString()))
            .CountAsync();
        envelopeCount.Should().Be(1, "replay should not duplicate command envelope");

        // Assert - Idempotency record persisted
        var idempotencyRecord = await db.ApiIdempotencyRecords
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey);
        idempotencyRecord.Should().NotBeNull("idempotency record should be persisted");
        idempotencyRecord!.ResponseStatusCode.Should().Be(200);
        idempotencyRecord.ResponseBodyJson.Should().Be(firstBody);
    }

    [Test]
    public async Task Register_SameKeyDifferentPayload_Returns409Conflict()
    {
        // Arrange - Use SAME email (scope identifier) but different other fields
        var firstRequest = new
        {
            name = "conflict-test-1",
            email = "conflict@example.com", // SAME email for both requests
            password = "password123",
            cpassword = "password123",
            isVisibleInRanking = true
        };

        var secondRequest = new
        {
            name = "conflict-test-2", // Different name = different payload fingerprint
            email = "conflict@example.com", // SAME email (same scope)
            password = "password456", // Different password = different fingerprint
            cpassword = "password456",
            isVisibleInRanking = false // Different visibility = different fingerprint
        };

        const string idempotencyKey = "test-conflict-001";

        // Act - First request with key
        SetIdempotencyKey(idempotencyKey);
        var firstResponse = await PostAsJsonWithApiOptionsAsync("/api/register", firstRequest);

        // Act - Second request with SAME key and SAME scope but DIFFERENT payload
        var secondResponse = await PostAsJsonWithApiOptionsAsync("/api/register", secondRequest);
        ClearIdempotencyKey();

        // Assert - First request succeeds
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert - Second request returns 409 Conflict
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var conflictBody = await secondResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        conflictBody.Should().NotBeNull();
        conflictBody!.Code.Should().Be("IDEMPOTENCY_KEY_FINGERPRINT_MISMATCH");

        // Assert - Only first user created (conflict prevented duplicate)
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == "conflict@example.com");
        user.Should().NotBeNull("first request should create user");
        user!.Name.Should().Be("conflict-test-1", "only first request should persist");

        // Assert - Only one command envelope (conflict prevented duplicate side effect, query by user ID)
        var envelopeCount = await db.CommandEnvelopes
            .Where(ce => ce.PayloadJson.Contains(user.Id.ToString()))
            .CountAsync();
        envelopeCount.Should().Be(1, "conflict should not create duplicate envelope");
    }

    [Test]
    public async Task AddTraining_SameKeyReplay_ReturnsStoredResponseWithoutDuplicateCommand()
    {
        // Arrange - Create user and prerequisites
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "training-replay-user",
            email: "training-replay@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Replay Exercise");
        var gymId = await CreateGymViaEndpointAsync(userId, "Replay Gym");
        var planId = await CreatePlanViaEndpointAsync(userId, "Replay Plan");
        var planDayId = await CreatePlanDayViaEndpointAsync(userId, planId, "Replay Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" }
        });

        var request = new
        {
            gym = gymId.ToString(),
            type = planDayId.ToString(),
            createdAt = DateTime.UtcNow,
            exercises = new[]
            {
                new { exercise = exerciseId.ToString(), series = 1, reps = 10, weight = 80.0, unit = "Kilograms" }
            }
        };

        const string idempotencyKey = "test-training-replay-001";

        // Act - Send same training request twice
        var (first, second) = await SendRepeatedRequestAsync($"/api/{userId}/addTraining", request, idempotencyKey);

        // Assert - Both succeed
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        secondBody.Should().Be(firstBody, "training replay should return stored response");

        // Assert - Only one training created
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var trainings = await db.Trainings.Where(t => t.UserId == userId).ToListAsync();
        trainings.Should().HaveCount(1, "replay should not duplicate training");

        // Assert - Only one TrainingCompleted command envelope
        var trainingCompletedEnvelopes = await db.CommandEnvelopes
            .Where(ce => ce.PayloadJson.Contains("TrainingCompleted"))
            .ToListAsync();
        trainingCompletedEnvelopes.Should().HaveCountLessThanOrEqualTo(1,
            "replay should not create duplicate TrainingCompleted command");
    }

    [Test]
    public async Task Register_MissingIdempotencyKey_Returns400BadRequest()
    {
        // Arrange
        var request = new
        {
            name = "no-key-user",
            email = "nokey@example.com",
            password = "password123",
            cpassword = "password123",
            isVisibleInRanking = true
        };

        // Act - Send without idempotency key (deliberately don't call SetIdempotencyKey)
        ClearIdempotencyKey(); // Ensure no key present
        var response = await Client.PostAsJsonAsync("/api/register", request);

        // Assert - Returns 400 BadRequest
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorBody = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        errorBody.Should().NotBeNull();
        errorBody!.Code.Should().Be("IDEMPOTENCY_KEY_REQUIRED");
        errorBody.Message.Should().Contain("Idempotency key is required");

        // Assert - No user created (middleware blocked before handler)
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == "nokey@example.com");
        user.Should().BeNull("request should be rejected before handler execution");
    }

    [Test]
    public async Task Register_SerialSameKey_ReplaysPreviousResponseWithoutDuplicateEnvelope()
    {
        // Arrange
        var request = new
        {
            name = "serial-user",
            email = "serial@example.com",
            password = "password123",
            cpassword = "password123",
            isVisibleInRanking = true
        };

        const string idempotencyKey = "test-serial-001";
        SetIdempotencyKey(idempotencyKey);

        // Act - Send multiple requests serially (not concurrently) with same key
        // This tests replay behavior without InMemory provider's lack of unique constraint enforcement
        var responses = new List<HttpResponseMessage>();
        for (int i = 0; i < 3; i++)
        {
            responses.Add(await PostAsJsonWithApiOptionsAsync("/api/register", request));
        }

        ClearIdempotencyKey();

        // Assert - All requests succeed (replay detection)
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));

        // Assert - All responses are identical
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
        var firstBody = bodies[0];
        bodies.Should().AllSatisfy(body => body.Should().Be(firstBody));

        // Assert - Only one user created
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == "serial@example.com");
        user.Should().NotBeNull("serial replay should not duplicate user");

        // Assert - Only one command envelope created (query by user ID)
        var envelopeCount = await db.CommandEnvelopes
            .Where(ce => ce.PayloadJson.Contains(user!.Id.ToString()))
            .CountAsync();
        envelopeCount.Should().Be(1, "serial replay should not duplicate envelope");
    }

    // Response DTOs for deserialization
    private sealed class ConflictResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("providedKey")]
        public string ProvidedKey { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("existingFingerprint")]
        public string ExistingFingerprint { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("currentFingerprint")]
        public string CurrentFingerprint { get; set; } = string.Empty;
    }

    private sealed class ErrorResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
    }
}
