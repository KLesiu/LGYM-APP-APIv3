using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using LgymApi.IntegrationTests.Authorization;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
public sealed class EndpointContractMatrixTests : IntegrationTestBase
{
    private static readonly string[] CutoverControllers =
    [
        "AccountController", "AdminUserController", "AppConfigAdminController", "AppConfigController",
        "EloRegistryController", "ExerciseController", "ExerciseScoresController", "GymController",
        "InAppNotificationController", "MainRecordsController", "MeasurementsController", "PlanController", "PlanDayController",
        "PushInstallationController", "PushNotificationAdminController", "RoleController", "TraineeDietPlanController",
        "TraineeNotesController", "TraineeRelationshipController", "TraineeReportingController", "TraineeSupplementationController",
        "TrainerAuthController", "TrainerDashboardProgressController", "TrainerDietPlansController", "TrainerInvitationController",
        "TrainerManagedPlansController", "TrainerReportingController", "TrainerSupplementationController", "TrainerTraineeNotesController",
        "TrainingController", "TutorialController", "UserController"
    ];

    private static readonly string[] ApprovedAccessClasses =
    [
        "public", "own", "trainer-shared", "admin", "authenticated-global"
    ];

    private static readonly string[] ApprovedAccessFacets =
    [
        "actor-derived-subject", "foreign-object", "owned-resource", "global-visible",
        "manager-override", "opaque-capability", "relationship-revocable"
    ];

    private static readonly string[] ApprovedEvidenceCategories =
    [
        "anonymous-intended-behavior", "invalid-capability-denial", "expired-capability-denial", "tampered-capability-denial",
        "owner-allow", "anonymous-denial", "no-client-subject", "foreign-object-denial-no-mutation",
        "active-relationship-allow", "unrelated-relationship-denial", "former-relationship-denial",
        "current-permission-allow", "ordinary-user-denial", "stale-token-demotion-denial",
        "ordinary-authenticated-allow", "owner-custom-allow", "foreign-custom-denial", "global-resource-allow",
        "current-manager-allow", "stale-manager-denial", "ordinary-manager-denial"
    ];

    [Test]
    public void MatrixParser_NormalizesACompleteRowAndRejectsMissingFields()
    {
        Action missingField = () => ParseMatrixRow("| GET | /api/example | Example.Action |");

        missingField.Should().Throw<AssertionException>()
            .WithMessage("*must define every contract column*");

        var parsed = ParseMatrixRow("| GET | /api/example | Example.Action | anonymous | none | 200 | application/json | msg | UUID string | none | no | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | public | none | EndpointContractMatrixTests.SemanticAuthorizationParser_AcceptsCompleteClassAndFacetProfiles |");

        parsed.ToDocumentRow().Should().Be("| GET | /api/example | Example.Action | anonymous | none | 200 | application/json | msg | UUID string | none | no | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | public | none | EndpointContractMatrixTests.SemanticAuthorizationParser_AcceptsCompleteClassAndFacetProfiles |");
    }

