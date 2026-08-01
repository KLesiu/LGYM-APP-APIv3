using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Enums;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class ExerciseScoresTests : IntegrationTestBase
{
    [Test]
    public async Task GetExerciseScoresChartData_WithNoScores_ReturnsEmptyList()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "chartuser",
            email: "chart@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Chart Exercise", BodyParts.Chest);

        var request = new { exerciseId = exerciseId.ToString() };
        var response = await Client.PostAsJsonAsync($"/api/exerciseScores/{userId}/getExerciseScoresChartData", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<ChartDataEntry>>();
        body.Should().NotBeNull();
        body.Should().BeEmpty();
    }

    [Test]
    public async Task GetExerciseScoresChartData_WithScores_ReturnsChartData()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "chartuser2",
            email: "chart2@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Chart Exercise 2", BodyParts.Chest);
        var gymId = await CreateGymViaEndpointAsync(userId, "Chart Gym");
        var planId = await CreatePlanViaEndpointAsync(userId, "Chart Plan");
        var planDayId = await CreatePlanDayViaEndpointAsync(userId, planId, "Chart Day", new List<PlanDayExerciseInput>
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
                new { exercise = exerciseId.ToString(), series = 1, reps = 10, weight = 50.0, unit = WeightUnits.Kilograms.ToString() },
                new { exercise = exerciseId.ToString(), series = 2, reps = 8, weight = 55.0, unit = WeightUnits.Kilograms.ToString() }
            }
        };

        await PostAsJsonWithApiOptionsAsync($"/api/{userId}/addTraining", trainingRequest);

        var chartRequest = new { exerciseId = exerciseId.ToString() };
        var response = await Client.PostAsJsonAsync($"/api/exerciseScores/{userId}/getExerciseScoresChartData", chartRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<ChartDataEntry>>();
        body.Should().NotBeNull();
        body.Should().HaveCountGreaterThanOrEqualTo(1);
        body!.Select(entry => entry.ExerciseId).Should().OnlyContain(id => id == exerciseId.ToString());

        var repeatResponse = await Client.PostAsJsonAsync($"/api/exerciseScores/{userId}/getExerciseScoresChartData", chartRequest);
        repeatResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var repeatBody = await repeatResponse.Content.ReadFromJsonAsync<List<ChartDataEntry>>();
        repeatBody.Should().NotBeNull();
        repeatBody!.Select(entry => entry.Id).Should().Equal(body.Select(entry => entry.Id));
        repeatBody.Select(entry => entry.ExerciseId).Should().Equal(body.Select(entry => entry.ExerciseId));
        TestContext.Progress.WriteLine(System.Text.Json.JsonSerializer.Serialize(body));
    }

    [Test]
    public async Task GetExerciseScoresChartData_WithMismatchedRouteUserId_ReturnsForbidden()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "authuser",
            email: "auth@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Auth Exercise", BodyParts.Chest);

        var (otherUserId, _) = await RegisterUserViaEndpointAsync(
            name: "chartother",
            email: "chartother@example.com",
            password: "password123");

        var request = new { exerciseId = exerciseId.ToString() };
        var response = await Client.PostAsJsonAsync($"/api/exerciseScores/{otherUserId}/getExerciseScoresChartData", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task GetExerciseScoresChartData_WithMalformedExerciseId_ReturnsBadRequest()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "chart-invalid-exercise",
            email: "chart-invalid-exercise@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.PostAsJsonAsync($"/api/exerciseScores/{userId}/getExerciseScoresChartData", new
        {
            exerciseId = "not-a-uuid"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetLastExerciseScores_WithNoScores_ReturnsNullScores()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "lastscoresuser",
            email: "lastscores@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "LastScores Exercise", BodyParts.Back);

        var request = new
        {
            exerciseId = exerciseId.ToString(),
            exerciseName = "LastScores Exercise",
            series = 3
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/exercise/{userId}/getLastExerciseScores", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LastExerciseScoresResponse>();
        body.Should().NotBeNull();
        body!.ExerciseId.Should().Be(exerciseId.ToString());
        body.SeriesScores.Should().HaveCount(3);
        body.SeriesScores.Should().AllSatisfy(s => s.Score.Should().BeNull());
    }

    [Test]
    public async Task GetLastExerciseScores_WithSeriesAboveLimit_ReturnsBadRequest()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "lastscoresuser-limit",
            email: "lastscores-limit@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "LastScores Limit Exercise", BodyParts.Back);

        var request = new
        {
            exerciseId = exerciseId.ToString(),
            exerciseName = "LastScores Limit Exercise",
            series = 31
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/exercise/{userId}/getLastExerciseScores", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task GetLastExerciseScores_WithExistingScores_ReturnsPreviousScores()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "lastscoresuser2",
            email: "lastscores2@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "LastScores Exercise 2", BodyParts.Back);
        var gymId = await CreateGymViaEndpointAsync(userId, "LastScores Gym");
        var planId = await CreatePlanViaEndpointAsync(userId, "LastScores Plan");
        var planDayId = await CreatePlanDayViaEndpointAsync(userId, planId, "LastScores Day", new List<PlanDayExerciseInput>
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
                new { exercise = exerciseId.ToString(), series = 1, reps = 10, weight = 60.0, unit = WeightUnits.Kilograms.ToString() },
                new { exercise = exerciseId.ToString(), series = 2, reps = 8, weight = 65.0, unit = WeightUnits.Kilograms.ToString() }
            }
        };

        await PostAsJsonWithApiOptionsAsync($"/api/{userId}/addTraining", trainingRequest);

        var request = new
        {
            exerciseId = exerciseId.ToString(),
            exerciseName = "LastScores Exercise 2",
            series = 3,
            gymId = gymId.ToString()
        };

        var response = await PostAsJsonWithApiOptionsAsync($"/api/exercise/{userId}/getLastExerciseScores", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LastExerciseScoresResponse>();
        body.Should().NotBeNull();
        body!.SeriesScores[0].Score.Should().NotBeNull();
        body.SeriesScores[0].Score!.Weight.Should().Be(60.0);
        body.SeriesScores[0].Score!.GymName.Should().Be("LastScores Gym");
    }

    [Test]
    public async Task GetExerciseScoresFromTrainingByExercise_WithNoScores_ReturnsEmptyList()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "historyuser",
            email: "history@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "History Exercise", BodyParts.Quads);

        var request = new { exerciseId = exerciseId.ToString() };
        var response = await Client.PostAsJsonAsync("/api/exercise/getExerciseScoresFromTrainingByExercise", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<ExerciseHistoryItem>>();
        body.Should().NotBeNull();
        body.Should().BeEmpty();
    }

    [Test]
    public async Task GetExerciseScoresFromTrainingByExercise_WithScores_ReturnsHistory()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "historyuser2",
            email: "history2@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "History Exercise 2", BodyParts.Quads);
        var gymId = await CreateGymViaEndpointAsync(userId, "History Gym");
        var planId = await CreatePlanViaEndpointAsync(userId, "History Plan");
        var planDayId = await CreatePlanDayViaEndpointAsync(userId, planId, "History Day", new List<PlanDayExerciseInput>
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
                new { exercise = exerciseId.ToString(), series = 1, reps = 10, weight = 100.0, unit = WeightUnits.Kilograms.ToString() }
            }
        };

        await PostAsJsonWithApiOptionsAsync($"/api/{userId}/addTraining", trainingRequest);

        var request = new { exerciseId = exerciseId.ToString() };
        var response = await Client.PostAsJsonAsync("/api/exercise/getExerciseScoresFromTrainingByExercise", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<ExerciseHistoryItem>>();
        body.Should().NotBeNull();
        body.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task ExerciseScoresChartRoute_PreservesLegacyJsonContract()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "chart-contract-user",
            email: "chart-contract@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Chart Contract Exercise", BodyParts.Chest);
        var gymId = await CreateGymViaEndpointAsync(userId, "Chart Contract Gym");
        var planId = await CreatePlanViaEndpointAsync(userId, "Chart Contract Plan");
        var planDayId = await CreatePlanDayViaEndpointAsync(userId, planId, "Chart Contract Day", new List<PlanDayExerciseInput>
        {
            new() { ExerciseId = exerciseId.ToString(), Series = 1, Reps = "10" }
        });

        var createTrainingResponse = await PostAsJsonWithApiOptionsAsync($"/api/{userId}/addTraining", new
        {
            gym = gymId.ToString(),
            type = planDayId.ToString(),
            createdAt = DateTime.UtcNow,
            exercises = new[]
            {
                new { exercise = exerciseId.ToString(), series = 1, reps = 10, weight = 50.0, unit = WeightUnits.Kilograms.ToString() }
            }
        });
        createTrainingResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var chartResponse = await Client.PostAsJsonAsync($"/api/exerciseScores/{userId}/getExerciseScoresChartData", new
        {
            exerciseId = exerciseId.ToString()
        });

        chartResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var chartDocument = JsonDocument.Parse(await chartResponse.Content.ReadAsStringAsync());
        chartDocument.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        chartDocument.RootElement.GetArrayLength().Should().BeGreaterThan(0);

        var chartEntry = chartDocument.RootElement[0];
        chartEntry.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo("_id", "value", "date", "exerciseName", "exerciseId");
        chartEntry.GetProperty("_id").GetString().Should().StartWith($"{exerciseId}-");
        chartEntry.GetProperty("value").ValueKind.Should().Be(JsonValueKind.Number);
        chartEntry.GetProperty("date").ValueKind.Should().Be(JsonValueKind.String);
        chartEntry.GetProperty("exerciseName").GetString().Should().Be("Chart Contract Exercise");
        chartEntry.GetProperty("exerciseId").GetString().Should().Be(exerciseId.ToString());
    }

    private sealed class ChartDataEntry
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public double Value { get; set; }

        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("exerciseId")]
        public string ExerciseId { get; set; } = string.Empty;
    }

    private sealed class LastExerciseScoresResponse
    {
        [JsonPropertyName("exerciseId")]
        public string ExerciseId { get; set; } = string.Empty;

        [JsonPropertyName("exerciseName")]
        public string ExerciseName { get; set; } = string.Empty;

        [JsonPropertyName("seriesScores")]
        public List<SeriesScoreItem> SeriesScores { get; set; } = new();
    }

    private sealed class SeriesScoreItem
    {
        [JsonPropertyName("series")]
        public int Series { get; set; }

        [JsonPropertyName("score")]
        public ScoreItem? Score { get; set; }
    }

    private sealed class ScoreItem
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("reps")]
        public int Reps { get; set; }

        [JsonPropertyName("weight")]
        public double Weight { get; set; }

        [JsonPropertyName("gymName")]
        public string? GymName { get; set; }
    }

    private sealed class ExerciseHistoryItem
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("date")]
        public DateTime Date { get; set; }

        [JsonPropertyName("gymName")]
        public string GymName { get; set; } = string.Empty;
    }
}
