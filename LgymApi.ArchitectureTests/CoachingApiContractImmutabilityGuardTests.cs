using System.IO;
using LgymApi.Api.Features.Trainer.Controllers;
using LgymApi.Application.Coaching.Invitations.Create;
using LgymApi.Application.Coaching.Invitations.CreateByEmail;
using LgymApi.Application.Coaching.Invitations.ListPaginated;
using LgymApi.Application.Coaching.Invitations.Revoke;
using LgymApi.Application.Coaching.ApiAdapters;
using LgymApi.Application.Coaching.ManagedPlans.Assign;
using LgymApi.Application.Coaching.ManagedPlans.Create;
using LgymApi.Application.Coaching.ManagedPlans.Delete;
using LgymApi.Application.Coaching.ManagedPlans.List;
using LgymApi.Application.Coaching.ManagedPlans.Unassign;
using LgymApi.Application.Coaching.ManagedPlans.Update;
using LgymApi.Application.Coaching.Progress.EloChart;
using LgymApi.Application.Coaching.Progress.ExerciseScoresChart;
using LgymApi.Application.Coaching.Progress.MainRecordsHistory;
using LgymApi.Application.Coaching.Progress.TrainingByDate;
using LgymApi.Application.Coaching.Progress.TrainingDates;
using LgymApi.Application.Coaching.Relationships.TrainerDashboard;
using LgymApi.Application.Coaching.Relationships.UnlinkTrainee;
using LgymApi.Application.Mapping.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class CoachingApiContractImmutabilityGuardTests
{
    private const string ContractChangeMessage = "Update this explicit compatibility manifest only with an approved Coaching API contract change.";

    [Test]
    public void Coaching_Controller_Actions_Must_Match_The_Explicit_Compatibility_Manifest()
    {
        var trees = ReadSourceTrees();
        var actualActions = DiscoverActions(trees).Select(ToActionContract).ToArray();
        var actualResponses = DiscoverActions(trees).SelectMany(ToResponseContracts).ToArray();
        var legacyInvitationCalls = DiscoverActions(trees)
            .Where(action => Calls(action.Method, "GetTrainerInvitationsAsync"))
            .Select(action => $"{action.Controller}.{action.Method.Identifier.ValueText}")
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(actualActions, Has.Length.EqualTo(30), $"Coaching must expose exactly 30 actions. {ContractChangeMessage}");
            Assert.That(actualActions.Select(action => (action.Route, action.Verb)).Distinct().ToArray(), Has.Length.EqualTo(30),
                $"Coaching route and verb pairs must be unique. {ContractChangeMessage}");
            AssertActionContractsMatch(actualActions);
            Assert.That(actualResponses, Is.EquivalentTo(ExpectedResponses),
                $"Coaching declared response DTOs and status codes changed. {ContractChangeMessage}");
            Assert.That(actualActions.Count(action => action.HasApiIdempotency), Is.EqualTo(1),
                $"Only POST /api/trainer/invitations may use ApiIdempotency. {ContractChangeMessage}");
            Assert.That(legacyInvitationCalls, Is.Empty,
                $"GetTrainerInvitationsAsync is application-only and must not be exposed by an HTTP action. {ContractChangeMessage}");
        });
    }

    [Test]
    public void CoachingManifestValidator_RejectsRouteVerbAuthorizationIdempotencyAndStatusDrift()
    {
        var changedContracts = ExpectedActions
            .Select(contract => contract.Action switch
            {
                "CreateInvitation" => contract with { Verb = "GET" },
                "GetInvitationsPaginated" => contract with { Route = "api/trainer/invitations/list" },
                "CreateInvitationByEmail" => contract with { Authorization = "AllowAnonymous" },
                "RevokeInvitation" => contract with { HasApiIdempotency = true },
                "GetDashboardTrainees" => contract with { SuccessStatus = "StatusCodes.Status201Created" },
                _ => contract
            })
            .ToArray();

        var exception = Assert.Throws<AssertionException>(() => AssertActionContractsMatch(changedContracts));

        Assert.That(exception!.Message, Does.Contain("CreateInvitation"));
        Assert.That(exception.Message, Does.Contain("GetInvitationsPaginated"));
        Assert.That(exception.Message, Does.Contain("CreateInvitationByEmail"));
        Assert.That(exception.Message, Does.Contain("RevokeInvitation"));
        Assert.That(exception.Message, Does.Contain("GetDashboardTrainees"));
    }

    [Test]
    public void TrainerControllerAdapters_UseOnlyTheirFocusedUseCasesAndMapper()
    {
        AssertConstructor(
            typeof(TrainerInvitationController),
            typeof(ITrainerInvitationApiPort),
            typeof(IMapper));
        AssertConstructor(
            typeof(TrainerDashboardProgressController),
            typeof(ITrainerDashboardProgressApiPort),
            typeof(IMapper));
        AssertConstructor(
            typeof(TrainerManagedPlansController),
            typeof(IManagedPlanAccountApiAdapter),
            typeof(IMapper));
    }

    private static void AssertConstructor(Type controllerType, params Type[] expectedDependencies)
    {
        var constructors = controllerType.GetConstructors();

        Assert.That(constructors, Has.Length.EqualTo(1), $"{controllerType.Name} must have one constructor.");
        Assert.That(
            constructors[0].GetParameters().Select(parameter => parameter.ParameterType),
            Is.EqualTo(expectedDependencies),
            $"{controllerType.Name} constructor dependencies changed.");
    }

    private static void AssertActionContractsMatch(IEnumerable<ActionContract> actual) =>
        Assert.That(actual, Is.EquivalentTo(ExpectedActions),
            $"Coaching action route, verb, authorization, idempotency, request, response, or success status changed. {ContractChangeMessage}");

    private static IReadOnlyList<SyntaxTree> ReadSourceTrees()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var trees = ArchitectureTestHelpers.ParseProjectSources("LgymApi.Api")
            .Where(tree => ExpectedControllerFiles.Contains(NormalizeRelativePath(repositoryRoot, tree.FilePath)))
            .ToList();
        var actualFiles = trees.Select(tree => NormalizeRelativePath(repositoryRoot, tree.FilePath)).ToHashSet(StringComparer.Ordinal);

        Assert.That(actualFiles, Is.EquivalentTo(ExpectedControllerFiles),
            $"Coaching controller source files changed or could not be found. {ContractChangeMessage}");
        return trees;
    }

    private static IEnumerable<DiscoveredAction> DiscoverActions(IEnumerable<SyntaxTree> trees)
    {
        var controllers = trees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(declaration => ExpectedControllerNames.Contains(declaration.Identifier.ValueText))
            .ToArray();
        var routes = controllers.GroupBy(declaration => declaration.Identifier.ValueText)
            .ToDictionary(group => group.Key, group => group.SelectMany(declaration => GetAttributes(declaration))
                .Where(attribute => GetAttributeName(attribute) == "Route")
                .Select(attribute => GetStringArgument(attribute) ?? "<missing>").Single(), StringComparer.Ordinal);
        var authorizations = controllers.GroupBy(declaration => declaration.Identifier.ValueText)
            .ToDictionary(group => group.Key, group => group.SelectMany(declaration => GetAttributes(declaration))
                .Where(IsAuthorization).Select(GetAuthorization).Single(), StringComparer.Ordinal);

        foreach (var declaration in controllers)
        {
            foreach (var method in declaration.Members.OfType<MethodDeclarationSyntax>())
            {
                var httpAttributes = GetAttributes(method).Where(attribute => GetHttpVerb(attribute) is not null).ToArray();
                var methodAuthorization = GetAttributes(method).Where(IsAuthorization).Select(GetAuthorization).ToArray();
                var authorization = methodAuthorization.Length == 0
                    ? authorizations[declaration.Identifier.ValueText]
                    : string.Join(" + ", new[] { authorizations[declaration.Identifier.ValueText] }.Concat(methodAuthorization));

                foreach (var httpAttribute in httpAttributes)
                {
                    yield return new DiscoveredAction(declaration.Identifier.ValueText, routes[declaration.Identifier.ValueText], authorization, method, httpAttribute);
                }
            }
        }
    }

    private static ActionContract ToActionContract(DiscoveredAction action)
    {
        var success = GetResponses(action.Method).Single(response => response.Status.StartsWith("StatusCodes.Status2", StringComparison.Ordinal));
        return new ActionContract(
            action.Controller,
            action.Method.Identifier.ValueText,
            CombineTemplates(action.ControllerRoute, GetStringArgument(action.HttpAttribute) ?? "<missing>"),
            GetHttpVerb(action.HttpAttribute)!,
            action.Authorization,
            GetAttributes(action.Method).Any(attribute => GetAttributeName(attribute) == "ApiIdempotency"),
            GetRequestType(action.Method),
            success.Response,
            success.Status);
    }

    private static IEnumerable<ResponseContract> ToResponseContracts(DiscoveredAction action) =>
        GetResponses(action.Method).Select(response => new ResponseContract(
            action.Controller, action.Method.Identifier.ValueText, response.Status, response.Response));

    private static IEnumerable<ResponseDeclaration> GetResponses(MethodDeclarationSyntax method) =>
        GetAttributes(method).Where(attribute => GetAttributeName(attribute) == "ProducesResponseType")
            .Select(attribute => new ResponseDeclaration(GetResponseStatus(attribute), GetResponseType(attribute)));

    private static string GetRequestType(MethodDeclarationSyntax method)
    {
        var requestTypes = method.ParameterList.Parameters
            .Where(parameter => GetAttributes(parameter).Any(attribute => GetAttributeName(attribute) is "FromBody" or "FromQuery"))
            .Select(parameter => parameter.Type?.ToString() ?? "<missing>")
            .ToArray();
        return requestTypes.Length == 0 ? "<none>" : string.Join(", ", requestTypes);
    }

    private static bool Calls(MethodDeclarationSyntax method, string methodName) => method.DescendantNodes()
        .OfType<InvocationExpressionSyntax>().Any(invocation => invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText == methodName,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == methodName,
            _ => false
        });

    private static IEnumerable<AttributeSyntax> GetAttributes(MemberDeclarationSyntax declaration) => declaration.AttributeLists.SelectMany(list => list.Attributes);

    private static IEnumerable<AttributeSyntax> GetAttributes(ParameterSyntax parameter) => parameter.AttributeLists.SelectMany(list => list.Attributes);

    private static bool IsAuthorization(AttributeSyntax attribute) => GetAttributeName(attribute) is "Authorize" or "AllowAnonymous";

    private static string GetAuthorization(AttributeSyntax attribute) => GetAttributeName(attribute) switch
    {
        "AllowAnonymous" => "AllowAnonymous",
        "Authorize" => attribute.ArgumentList?.Arguments.SingleOrDefault(argument => argument.NameEquals?.Name.Identifier.ValueText == "Policy")?.Expression.ToString() ?? "Authorize",
        _ => "<missing>"
    };

    private static string? GetHttpVerb(AttributeSyntax attribute) => GetAttributeName(attribute) switch
    {
        "HttpGet" => "GET",
        "HttpPost" => "POST",
        _ => null
    };

    private static string GetResponseStatus(AttributeSyntax attribute) => attribute.ArgumentList?.Arguments switch
    {
        { Count: 1 } arguments => arguments[0].Expression.ToString(),
        { Count: > 1 } arguments => arguments[1].Expression.ToString(),
        _ => "<missing>"
    };

    private static string GetResponseType(AttributeSyntax attribute) =>
        attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is TypeOfExpressionSyntax typeOf ? typeOf.Type.ToString() : "<missing>";

    private static string GetAttributeName(AttributeSyntax attribute) => attribute.Name.ToString().Replace("Attribute", string.Empty, StringComparison.Ordinal);

    private static string? GetStringArgument(AttributeSyntax attribute) =>
        attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal ? literal.Token.ValueText : null;

    private static string CombineTemplates(string controllerTemplate, string actionTemplate) => $"{controllerTemplate.TrimEnd('/')}/{actionTemplate.TrimStart('/')}";

    private static string NormalizeRelativePath(string repositoryRoot, string filePath) => Path.GetRelativePath(repositoryRoot, filePath).Replace('\\', '/');

    private static readonly HashSet<string> ExpectedControllerNames =
    ["TrainerInvitationController", "TrainerDashboardProgressController", "TrainerManagedPlansController", "TraineeRelationshipController", "TrainerTraineeNotesController", "TraineeNotesController", "PublicInvitationController"];

    private static readonly HashSet<string> ExpectedControllerFiles =
    [
        "LgymApi.Api/Features/Public/Controllers/PublicInvitationController.cs",
        "LgymApi.Api/Features/Trainer/Controllers/TrainerDashboardProgressController.cs",
        "LgymApi.Api/Features/Trainer/Controllers/TrainerInvitationController.cs",
        "LgymApi.Api/Features/Trainer/Controllers/TrainerManagedPlansController.cs",
        "LgymApi.Api/Features/Trainer/Controllers/TraineeNotesController.cs",
        "LgymApi.Api/Features/Trainer/Controllers/TraineeRelationshipController.cs",
        "LgymApi.Api/Features/Trainer/Controllers/TrainerTraineeNotesController.cs"
    ];

    private static readonly ActionContract[] ExpectedActions =
    [
        new("TrainerInvitationController", "CreateInvitation", "api/trainer/invitations", "POST", "AuthConstants.Policies.TrainerAccess", true, "CreateTrainerInvitationRequest", "TrainerInvitationDto", "StatusCodes.Status200OK"),
        new("TrainerInvitationController", "GetInvitationsPaginated", "api/trainer/invitations/paginated", "POST", "AuthConstants.Policies.TrainerAccess", false, "PaginatedTrainerInvitationRequest", "PaginatedTrainerInvitationResult", "StatusCodes.Status200OK"),
        new("TrainerInvitationController", "CreateInvitationByEmail", "api/trainer/invitations/by-email", "POST", "AuthConstants.Policies.TrainerAccess", false, "CreateTrainerInvitationByEmailRequest", "TrainerInvitationDto", "StatusCodes.Status200OK"),
        new("TrainerInvitationController", "RevokeInvitation", "api/trainer/invitations/{invitationId}/revoke", "POST", "AuthConstants.Policies.TrainerAccess", false, "<none>", "ResponseMessageDto", "StatusCodes.Status200OK"),
        new("TrainerDashboardProgressController", "GetDashboardTrainees", "api/trainer/trainees", "GET", "AuthConstants.Policies.TrainerAccess", false, "TrainerDashboardTraineesRequest", "TrainerDashboardTraineesResponse", "StatusCodes.Status200OK"),
        new("TrainerDashboardProgressController", "GetTraineeTrainingDates", "api/trainer/trainees/{traineeId}/trainings/dates", "GET", "AuthConstants.Policies.TrainerAccess", false, "<none>", "List<DateTime>", "StatusCodes.Status200OK"),
        new("TrainerDashboardProgressController", "GetTraineeTrainingByDate", "api/trainer/trainees/{traineeId}/trainings/by-date", "POST", "AuthConstants.Policies.TrainerAccess", false, "TrainingByDateRequestDto", "List<TrainingByDateDetailsDto>", "StatusCodes.Status200OK"),
        new("TrainerDashboardProgressController", "GetTraineeExerciseScoresChartData", "api/trainer/trainees/{traineeId}/exercise-scores/chart", "POST", "AuthConstants.Policies.TrainerAccess", false, "ExerciseScoresChartRequestDto", "List<ExerciseScoresChartDataDto>", "StatusCodes.Status200OK"),
        new("TrainerDashboardProgressController", "GetTraineeEloChart", "api/trainer/trainees/{traineeId}/elo/chart", "GET", "AuthConstants.Policies.TrainerAccess", false, "<none>", "List<EloRegistryBaseChartDto>", "StatusCodes.Status200OK"),
        new("TrainerDashboardProgressController", "GetTraineeMainRecordsHistory", "api/trainer/trainees/{traineeId}/main-records/history", "GET", "AuthConstants.Policies.TrainerAccess", false, "<none>", "List<MainRecordResponseDto>", "StatusCodes.Status200OK"),
        new("TrainerDashboardProgressController", "UnlinkTrainee", "api/trainer/trainees/{traineeId}/unlink", "POST", "AuthConstants.Policies.TrainerAccess", false, "<none>", "ResponseMessageDto", "StatusCodes.Status200OK"),
        new("TrainerManagedPlansController", "GetTraineePlans", "api/trainer/trainees/{traineeId}/plans", "GET", "AuthConstants.Policies.TrainerAccess", false, "<none>", "List<TrainerManagedPlanDto>", "StatusCodes.Status200OK"),
        new("TrainerManagedPlansController", "CreateTraineePlan", "api/trainer/trainees/{traineeId}/plans", "POST", "AuthConstants.Policies.TrainerAccess", false, "TrainerPlanFormRequest", "TrainerManagedPlanDto", "StatusCodes.Status201Created"),
        new("TrainerManagedPlansController", "UpdateTraineePlan", "api/trainer/trainees/{traineeId}/plans/{planId}/update", "POST", "AuthConstants.Policies.TrainerAccess", false, "TrainerPlanFormRequest", "TrainerManagedPlanDto", "StatusCodes.Status200OK"),
        new("TrainerManagedPlansController", "DeleteTraineePlan", "api/trainer/trainees/{traineeId}/plans/{planId}/delete", "POST", "AuthConstants.Policies.TrainerAccess", false, "<none>", "ResponseMessageDto", "StatusCodes.Status200OK"),
        new("TrainerManagedPlansController", "AssignTraineePlan", "api/trainer/trainees/{traineeId}/plans/{planId}/assign", "POST", "AuthConstants.Policies.TrainerAccess", false, "<none>", "ResponseMessageDto", "StatusCodes.Status200OK"),
        new("TrainerManagedPlansController", "UnassignTraineePlan", "api/trainer/trainees/{traineeId}/plans/unassign", "POST", "AuthConstants.Policies.TrainerAccess", false, "<none>", "ResponseMessageDto", "StatusCodes.Status200OK"),
        new("TraineeRelationshipController", "AcceptInvitation", "api/trainee/invitations/{invitationId}/accept", "POST", "Authorize", false, "<none>", "ResponseMessageDto", "StatusCodes.Status200OK"),
        new("TraineeRelationshipController", "RejectInvitation", "api/trainee/invitations/{invitationId}/reject", "POST", "Authorize", false, "<none>", "ResponseMessageDto", "StatusCodes.Status200OK"),
        new("TraineeRelationshipController", "DetachFromTrainer", "api/trainee/trainer/detach", "POST", "Authorize", false, "<none>", "ResponseMessageDto", "StatusCodes.Status200OK"),
        new("TraineeRelationshipController", "GetCurrentTrainer", "api/trainee/trainer", "GET", "Authorize", false, "<none>", "TraineeTrainerProfileDto", "StatusCodes.Status200OK"),
        new("TraineeRelationshipController", "GetActiveAssignedPlan", "api/trainee/plan/active", "GET", "Authorize", false, "<none>", "TrainerManagedPlanDto", "StatusCodes.Status200OK"),
        new("TrainerTraineeNotesController", "GetNotes", "api/trainer/trainees/{traineeId}/notes", "GET", "AuthConstants.Policies.TrainerAccess", false, "<none>", "List<TraineeNoteDto>", "StatusCodes.Status200OK"),
        new("TrainerTraineeNotesController", "CreateNote", "api/trainer/trainees/{traineeId}/notes", "POST", "AuthConstants.Policies.TrainerAccess", false, "UpsertTraineeNoteRequest", "TraineeNoteDto", "StatusCodes.Status201Created"),
        new("TrainerTraineeNotesController", "UpdateNote", "api/trainer/trainees/{traineeId}/notes/{noteId}/update", "POST", "AuthConstants.Policies.TrainerAccess", false, "UpsertTraineeNoteRequest", "TraineeNoteDto", "StatusCodes.Status200OK"),
        new("TrainerTraineeNotesController", "DeleteNote", "api/trainer/trainees/{traineeId}/notes/{noteId}/delete", "POST", "AuthConstants.Policies.TrainerAccess", false, "<none>", "ResponseMessageDto", "StatusCodes.Status200OK"),
        new("TrainerTraineeNotesController", "GetNoteHistory", "api/trainer/trainees/{traineeId}/notes/{noteId}/history", "GET", "AuthConstants.Policies.TrainerAccess", false, "<none>", "List<TraineeNoteHistoryDto>", "StatusCodes.Status200OK"),
        new("TraineeNotesController", "GetVisibleNotes", "api/trainee/notes", "GET", "Authorize", false, "<none>", "List<TraineeNoteDto>", "StatusCodes.Status200OK"),
        new("TraineeNotesController", "GetVisibleNote", "api/trainee/notes/{noteId}", "GET", "Authorize", false, "<none>", "TraineeNoteDto", "StatusCodes.Status200OK"),
        new("PublicInvitationController", "GetInvitationStatus", "api/invitations/{invitationId}", "GET", "AllowAnonymous", false, "string?", "PublicInvitationStatusDto", "StatusCodes.Status200OK")
    ];

    private static readonly ResponseContract[] ExpectedResponses =
    new ResponseContract[]
    {
        new("TrainerDashboardProgressController", "GetTraineeTrainingDates", "StatusCodes.Status400BadRequest", "ResponseMessageDto"), new("TrainerDashboardProgressController", "GetTraineeTrainingDates", "StatusCodes.Status404NotFound", "ResponseMessageDto"),
        new("TrainerDashboardProgressController", "GetTraineeTrainingByDate", "StatusCodes.Status400BadRequest", "ResponseMessageDto"), new("TrainerDashboardProgressController", "GetTraineeTrainingByDate", "StatusCodes.Status404NotFound", "ResponseMessageDto"),
        new("TrainerDashboardProgressController", "GetTraineeExerciseScoresChartData", "StatusCodes.Status400BadRequest", "ResponseMessageDto"), new("TrainerDashboardProgressController", "GetTraineeExerciseScoresChartData", "StatusCodes.Status404NotFound", "ResponseMessageDto"),
        new("TrainerDashboardProgressController", "GetTraineeEloChart", "StatusCodes.Status400BadRequest", "ResponseMessageDto"), new("TrainerDashboardProgressController", "GetTraineeEloChart", "StatusCodes.Status404NotFound", "ResponseMessageDto"),
        new("TrainerDashboardProgressController", "GetTraineeMainRecordsHistory", "StatusCodes.Status400BadRequest", "ResponseMessageDto"), new("TrainerDashboardProgressController", "GetTraineeMainRecordsHistory", "StatusCodes.Status404NotFound", "ResponseMessageDto"),
        new("PublicInvitationController", "GetInvitationStatus", "StatusCodes.Status404NotFound", "<missing>")
    }.Concat(ExpectedActions.Select(action => new ResponseContract(action.Controller, action.Action, action.SuccessStatus, action.SuccessResponse))).ToArray();

    private sealed record ActionContract(string Controller, string Action, string Route, string Verb, string Authorization, bool HasApiIdempotency, string Request, string SuccessResponse, string SuccessStatus);
    private sealed record ResponseContract(string Controller, string Action, string Status, string Response);
    private sealed record ResponseDeclaration(string Status, string Response);
    private sealed record DiscoveredAction(string Controller, string ControllerRoute, string Authorization, MethodDeclarationSyntax Method, AttributeSyntax HttpAttribute);
}