    [TestCaseSource(nameof(CompleteSemanticAuthorizationRows))]
    [AuthorizationEvidence("GET", "/api/invitations/{invitationId}", "public", "anonymous-intended-behavior")]
    [AuthorizationEvidence("GET", "/api/invitations/{invitationId}", "public", "invalid-capability-denial")]
    [AuthorizationEvidence("GET", "/api/invitations/{invitationId}", "public", "expired-capability-denial")]
    [AuthorizationEvidence("GET", "/api/invitations/{invitationId}", "public", "tampered-capability-denial")]
    [AuthorizationEvidence("POST", "/api/logout", "own", "owner-allow")]
    [AuthorizationEvidence("POST", "/api/logout", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/logout", "own", "no-client-subject")]
    [AuthorizationEvidence("GET", "/api/own/{id}", "own", "owner-allow")]
    [AuthorizationEvidence("GET", "/api/own/{id}", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/own/{id}", "own", "foreign-object-denial-no-mutation")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/plans", "trainer-shared", "active-relationship-allow")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/plans", "trainer-shared", "unrelated-relationship-denial")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/plans", "trainer-shared", "former-relationship-denial")]
    [AuthorizationEvidence("GET", "/api/trainer/trainees/{traineeId}/plans", "trainer-shared", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/admin/users/{id}", "admin", "current-permission-allow")]
    [AuthorizationEvidence("GET", "/api/admin/users/{id}", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("GET", "/api/admin/users/{id}", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "ordinary-authenticated-allow")]
    [AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "owner-custom-allow")]
    [AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "foreign-custom-denial")]
    [AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "global-resource-allow")]
    [AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "current-manager-allow")]
    [AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "stale-manager-denial")]
    [AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "ordinary-manager-denial")]
    public void SemanticAuthorizationParser_AcceptsCompleteClassAndFacetProfiles(string row)
    {
        Action parse = () => AssertAuthorizationEvidenceResolves(ParseMatrixRow(row));

        parse.Should().NotThrow("a complete semantic authorization profile must preserve all existing columns and add access class, access facets, and executable authorization evidence");
    }

    [TestCaseSource(nameof(SemanticAuthorizationSchemaFailureRows))]
    public void SemanticAuthorizationParser_RejectsMissingOrUnknownSchemaValues(string row, string expectedMessage)
    {
        Action parse = () => ParseMatrixRow(row);

        parse.Should().Throw<AssertionException>().WithMessage(expectedMessage);
    }

    [TestCaseSource(nameof(SemanticAuthorizationGuardFailureRows))]
    [AuthorizationEvidence("POST", "/api/logout", "own", "owner-allow")]
    [AuthorizationEvidence("POST", "/api/logout", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "ordinary-authenticated-allow")]
    [AuthorizationEvidence("GET", "/api/exercise/{id}/getExercise", "authenticated-global", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/evidence/unknown-category", "own", "unknown-category")]
    [AuthorizationEvidence("POST", "/api/evidence/method", "own", "owner-allow")]
    [AuthorizationEvidence("GET", "/api/evidence/other-route", "own", "owner-allow")]
    [AuthorizationEvidence("GET", "/api/evidence/class", "authenticated-global", "owner-allow")]
    public void SemanticAuthorizationGuard_RejectsIncompleteOrMismatchedEvidence(string row, string expectedMessage)
    {
        Action parse = () => AssertAuthorizationEvidenceResolves(ParseMatrixRow(row));

        parse.Should().Throw<AssertionException>().WithMessage(expectedMessage);
    }

    [Test]
    public void LiveControllerEndpointInventory_MatchesTheBaselineMatrix()
    {
        var rows = GetLiveRows();
        rows.Should().NotBeEmpty();
        rows.Select(row => row.Row.RouteKey).Should().OnlyHaveUniqueItems("each current route and method needs exactly one matrix row");
        rows.Should().OnlyContain(row => row.ApiDescriptionCount == 1, "each endpoint must have exactly one live API description");
        rows.Select(row => row.Row).Should().OnlyContain(row => row.HasContractMetadata, "every row needs concrete contract metadata and one executable evidence owner");

        var inventory = string.Join('\n', rows.Select(row => row.Row.EndpointKey));
        var inventoryHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inventory))).ToLowerInvariant();
        var matrix = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "validation", "issue-395-validation-matrix.md"));
        var matrixRows = ReadMatrixRows(matrix);

        matrix.Should().Contain("Todo 1 baseline SHA: `b22198b1509bf0e68f9e85dcd7b1375c768e2713`");
        matrix.Should().Contain($"Route inventory SHA-256: `{inventoryHash}`");
        matrix.Should().Contain($"Route count: `{rows.Length}`");
        AssertRowsMatch(matrixRows, rows.Select(row => row.Row).ToArray());

        foreach (var row in matrixRows)
        {
            AssertEvidenceLocatorResolves(row.EvidenceLocator, row.EndpointKey);
            if (row.AuthorizationEvidence != "unresolved")
            {
                AssertAuthorizationEvidenceResolves(row);
            }
        }

        TestContext.Progress.WriteLine($"Route count: {rows.Length}");
        TestContext.Progress.WriteLine($"Route inventory SHA-256: {inventoryHash}");

        var unresolved = matrixRows
            .Where(row => row.AuthorizationEvidence == "unresolved")
            .Select(row => row.RouteKey)
            .OrderBy(routeKey => routeKey, StringComparer.Ordinal)
            .ToArray();
        if (unresolved.Length != 0)
        {
            throw new AssertionException(
                $"Endpoint contract matrix field 'authorization evidence' has {unresolved.Length} unresolved routes. " +
                "Tasks 8-10 must replace every unresolved value with executable semantic evidence. " +
                $"First unresolved route: '{unresolved[0]}'.");
        }
    }

    [Test]
    public void AdapterCutoverControllers_AreAllRepresentedByOneOrMoreLiveMatrixRows()
    {
        var liveRows = GetLiveRows().Select(row => row.Row).ToArray();
        var matrix = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "validation", "issue-395-validation-matrix.md"));
        var matrixRows = ReadMatrixRows(matrix);

        CutoverControllers.Should().OnlyHaveUniqueItems();
        foreach (var controller in CutoverControllers)
        {
            liveRows.Should().Contain(row => row.Controller == controller, $"{controller} was changed by the adapter cutover");
            matrixRows.Should().Contain(row => row.Controller == controller, $"{controller} needs a current matrix row");
        }

        matrix.Should().Contain("EndpointContractMatrixTests.AdapterCutoverRows_ExecuteHttpCompatibilityContracts");
        matrix.Should().Contain("EndpointContractMatrixTests.RepresentativeLegacyRows_ExecuteHttpCompatibilityContracts");
        AssertEvidenceLocatorResolves("EndpointContractMatrixTests.AdapterCutoverRows_ExecuteHttpCompatibilityContracts", "adapter-cutover coverage");
        AssertEvidenceLocatorResolves("EndpointContractMatrixTests.RepresentativeLegacyRows_ExecuteHttpCompatibilityContracts", "legacy-route coverage");
    }

    [Test]
    public async Task AdapterCutoverRows_ExecuteHttpCompatibilityContracts()
    {
        var user = await SeedUserAsync("matrix-cutover", "matrix-cutover@example.com");
        SetAuthorizationHeader(user.Id);

        using var gymResponse = await Client.PostAsJsonAsync($"/api/gym/{user.Id}/addGym", new { name = "Matrix Gym" });
        gymResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertLegacyMessageAsync(gymResponse, "Created");

        using var measurementResponse = await Client.PostAsJsonAsync("/api/measurements/add", new
        {
            bodyPart = BodyParts.BodyWeight.ToString(),
            unit = MeasurementUnits.Kilograms.ToString(),
            value = 80.5
        });
        measurementResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertLegacyMessageAsync(measurementResponse, "Created");

        var exerciseId = await CreateExerciseViaEndpointAsync(user.Id, "Matrix Exercise", BodyParts.Back);
        using var exerciseResponse = await Client.GetAsync($"/api/exercise/{exerciseId}/getExercise");
        exerciseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var exercise = JsonDocument.Parse(await exerciseResponse.Content.ReadAsStringAsync()))
        {
            exercise.RootElement.GetProperty("_id").GetString().Should().Be(exerciseId.ToString());
            exercise.RootElement.GetProperty("bodyPart").GetProperty("id").GetString().Should().Be(BodyParts.Back.ToString());
        }

        using var recordResponse = await PostAsJsonWithApiOptionsAsync($"/api/mainRecords/{user.Id}/addNewRecord", new
        {
            exercise = exerciseId.ToString(),
            weight = 100.0,
            unit = WeightUnits.Kilograms.ToString(),
            date = DateTime.UtcNow
        });
        recordResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertLegacyMessageAsync(recordResponse, "Created");

        using var eloResponse = await Client.GetAsync($"/api/eloRegistry/{user.Id}/getEloRegistryChart");
        eloResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var elo = JsonDocument.Parse(await eloResponse.Content.ReadAsStringAsync());
        var entry = elo.RootElement.EnumerateArray().Single();
        LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.EloRegistry>.TryParse(entry.GetProperty("_id").GetString()!, out _).Should().BeTrue("cutover UUID wire values remain JSON strings");

        using var unauthorizedPushResponse = await SendWithoutAuthorizationAsync(
            HttpMethod.Post,
            "/api/internal/push/test-event",
            new { recipientUserId = user.Id.ToString(), type = "System", eventId = "matrix" });
        unauthorizedPushResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task RepresentativeLegacyRows_ExecuteHttpCompatibilityContracts()
    {
        var registerRequest = new
        {
            name = "matrix_legacy",
            email = "matrix_legacy@example.com",
            password = "password123",
            cpassword = "password123",
            isVisibleInRanking = true,
            adultConfirmed = true
        };

        var repeated = await SendRepeatedRequestAsync("/api/register", registerRequest, "matrix-legacy-register");
        using var firstRegistration = repeated.first;
        using var secondRegistration = repeated.second;
        firstRegistration.StatusCode.Should().Be(HttpStatusCode.OK);
        secondRegistration.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertLegacyMessageAsync(firstRegistration);
        await AssertLegacyMessageAsync(secondRegistration);

        using var loginResponse = await Client.PostAsJsonAsync("/api/login", new { name = "matrix_legacy", password = "password123" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var login = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync()))
        {
            login.RootElement.GetProperty("req").GetProperty("_id").GetString().Should().NotBeNullOrWhiteSpace();
            login.RootElement.TryGetProperty("user", out _).Should().BeFalse();
        }

        using var polishRequest = new HttpRequestMessage(HttpMethod.Post, "/api/appConfig/getAppVersion")
        {
            Content = JsonContent.Create(new { platform = "Unknown" })
        };
        polishRequest.Headers.AcceptLanguage.ParseAdd("pl");
        using var localizedResponse = await Client.SendAsync(polishRequest);
        localizedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var localizedBody = JsonDocument.Parse(await localizedResponse.Content.ReadAsStringAsync());
        localizedBody.RootElement.GetProperty("errors").GetProperty("platform")[0].GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public void MatrixComparison_RejectsRouteDriftWithTheFieldAndValues()
    {
        AssertDrift("route", row => row with { Route = "/api/renamed" }, "/api/example", "/api/renamed");
    }

    [Test]
    public void MatrixComparison_RejectsStatusDriftWithTheFieldAndValues()
    {
        AssertDrift("statuses", row => row with { StatusCodes = "201" }, "200", "201");
    }

    [Test]
    public void MatrixComparison_RejectsLegacyPropertyDriftWithTheFieldAndValues()
    {
        AssertDrift("legacy fields evidence", row => row with { LegacyFieldsEvidence = "message" }, "msg", "message");
    }

    [Test]
    public void MatrixComparison_RejectsLocalizationDriftWithTheFieldAndValues()
    {
        AssertDrift("localization evidence", row => row with { LocalizationEvidence = "WrongLocalizationTest" }, "RequestLocalizationIntegrationTests", "WrongLocalizationTest");
    }

    [Test]
    public void MatrixComparison_RejectsUuidDriftWithTheFieldAndValues()
    {
        AssertDrift("UUID strings evidence", row => row with { UuidStringEvidence = "GuidObject" }, "UUID string", "GuidObject");
    }

    private static void AssertDrift(string field, Func<MatrixRow, MatrixRow> mutate, string expected, string actual)
    {
        var baseline = ExampleRow();
        Action action = () => AssertRowsMatch([baseline], [mutate(baseline)]);

        action.Should().Throw<AssertionException>()
            .WithMessage($"*{field}*{expected}*{actual}*");
    }

    private EndpointMatrixRow[] GetLiveRows()
    {
        var descriptions = Factory.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
            .ApiDescriptionGroups.Items
            .SelectMany(group => group.Items)
            .Where(description => description.ActionDescriptor is ControllerActionDescriptor)
            .ToArray();

        return Factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Select(method => CreateRow(endpoint, method, descriptions)) ?? [])
            .OrderBy(row => row.Row.EndpointKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static EndpointMatrixRow CreateRow(RouteEndpoint endpoint, string method, IReadOnlyCollection<ApiDescription> descriptions)
    {
        var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
        ArgumentNullException.ThrowIfNull(action);

        var route = "/" + endpoint.RoutePattern.RawText!.TrimStart('/');
        var matchingDescriptions = descriptions.Where(description =>
            description.HttpMethod == method
            && "/" + description.RelativePath!.TrimStart('/') == route
            && description.ActionDescriptor is ControllerActionDescriptor describedAction
            && describedAction.ControllerTypeInfo == action.ControllerTypeInfo
            && describedAction.ActionName == action.ActionName).ToArray();
        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
        var apiDescription = matchingDescriptions.SingleOrDefault();
        var request = apiDescription?.ParameterDescriptions
            .Where(parameter => parameter.Source?.Id == "Body")
            .Select(parameter => parameter.Type.Name)
            .DefaultIfEmpty("none")
            .SingleOrDefault() ?? "none";
        var responses = apiDescription is null
            ? Array.Empty<string>()
            : apiDescription.SupportedResponseTypes
                .Select(response => response.StatusCode.ToString())
                .OrderBy(status => status, StringComparer.Ordinal)
                .ToArray();
        var contentTypes = apiDescription is null
            ? Array.Empty<string>()
            : apiDescription.SupportedResponseTypes
                .SelectMany(response => response.ApiResponseFormats)
                .Select(format => format.MediaType)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        var idempotency = endpoint.Metadata.GetMetadata<LgymApi.Api.Idempotency.ApiIdempotencyAttribute>();

        return new EndpointMatrixRow(
            new MatrixRow(
                method,
                route,
                $"{action.ControllerTypeInfo.Name}.{action.ActionName}",
                anonymous ? "anonymous" : authorization.Any() ? string.Join(',', authorization.Select(data => data.Policy ?? data.Roles ?? "authorize")) : "implicit",
                request,
                string.Join(',', responses),
                string.Join(',', contentTypes),
                "ContractCompatibilityTests",
                "PostgreSqlApiCompatibilityTests; TypedIdEfTests",
                "EnumLookupResponseSnapshotTests",
                "RequestLocalizationIntegrationTests",
                idempotency is null ? "no" : $"{idempotency.ScopeSource}:{idempotency.RouteTemplate}",
                "EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix",
                string.Empty,
                string.Empty,
                string.Empty),
            matchingDescriptions.Length);
    }

    private async Task<HttpResponseMessage> SendWithoutAuthorizationAsync(HttpMethod method, string route, object body)
    {
        var authorization = Client.DefaultRequestHeaders.Authorization;
        try
        {
            ClearAuthorizationHeader();
            using var request = new HttpRequestMessage(method, route)
            {
                Content = JsonContent.Create(body)
            };
            return await Client.SendAsync(request);
        }
        finally
        {
            Client.DefaultRequestHeaders.Authorization = authorization;
        }
    }

    private static async Task AssertLegacyMessageAsync(HttpResponseMessage response, string? expectedMessage = null)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("msg").GetString().Should().NotBeNullOrWhiteSpace();
        body.RootElement.TryGetProperty("message", out _).Should().BeFalse();
        if (expectedMessage is not null)
        {
            body.RootElement.GetProperty("msg").GetString().Should().Be(expectedMessage);
        }
    }

    private static IReadOnlyList<MatrixRow> ReadMatrixRows(string matrix)
    {
        var lines = matrix.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var headerIndex = Array.FindIndex(lines, line => line.StartsWith("| method | route | action |", StringComparison.Ordinal));
        headerIndex.Should().BeGreaterThanOrEqualTo(0, "the endpoint matrix must have its contract header");

        return lines.Skip(headerIndex + 2)
            .TakeWhile(line => line.StartsWith("| ", StringComparison.Ordinal))
            .Select(ParseMatrixRow)
            .ToArray();
    }

    private static void AssertRowsMatch(IReadOnlyList<MatrixRow> expectedRows, IReadOnlyList<MatrixRow> actualRows)
    {
        expectedRows.Should().HaveSameCount(actualRows, "the document must have one row per live controller endpoint");
        expectedRows.Select(row => row.RouteKey).Should().OnlyHaveUniqueItems("the document must not duplicate a method and route");
        actualRows.Select(row => row.RouteKey).Should().OnlyHaveUniqueItems("live metadata must not duplicate a method and route");

        var expectedByAction = expectedRows.GroupBy(row => row.Action, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal);
        var actualByAction = actualRows.GroupBy(row => row.Action, StringComparer.Ordinal).ToDictionary(group => group.Key, StringComparer.Ordinal);
        foreach (var expectedGroup in expectedByAction)
        {
            actualByAction.TryGetValue(expectedGroup.Key, out var actualGroup).Should().BeTrue($"action '{expectedGroup.Key}' must remain in the live endpoint inventory");
            var expected = expectedGroup.OrderBy(row => row.EndpointKey, StringComparer.Ordinal).ToArray();
            var actual = actualGroup!.OrderBy(row => row.EndpointKey, StringComparer.Ordinal).ToArray();
            expected.Should().HaveSameCount(actual, $"action '{expectedGroup.Key}' must keep its route aliases");

            for (var index = 0; index < expected.Length; index++)
            {
                AssertRowMatches(expected[index], actual[index]);
            }
        }

        actualByAction.Keys.OrderBy(key => key, StringComparer.Ordinal)
            .Should().Equal(expectedByAction.Select(group => group.Key));
    }

    private static void AssertRowMatches(MatrixRow expected, MatrixRow actual)
    {
        AssertFieldMatches(expected, actual, "method", expected.Method, actual.Method);
        AssertFieldMatches(expected, actual, "route", expected.Route, actual.Route);
        AssertFieldMatches(expected, actual, "action", expected.Action, actual.Action);
        AssertFieldMatches(expected, actual, "authorization", expected.Authorization, actual.Authorization);
        AssertFieldMatches(expected, actual, "request DTO", expected.RequestDto, actual.RequestDto);
        AssertFieldMatches(expected, actual, "statuses", expected.StatusCodes, actual.StatusCodes);
        AssertFieldMatches(expected, actual, "content types", expected.ContentTypes, actual.ContentTypes);
        AssertFieldMatches(expected, actual, "legacy fields evidence", expected.LegacyFieldsEvidence, actual.LegacyFieldsEvidence);
        AssertFieldMatches(expected, actual, "UUID strings evidence", expected.UuidStringEvidence, actual.UuidStringEvidence);
        AssertFieldMatches(expected, actual, "enum policy evidence", expected.EnumPolicyEvidence, actual.EnumPolicyEvidence);
        AssertFieldMatches(expected, actual, "localization evidence", expected.LocalizationEvidence, actual.LocalizationEvidence);
        AssertFieldMatches(expected, actual, "idempotency", expected.Idempotency, actual.Idempotency);
        AssertFieldMatches(expected, actual, "evidence locator", expected.EvidenceLocator, actual.EvidenceLocator);
    }

    private static void AssertFieldMatches(MatrixRow expected, MatrixRow actual, string field, string expectedValue, string actualValue)
    {
        if (!string.Equals(expectedValue, actualValue, StringComparison.Ordinal))
        {
            throw new AssertionException($"Endpoint contract matrix drift for '{expected.EndpointKey}' field '{field}': expected '{expectedValue}', actual '{actualValue}'.");
        }
    }

    private static void AssertEvidenceLocatorResolves(string locator, string endpointKey)
    {
        _ = ResolveExecutableTestMethod(locator, endpointKey, "evidence locator");
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/admin/users/{id}", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("GET", "/api/appconfig/{id}", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("GET", "/api/roles", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("GET", "/api/roles/permission-claims", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("GET", "/api/roles/{id}", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/admin/users/paginated", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/admin/users/{id}/block", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/admin/users/{id}/delete", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/admin/users/{id}/unblock", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/admin/users/{id}/update", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/appConfig/createNewAppVersion/{id}", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/appconfig/paginated", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/appconfig/{id}/delete", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/appconfig/{id}/update", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/roles", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/roles/paginated", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/roles/users/{id}/roles", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/roles/{id}/delete", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/roles/{id}/update", "admin", "ordinary-user-denial")]
    [AuthorizationEvidence("POST", "/api/internal/push/test-event", "admin", "ordinary-user-denial")]
    public async Task Task8_AdminRoutes_OrdinaryUserIsDenied()
    {
        var user = await SeedUserAsync("task8-ordinary", "task8-ordinary@example.com");
        SetAuthorizationHeader(user.Id);

        var requestIndex = 0;
        foreach (var send in CreateTask8AdminRouteRequests(user.Id.ToString()))
        {
            using var response = await send();
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "admin request {0} must be denied before action execution", requestIndex++);
        }
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/admin/users/{id}", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("GET", "/api/appconfig/{id}", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("GET", "/api/roles", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("GET", "/api/roles/permission-claims", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("GET", "/api/roles/{id}", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/admin/users/paginated", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/admin/users/{id}/block", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/admin/users/{id}/delete", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/admin/users/{id}/unblock", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/admin/users/{id}/update", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/appConfig/createNewAppVersion/{id}", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/appconfig/paginated", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/appconfig/{id}/delete", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/appconfig/{id}/update", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/roles", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/roles/paginated", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/roles/users/{id}/roles", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/roles/{id}/delete", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/roles/{id}/update", "admin", "stale-token-demotion-denial")]
    [AuthorizationEvidence("POST", "/api/internal/push/test-event", "admin", "stale-token-demotion-denial")]
    public async Task Task8_AdminRoutes_PreIssuedTokenIsDeniedAfterDemotion()
    {
        var demotedAdministrator = await SeedUserAsync("task8-demoted", "task8-demoted@example.com", isAdmin: true);
        SetAuthorizationHeader(demotedAdministrator.Id);
        var preIssuedToken = Client.DefaultRequestHeaders.Authorization;

        var administrator = await SeedAdminAsync();
        SetAuthorizationHeader(administrator.Id);
        using (var demotion = await Client.PostAsJsonAsync($"/api/roles/users/{demotedAdministrator.Id}/roles", new { roles = new[] { "User" } }))
        {
            demotion.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        Client.DefaultRequestHeaders.Authorization = preIssuedToken;
        var requestIndex = 0;
        foreach (var send in CreateTask8AdminRouteRequests(demotedAdministrator.Id.ToString()))
        {
            using var response = await send();
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "demoted administrator request {0} must be denied before action execution", requestIndex++);
        }
    }

    private IEnumerable<Func<Task<HttpResponseMessage>>> CreateTask8AdminRouteRequests(string targetId)
    {
        yield return () => Client.GetAsync($"/api/admin/users/{targetId}");
        yield return () => Client.GetAsync($"/api/appconfig/{targetId}");
        yield return () => Client.GetAsync("/api/roles");
        yield return () => Client.GetAsync("/api/roles/permission-claims");
        yield return () => Client.GetAsync($"/api/roles/{targetId}");
        yield return () => Client.PostAsJsonAsync("/api/admin/users/paginated", new { page = 1, pageSize = 1 });
        yield return () => Client.PostAsJsonAsync($"/api/admin/users/{targetId}/block", new { });
        yield return () => Client.PostAsJsonAsync($"/api/admin/users/{targetId}/delete", new { });
        yield return () => Client.PostAsJsonAsync($"/api/admin/users/{targetId}/unblock", new { });
        yield return () => Client.PostAsJsonAsync($"/api/admin/users/{targetId}/update", new { });
        yield return () => Client.PostAsJsonAsync($"/api/appConfig/createNewAppVersion/{targetId}", new
        {
            platform = "Android",
            minRequiredVersion = "1.0.0",
            latestVersion = "1.0.0",
            forceUpdate = false,
            updateUrl = "https://example.com",
            releaseNotes = "Task 8"
        });
        yield return () => Client.PostAsJsonAsync("/api/appconfig/paginated", new { page = 1, pageSize = 1 });
        yield return () => Client.PostAsJsonAsync($"/api/appconfig/{targetId}/delete", new { });
        yield return () => Client.PostAsJsonAsync($"/api/appconfig/{targetId}/update", new
        {
            platform = "Android",
            minRequiredVersion = "1.0.0",
            latestVersion = "1.0.0",
            forceUpdate = false,
            updateUrl = "https://example.com",
            releaseNotes = "Task 8"
        });
        yield return () => Client.PostAsJsonAsync("/api/roles", new { });
        yield return () => Client.PostAsJsonAsync("/api/roles/paginated", new { page = 1, pageSize = 1 });
        yield return () => Client.PostAsJsonAsync($"/api/roles/users/{targetId}/roles", new { roles = Array.Empty<string>() });
        yield return () => Client.PostAsJsonAsync($"/api/roles/{targetId}/delete", new { });
        yield return () => Client.PostAsJsonAsync($"/api/roles/{targetId}/update", new { });
        yield return () => Client.PostAsJsonAsync("/api/internal/push/test-event", new
        {
            recipientUserId = targetId,
            type = "internal.test.push",
            eventId = "task8-stale-push-test-event",
            entityId = (string?)null,
            inAppNotificationId = (string?)null,
            deeplink = (string?)null
        });
    }

    [Test]
    [AuthorizationEvidence("GET", "/api/account/external-logins", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/checkToken", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/deleteAccount", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/tutorials/active", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/tutorials/{tutorialType}", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/account/link-google", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/account/unlink-google", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/changeVisibilityInRanking", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/logout", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/tutorials/complete", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/tutorials/completeStep", "own", "anonymous-denial")]
    [AuthorizationEvidence("POST", "/api/updateTimeZone", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/trainer/checkToken", "own", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/enums", "authenticated-global", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/enums/all", "authenticated-global", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/enums/{enumType}", "authenticated-global", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/getUsersRanking", "authenticated-global", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/userInfo/{id}/getUserEloPoints", "authenticated-global", "anonymous-denial")]
    [AuthorizationEvidence("GET", "/api/{id}/isAdmin", "authenticated-global", "anonymous-denial")]
    public async Task Task8_IdentityAndReferenceRoutes_AnonymousRequestsAreDenied()
    {
        ClearAuthorizationHeader();
        var id = Id<AccountReference>.New().ToString();
        var requests = new Func<Task<HttpResponseMessage>>[]
        {
            () => Client.GetAsync("/api/account/external-logins"),
            () => Client.GetAsync("/api/checkToken"),
            () => Client.GetAsync("/api/deleteAccount"),
            () => Client.GetAsync("/api/tutorials/active"),
            () => Client.GetAsync("/api/tutorials/OnboardingDemo"),
            () => Client.PostAsJsonAsync("/api/account/link-google", new { idToken = "missing" }),
            () => Client.PostAsync("/api/account/unlink-google", null),
            () => Client.PostAsJsonAsync("/api/changeVisibilityInRanking", new { isVisibleInRanking = true }),
            () => Client.PostAsync("/api/logout", null),
            () => Client.PostAsJsonAsync("/api/tutorials/complete", new { tutorialType = "OnboardingDemo" }),
            () => Client.PostAsJsonAsync("/api/tutorials/completeStep", new { tutorialType = "OnboardingDemo", step = "CreateArea" }),
            () => Client.PostAsJsonAsync("/api/updateTimeZone", new { preferredTimeZone = "UTC" }),
            () => Client.GetAsync("/api/trainer/checkToken"),
            () => Client.GetAsync("/api/enums"),
            () => Client.GetAsync("/api/enums/all"),
            () => Client.GetAsync("/api/enums/BodyParts"),
            () => Client.GetAsync("/api/getUsersRanking"),
            () => Client.GetAsync($"/api/userInfo/{id}/getUserEloPoints"),
            () => Client.GetAsync($"/api/{id}/isAdmin")
        };

        foreach (var send in requests)
        {
            using var response = await send();
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    private static void AssertAuthorizationEvidenceResolves(MatrixRow row)
    {
        var facets = ParseFacets(row.AccessFacets);
        if (row.AccessClass == "own"
            && row.RequestDto == "none"
            && !row.Route.Contains('{')
            && row.AccessFacets == "none"
            && !facets.Contains("actor-derived-subject", StringComparer.Ordinal))
        {
            throw new AssertionException(
                $"Endpoint contract matrix authorization drift for '{row.RouteKey}' field 'access facets': " +
                "an own subjectless route requires 'actor-derived-subject' and evidence category 'no-client-subject'.");
        }

        var locators = row.AuthorizationEvidence
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var duplicateLocator = locators
            .GroupBy(locator => locator, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateLocator is not null)
        {
            throw new AssertionException(
                $"Endpoint contract matrix authorization drift for '{row.RouteKey}' field 'authorization evidence': " +
                $"duplicate locator '{duplicateLocator.Key}'.");
        }

        var categories = new List<string>();
        foreach (var locator in locators)
        {
            var method = ResolveExecutableTestMethod(locator, row.RouteKey, "authorization evidence");
            var attributes = method.GetCustomAttributes<AuthorizationEvidenceAttribute>(inherit: false).ToArray();
            if (attributes.Length == 0)
            {
                throw new AssertionException(
                    $"Endpoint contract matrix authorization drift for '{row.RouteKey}' field 'authorization evidence': " +
                    $"locator '{locator}' is generic compatibility-only evidence cannot prove semantic authorization.");
            }

            var exact = attributes.Where(attribute =>
                    attribute.Method == row.Method
                    && attribute.Route == row.Route
                    && attribute.AccessClass == row.AccessClass)
                .ToArray();
            if (exact.Length == 0)
            {
                AssertAuthorizationEvidenceIdentity(row, locator, attributes);
            }

            foreach (var attribute in exact)
            {
                if (!ApprovedEvidenceCategories.Contains(attribute.Category, StringComparer.Ordinal))
                {
                    throw new AssertionException(
                        $"Endpoint contract matrix authorization drift for '{row.RouteKey}' field 'evidence category': " +
                        $"unknown category '{attribute.Category}' at '{locator}'.");
                }

                categories.Add(attribute.Category);
            }
        }

        var duplicateCategory = categories
            .GroupBy(category => category, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateCategory is not null)
        {
            throw new AssertionException(
                $"Endpoint contract matrix authorization drift for '{row.RouteKey}' field 'authorization evidence categories': " +
                $"duplicate category '{duplicateCategory.Key}'.");
        }

        var missing = RequiredEvidenceCategories(row.AccessClass, facets)
            .Where(category => !categories.Contains(category, StringComparer.Ordinal))
            .ToArray();
        if (missing.Length != 0)
        {
            throw new AssertionException(
                $"Endpoint contract matrix authorization drift for '{row.RouteKey}' field 'authorization evidence categories': " +
                $"missing {string.Join(", ", missing)}.");
        }
    }

    private static void AssertAuthorizationEvidenceIdentity(
        MatrixRow row,
        string locator,
        IReadOnlyCollection<AuthorizationEvidenceAttribute> attributes)
    {
        var methodMismatch = attributes.FirstOrDefault(attribute =>
            attribute.Route == row.Route && attribute.AccessClass == row.AccessClass);
        if (methodMismatch is not null)
        {
            throw new AssertionException(
                $"Endpoint contract matrix authorization drift for '{row.RouteKey}' field 'authorization evidence method': " +
                $"expected '{row.Method}', actual '{methodMismatch.Method}' at '{locator}'.");
        }

        var classMismatch = attributes.FirstOrDefault(attribute =>
            attribute.Method == row.Method && attribute.Route == row.Route);
        if (classMismatch is not null)
        {
            throw new AssertionException(
                $"Endpoint contract matrix authorization drift for '{row.RouteKey}' field 'authorization evidence access class': " +
                $"expected '{row.AccessClass}', actual '{classMismatch.AccessClass}' at '{locator}'.");
        }

        var routeMismatch = attributes.LastOrDefault(attribute =>
            attribute.Method == row.Method && attribute.AccessClass == row.AccessClass);
        if (routeMismatch is not null)
        {
            throw new AssertionException(
                $"Endpoint contract matrix authorization drift for '{row.RouteKey}' field 'authorization evidence route': " +
                $"expected '{row.Route}', actual '{routeMismatch.Route}' at '{locator}'.");
        }

        throw new AssertionException(
            $"Endpoint contract matrix authorization drift for '{row.RouteKey}' field 'authorization evidence': " +
            $"locator '{locator}' has no metadata matching method, route, and access class.");
    }

    private static MethodInfo ResolveExecutableTestMethod(string locator, string endpointKey, string field)
    {
        var parts = locator.Split('.', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw new AssertionException($"'{endpointKey}' field '{field}' needs a Type.Method executable evidence locator: '{locator}'.");
        }

        var fixture = typeof(EndpointContractMatrixTests).Assembly.GetTypes()
            .SingleOrDefault(type => type.Name == parts[0]);
        if (fixture is null)
        {
            throw new AssertionException($"'{endpointKey}' field '{field}' locator '{locator}' must resolve to a test fixture.");
        }

        var method = fixture.GetMethod(parts[1], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method is null)
        {
            throw new AssertionException($"'{endpointKey}' field '{field}' locator '{locator}' must resolve to a test method.");
        }

        if (!method.GetCustomAttributes(inherit: true)
                .Any(attribute => attribute is TestAttribute or TestCaseAttribute or TestCaseSourceAttribute))
        {
            throw new AssertionException($"'{endpointKey}' field '{field}' locator '{locator}' must resolve to an executable NUnit test.");
        }

        return method;
    }

    private static MatrixRow ParseMatrixRow(string line)
    {
        var values = line.Trim().Trim('|').Split('|').Select(value => value.Trim()).ToArray();
        var routeKey = values.Length >= 2 ? $"{values[0]} {values[1]}" : line;
        if (values.Length != 16)
        {
            var columns = values.Length >= 13 ? "every semantic authorization column" : "every contract column";
            throw new AssertionException($"Matrix row '{routeKey}' must define {columns}; found {values.Length} columns.");
        }

        for (var index = 0; index < 13; index++)
        {
            if (string.IsNullOrWhiteSpace(values[index]))
            {
                throw new AssertionException($"Matrix row '{routeKey}' cannot omit contract field {index + 1}.");
            }
        }

        if (string.IsNullOrWhiteSpace(values[13]))
        {
            throw new AssertionException($"Matrix row '{routeKey}' field 'access class' is required.");
        }

        if (!ApprovedAccessClasses.Contains(values[13], StringComparer.Ordinal))
        {
            throw new AssertionException(
                $"Matrix row '{routeKey}' field 'access class' must be one of: {string.Join(", ", ApprovedAccessClasses)}; actual '{values[13]}'.");
        }

        if (string.IsNullOrWhiteSpace(values[14]))
        {
            throw new AssertionException($"Matrix row '{routeKey}' field 'access facets' must use 'none' when no facet applies.");
        }

        _ = ParseFacets(values[14], routeKey);
        if (string.IsNullOrWhiteSpace(values[15]))
        {
            throw new AssertionException($"Matrix row '{routeKey}' field 'authorization evidence' is required.");
        }

        return new MatrixRow(
            values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7],
            values[8], values[9], values[10], values[11], values[12], values[13], values[14], values[15]);
    }

    private static string[] ParseFacets(string value, string? routeKey = null)
    {
        if (value == "none")
        {
            return [];
        }

        var facets = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var duplicate = facets.GroupBy(facet => facet, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new AssertionException($"Matrix row '{routeKey ?? "unknown"}' field 'access facets' duplicates '{duplicate.Key}'.");
        }

        var unknown = facets.FirstOrDefault(facet => !ApprovedAccessFacets.Contains(facet, StringComparer.Ordinal));
        if (unknown is not null)
        {
            throw new AssertionException(
                $"Matrix row '{routeKey ?? "unknown"}' field 'access facets' must contain only: " +
                $"{string.Join(", ", ApprovedAccessFacets)}; actual '{unknown}'.");
        }

        return facets;
    }

    private static IEnumerable<string> RequiredEvidenceCategories(string accessClass, IReadOnlyCollection<string> facets)
    {
        string[] baseCategories = accessClass switch
        {
            "public" => ["anonymous-intended-behavior"],
            "own" => ["owner-allow", "anonymous-denial"],
            "trainer-shared" => ["active-relationship-allow", "unrelated-relationship-denial", "anonymous-denial"],
            "admin" => ["current-permission-allow", "ordinary-user-denial", "stale-token-demotion-denial"],
            "authenticated-global" => ["ordinary-authenticated-allow", "anonymous-denial"],
            _ => []
        };
        foreach (var category in baseCategories)
        {
            yield return category;
        }

        foreach (var facet in facets)
        {
            string[] facetCategories = facet switch
            {
                "actor-derived-subject" => ["no-client-subject"],
                "foreign-object" => ["foreign-object-denial-no-mutation"],
                "owned-resource" => ["owner-custom-allow", "foreign-custom-denial"],
                "global-visible" => ["global-resource-allow"],
                "manager-override" => ["current-manager-allow", "stale-manager-denial", "ordinary-manager-denial"],
                "opaque-capability" => ["invalid-capability-denial", "expired-capability-denial", "tampered-capability-denial"],
                "relationship-revocable" => ["former-relationship-denial"],
                _ => []
            };
            foreach (var category in facetCategories)
            {
                yield return category;
            }
        }
    }

    private static IEnumerable<TestCaseData> CompleteSemanticAuthorizationRows()
    {
        yield return new TestCaseData("| GET | /api/invitations/{invitationId} | PublicInvitationController.GetInvitationStatus | anonymous | none | 200,404 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | public | opaque-capability | EndpointContractMatrixTests.SemanticAuthorizationParser_AcceptsCompleteClassAndFacetProfiles |")
            .SetName("public opaque-capability profile is complete");
        yield return new TestCaseData("| POST | /api/logout | UserController.Logout | authorize | none | 200 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | own | actor-derived-subject | EndpointContractMatrixTests.SemanticAuthorizationParser_AcceptsCompleteClassAndFacetProfiles |")
            .SetName("own actor-derived-subject profile is complete");
        yield return new TestCaseData("| GET | /api/own/{id} | ExampleController.GetOwn | authorize | none | 200,404 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | own | foreign-object | EndpointContractMatrixTests.SemanticAuthorizationParser_AcceptsCompleteClassAndFacetProfiles |")
            .SetName("own foreign-object profile is complete");
        yield return new TestCaseData("| GET | /api/trainer/trainees/{traineeId}/plans | TrainerManagedPlansController.GetTraineePlans | policy.trainer.access | none | 200 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | trainer-shared | relationship-revocable | EndpointContractMatrixTests.SemanticAuthorizationParser_AcceptsCompleteClassAndFacetProfiles |")
            .SetName("trainer-shared relationship-revocable profile is complete");
        yield return new TestCaseData("| GET | /api/admin/users/{id} | AdminUserController.GetUser | policy.admin.access | none | 200,404 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | admin | none | EndpointContractMatrixTests.SemanticAuthorizationParser_AcceptsCompleteClassAndFacetProfiles |")
            .SetName("admin profile is complete");
        yield return new TestCaseData("| GET | /api/exercise/{id}/getExercise | ExerciseController.GetExercise | authorize | none | 200,404 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | authenticated-global | owned-resource,global-visible,manager-override | EndpointContractMatrixTests.SemanticAuthorizationParser_AcceptsCompleteClassAndFacetProfiles |")
            .SetName("authenticated-global mixed-visibility profile is complete");
    }

    private static IEnumerable<TestCaseData> SemanticAuthorizationSchemaFailureRows()
    {
        yield return new TestCaseData(
                "| GET | /api/example | ExampleController.Get | authorize | none | 200 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix |  | none | EndpointContractMatrixTests.SemanticAuthorizationParser_AcceptsCompleteClassAndFacetProfiles |",
                "*GET /api/example*field 'access class'*required*")
            .SetName("missing access class identifies route and field");
        yield return new TestCaseData(
                "| GET | /api/example | ExampleController.Get | authorize | none | 200 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | trainer | none | EndpointContractMatrixTests.SemanticAuthorizationParser_AcceptsCompleteClassAndFacetProfiles |",
                "*GET /api/example*field 'access class'*public*own*trainer-shared*admin*authenticated-global*")
            .SetName("unknown access class lists every approved value");
        yield return new TestCaseData(
                "| GET | /api/example | ExampleController.Get | authorize | none | 200 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | own |  | EndpointContractMatrixTests.SemanticAuthorizationParser_AcceptsCompleteClassAndFacetProfiles |",
                "*GET /api/example*field 'access facets'*must use 'none' when no facet applies*")
            .SetName("missing access facets requires explicit none");
        yield return new TestCaseData(
                "| GET | /api/example | ExampleController.Get | authorize | none | 200 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | own | delegated | EndpointContractMatrixTests.SemanticAuthorizationParser_AcceptsCompleteClassAndFacetProfiles |",
                "*GET /api/example*field 'access facets'*actor-derived-subject*foreign-object*owned-resource*global-visible*manager-override*opaque-capability*relationship-revocable*")
            .SetName("unknown access facet lists every approved value");
        yield return new TestCaseData(
                "| GET | /api/example | ExampleController.Get | authorize | none | 200 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | own | none |  |",
                "*GET /api/example*field 'authorization evidence'*required*")
            .SetName("missing authorization evidence identifies route and field");
        yield return new TestCaseData(
                "| GET | /api/example | ExampleController.Get | authorize | none | 200 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | own | none |",
                "*GET /api/example*must define every semantic authorization column*")
            .SetName("malformed semantic row identifies route");
    }

    private static IEnumerable<TestCaseData> SemanticAuthorizationGuardFailureRows()
    {
        const string fixture = "EndpointContractMatrixTests.SemanticAuthorizationGuard_RejectsIncompleteOrMismatchedEvidence";

        yield return new TestCaseData(
                $"| POST | /api/logout | UserController.Logout | authorize | none | 200 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | own | none | {fixture} |",
                "*POST /api/logout*field 'access facets'*actor-derived-subject*no-client-subject*")
            .SetName("subjectless own route requires actor-derived proof");
        yield return new TestCaseData(
                $"| GET | /api/evidence/duplicate | ExampleController.Get | authorize | none | 200 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | own | foreign-object | {fixture}; {fixture} |",
                $"*GET /api/evidence/duplicate*field 'authorization evidence'*duplicate*{fixture}*")
            .SetName("duplicate authorization evidence is rejected");
        yield return new TestCaseData(
                "| GET | /api/evidence/generic | ExampleController.Get | authorize | none | 200 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | own | foreign-object | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix |",
                "*GET /api/evidence/generic*field 'authorization evidence'*generic compatibility-only evidence cannot prove semantic authorization*")
            .SetName("generic compatibility-only evidence is rejected");
        yield return new TestCaseData(
                $"| GET | /api/evidence/unknown-category | ExampleController.Get | authorize | none | 200 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | own | foreign-object | {fixture} |",
                "*GET /api/evidence/unknown-category*field 'evidence category'*unknown-category*")
            .SetName("unknown evidence category is rejected");
        yield return new TestCaseData(
                $"| GET | /api/evidence/method | ExampleController.Get | authorize | none | 200 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | own | foreign-object | {fixture} |",
                "*GET /api/evidence/method*field 'authorization evidence method'*expected 'GET'*actual 'POST'*")
            .SetName("evidence method must match matrix method");
        yield return new TestCaseData(
                $"| GET | /api/evidence/route | ExampleController.Get | authorize | none | 200 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | own | foreign-object | {fixture} |",
                "*GET /api/evidence/route*field 'authorization evidence route'*actual '/api/evidence/other-route'*")
            .SetName("evidence route must match matrix route");
        yield return new TestCaseData(
                $"| GET | /api/evidence/class | ExampleController.Get | authorize | none | 200 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | own | foreign-object | {fixture} |",
                "*GET /api/evidence/class*field 'authorization evidence access class'*expected 'own'*actual 'authenticated-global'*")
            .SetName("evidence access class must match matrix class");
        yield return new TestCaseData(
                $"| GET | /api/exercise/{{id}}/getExercise | ExerciseController.GetExercise | authorize | none | 200,404 | application/json | ContractCompatibilityTests | TypedIdEfTests | EnumLookupResponseSnapshotTests | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | authenticated-global | owned-resource,global-visible,manager-override | {fixture} |",
                "*GET /api/exercise/{id}/getExercise*field 'authorization evidence categories'*owner-custom-allow*foreign-custom-denial*global-resource-allow*current-manager-allow*stale-manager-denial*ordinary-manager-denial*")
            .SetName("mixed Exercise visibility requires class and every facet category");
    }

    private static MatrixRow ExampleRow() => ParseMatrixRow("| GET | /api/example | Example.Action | anonymous | none | 200 | application/json | msg | UUID string | none | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix | public | none | EndpointContractMatrixTests.SemanticAuthorizationParser_AcceptsCompleteClassAndFacetProfiles |");

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "LgymApi.sln")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root for the endpoint matrix.");
    }

    private sealed record EndpointMatrixRow(MatrixRow Row, int ApiDescriptionCount);

    private sealed record MatrixRow(
        string Method,
        string Route,
        string Action,
        string Authorization,
        string RequestDto,
        string StatusCodes,
        string ContentTypes,
        string LegacyFieldsEvidence,
        string UuidStringEvidence,
        string EnumPolicyEvidence,
        string LocalizationEvidence,
        string Idempotency,
        string EvidenceLocator,
        string AccessClass,
        string AccessFacets,
        string AuthorizationEvidence)
    {
        public string Controller => Action.Split('.', 2, StringSplitOptions.None)[0];

        public string RouteKey => $"{Method} {Route}";

        public string EndpointKey => $"{RouteKey} | {Action}";

        public bool HasContractMetadata =>
            !string.IsNullOrWhiteSpace(Authorization)
            && !string.IsNullOrWhiteSpace(RequestDto)
            && !string.IsNullOrWhiteSpace(StatusCodes)
            && !string.IsNullOrWhiteSpace(ContentTypes)
            && !string.IsNullOrWhiteSpace(Idempotency)
            && !string.IsNullOrWhiteSpace(LegacyFieldsEvidence)
            && !string.IsNullOrWhiteSpace(UuidStringEvidence)
            && !string.IsNullOrWhiteSpace(EnumPolicyEvidence)
            && !string.IsNullOrWhiteSpace(LocalizationEvidence)
            && !string.IsNullOrWhiteSpace(EvidenceLocator)
            && Action.Contains('.', StringComparison.Ordinal);

        public string ToDocumentRow() => $"| {Method} | {Route} | {Action} | {Authorization} | {RequestDto} | {StatusCodes} | {ContentTypes} | {LegacyFieldsEvidence} | {UuidStringEvidence} | {EnumPolicyEvidence} | {LocalizationEvidence} | {Idempotency} | {EvidenceLocator} | {AccessClass} | {AccessFacets} | {AuthorizationEvidence} |";
    }
}
