using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class LegacyWorkoutProgressRouteSnapshotTests
{
    [Test]
    public void WorkoutProgress_LegacyRoutes_ShouldMatchPinnedHttpMethodAndTemplate()
    {
        var expectedRoutes = ExpectedRoutes.ToHashSet();
        var expectedActions = expectedRoutes
            .Select(static route => new ControllerAction(route.Controller, route.Action))
            .ToHashSet();
        var discoveredRoutes = DiscoverRoutes(expectedActions).ToList();
        var discoveredActions = discoveredRoutes
            .Select(static route => new ControllerAction(route.Controller, route.Action))
            .ToHashSet();

        Assert.Multiple(() =>
        {
            Assert.That(
                discoveredActions,
                Is.EquivalentTo(expectedActions),
                "Every pinned legacy action must be discovered from its controller attributes.");
            Assert.That(
                discoveredRoutes,
                Is.EquivalentTo(expectedRoutes),
                "Workout & Progress legacy HTTP methods and route templates changed.");
        });
    }

    private static IEnumerable<LegacyRoute> DiscoverRoutes(IReadOnlySet<ControllerAction> expectedActions)
    {
        var syntaxTrees = ArchitectureTestHelpers.ParseProjectSources("LgymApi.Api");

        foreach (var classDeclaration in syntaxTrees
                     .SelectMany(static tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()))
        {
            var controller = classDeclaration.Identifier.ValueText;
            var controllerActions = expectedActions
                .Where(action => action.Controller == controller)
                .ToHashSet();

            if (controllerActions.Count == 0)
            {
                continue;
            }

            var controllerTemplate = GetRouteTemplate(classDeclaration.AttributeLists);
            Assert.That(controllerTemplate, Is.Not.Null, $"{controller} must declare its route template.");

            foreach (var methodDeclaration in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
            {
                var action = methodDeclaration.Identifier.ValueText;
                if (!controllerActions.Contains(new ControllerAction(controller, action)))
                {
                    continue;
                }

                foreach (var attribute in methodDeclaration.AttributeLists.SelectMany(static list => list.Attributes))
                {
                    if (!TryGetHttpMethod(attribute, out var method))
                    {
                        continue;
                    }

                    var actionTemplate = GetStringArgument(attribute);
                    Assert.That(actionTemplate, Is.Not.Null, $"{controller}.{action} must declare its route template.");
                    yield return new LegacyRoute(controller, action, method, CombineTemplates(controllerTemplate!, actionTemplate!));
                }
            }
        }
    }

    private static string? GetRouteTemplate(SyntaxList<AttributeListSyntax> attributeLists)
    {
        return attributeLists
            .SelectMany(static list => list.Attributes)
            .Where(static attribute => GetAttributeName(attribute) == "Route")
            .Select(GetStringArgument)
            .SingleOrDefault();
    }

    private static bool TryGetHttpMethod(AttributeSyntax attribute, out string method)
    {
        method = GetAttributeName(attribute) switch
        {
            "HttpGet" => "GET",
            "HttpPost" => "POST",
            _ => string.Empty
        };

        return method.Length > 0;
    }

    private static string GetAttributeName(AttributeSyntax attribute)
    {
        var name = attribute.Name.ToString();
        var unqualifiedName = name[(name.LastIndexOf('.') + 1)..];
        return unqualifiedName.EndsWith("Attribute", StringComparison.Ordinal)
            ? unqualifiedName[..^"Attribute".Length]
            : unqualifiedName;
    }

    private static string? GetStringArgument(AttributeSyntax attribute)
    {
        return attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax
        {
            RawKind: (int)SyntaxKind.StringLiteralExpression
        } literal
            ? literal.Token.ValueText
            : null;
    }

    private static string CombineTemplates(string controllerTemplate, string actionTemplate)
    {
        return $"{controllerTemplate.TrimEnd('/')}/{actionTemplate.TrimStart('/')}";
    }

    private static readonly LegacyRoute[] ExpectedRoutes =
    [
        new("TrainingController", "AddTraining", "POST", "api/{id}/addTraining"),
        new("TrainingController", "GetLastTraining", "GET", "api/{id}/getLastTraining"),
        new("TrainingController", "GetTrainingByDate", "POST", "api/{id}/getTrainingByDate"),
        new("TrainingController", "GetTrainingDates", "GET", "api/{id}/getTrainingDates"),
        new("ExerciseScoresController", "GetExerciseScoresChartData", "POST", "api/exerciseScores/{id}/getExerciseScoresChartData"),
        new("MeasurementsController", "AddMeasurement", "POST", "api/measurements/add"),
        new("MeasurementsController", "AddMeasurementsBulk", "POST", "api/measurements/add-bulk"),
        new("MeasurementsController", "GetMeasurementDetail", "GET", "api/measurements:/{id}/getMeasurementDetail"),
        new("MeasurementsController", "GetMeasurementsHistory", "GET", "api/measurements/{id}/getHistory"),
        new("MeasurementsController", "GetMeasurementsList", "GET", "api/measurements/{id}/list"),
        new("MeasurementsController", "GetMeasurementsTrend", "GET", "api/measurements/{id}/trend"),
        new("MeasurementsController", "GetMeasurementsTrends", "GET", "api/measurements/{id}/trends"),
        new("MainRecordsController", "AddNewRecord", "POST", "api/mainRecords/{id}/addNewRecord"),
        new("MainRecordsController", "GetMainRecordsHistory", "GET", "api/mainRecords/{id}/getMainRecordsHistory"),
        new("MainRecordsController", "GetLastMainRecords", "GET", "api/mainRecords/{id}/getLastMainRecords"),
        new("MainRecordsController", "DeleteMainRecord", "GET", "api/mainRecords/{id}/deleteMainRecord"),
        new("MainRecordsController", "UpdateMainRecords", "POST", "api/mainRecords/{id}/updateMainRecords"),
        new("MainRecordsController", "GetRecordOrPossibleRecordInExercise", "POST", "api/mainRecords/getRecordOrPossibleRecordInExercise"),
        new("EloRegistryController", "GetEloRegistryChart", "GET", "api/eloRegistry/{id}/getEloRegistryChart"),
        new("UserController", "GetUsersRanking", "GET", "api/getUsersRanking"),
        new("UserController", "GetUserElo", "GET", "api/userInfo/{id}/getUserEloPoints"),
        new("UserController", "ChangeVisibilityInRanking", "POST", "api/changeVisibilityInRanking")
    ];

    private sealed record ControllerAction(string Controller, string Action);

    private sealed record LegacyRoute(string Controller, string Action, string Method, string Template);
}
