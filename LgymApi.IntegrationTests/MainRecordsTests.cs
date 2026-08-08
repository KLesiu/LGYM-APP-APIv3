using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class MainRecordsTests : IntegrationTestBase
{
    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/mainRecords/{id}/addNewRecord", "own", "owner-allow")]
    public async Task AddNewRecord_WithValidData_CreatesRecord()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "recorduser",
            email: "record@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Bench Press", BodyParts.Chest);

        var request = new
        {
            exercise = exerciseId.ToString(),
            weight = 100.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userId}/addNewRecord", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Created");
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/mainRecords/{id}/addNewRecord", "own", "foreign-object-denial-no-mutation")]
    public async Task AddNewRecord_WithMismatchedRouteUserId_ReturnsForbidden()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "recorduser2",
            email: "record2@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Deadlift", BodyParts.Back);

        var (otherUserId, _) = await RegisterUserViaEndpointAsync(
            name: "recordother",
            email: "recordother@example.com",
            password: "password123");

        var request = new
        {
            exercise = exerciseId.ToString(),
            weight = 150.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{otherUserId}/addNewRecord", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/mainRecords/{id}/getMainRecordsHistory", "own", "foreign-object-denial-no-mutation")]
    public async Task GetMainRecordsHistory_WithMismatchedRouteUserId_ReturnsForbidden()
    {
        var (_, userAToken) = await RegisterUserViaEndpointAsync(
            name: "historymismatch-a",
            email: "historymismatch-a@example.com",
            password: "password123");

        var (userBId, _) = await RegisterUserViaEndpointAsync(
            name: "historymismatch-b",
            email: "historymismatch-b@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userAToken);

        var response = await Client.GetAsync($"/api/mainRecords/{userBId}/getMainRecordsHistory");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/mainRecords/{id}/getLastMainRecords", "own", "foreign-object-denial-no-mutation")]
    public async Task GetLastMainRecords_WithMismatchedRouteUserId_ReturnsForbidden()
    {
        var (_, userAToken) = await RegisterUserViaEndpointAsync(
            name: "lastmainmismatch-a",
            email: "lastmainmismatch-a@example.com",
            password: "password123");

        var (userBId, _) = await RegisterUserViaEndpointAsync(
            name: "lastmainmismatch-b",
            email: "lastmainmismatch-b@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userAToken);

        var response = await Client.GetAsync($"/api/mainRecords/{userBId}/getLastMainRecords");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AddNewRecord_WithInvalidExerciseId_ReturnsNotFound()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "recorduser3",
            email: "record3@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var nonExistentExerciseId = Domain.ValueObjects.Id<Exercise>.New();
        var request = new
        {
            exercise = nonExistentExerciseId.ToString(),
            weight = 100.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userId}/addNewRecord", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetMainRecordsHistory_WithNoRecords_ReturnsNotFound()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "historyuser",
            email: "history@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/mainRecords/{userId}/getMainRecordsHistory");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/mainRecords/{id}/getMainRecordsHistory", "own", "owner-allow")]
    public async Task GetMainRecordsHistory_WithRecords_ReturnsHistory()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "historyuser2",
            email: "history2@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Squat", BodyParts.Quads);

        var addRequest = new
        {
            exercise = exerciseId.ToString(),
            weight = 120.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        };
        await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userId}/addNewRecord", addRequest);

        var response = await Client.GetAsync($"/api/mainRecords/{userId}/getMainRecordsHistory");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<MainRecordResponse>>();
        body.Should().NotBeNull();
        body.Should().HaveCountGreaterThanOrEqualTo(1);
        body![0].Weight.Should().Be(120.0);
    }

    [Test]
    public async Task GetLastMainRecords_WithNoRecords_ReturnsNotFound()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "lastuser",
            email: "last@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/mainRecords/{userId}/getLastMainRecords");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/mainRecords/{id}/getLastMainRecords", "own", "owner-allow")]
    public async Task GetLastMainRecords_WithRecords_ReturnsGreatestPerExercise()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "lastuser2",
            email: "last2@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "OHP", BodyParts.Shoulders);

        var firstRequest = new
        {
            exercise = exerciseId.ToString(),
            weight = 50.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow.AddDays(-1)
        };
        await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userId}/addNewRecord", firstRequest);

        var secondRequest = new
        {
            exercise = exerciseId.ToString(),
            weight = 55.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        };
        await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userId}/addNewRecord", secondRequest);

        var response = await Client.GetAsync($"/api/mainRecords/{userId}/getLastMainRecords");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<MainRecordWithExerciseResponse>>();
        body.Should().NotBeNull();
        body.Should().HaveCount(1);
        body![0].Weight.Should().Be(55.0);
        body[0].ExerciseDetails.Id.Should().Be(exerciseId.ToString());
        body[0].ExerciseDetails.Name.Should().Be("OHP");
    }

    [Test]
    public async Task GetLastMainRecords_WhenLatestIsNotGreatest_ReturnsGreatestPerExercise()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "lastuser3",
            email: "last3@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Strict OHP", BodyParts.Shoulders);

        var heavierOlderRequest = new
        {
            exercise = exerciseId.ToString(),
            weight = 60.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow.AddDays(-2)
        };
        await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userId}/addNewRecord", heavierOlderRequest);

        var lighterLatestRequest = new
        {
            exercise = exerciseId.ToString(),
            weight = 55.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        };
        await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userId}/addNewRecord", lighterLatestRequest);

        var response = await Client.GetAsync($"/api/mainRecords/{userId}/getLastMainRecords");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<MainRecordWithExerciseResponse>>();
        body.Should().NotBeNull();
        body.Should().HaveCount(1);
        body![0].Weight.Should().Be(60.0);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/mainRecords/{id}/deleteMainRecord", "own", "owner-allow")]
    public async Task DeleteMainRecord_WithValidId_DeletesRecord()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "deleteuser",
            email: "delete@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Curl", BodyParts.Biceps);

        var addRequest = new
        {
            exercise = exerciseId.ToString(),
            weight = 20.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        };
        await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userId}/addNewRecord", addRequest);

        var historyResponse = await Client.GetAsync($"/api/mainRecords/{userId}/getMainRecordsHistory");
        var records = await historyResponse.Content.ReadFromJsonAsync<List<MainRecordResponse>>();
        var recordId = records![0].Id;

        var deleteResponse = await Client.GetAsync($"/api/mainRecords/{recordId}/deleteMainRecord");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var verifyResponse = await Client.GetAsync($"/api/mainRecords/{userId}/getMainRecordsHistory");
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeleteMainRecord_WithInvalidId_ReturnsNotFound()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "deleteuser2",
            email: "delete2@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var nonExistentId = Domain.ValueObjects.Id<MainRecord>.New();
        var response = await Client.GetAsync($"/api/mainRecords/{nonExistentId}/deleteMainRecord");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeleteMainRecord_WithNonGuidRouteId_ReturnsBadRequest()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "deleteuser3",
            email: "delete3@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Delete Invalid Route", BodyParts.Back);

        var addRequest = new
        {
            exercise = exerciseId.ToString(),
            weight = 42.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        };
        await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userId}/addNewRecord", addRequest);

        var response = await Client.GetAsync("/api/mainRecords/not-a-guid/deleteMainRecord");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("GET", "/api/mainRecords/{id}/deleteMainRecord", "own", "foreign-object-denial-no-mutation")]
    public async Task DeleteMainRecord_WithOtherUsersRecord_ReturnsForbidden()
    {
        var (_, userAToken) = await RegisterUserViaEndpointAsync(
            name: "deletenonowner-a",
            email: "deletenonowner-a@example.com",
            password: "password123");

        var (userBId, userBToken) = await RegisterUserViaEndpointAsync(
            name: "deletenonowner-b",
            email: "deletenonowner-b@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userBToken);

        var exerciseId = await CreateExerciseViaEndpointAsync(userBId, "Delete NonOwner", BodyParts.Back);

        var addRequest = new
        {
            exercise = exerciseId.ToString(),
            weight = 66.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        };
        await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userBId}/addNewRecord", addRequest);

        var historyResponse = await Client.GetAsync($"/api/mainRecords/{userBId}/getMainRecordsHistory");
        var records = await historyResponse.Content.ReadFromJsonAsync<List<MainRecordResponse>>();
        var recordId = records![0].Id;

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userAToken);

        var deleteResponse = await Client.GetAsync($"/api/mainRecords/{recordId}/deleteMainRecord");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/mainRecords/{id}/updateMainRecords", "own", "owner-allow")]
    public async Task UpdateMainRecords_WithValidData_UpdatesRecord()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "updateuser",
            email: "update@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Rows", BodyParts.Back);

        var addRequest = new
        {
            exercise = exerciseId.ToString(),
            weight = 70.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        };
        await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userId}/addNewRecord", addRequest);

        var historyResponse = await Client.GetAsync($"/api/mainRecords/{userId}/getMainRecordsHistory");
        var records = await historyResponse.Content.ReadFromJsonAsync<List<MainRecordResponse>>();
        var recordId = records![0].Id;

        var updateRequest = new
        {
            _id = recordId,
            exercise = exerciseId.ToString(),
            weight = 80.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        };

        var updateResponse = await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userId}/updateMainRecords", updateRequest);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var verifyResponse = await Client.GetAsync($"/api/mainRecords/{userId}/getMainRecordsHistory");
        var updatedRecords = await verifyResponse.Content.ReadFromJsonAsync<List<MainRecordResponse>>();
        updatedRecords![0].Weight.Should().Be(80.0);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/mainRecords/{id}/updateMainRecords", "own", "foreign-object-denial-no-mutation")]
    public async Task UpdateMainRecords_WithOtherUsersRecordAndOwnRouteUserId_ReturnsForbidden()
    {
        var (userAId, userAToken) = await RegisterUserViaEndpointAsync(
            name: "updatenonowner-a",
            email: "updatenonowner-a@example.com",
            password: "password123");

        var (userBId, userBToken) = await RegisterUserViaEndpointAsync(
            name: "updatenonowner-b",
            email: "updatenonowner-b@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userBToken);

        var exerciseId = await CreateExerciseViaEndpointAsync(userBId, "Update NonOwner", BodyParts.Shoulders);

        var addRequest = new
        {
            exercise = exerciseId.ToString(),
            weight = 45.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        };
        await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userBId}/addNewRecord", addRequest);

        var historyResponse = await Client.GetAsync($"/api/mainRecords/{userBId}/getMainRecordsHistory");
        var records = await historyResponse.Content.ReadFromJsonAsync<List<MainRecordResponse>>();
        var recordId = records![0].Id;

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userAToken);

        var updateRequest = new
        {
            _id = recordId,
            exercise = exerciseId.ToString(),
            weight = 47.5,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userAId}/updateMainRecords", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task UpdateMainRecords_WithMismatchedRouteUserId_ReturnsForbidden()
    {
        var (_, userAToken) = await RegisterUserViaEndpointAsync(
            name: "updateroute-a",
            email: "updateroute-a@example.com",
            password: "password123");

        var (userBId, userBToken) = await RegisterUserViaEndpointAsync(
            name: "updateroute-b",
            email: "updateroute-b@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userBToken);

        var exerciseId = await CreateExerciseViaEndpointAsync(userBId, "Update Route Mismatch", BodyParts.Shoulders);

        var addRequest = new
        {
            exercise = exerciseId.ToString(),
            weight = 45.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        };
        await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userBId}/addNewRecord", addRequest);

        var historyResponse = await Client.GetAsync($"/api/mainRecords/{userBId}/getMainRecordsHistory");
        var records = await historyResponse.Content.ReadFromJsonAsync<List<MainRecordResponse>>();
        var recordId = records![0].Id;

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userAToken);

        var updateRequest = new
        {
            _id = recordId,
            exercise = exerciseId.ToString(),
            weight = 47.5,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userBId}/updateMainRecords", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/mainRecords/getRecordOrPossibleRecordInExercise", "own", "owner-allow")]
    public async Task GetRecordOrPossibleRecordInExercise_WithRecord_ReturnsRecord()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "possibleuser",
            email: "possible@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Dips", BodyParts.Triceps);

        var addRequest = new
        {
            exercise = exerciseId.ToString(),
            weight = 30.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        };
        await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userId}/addNewRecord", addRequest);

        var request = new { exerciseId = exerciseId.ToString() };
        var response = await Client.PostAsJsonAsync("/api/mainRecords/getRecordOrPossibleRecordInExercise", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PossibleRecordResponse>();
        body.Should().NotBeNull();
        body!.Weight.Should().Be(30.0);
        body.Reps.Should().Be(1);
    }

    [Test]
    public async Task GetRecordOrPossibleRecordInExercise_WithTrainingCreatedRecord_ReturnsMainRecordShape()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "possibleuser2",
            email: "possible2@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Pullups", BodyParts.Back);
        var gymId = await CreateGymViaEndpointAsync(userId, "Possible Gym");
        var planId = await CreatePlanViaEndpointAsync(userId, "Possible Plan");
        var planDayId = await CreatePlanDayViaEndpointAsync(userId, planId, "Possible Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 3, Reps = "10" }
        });

        var trainingRequest = new
        {
            gym = gymId.ToString(),
            type = planDayId.ToString(),
            createdAt = DateTime.UtcNow,
            exercises = new[]
            {
                new { exercise = exerciseId.ToString(), series = 1, reps = 12, weight = 0.0, unit = WeightUnits.Kilograms.ToString() }
            }
        };
        await PostAsJsonWithApiOptionsAsync($"/api/{userId}/addTraining", trainingRequest);

        await ProcessPendingCommandsAsync();

        var request = new { exerciseId = exerciseId.ToString() };
        var response = await Client.PostAsJsonAsync("/api/mainRecords/getRecordOrPossibleRecordInExercise", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PossibleRecordResponse>();
        body.Should().NotBeNull();
        body!.Reps.Should().Be(1);
    }

    [Test]
    public async Task GetRecordOrPossibleRecordInExercise_WithNoData_ReturnsNotFound()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "possibleuser3",
            email: "possible3@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "NoData Exercise", BodyParts.Chest);

        var request = new { exerciseId = exerciseId.ToString() };
        var response = await Client.PostAsJsonAsync("/api/mainRecords/getRecordOrPossibleRecordInExercise", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AddNewRecord_WithAliasUnit_ReturnsBadRequest()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "recordstrict",
            email: "recordstrict@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Strict Bench", BodyParts.Chest);

        var request = new
        {
            exercise = exerciseId.ToString(),
            weight = 95.0,
            unit = "kg",
            date = DateTime.UtcNow
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userId}/addNewRecord", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed class MainRecordResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("exercise")]
        public string ExerciseId { get; set; } = string.Empty;

        [JsonPropertyName("weight")]
        public double Weight { get; set; }

        [JsonPropertyName("date")]
        public DateTime Date { get; set; }
    }

    private sealed class MainRecordWithExerciseResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("weight")]
        public double Weight { get; set; }

        [JsonPropertyName("exerciseDetails")]
        public ExerciseDetailsResponse ExerciseDetails { get; set; } = new();
    }

    private sealed class ExerciseDetailsResponse
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class PossibleRecordResponse
    {
        [JsonPropertyName("weight")]
        public double Weight { get; set; }

        [JsonPropertyName("reps")]
        public int Reps { get; set; }
    }
}
