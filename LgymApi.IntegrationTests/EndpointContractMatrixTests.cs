using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Domain.Enums;
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

    [Test]
    public void MatrixParser_NormalizesACompleteRowAndRejectsMissingFields()
    {
        Action missingField = () => ParseMatrixRow("| GET | /api/example | Example.Action |");

        missingField.Should().Throw<AssertionException>()
            .WithMessage("*must define every contract column*");

        var parsed = ParseMatrixRow("| GET | /api/example | Example.Action | anonymous | none | 200 | application/json | msg | UUID string | none | no | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix |");

        parsed.ToDocumentRow().Should().Be("| GET | /api/example | Example.Action | anonymous | none | 200 | application/json | msg | UUID string | none | no | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix |");
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
        }

        TestContext.Progress.WriteLine($"Route count: {rows.Length}");
        TestContext.Progress.WriteLine($"Route inventory SHA-256: {inventoryHash}");
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
            isVisibleInRanking = true
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
                "EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix"),
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
        var parts = locator.Split('.', 2, StringSplitOptions.TrimEntries);
        parts.Should().HaveCount(2, $"'{endpointKey}' needs a Type.Method executable evidence locator");
        var fixture = typeof(EndpointContractMatrixTests).Assembly.GetTypes()
            .SingleOrDefault(type => type.Name == parts[0]);
        fixture.Should().NotBeNull($"'{endpointKey}' evidence locator '{locator}' must resolve to a test fixture");
        var method = fixture!.GetMethod(parts[1], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        method.Should().NotBeNull($"'{endpointKey}' evidence locator '{locator}' must resolve to a test method");
        method!.GetCustomAttributes(inherit: true)
            .Any(attribute => attribute is TestAttribute or TestCaseAttribute or TestCaseSourceAttribute)
            .Should().BeTrue($"'{endpointKey}' evidence locator '{locator}' must resolve to an executable NUnit test");
    }

    private static MatrixRow ParseMatrixRow(string line)
    {
        var values = line.Trim().Trim('|').Split('|').Select(value => value.Trim()).ToArray();
        values.Should().HaveCount(13, $"matrix row '{line}' must define every contract column");
        values.Should().OnlyContain(value => !string.IsNullOrWhiteSpace(value), $"matrix row '{line}' cannot omit a contract field");
        return new MatrixRow(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8], values[9], values[10], values[11], values[12]);
    }

    private static MatrixRow ExampleRow() => ParseMatrixRow("| GET | /api/example | Example.Action | anonymous | none | 200 | application/json | msg | UUID string | none | RequestLocalizationIntegrationTests | no | EndpointContractMatrixTests.LiveControllerEndpointInventory_MatchesTheBaselineMatrix |");

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
        string EvidenceLocator)
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

        public string ToDocumentRow() => $"| {Method} | {Route} | {Action} | {Authorization} | {RequestDto} | {StatusCodes} | {ContentTypes} | {LegacyFieldsEvidence} | {UuidStringEvidence} | {EnumPolicyEvidence} | {LocalizationEvidence} | {Idempotency} | {EvidenceLocator} |";
    }
}
