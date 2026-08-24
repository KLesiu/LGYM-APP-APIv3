using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class EnumLookupResponseSnapshotTests : IntegrationTestBase
{
    [Test]
    public async Task Representative_Exercise_Measurement_And_MainRecord_Responses_Match_Bilingual_Lookup_Snapshots()
    {
        var (userId, _) = await RegisterUserViaEndpointAsync(
            name: "enum-snapshot-user",
            email: "enum-snapshot@example.com",
            password: "password123");
        SetAuthorizationHeader(userId);

        var exerciseId = await CreateExerciseViaEndpointAsync(userId, "Snapshot row", BodyParts.Back);

        var addMeasurementResponse = await Client.PostAsJsonAsync("/api/measurements/add", new
        {
            bodyPart = BodyParts.BodyWeight.ToString(),
            value = 81.5,
            unit = MeasurementUnits.Kilograms.ToString()
        });
        addMeasurementResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var db = GetDbContext();
        var measurementId = await db.Measurements
            .Where(measurement => measurement.UserId == userId)
            .Select(measurement => measurement.Id)
            .SingleAsync();

        var addRecordResponse = await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{userId}/addNewRecord", new
        {
            exercise = exerciseId.ToString(),
            weight = 102.5,
            unit = WeightUnits.Kilograms.ToString(),
            date = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        });
        addRecordResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var exerciseEnglish = await GetJsonAsync($"/api/exercise/{exerciseId}/getExercise", "en");
        var exercisePolish = await GetJsonAsync($"/api/exercise/{exerciseId}/getExercise", "pl");
        exerciseEnglish.Should().Be(JsonSerializer.Serialize(new
        {
            _id = exerciseId.ToString(),
            name = "Snapshot row",
            displayName = "Snapshot row",
            user = userId.ToString(),
            bodyPart = new { id = "Back", name = "Back", displayName = "Back" },
            eloFormula = new { id = "Standard", displayName = "Standard" },
            description = "Test description",
            image = (string?)null
        }, SnapshotOptions));
        exercisePolish.Should().Be(JsonSerializer.Serialize(new
        {
            _id = exerciseId.ToString(),
            name = "Snapshot row",
            displayName = "Snapshot row",
            user = userId.ToString(),
            bodyPart = new { id = "Back", name = "Plecy", displayName = "Plecy" },
            eloFormula = new { id = "Standard", displayName = "Standardowa" },
            description = "Test description",
            image = (string?)null
        }, SnapshotOptions));

        var measurementEnglish = await GetJsonAsync($"/api/measurements:/{measurementId}/getMeasurementDetail", "en");
        var measurementPolish = await GetJsonAsync($"/api/measurements:/{measurementId}/getMeasurementDetail", "pl");
        using var measurementDocument = JsonDocument.Parse(measurementEnglish);
        var createdAt = measurementDocument.RootElement.GetProperty("createdAt").GetString();
        var updatedAt = measurementDocument.RootElement.GetProperty("updatedAt").GetString();
        measurementEnglish.Should().Be(JsonSerializer.Serialize(new
        {
            user = userId.ToString(),
            bodyPart = new { id = "BodyWeight", name = "Body weight", displayName = "Body weight" },
            unit = new { id = "Kilograms", name = "kg", displayName = "kg" },
            value = 81.5,
            createdAt,
            updatedAt
        }, SnapshotOptions));
        measurementPolish.Should().Be(JsonSerializer.Serialize(new
        {
            user = userId.ToString(),
            bodyPart = new { id = "BodyWeight", name = "Masa ciała", displayName = "Masa ciała" },
            unit = new { id = "Kilograms", name = "kg", displayName = "kg" },
            value = 81.5,
            createdAt,
            updatedAt
        }, SnapshotOptions));

        var mainRecordEnglish = await GetJsonAsync($"/api/mainRecords/{userId}/getLastMainRecords", "en");
        var mainRecordPolish = await GetJsonAsync($"/api/mainRecords/{userId}/getLastMainRecords", "pl");
        using var mainRecordDocument = JsonDocument.Parse(mainRecordEnglish);
        var mainRecord = mainRecordDocument.RootElement.EnumerateArray().Single();
        var recordId = mainRecord.GetProperty("_id").GetString();
        var date = mainRecord.GetProperty("date").GetString();
        mainRecordEnglish.Should().Be(JsonSerializer.Serialize(new[]
        {
            new
            {
                _id = recordId, weight = 102.5, date, unit = new { id = "Kilograms", name = "kg", displayName = "kg" }, exercise = exerciseId.ToString(),
                exerciseDetails = new
                {
                    _id = exerciseId.ToString(), name = "Snapshot row", displayName = "Snapshot row", user = userId.ToString(),
                    bodyPart = new { id = "Back", name = "Back", displayName = "Back" },
                    eloFormula = new { id = "Standard", displayName = "Standard" }, description = "Test description", image = (string?)null
                }
            }
        }, SnapshotOptions));
        mainRecordPolish.Should().Be(JsonSerializer.Serialize(new[]
        {
            new
            {
                _id = recordId, weight = 102.5, date, unit = new { id = "Kilograms", name = "kg", displayName = "kg" }, exercise = exerciseId.ToString(),
                exerciseDetails = new
                {
                    _id = exerciseId.ToString(), name = "Snapshot row", displayName = "Snapshot row", user = userId.ToString(),
                    bodyPart = new { id = "Back", name = "Plecy", displayName = "Plecy" },
                    eloFormula = new { id = "Standard", displayName = "Standardowa" }, description = "Test description", image = (string?)null
                }
            }
        }, SnapshotOptions));
    }

    private async Task<string> GetJsonAsync(string path, string culture)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.AcceptLanguage.ParseAdd(culture);

        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static readonly JsonSerializerOptions SnapshotOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
