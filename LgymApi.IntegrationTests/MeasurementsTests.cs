using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class MeasurementsTests : IntegrationTestBase
{
    [Test]
    public async Task AddMeasurement_WithBodyWeight_CreatesMeasurement()
    {
        var (_, token) = await RegisterUserViaEndpointAsync(
            name: "measureuser",
            email: "measure@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var request = new
        {
            bodyPart = BodyParts.BodyWeight.ToString(),
            value = 80.5,
            unit = MeasurementUnits.Kilograms.ToString()
        };

        var response = await Client.PostAsJsonAsync("/api/measurements/add", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body!.Message.Should().Be("Created");
    }

    [Test]
    public async Task AddMeasurementsBulk_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await Client.PostAsJsonAsync("/api/measurements/add-bulk", new
        {
            measurements = new object[]
            {
                new { bodyPart = BodyParts.BodyWeight.ToString(), value = 80.2, unit = MeasurementUnits.Kilograms.ToString() }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetMeasurementsTrends_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await Client.GetAsync($"/api/measurements/{Id<UserEntity>.New()}/trends");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetMeasurementsHistory_WithLengthUnitConversion_ReturnsConvertedValues()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "historymeasureuser",
            email: "historymeasure@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        await AddMeasurementAsync(BodyParts.Waist, 120.0, MeasurementUnits.Centimeters);
        await AddMeasurementAsync(BodyParts.Waist, 1.1, MeasurementUnits.Meters);

        var response = await Client.GetAsync($"/api/measurements/{userId}/getHistory?bodyPart=Waist&unit=Centimeters");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<MeasurementsHistoryResponse>();
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            body!.Measurements.Should().HaveCount(2);
            body.Measurements[0].Value.Should().Be(120.0);
            body.Measurements[1].Value.Should().Be(110.0);
            body.Measurements[0].Unit!.Id.Should().Be(MeasurementUnits.Centimeters.ToString());
            body.Measurements[0].Unit!.Name.Should().Be(LgymApi.Resources.Enums.MeasurementUnits_Centimeters);
            body.Measurements[0].Unit!.DisplayName.Should().Be(LgymApi.Resources.Enums.MeasurementUnits_Centimeters);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Test]
    public async Task GetMeasurementsTrend_WhenValueGrows_ReturnsUpDirection()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "trendupuser",
            email: "trendup@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        await AddMeasurementAsync(BodyParts.BodyWeight, 80.0, MeasurementUnits.Kilograms);
        await AddMeasurementAsync(BodyParts.BodyWeight, 94.1, MeasurementUnits.Kilograms);

        var response = await Client.GetAsync($"/api/measurements/{userId}/trend?bodyPart=BodyWeight&unit=Kilograms");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MeasurementTrendResponse>();
        body!.FirstMeasurementValue.Should().Be(80.0);
        body.LastMeasurementValue.Should().Be(94.1);
        body.Difference.Should().Be(14.1);
        body.Direction.Should().Be("up");
    }

    [Test]
    public async Task GetMeasurementsTrend_WhenValueDrops_ReturnsDownDirection()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "trenddownuser",
            email: "trenddown@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        await AddMeasurementAsync(BodyParts.Waist, 90.0, MeasurementUnits.Centimeters);
        await AddMeasurementAsync(BodyParts.Waist, 86.6, MeasurementUnits.Centimeters);

        var response = await Client.GetAsync($"/api/measurements/{userId}/trend?bodyPart=Waist&unit=Centimeters");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MeasurementTrendResponse>();
        body!.Difference.Should().Be(3.4);
        body.Direction.Should().Be("down");
    }

    [Test]
    public async Task GetMeasurementsTrend_WhenValueDoesNotChange_ReturnsSameDirection()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "trendsameuser",
            email: "trendsame@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        await AddMeasurementAsync(BodyParts.BodyFat, 18.0, MeasurementUnits.Percent);
        await AddMeasurementAsync(BodyParts.BodyFat, 18.0, MeasurementUnits.Percent);

        var response = await Client.GetAsync($"/api/measurements/{userId}/trend?bodyPart=BodyFat&unit=Percent");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MeasurementTrendResponse>();
        body!.Difference.Should().Be(0.0);
        body.Direction.Should().Be("same");
    }

    [Test]
    public async Task GetMeasurementsTrend_WhenOnlyOneMeasurementExists_ReturnsInsufficientData()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "trendoneuser",
            email: "trendone@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        await AddMeasurementAsync(BodyParts.Neck, 40.0, MeasurementUnits.Centimeters);

        var response = await Client.GetAsync($"/api/measurements/{userId}/trend?bodyPart=Neck&unit=Centimeters");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MeasurementTrendResponse>();
        body!.Direction.Should().Be("insufficient_data");
        body.Points.Should().Be(1);
    }

    [Test]
    public async Task GetMeasurementsTrend_WhenNoMeasurementsExist_ReturnsInsufficientData()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "trendemptyuser",
            email: "trendempty@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/measurements/{userId}/trend?bodyPart=Bmi&unit=Bmi");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MeasurementTrendResponse>();
        body!.Direction.Should().Be("insufficient_data");
        body.Points.Should().Be(0);
    }

    [Test]
    public async Task GetMeasurementsTrends_WithMultipleTypes_ReturnsSummariesPerType()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "trendmultiuser",
            email: "trendmulti@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        await AddMeasurementAsync(BodyParts.BodyWeight, 80.0, MeasurementUnits.Kilograms);
        await AddMeasurementAsync(BodyParts.BodyWeight, 82.0, MeasurementUnits.Kilograms);
        await AddMeasurementAsync(BodyParts.Waist, 90.0, MeasurementUnits.Centimeters);
        await AddMeasurementAsync(BodyParts.Waist, 88.0, MeasurementUnits.Centimeters);
        await AddMeasurementAsync(BodyParts.BodyFat, 15.0, MeasurementUnits.Percent);

        var response = await Client.GetAsync($"/api/measurements/{userId}/trends");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MeasurementTrendsResponse>();
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            var bodyWeightTrend = body!.Trends.Should().ContainSingle(x => x.BodyPart != null && x.BodyPart.Id == BodyParts.BodyWeight.ToString() && x.Direction == "up").Which;
            bodyWeightTrend.BodyPart!.Name.Should().Be(LgymApi.Resources.Enums.BodyParts_BodyWeight);
            bodyWeightTrend.BodyPart.DisplayName.Should().Be(LgymApi.Resources.Enums.BodyParts_BodyWeight);

            var waistTrend = body.Trends.Should().ContainSingle(x => x.BodyPart != null && x.BodyPart.Id == BodyParts.Waist.ToString() && x.Direction == "down").Which;
            waistTrend.BodyPart!.Name.Should().Be(LgymApi.Resources.Enums.BodyParts_Waist);
            waistTrend.BodyPart.DisplayName.Should().Be(LgymApi.Resources.Enums.BodyParts_Waist);

            var bodyFatTrend = body.Trends.Should().ContainSingle(x => x.BodyPart != null && x.BodyPart.Id == BodyParts.BodyFat.ToString() && x.Direction == "insufficient_data").Which;
            bodyFatTrend.BodyPart!.Name.Should().Be(LgymApi.Resources.Enums.BodyParts_BodyFat);
            bodyFatTrend.BodyPart.DisplayName.Should().Be(LgymApi.Resources.Enums.BodyParts_BodyFat);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Test]
    public async Task AddMeasurementsBulk_WithMultipleOptionalMeasurements_CreatesAllProvidedMeasurements()
    {
        var (userId, token) = await RegisterUserViaEndpointAsync(
            name: "bulkmeasureuser",
            email: "bulkmeasure@example.com",
            password: "password123");

        Client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.PostAsJsonAsync("/api/measurements/add-bulk", new
        {
            measurements = new object[]
            {
                new { bodyPart = BodyParts.BodyWeight.ToString(), value = 80.2, unit = MeasurementUnits.Kilograms.ToString() },
                new { bodyPart = BodyParts.Waist.ToString(), value = 89.4, unit = MeasurementUnits.Centimeters.ToString() },
                new { bodyPart = BodyParts.BodyFat.ToString(), value = 16.1, unit = MeasurementUnits.Percent.ToString() }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var historyResponse = await Client.GetFromJsonAsync<MeasurementsHistoryResponse>($"/api/measurements/{userId}/getHistory");
        historyResponse!.Measurements.Should().HaveCount(3);
    }

    [Test]
    public async Task GetMeasurementDetail_UsesLegacyColonRouteAndEnforcesOwnership()
    {
        var owner = await SeedUserAsync(name: "measurement-owner", email: "measurement-owner@example.com");
        var otherUser = await SeedUserAsync(name: "measurement-other", email: "measurement-other@example.com");
        SetAuthorizationHeader(owner.Id);

        var addResponse = await Client.PostAsJsonAsync("/api/measurements/add", new
        {
            bodyPart = BodyParts.BodyWeight.ToString(),
            value = 75.5,
            unit = MeasurementUnits.Kilograms.ToString()
        });
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var db = GetDbContext();
        var measurementId = await db.Measurements
            .Where(measurement => measurement.UserId == owner.Id)
            .Select(measurement => measurement.Id)
            .SingleAsync();

        SetAuthorizationHeader(otherUser.Id);
        var forbiddenResponse = await Client.GetAsync($"/api/measurements:/{measurementId}/getMeasurementDetail");
        forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        SetAuthorizationHeader(owner.Id);
        var detailResponse = await Client.GetAsync($"/api/measurements:/{measurementId}/getMeasurementDetail");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var detailJson = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        detailJson.RootElement.GetProperty("user").GetString().Should().Be(owner.Id.ToString());
        detailJson.RootElement.GetProperty("value").GetDouble().Should().Be(75.5);
        detailJson.RootElement.GetProperty("bodyPart").GetProperty("id").GetString().Should().Be(BodyParts.BodyWeight.ToString());
        detailJson.RootElement.GetProperty("unit").GetProperty("id").GetString().Should().Be(MeasurementUnits.Kilograms.ToString());
    }

    [Test]
    public async Task GetMeasurementDetail_WithMalformedId_ReturnsBadRequest()
    {
        var user = await SeedUserAsync(name: "measurement-invalid-id", email: "measurement-invalid-id@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.GetAsync("/api/measurements:/not-a-guid/getMeasurementDetail");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task AddMeasurementAsync(BodyParts bodyPart, double value, MeasurementUnits unit)
    {
        var response = await Client.PostAsJsonAsync("/api/measurements/add", new
        {
            bodyPart = bodyPart.ToString(),
            value,
            unit = unit.ToString()
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed class MeasurementsHistoryResponse
    {
        [JsonPropertyName("measurements")]
        public List<MeasurementResponse> Measurements { get; set; } = new();
    }

    private sealed class MeasurementTrendsResponse
    {
        [JsonPropertyName("trends")]
        public List<MeasurementTrendResponse> Trends { get; set; } = new();
    }

    private sealed class MeasurementResponse
    {
        [JsonPropertyName("bodyPart")]
        public BodyPartLookup? BodyPart { get; set; }

        [JsonPropertyName("unit")]
        public UnitLookup? Unit { get; set; }

        [JsonPropertyName("value")]
        public double Value { get; set; }
    }

    private sealed class BodyPartLookup
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;
    }

    private sealed class UnitLookup
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;
    }

    private sealed class MeasurementTrendResponse
    {
        [JsonPropertyName("bodyPart")]
        public BodyPartLookup? BodyPart { get; set; }

        [JsonPropertyName("firstMeasurementValue")]
        public double? FirstMeasurementValue { get; set; }

        [JsonPropertyName("lastMeasurementValue")]
        public double? LastMeasurementValue { get; set; }

        [JsonPropertyName("difference")]
        public double? Difference { get; set; }

        [JsonPropertyName("direction")]
        public string Direction { get; set; } = string.Empty;

        [JsonPropertyName("points")]
        public int Points { get; set; }
    }
}
