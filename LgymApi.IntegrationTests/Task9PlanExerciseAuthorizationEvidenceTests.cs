using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LgymApi.IntegrationTests.Authorization;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class Task9PlanExerciseAuthorizationEvidenceTests : IntegrationTestBase
{
    [Test]
    [AuthorizationEvidence("POST", "/api/{id}/createPlan", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/{id}/updatePlan", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/{id}/deletePlan", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/{id}/share", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/{id}/getPlanConfig", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/{id}/checkIsUserHavePlan", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/{id}/getPlansList", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/{id}/setNewActivePlan", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/exercise/{id}/addUserExercise", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/exercise/{id}/getExerciseByBodyPart", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/exercise/{id}/getAllExercises", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/exercise/{id}/getAllUserExercises", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/exercise/getExerciseScoresFromTrainingByExercise", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/exercise/{id}/getLastExerciseScores", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/eloRegistry/{id}/getEloRegistryChart", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/exerciseScores/{id}/getExerciseScoresChartData", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/gym/{id}/addGym", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/gym/{id}/deleteGym", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/gym/{id}/getGym", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/gym/{id}/getGyms", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/gym/editGym", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/measurements/add", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/measurements/add-bulk", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/measurements/{id}/getHistory", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/measurements/{id}/list", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/measurements/{id}/trend", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/measurements/{id}/trends", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/measurements:/{id}/getMeasurementDetail", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/mainRecords/{id}/addNewRecord", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/mainRecords/{id}/getMainRecordsHistory", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/mainRecords/{id}/getLastMainRecords", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/mainRecords/{id}/deleteMainRecord", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/mainRecords/{id}/updateMainRecords", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/mainRecords/getRecordOrPossibleRecordInExercise", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/{id}/addTraining", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/{id}/getLastTraining", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/{id}/getTrainingByDate", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/{id}/getTrainingDates", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/exercise/{id}/deleteExercise", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/exercise/updateExercise", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/exercise/getAllGlobalExercises", "authenticated-global", "anonymous-denial")]
    public async Task Task9_PlanAndExerciseRoutes_AnonymousRequestsAreDenied()
    {
        ClearAuthorizationHeader();
        var id = "00000000-0000-0000-0000-000000000001";
        var requests = new Func<Task<HttpResponseMessage>>[]
        {
            () => Client.PostAsJsonAsync($"/api/{id}/createPlan", new { name = "Denied" }),
            () => Client.PostAsJsonAsync($"/api/{id}/updatePlan", new { _id = id, name = "Denied" }),
            () => Client.PostAsync($"/api/{id}/deletePlan", null),
            () => Client.PostAsync($"/api/{id}/share", null),
            () => Client.GetAsync($"/api/{id}/getPlanConfig"),
            () => Client.GetAsync($"/api/{id}/checkIsUserHavePlan"),
            () => Client.GetAsync($"/api/{id}/getPlansList"),
            () => Client.PostAsJsonAsync($"/api/{id}/setNewActivePlan", new { _id = id }),
            () => Client.PostAsJsonAsync($"/api/exercise/{id}/addUserExercise", new { name = "Denied", bodyPart = "Chest" }),
            () => Client.PostAsJsonAsync($"/api/exercise/{id}/getExerciseByBodyPart", new { bodyPart = "Chest" }),
            () => Client.GetAsync($"/api/exercise/{id}/getAllExercises"),
            () => Client.GetAsync($"/api/exercise/{id}/getAllUserExercises"),
            () => Client.PostAsJsonAsync("/api/exercise/getExerciseScoresFromTrainingByExercise", new { exerciseId = id }),
            () => Client.GetAsync($"/api/exercise/{id}/getExercise"),
            () => Client.PostAsJsonAsync($"/api/exercise/{id}/getLastExerciseScores", new { exerciseId = id, exerciseName = "Denied", series = 1, gymId = id }),
            () => Client.GetAsync($"/api/eloRegistry/{id}/getEloRegistryChart"),
            () => Client.PostAsJsonAsync($"/api/exerciseScores/{id}/getExerciseScoresChartData", new { exerciseId = id }),
            () => Client.PostAsJsonAsync($"/api/gym/{id}/addGym", new { name = "Denied" }),
            () => Client.PostAsJsonAsync($"/api/gym/{id}/deleteGym", new { }),
            () => Client.GetAsync($"/api/gym/{id}/getGym"),
            () => Client.GetAsync($"/api/gym/{id}/getGyms"),
            () => Client.PostAsJsonAsync("/api/gym/editGym", new { _id = id, name = "Denied" }),
            () => Client.PostAsJsonAsync("/api/measurements/add", new { bodyPart = "BodyWeight", value = 1, unit = "Kilograms" }),
            () => Client.PostAsJsonAsync("/api/measurements/add-bulk", new { measurements = Array.Empty<object>() }),
            () => Client.GetAsync($"/api/measurements/{id}/getHistory"),
            () => Client.GetAsync($"/api/measurements/{id}/list"),
            () => Client.GetAsync($"/api/measurements/{id}/trend"),
            () => Client.GetAsync($"/api/measurements/{id}/trends"),
            () => Client.GetAsync($"/api/measurements:/{id}/getMeasurementDetail"),
            () => Client.PostAsJsonAsync($"/api/mainRecords/{id}/addNewRecord", new { }),
            () => Client.GetAsync($"/api/mainRecords/{id}/getMainRecordsHistory"),
            () => Client.GetAsync($"/api/mainRecords/{id}/getLastMainRecords"),
            () => Client.GetAsync($"/api/mainRecords/{id}/deleteMainRecord"),
            () => Client.PostAsJsonAsync($"/api/mainRecords/{id}/updateMainRecords", new { }),
            () => Client.PostAsJsonAsync("/api/mainRecords/getRecordOrPossibleRecordInExercise", new { }),
            () => Client.PostAsJsonAsync($"/api/{id}/addTraining", new { }),
            () => Client.GetAsync($"/api/{id}/getLastTraining"),
            () => Client.PostAsJsonAsync($"/api/{id}/getTrainingByDate", new { createdAt = DateTime.UtcNow }),
            () => Client.GetAsync($"/api/{id}/getTrainingDates"),
            () => Client.PostAsJsonAsync($"/api/exercise/{id}/deleteExercise", new { id }),
            () => Client.PostAsJsonAsync("/api/exercise/updateExercise", new { _id = id, name = "Denied", bodyPart = "Chest" }),
            () => Client.GetAsync("/api/exercise/getAllGlobalExercises")
        };

        foreach (var send in requests)
        {
            using var response = await send();
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/planDay/{id}/createPlanDay", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/planDay/updatePlanDay", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/planDay/{id}/getPlanDay", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/planDay/{id}/getPlanDays", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/planDay/{id}/getPlanDaysTypes", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/planDay/{id}/deletePlanDay", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/planDay/{id}/getPlanDaysInfo", "own", "anonymous-denial")]
    public async Task Task9_PlanDayRoutes_AnonymousRequestsAreUnauthorized()
    {
        ClearAuthorizationHeader();
        var id = "00000000-0000-0000-0000-000000000001";
        var requests = new Func<Task<HttpResponseMessage>>[]
        {
            () => Client.PostAsJsonAsync($"/api/planDay/{id}/createPlanDay", new { name = "Denied", exercises = new[] { new { exercise = id, series = 1, reps = "1" } } }),
            () => Client.PostAsJsonAsync("/api/planDay/updatePlanDay", new { _id = id, name = "Denied", exercises = new[] { new { exercise = id, series = 1, reps = "1" } } }),
            () => Client.GetAsync($"/api/planDay/{id}/getPlanDay"),
            () => Client.GetAsync($"/api/planDay/{id}/getPlanDays"),
            () => Client.GetAsync($"/api/planDay/{id}/getPlanDaysTypes"),
            () => Client.GetAsync($"/api/planDay/{id}/deletePlanDay"),
            () => Client.GetAsync($"/api/planDay/{id}/getPlanDaysInfo")
        };

        foreach (var send in requests)
        {
            using var response = await send();
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/exercise/addExercise", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/exercise/addExerciseWithFormula", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/exercise/updateExerciseWithFormula", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/exercise/{id}/addGlobalTranslation", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/exercise/{id}/addUserExerciseWithFormula", "admin", "ordinary-user-denial")]
    public async Task Task9_GlobalExerciseRoutes_OrdinaryUserIsForbidden()
    {
        var setupAdmin = await SeedAdminAsync();
        var globalExercise = await CreateGlobalExerciseViaEndpointAsync(setupAdmin.Id, "Task9 Global");
        var user = await SeedUserAsync("task9-exercise-ordinary", "task9-exercise-ordinary@example.com");
        SetAuthorizationHeader(user.Id);
        var responses = CreateGlobalExerciseRouteRequests(user.Id.ToString(), globalExercise.ToString());

        foreach (var send in responses)
        {
            using var response = await send();
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/exercise/addExercise", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/exercise/addExerciseWithFormula", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/exercise/updateExerciseWithFormula", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/exercise/{id}/addGlobalTranslation", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/exercise/{id}/addUserExerciseWithFormula", "admin", "stale-token-demotion-denial")]
    public async Task Task9_GlobalExerciseRoutes_PreIssuedAdminTokenIsDeniedAfterDemotion()
    {
        var demotedAdmin = await SeedAdminAsync();
        SetAuthorizationHeader(demotedAdmin.Id);
        var preIssuedToken = Client.DefaultRequestHeaders.Authorization;
        var currentAdmin = await SeedAdminAsync();
        var globalExercise = await CreateGlobalExerciseViaEndpointAsync(currentAdmin.Id, "Task9 Demotion Global");
        SetAuthorizationHeader(currentAdmin.Id);
        using (var demotion = await Client.PostAsJsonAsync($"/api/roles/users/{demotedAdmin.Id}/roles", new { roles = new[] { "User" } }))
        {
            demotion.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        Client.DefaultRequestHeaders.Authorization = preIssuedToken;
        foreach (var send in CreateGlobalExerciseRouteRequests(demotedAdmin.Id.ToString(), globalExercise.ToString()))
        {
            using var response = await send();
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    private IEnumerable<Func<Task<HttpResponseMessage>>> CreateGlobalExerciseRouteRequests(string accountId, string exerciseId)
    {
        yield return () => Client.PostAsJsonAsync("/api/exercise/addExercise", new { name = "Denied Global", bodyPart = "Chest" });
        yield return () => Client.PostAsJsonAsync("/api/exercise/addExerciseWithFormula", new { name = "Denied Formula", bodyPart = "Chest", eloFormula = "Standard" });
        yield return () => Client.PostAsJsonAsync("/api/exercise/updateExerciseWithFormula", new { _id = exerciseId, name = "Denied Update", bodyPart = "Chest", eloFormula = "Standard" });
        yield return () => Client.PostAsJsonAsync($"/api/exercise/{accountId}/addGlobalTranslation", new { exerciseId, culture = "pl", name = "Odmowa" });
        yield return () => Client.PostAsJsonAsync($"/api/exercise/{accountId}/addUserExerciseWithFormula", new { name = "Denied User Formula", bodyPart = "Chest", eloFormula = "Standard" });
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "global-resource-allow")]
    [AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "current-manager-allow")]
    [AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "stale-manager-denial")]
    public async Task Task9_ExerciseDetail_GlobalManagerAndDemotedManagerBehaveAsExpected()
    {
        var ordinaryUser = await SeedUserAsync("task9-detail-user", "task9-detail-user@example.com");
        var setupAdmin = await SeedAdminAsync();
        var globalExercise = await CreateGlobalExerciseViaEndpointAsync(setupAdmin.Id, "Task9 Detail Global");
        SetAuthorizationHeader(ordinaryUser.Id);
        using (var globalResponse = await Client.GetAsync($"/api/exercise/{globalExercise}/getExercise"))
        {
            globalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var owner = await SeedUserAsync("task9-detail-owner", "task9-detail-owner@example.com");
        var customExercise = await CreateExerciseViaEndpointAsync(owner.Id, "Task9 Detail Custom");
        var manager = await SeedAdminAsync();
        SetAuthorizationHeader(manager.Id);
        using (var managerResponse = await Client.GetAsync($"/api/exercise/{customExercise}/getExercise"))
        {
            managerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var preIssuedToken = Client.DefaultRequestHeaders.Authorization;
        var currentAdmin = await SeedAdminAsync();
        SetAuthorizationHeader(currentAdmin.Id);
        using (var demotion = await Client.PostAsJsonAsync($"/api/roles/users/{manager.Id}/roles", new { roles = new[] { "User" } }))
        {
            demotion.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        Client.DefaultRequestHeaders.Authorization = preIssuedToken;
        using var demotedResponse = await Client.GetAsync($"/api/exercise/{customExercise}/getExercise");
        demotedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    [AuthorizationEvidence("POST", "/api/exercise/{id}/getLastExerciseScores", "own", "foreign-object-denial-no-mutation")]
    public async Task Task9_LastExerciseScores_WithForeignRouteIsForbidden()
    {
        var attacker = await SeedUserAsync("task9-last-attacker", "task9-last-attacker@example.com");
        var victim = await SeedUserAsync("task9-last-victim", "task9-last-victim@example.com");
        SetAuthorizationHeader(attacker.Id);
        var id = "00000000-0000-0000-0000-000000000001";

        using var response = await Client.PostAsJsonAsync($"/api/exercise/{victim.Id}/getLastExerciseScores", new { exerciseId = id, exerciseName = "Denied", series = 1, gymId = id });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
