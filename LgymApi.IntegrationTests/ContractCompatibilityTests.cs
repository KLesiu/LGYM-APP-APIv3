using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class ContractCompatibilityTests : IntegrationTestBase
{
    [Test]
    public async Task Register_ReturnsLegacyMsgField()
    {
        var request = new
        {
            name = "contract_user",
            email = "contract_user@example.com",
            password = "password123",
            cpassword = "password123",
            isVisibleInRanking = true
        };

        // Set idempotency key for registration endpoint (required by T9 middleware)
        SetIdempotencyKey("test-contract-register-legacy-msg");

        var response = await Client.PostAsJsonAsync("/api/register", request);

        // Clear idempotency key after request
        ClearIdempotencyKey();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = await ReadJsonAsync(response);
        AssertLegacyMsgFieldPresent(json, "msg property must be present for backward compatibility");
    }

    [Test]
    public async Task Login_ReturnsLegacyReqField()
    {
        await SeedUserAsync(name: "contract_login", email: "contract_login@example.com", password: "password123");

        var response = await Client.PostAsJsonAsync("/api/login", new
        {
            name = "contract_login",
            password = "password123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = await ReadJsonAsync(response);
        json.RootElement.TryGetProperty("token", out var token).Should().BeTrue();
        token.GetString().Should().NotBeNullOrWhiteSpace();

        AssertLegacyReqFieldPresent(json, requireIdField: true, requireNameField: true);
        json.RootElement.TryGetProperty("req", out var req).Should().BeTrue();
        req.TryGetProperty("name", out var userName).Should().BeTrue();
        userName.GetString().Should().Be("contract_login");

        // Contract guard: login payload must keep `req` (legacy shape), not `user`.
        json.RootElement.TryGetProperty("user", out _).Should().BeFalse();
    }

    [Test]
    public async Task AppConfig_GetAppVersion_ReturnsExpectedJsonShape()
    {
        var admin = await SeedAdminAsync();
        SetAuthorizationHeader(admin.Id);

        var createResponse = await Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{admin.Id}", new
        {
            platform = Platforms.Android.ToString(),
            minRequiredVersion = "1.2.3",
            latestVersion = "2.0.0",
            forceUpdate = true,
            updateUrl = "https://example.com/app",
            releaseNotes = "contract-check"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var getResponse = await Client.PostAsJsonAsync("/api/appConfig/getAppVersion", new
        {
            platform = Platforms.Android.ToString()
        });
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = await ReadJsonAsync(getResponse);
        json.RootElement.TryGetProperty("minRequiredVersion", out _).Should().BeTrue();
        json.RootElement.TryGetProperty("latestVersion", out _).Should().BeTrue();
        json.RootElement.TryGetProperty("forceUpdate", out _).Should().BeTrue();
        json.RootElement.TryGetProperty("updateUrl", out _).Should().BeTrue();
        json.RootElement.TryGetProperty("releaseNotes", out _).Should().BeTrue();
    }

    [Test]
    public async Task MainRecords_PossibleRecord_ReturnsExpectedJsonShape()
    {
        var user = await SeedUserAsync(name: "contract_mainrecord", email: "contract_mainrecord@example.com");
        SetAuthorizationHeader(user.Id);

        var exerciseId = await CreateExerciseViaEndpointAsync(user.Id, name: "Contract Main Record Exercise");

        var addResponse = await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{user.Id}/addNewRecord", new
        {
            exercise = exerciseId.ToString(),
            weight = 100.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        });
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Client.PostAsJsonAsync("/api/mainRecords/getRecordOrPossibleRecordInExercise", new
        {
            exerciseId = exerciseId.ToString()
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = await ReadJsonAsync(response);
        json.RootElement.TryGetProperty("weight", out _).Should().BeTrue();
        json.RootElement.TryGetProperty("reps", out _).Should().BeTrue();
        json.RootElement.TryGetProperty("date", out _).Should().BeTrue();

        json.RootElement.TryGetProperty("unit", out var unit).Should().BeTrue();
        unit.TryGetProperty("name", out var unitName).Should().BeTrue();
        unitName.GetString().Should().NotBeNullOrWhiteSpace();
        unit.TryGetProperty("displayName", out var unitDisplayName).Should().BeTrue();
        unitDisplayName.GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task Measurements_LegacyRoutes_PreserveMessagesAndObjectEnvelopes()
    {
        var user = await SeedUserAsync(name: "contract_measurements", email: "contract_measurements@example.com");
        SetAuthorizationHeader(user.Id);

        var addResponse = await Client.PostAsJsonAsync("/api/measurements/add", new
        {
            bodyPart = BodyParts.BodyWeight.ToString(),
            unit = MeasurementUnits.Kilograms.ToString(),
            value = 80.5
        });
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var addJson = await ReadJsonAsync(addResponse))
        {
            AssertLegacyMsgFieldPresent(addJson, "measurement add must return the legacy msg envelope");
            addJson.RootElement.GetProperty("msg").GetString().Should().Be("Created");
        }

        var bulkResponse = await Client.PostAsJsonAsync("/api/measurements/add-bulk", new
        {
            measurements = new[]
            {
                new
                {
                    bodyPart = BodyParts.BodyWeight.ToString(),
                    unit = MeasurementUnits.Kilograms.ToString(),
                    value = 81.0
                },
                new
                {
                    bodyPart = BodyParts.Waist.ToString(),
                    unit = MeasurementUnits.Centimeters.ToString(),
                    value = 90.0
                }
            }
        });
        bulkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var bulkJson = await ReadJsonAsync(bulkResponse))
        {
            AssertLegacyMsgFieldPresent(bulkJson, "measurement bulk add must return the legacy msg envelope");
            bulkJson.RootElement.GetProperty("msg").GetString().Should().Be("Created");
        }

        foreach (var route in new[]
                 {
                     $"/api/measurements/{user.Id}/getHistory?bodyPart=BodyWeight&unit=Kilograms",
                     $"/api/measurements/{user.Id}/list?bodyPart=BodyWeight&unit=Kilograms"
                 })
        {
            var response = await Client.GetAsync(route);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var json = await ReadJsonAsync(response);
            json.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
            var measurements = json.RootElement.GetProperty("measurements");
            measurements.ValueKind.Should().Be(JsonValueKind.Array);
            var measurementItems = measurements.EnumerateArray().ToList();
            measurementItems.Should().HaveCount(2);
            measurementItems.Should().OnlyContain(measurement =>
                measurement.GetProperty("user").GetString() == user.Id.ToString()
                && measurement.GetProperty("bodyPart").GetProperty("id").GetString() == BodyParts.BodyWeight.ToString()
                && measurement.GetProperty("unit").GetProperty("id").GetString() == MeasurementUnits.Kilograms.ToString());
            measurementItems.Select(measurement => measurement.GetProperty("value").GetDouble()).Should().BeEquivalentTo([80.5, 81.0]);
            AssertEnumLookup(measurementItems[0].GetProperty("bodyPart"), BodyParts.BodyWeight.ToString());
            AssertEnumLookup(measurementItems[0].GetProperty("unit"), MeasurementUnits.Kilograms.ToString());
        }

        var trendResponse = await Client.GetAsync($"/api/measurements/{user.Id}/trend?bodyPart=BodyWeight&unit=Kilograms");
        trendResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var trendJson = await ReadJsonAsync(trendResponse))
        {
            trendJson.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
            AssertEnumLookup(trendJson.RootElement.GetProperty("bodyPart"), BodyParts.BodyWeight.ToString());
            AssertEnumLookup(trendJson.RootElement.GetProperty("unit"), MeasurementUnits.Kilograms.ToString());
            trendJson.RootElement.GetProperty("direction").GetString().Should().Be("up");
            trendJson.RootElement.GetProperty("points").GetInt32().Should().Be(2);
        }

        var trendsResponse = await Client.GetAsync($"/api/measurements/{user.Id}/trends");
        trendsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var trendsJson = await ReadJsonAsync(trendsResponse);
        trendsJson.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        var trends = trendsJson.RootElement.GetProperty("trends");
        trends.ValueKind.Should().Be(JsonValueKind.Array);
        trends.EnumerateArray().ToList().Should().Contain(trend => trend.GetProperty("bodyPart").GetProperty("id").GetString() == BodyParts.BodyWeight.ToString());
    }

    [Test]
    public async Task MainRecords_LegacyRoutes_PreserveMessagesIdsAndEnumLookups()
    {
        var user = await SeedUserAsync(name: "contract_main_records", email: "contract_main_records@example.com");
        SetAuthorizationHeader(user.Id);
        var exerciseId = await CreateExerciseViaEndpointAsync(user.Id, "Contract Main Record");

        var addResponse = await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{user.Id}/addNewRecord", new
        {
            exercise = exerciseId.ToString(),
            weight = 100.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        });
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var addJson = await ReadJsonAsync(addResponse))
        {
            AssertLegacyMsgFieldPresent(addJson, "main-record add must return the legacy msg envelope");
            addJson.RootElement.GetProperty("msg").GetString().Should().Be("Created");
        }

        var historyResponse = await Client.GetAsync($"/api/mainRecords/{user.Id}/getMainRecordsHistory");
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        string recordId;
        using (var historyJson = await ReadJsonAsync(historyResponse))
        {
            historyJson.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            var record = historyJson.RootElement.EnumerateArray().Single();
            recordId = record.GetProperty("_id").GetString()!;
            Id<LgymApi.Domain.Entities.MainRecord>.TryParse(recordId, out _).Should().BeTrue();
            record.GetProperty("exercise").GetString().Should().Be(exerciseId.ToString());
            AssertEnumLookup(record.GetProperty("unit"), WeightUnits.Kilograms.ToString());
        }

        var lastResponse = await Client.GetAsync($"/api/mainRecords/{user.Id}/getLastMainRecords");
        lastResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var lastJson = await ReadJsonAsync(lastResponse))
        {
            var record = lastJson.RootElement.EnumerateArray().Single();
            record.GetProperty("_id").GetString().Should().Be(recordId);
            record.GetProperty("exerciseDetails").GetProperty("_id").GetString().Should().Be(exerciseId.ToString());
        }

        var possibleResponse = await Client.PostAsJsonAsync("/api/mainRecords/getRecordOrPossibleRecordInExercise", new { exerciseId = exerciseId.ToString() });
        possibleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var possibleJson = await ReadJsonAsync(possibleResponse))
        {
            possibleJson.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
            possibleJson.RootElement.GetProperty("weight").GetDouble().Should().Be(100.0);
            possibleJson.RootElement.GetProperty("reps").GetDouble().Should().Be(1);
            AssertEnumLookup(possibleJson.RootElement.GetProperty("unit"), WeightUnits.Kilograms.ToString());
        }

        var updateResponse = await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{user.Id}/updateMainRecords", new
        {
            _id = recordId,
            exercise = exerciseId.ToString(),
            weight = 105.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var updateJson = await ReadJsonAsync(updateResponse))
        {
            AssertLegacyMsgFieldPresent(updateJson, "main-record update must return the legacy msg envelope");
            updateJson.RootElement.GetProperty("msg").GetString().Should().Be("Updated");
        }

        var deleteResponse = await Client.GetAsync($"/api/mainRecords/{recordId}/deleteMainRecord");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var deleteJson = await ReadJsonAsync(deleteResponse);
        AssertLegacyMsgFieldPresent(deleteJson, "legacy main-record delete must retain its GET route and msg envelope");
    }

    [Test]
    public async Task EloAndRanking_LegacyRoutes_PreserveArrayAndMessageShapes()
    {
        var visibleUser = await SeedUserAsync(name: "contract_visible", email: "contract_visible@example.com", elo: 1250);
        await SeedUserAsync(name: "contract_hidden", email: "contract_hidden@example.com", elo: 3000, isVisibleInRanking: false);
        await SeedUserAsync(name: "contract_tester", email: "contract_tester@example.com", elo: 2900, isTester: true);
        await SeedUserAsync(name: "contract_deleted", email: "contract_deleted@example.com", elo: 2800, isDeleted: true);
        SetAuthorizationHeader(visibleUser.Id);

        var chartResponse = await Client.GetAsync($"/api/eloRegistry/{visibleUser.Id}/getEloRegistryChart");
        chartResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var chartJson = await ReadJsonAsync(chartResponse))
        {
            chartJson.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            var entry = chartJson.RootElement.EnumerateArray().Single();
            Id<LgymApi.Domain.Entities.EloRegistry>.TryParse(entry.GetProperty("_id").GetString(), out _).Should().BeTrue();
            entry.GetProperty("value").GetInt32().Should().Be(1250);
            entry.GetProperty("date").GetString().Should().NotBeNullOrWhiteSpace();
        }

        var eloResponse = await Client.GetAsync($"/api/userInfo/{visibleUser.Id}/getUserEloPoints");
        eloResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var eloJson = await ReadJsonAsync(eloResponse))
        {
            eloJson.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
            eloJson.RootElement.GetProperty("elo").GetInt32().Should().Be(1250);
            eloJson.RootElement.TryGetProperty("req", out _).Should().BeFalse("the legacy user ELO route returns a dedicated object, not the login envelope");
        }

        var rankingResponse = await Client.GetAsync("/api/getUsersRanking");
        rankingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var rankingJson = await ReadJsonAsync(rankingResponse))
        {
            rankingJson.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            var ranking = rankingJson.RootElement.EnumerateArray().Single();
            ranking.GetProperty("name").GetString().Should().Be("contract_visible");
            ranking.GetProperty("elo").GetInt32().Should().Be(1250);
            ranking.TryGetProperty("avatar", out _).Should().BeFalse("null avatar values are omitted by the established JSON contract");
            ranking.GetProperty("profileRank").GetString().Should().NotBeNullOrWhiteSpace();
            ranking.TryGetProperty("_id", out _).Should().BeFalse("ranking uses its established anonymous entry shape");
        }

        var visibilityResponse = await Client.PostAsJsonAsync("/api/changeVisibilityInRanking", new { isVisibleInRanking = false });
        visibilityResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var visibilityJson = await ReadJsonAsync(visibilityResponse);
        AssertLegacyMsgFieldPresent(visibilityJson, "ranking visibility mutation must return the legacy msg envelope");
        visibilityJson.RootElement.GetProperty("msg").GetString().Should().Be("Updated");
    }

    [Test]
    public async Task Gym_AddGym_ReturnsLegacyMsgField()
    {
        var user = await SeedUserAsync(name: "contract_gym", email: "contract_gym@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.PostAsJsonAsync($"/api/gym/{user.Id}/addGym", new
        {
            name = "Contract Test Gym"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await ReadJsonAsync(response);
        AssertLegacyMsgFieldPresent(json, "addGym endpoint must return msg field for backward compatibility");
    }

    [Test]
    public async Task Gym_GetGyms_ReturnsListWithLegacyIdFields()
    {
        var user = await SeedUserAsync(name: "contract_gym_list", email: "contract_gym_list@example.com");
        SetAuthorizationHeader(user.Id);

        var gymId = await CreateGymViaEndpointAsync(user.Id, "Test Gym 1");
        await CreateGymViaEndpointAsync(user.Id, "Test Gym 2");

        var response = await Client.GetAsync($"/api/gym/{user.Id}/getGyms");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = await ReadJsonAsync(response);
        AssertJsonArrayHasLegacyIdFields(json, "each gym in list must have _id");
    }

    [Test]
    public async Task Gym_GetGym_ReturnsEntityWithLegacyIdField()
    {
        var user = await SeedUserAsync(name: "contract_gym_get", email: "contract_gym_get@example.com");
        SetAuthorizationHeader(user.Id);

        var gymId = await CreateGymViaEndpointAsync(user.Id, "Test Gym");

        var response = await Client.GetAsync($"/api/gym/{gymId}/getGym");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = await ReadJsonAsync(response);
        AssertLegacyIdFieldPresent(json, "_id property must be present for gym detail response");
    }

    [Test]
    public async Task Plan_CreatePlan_ReturnsLegacyMsgField()
    {
        var user = await SeedUserAsync(name: "contract_plan", email: "contract_plan@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await Client.PostAsJsonAsync($"/api/{user.Id}/createPlan", new
        {
            name = "Contract Test Plan"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await ReadJsonAsync(response);
        AssertLegacyMsgFieldPresent(json, "createPlan endpoint must return msg field for backward compatibility");
    }

    [Test]
    public async Task Plan_GetPlansList_ReturnsListWithLegacyIdFields()
    {
        var user = await SeedUserAsync(name: "contract_plan_list", email: "contract_plan_list@example.com");
        SetAuthorizationHeader(user.Id);

        await CreatePlanViaEndpointAsync(user.Id, "Test Plan 1");
        await CreatePlanViaEndpointAsync(user.Id, "Test Plan 2");

        var response = await Client.GetAsync($"/api/{user.Id}/getPlansList");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = await ReadJsonAsync(response);
        AssertJsonArrayHasLegacyIdFields(json, "each plan in list must have _id");
    }

    [Test]
    public async Task Plan_UpdatePlan_ReturnsLegacyMsgField()
    {
        var user = await SeedUserAsync(name: "contract_plan_update", email: "contract_plan_update@example.com");
        var planId = await CreatePlanViaEndpointAsync(user.Id, "Contract Plan");

        var response = await PostAsJsonWithApiOptionsAsync($"/api/{user.Id}/updatePlan", new
        {
            _id = planId.ToString(),
            name = "Updated Contract Plan"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await ReadJsonAsync(response);
        AssertLegacyMsgFieldPresent(json, "updatePlan endpoint must return msg field for backward compatibility");
        json.RootElement.GetProperty("msg").GetString().Should().Be("Updated");
    }

    [Test]
    public async Task Plan_GetPlanConfig_ReturnsLegacyPlanFormShape()
    {
        var user = await SeedUserAsync(name: "contract_plan_config", email: "contract_plan_config@example.com");
        var planId = await CreatePlanViaEndpointAsync(user.Id, "Configured Plan");

        var response = await Client.GetAsync($"/api/{user.Id}/getPlanConfig");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await ReadJsonAsync(response);
        AssertLegacyIdFieldPresent(json, "getPlanConfig must return the legacy _id field");
        json.RootElement.GetProperty("_id").GetString().Should().Be(planId.ToString());
        json.RootElement.GetProperty("name").GetString().Should().Be("Configured Plan");
        json.RootElement.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task Plan_CheckIsUserHavePlan_WithPlanWithoutDays_ReturnsBooleanFalsePayload()
    {
        var user = await SeedUserAsync(name: "contract_plan_check", email: "contract_plan_check@example.com");
        await CreatePlanViaEndpointAsync(user.Id, "Existing Plan");

        var response = await Client.GetAsync($"/api/{user.Id}/checkIsUserHavePlan");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await ReadJsonAsync(response);
        json.RootElement.ValueKind.Should().Be(JsonValueKind.False);
    }

    [Test]
    public async Task Plan_SetNewActivePlan_ReturnsLegacyMsgField()
    {
        var user = await SeedUserAsync(name: "contract_plan_active", email: "contract_plan_active@example.com");
        await CreatePlanViaEndpointAsync(user.Id, "First Plan");
        var planId = await CreatePlanViaEndpointAsync(user.Id, "Second Plan");

        var response = await PostAsJsonWithApiOptionsAsync($"/api/{user.Id}/setNewActivePlan", new
        {
            _id = planId.ToString()
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await ReadJsonAsync(response);
        AssertLegacyMsgFieldPresent(json, "setNewActivePlan endpoint must return msg field for backward compatibility");
        json.RootElement.GetProperty("msg").GetString().Should().Be("Updated");
    }

    [Test]
    public async Task Plan_CopyPlan_ReturnsPlanDtoShape()
    {
        var sourceUser = await SeedUserAsync(name: "contract_plan_copy_source", email: "contract_plan_copy_source@example.com");
        var sourcePlanId = await CreatePlanViaEndpointAsync(sourceUser.Id, "Copy Source Plan");

        var shareResponse = await Client.PostAsync($"/api/{sourcePlanId}/share", null);
        shareResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var shareJson = await ReadJsonAsync(shareResponse);
        var shareCode = shareJson.RootElement.GetProperty("shareCode").GetString();

        var destinationUser = await SeedUserAsync(name: "contract_plan_copy_destination", email: "contract_plan_copy_destination@example.com");
        SetAuthorizationHeader(destinationUser.Id);
        var response = await Client.PostAsJsonAsync("/api/copy", new { shareCode });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var json = await ReadJsonAsync(response);
        json.RootElement.GetProperty("id").GetString().Should().NotBeNullOrWhiteSpace();
        json.RootElement.TryGetProperty("_id", out _).Should().BeFalse("copyPlan returns PlanDto, which uses id rather than legacy _id");
        json.RootElement.GetProperty("name").GetString().Should().Be("Copy Source Plan");
        json.RootElement.GetProperty("isActive").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("userId").GetString().Should().Be(destinationUser.Id.ToString());
    }

    [Test]
    public async Task Plan_GenerateShareCode_ReturnsShareCodeShape()
    {
        var user = await SeedUserAsync(name: "contract_plan_share", email: "contract_plan_share@example.com");
        var planId = await CreatePlanViaEndpointAsync(user.Id, "Share Plan");

        var response = await Client.PostAsync($"/api/{planId}/share", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await ReadJsonAsync(response);
        json.RootElement.TryGetProperty("shareCode", out var shareCode).Should().BeTrue("share endpoint must return shareCode");
        shareCode.GetString().Should().HaveLength(10);
        json.RootElement.TryGetProperty("code", out _).Should().BeFalse("share endpoint must retain the shareCode property name");
    }

    [Test]
    public async Task Plan_DeletePlan_ReturnsLegacyMsgField()
    {
        var user = await SeedUserAsync(name: "contract_plan_delete", email: "contract_plan_delete@example.com");
        var planId = await CreatePlanViaEndpointAsync(user.Id, "Delete Plan");

        var response = await Client.PostAsync($"/api/{planId}/deletePlan", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await ReadJsonAsync(response);
        AssertLegacyMsgFieldPresent(json, "deletePlan endpoint must return msg field for backward compatibility");
        json.RootElement.GetProperty("msg").GetString().Should().Be("Deleted.");
    }

    [Test]
    public async Task Exercise_AddUserExercise_ReturnsLegacyMsgField()
    {
        var user = await SeedUserAsync(name: "contract_exercise", email: "contract_exercise@example.com");
        SetAuthorizationHeader(user.Id);

        var response = await PostAsJsonWithApiOptionsAsync($"/api/exercise/{user.Id}/addUserExercise", new
        {
            name = "Contract Exercise",
            bodyPart = BodyParts.Chest.ToString(),
            description = "Test"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await ReadJsonAsync(response);
        AssertLegacyMsgFieldPresent(json, "addUserExercise endpoint must return msg field for backward compatibility");
    }

    [Test]
    public async Task Exercise_GetAllUserExercises_ReturnsListWithLegacyIdFields()
    {
        var user = await SeedUserAsync(name: "contract_exercise_list", email: "contract_exercise_list@example.com");
        SetAuthorizationHeader(user.Id);

        await CreateExerciseViaEndpointAsync(user.Id, "Exercise A", BodyParts.Chest);
        await CreateExerciseViaEndpointAsync(user.Id, "Exercise B", BodyParts.Back);

        var response = await Client.GetAsync($"/api/exercise/{user.Id}/getAllUserExercises");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = await ReadJsonAsync(response);
        AssertJsonArrayHasLegacyIdFields(json, "each exercise in list must have _id");
    }



    [TestCase("/api/gym/{0}/addGym", "name", "Test Gym 1")]
    [TestCase("/api/{0}/createPlan", "name", "Test Plan 1")]
    public async Task PostMutationEndpoint_ReturnsLegacyMsgField(string routeTemplate, string propertyName, string propertyValue)
    {
        var user = await SeedUserAsync($"contract_{Id<ContractCompatibilityTests>.New()}", $"contract_{Id<ContractCompatibilityTests>.New()}@example.com");
        SetAuthorizationHeader(user.Id);

        var route = string.Format(routeTemplate, user.Id);
        var request = CreateDynamicRequest(propertyName, propertyValue);

        var response = await Client.PostAsJsonAsync(route, request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = await ReadJsonAsync(response);
        AssertLegacyMsgFieldPresent(json, $"endpoint {route} must return msg field");
    }

    [TestCase("/api/gym/{0}/getGyms")]
    [TestCase("/api/{0}/getPlansList")]
    [TestCase("/api/exercise/{0}/getAllUserExercises")]
    public async Task GetListEndpoint_ReturnsLegacyIdFields(string routeTemplate)
    {
        var user = await SeedUserAsync($"contract_list_{Id<ContractCompatibilityTests>.New()}", $"contract_list_{Id<ContractCompatibilityTests>.New()}@example.com");
        SetAuthorizationHeader(user.Id);

        // Seed test data for each endpoint
        if (routeTemplate.Contains("gym"))
        {
            await CreateGymViaEndpointAsync(user.Id, "Seed Gym");
        }
        else if (routeTemplate.Contains("Plan"))
        {
            await CreatePlanViaEndpointAsync(user.Id, "Seed Plan");
        }
        else if (routeTemplate.Contains("exercise"))
        {
            await CreateExerciseViaEndpointAsync(user.Id, "Seed Exercise");
        }

        var route = string.Format(routeTemplate, user.Id);

        var response = await Client.GetAsync(route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = await ReadJsonAsync(response);
        AssertJsonArrayHasLegacyIdFields(json, $"endpoint {route} must return array items with _id");
    }

    #region Shared Assertion Helpers

    private static void AssertLegacyIdFieldPresent(JsonDocument json, string because)
    {
        json.RootElement.TryGetProperty("_id", out var id).Should().BeTrue(because);
        id.GetString().Should().NotBeNullOrWhiteSpace();
        // Contract guard: API must use `_id` (legacy MongoDB naming), not `id`
        json.RootElement.TryGetProperty("id", out _).Should().BeFalse($"response must not have 'id' field when '_id' is present (legacy compatibility)");
    }

    private static void AssertLegacyMsgFieldPresent(JsonDocument json, string because)
    {
        json.RootElement.TryGetProperty("msg", out var msg).Should().BeTrue(because);
        msg.GetString().Should().NotBeNullOrWhiteSpace();
        // Contract guard: legacy clients expect `msg`; an accidental switch to `message` would break them.
        json.RootElement.TryGetProperty("message", out _).Should().BeFalse($"response must not have 'message' field when 'msg' is present (legacy compatibility)");
    }

    private static void AssertLegacyReqFieldPresent(JsonDocument json, bool requireIdField, bool requireNameField)
    {
        json.RootElement.TryGetProperty("req", out var req).Should().BeTrue("legacy clients expect 'req' field");
        if (requireIdField)
        {
            req.TryGetProperty("_id", out var userId).Should().BeTrue("req must contain _id");
            userId.GetString().Should().NotBeNullOrWhiteSpace();
        }
        if (requireNameField)
        {
            req.TryGetProperty("name", out var userName).Should().BeTrue("req must contain name");
            userName.GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    private static void AssertJsonArrayHasLegacyIdFields(JsonDocument json, string because)
    {
        json.RootElement.ValueKind.Should().Be(JsonValueKind.Array, because);
        var array = json.RootElement.EnumerateArray().ToList();
        array.Should().NotBeEmpty(because);

        foreach (var item in array)
        {
            item.TryGetProperty("_id", out var id).Should().BeTrue($"{because} - each item must have _id");
            id.GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    private static void AssertEnumLookup(JsonElement lookup, string expectedId)
    {
        lookup.ValueKind.Should().Be(JsonValueKind.Object);
        lookup.GetProperty("id").GetString().Should().Be(expectedId);
        lookup.GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();
        lookup.GetProperty("displayName").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static object CreateDynamicRequest(string propertyName, object propertyValue)
    {
        return new Dictionary<string, object> { { propertyName, propertyValue } };
    }

    #endregion

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }
}
