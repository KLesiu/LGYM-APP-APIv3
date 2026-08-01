using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ReportingApiContractImmutabilityGuardTests
{
    private const string ContractChangeMessage =
        "Reporting API contracts may change only through an explicit future breaking-change/baseline update plan.";

    [Test]
    public void Reporting_Controller_Routes_Verbs_Aliases_And_Declared_Responses_Must_Match_Baseline()
    {
        var trees = ReadSourceTrees(ExpectedControllerFiles);
        var expectedControllers = ExpectedControllerRoutes.ToHashSet();
        var expectedRoutes = ExpectedRoutes.ToHashSet();
        var expectedResponses = ExpectedResponses.ToHashSet();

        var actualControllers = trees
            .SelectMany(static tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(static declaration => ExpectedControllerNames.Contains(declaration.Identifier.ValueText))
            .SelectMany(static declaration => declaration.AttributeLists
                .SelectMany(static list => list.Attributes)
                .Where(static attribute => GetAttributeName(attribute) == "Route")
                .Select(attribute => new ControllerRoute(declaration.Identifier.ValueText, GetStringArgument(attribute) ?? "<missing>")))
            .ToHashSet();

        var actualRoutes = DiscoverControllerActions(trees)
            .SelectMany(static action => action.HttpAttributes.Select(attribute => new RouteContract(
                action.Controller,
                action.Method.Identifier.ValueText,
                GetHttpVerb(attribute),
                CombineTemplates(action.ControllerRoute, GetStringArgument(attribute) ?? "<missing>"))))
            .ToHashSet();

        var actualResponses = DiscoverControllerActions(trees)
            .SelectMany(static action => action.Method.AttributeLists
                .SelectMany(static list => list.Attributes)
                .Where(static attribute => GetAttributeName(attribute) == "ProducesResponseType")
                .Select(attribute => new ResponseContract(
                    action.Controller,
                    action.Method.Identifier.ValueText,
                    GetResponseStatus(attribute),
                    GetResponseType(attribute))))
            .ToHashSet();

        Assert.Multiple(() =>
        {
            Assert.That(actualControllers, Is.EquivalentTo(expectedControllers),
                $"Reporting controller route prefixes changed. {ContractChangeMessage}");
            Assert.That(actualRoutes, Is.EquivalentTo(expectedRoutes),
                $"Reporting route templates, HTTP verbs, action names, or compatibility aliases changed. {ContractChangeMessage}");
            Assert.That(actualResponses, Is.EquivalentTo(expectedResponses),
                $"Reporting declared response DTO metadata changed. {ContractChangeMessage}");
        });
    }

    [Test]
    public void Reporting_Dto_Public_Property_And_Json_Name_Surface_Must_Match_Baseline()
    {
        var trees = ReadSourceTrees(ExpectedDtoFiles);
        var expectedTypes = ExpectedDtoTypes.ToHashSet(StringComparer.Ordinal);
        var expectedProperties = ExpectedDtoProperties.ToHashSet();
        var actualTypes = new HashSet<string>(StringComparer.Ordinal);
        var actualProperties = new HashSet<DtoPropertyContract>();

        foreach (var declaration in trees
                     .SelectMany(static tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()))
        {
            var dtoName = declaration.Identifier.ValueText;
            actualTypes.Add(dtoName);

            foreach (var property in declaration.Members.OfType<PropertyDeclarationSyntax>())
            {
                actualProperties.Add(new DtoPropertyContract(
                    dtoName,
                    property.Identifier.ValueText,
                    GetJsonPropertyName(property) ?? "<missing>"));
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(actualTypes, Is.EquivalentTo(expectedTypes),
                $"Reporting public DTO type names changed. {ContractChangeMessage}");
            Assert.That(actualProperties, Is.EquivalentTo(expectedProperties),
                $"Reporting DTO public property names or JsonPropertyName values changed. {ContractChangeMessage}");
        });
    }

    [Test]
    public void Reporting_Legacy_Id_And_Message_Fields_Must_Remain_Declared()
    {
        var trees = ReadSourceTrees(ExpectedDtoFiles);
        var actualLegacyFields = trees
            .SelectMany(static tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(static declaration => ExpectedDtoTypeSet.Contains(declaration.Identifier.ValueText))
            .SelectMany(static declaration => declaration.Members.OfType<PropertyDeclarationSyntax>()
                .Select(property => new DtoPropertyContract(
                    declaration.Identifier.ValueText,
                    property.Identifier.ValueText,
                    GetJsonPropertyName(property) ?? "<missing>")))
            .Where(static property => ExpectedLegacyFields.Contains(property))
            .ToHashSet();

        Assert.That(actualLegacyFields, Is.EquivalentTo(ExpectedLegacyFields),
            $"Reporting required legacy '_id' or 'msg' fields changed or were removed. {ContractChangeMessage}");
    }

    private static IReadOnlyList<SyntaxTree> ReadSourceTrees(IReadOnlySet<string> expectedFiles)
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var trees = ArchitectureTestHelpers.ParseProjectSources("LgymApi.Api")
            .Where(tree => expectedFiles.Contains(NormalizeRelativePath(repositoryRoot, tree.FilePath)))
            .ToList();
        var actualFiles = trees
            .Select(tree => NormalizeRelativePath(repositoryRoot, tree.FilePath))
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(actualFiles, Is.EquivalentTo(expectedFiles),
            $"Reporting contract source files changed or could not be found. {ContractChangeMessage}");

        return trees;
    }

    private static IEnumerable<ControllerAction> DiscoverControllerActions(IEnumerable<SyntaxTree> trees)
    {
        var controllerRoutes = trees
            .SelectMany(static tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(static declaration => ExpectedControllerNames.Contains(declaration.Identifier.ValueText))
            .SelectMany(static declaration => declaration.AttributeLists
                .SelectMany(static list => list.Attributes)
                .Where(static attribute => GetAttributeName(attribute) == "Route")
                .Select(attribute => new ControllerRoute(declaration.Identifier.ValueText, GetStringArgument(attribute) ?? "<missing>")))
            .ToDictionary(static route => route.Controller, static route => route.Template, StringComparer.Ordinal);

        foreach (var declaration in trees
                     .SelectMany(static tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                     .Where(static declaration => ExpectedControllerNames.Contains(declaration.Identifier.ValueText)))
        {
            var controller = declaration.Identifier.ValueText;
            Assert.That(controllerRoutes.TryGetValue(controller, out var controllerRoute), Is.True,
                $"{controller} must declare its route prefix. {ContractChangeMessage}");

            foreach (var method in declaration.Members.OfType<MethodDeclarationSyntax>())
            {
                var httpAttributes = method.AttributeLists
                    .SelectMany(static list => list.Attributes)
                    .Where(static attribute => GetHttpVerb(attribute) is not null)
                    .ToList();

                if (httpAttributes.Count > 0)
                {
                    yield return new ControllerAction(controller, controllerRoute!, method, httpAttributes);
                }
            }
        }
    }

    private static string? GetHttpVerb(AttributeSyntax attribute) => GetAttributeName(attribute) switch
    {
        "HttpGet" => "GET",
        "HttpPost" => "POST",
        _ => null
    };

    private static string GetResponseStatus(AttributeSyntax attribute) =>
        attribute.ArgumentList?.Arguments.Skip(1).FirstOrDefault()?.Expression.ToString() ?? "<missing>";

    private static string GetResponseType(AttributeSyntax attribute) =>
        attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is TypeOfExpressionSyntax typeOf
            ? typeOf.Type.ToString()
            : "<missing>";

    private static string? GetJsonPropertyName(PropertyDeclarationSyntax property) => property.AttributeLists
        .SelectMany(static list => list.Attributes)
        .Where(static attribute => GetAttributeName(attribute) == "JsonPropertyName")
        .Select(GetStringArgument)
        .SingleOrDefault();

    private static string GetAttributeName(AttributeSyntax attribute)
    {
        var name = attribute.Name.ToString();
        var unqualifiedName = name[(name.LastIndexOf('.') + 1)..];
        return unqualifiedName.EndsWith("Attribute", StringComparison.Ordinal)
            ? unqualifiedName[..^"Attribute".Length]
            : unqualifiedName;
    }

    private static string? GetStringArgument(AttributeSyntax attribute) =>
        attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal
            ? literal.Token.ValueText
            : null;

    private static string CombineTemplates(string controllerTemplate, string actionTemplate) =>
        $"{controllerTemplate.TrimEnd('/')}/{actionTemplate.TrimStart('/')}";

    private static string NormalizeRelativePath(string repositoryRoot, string path) =>
        ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repositoryRoot, path));

    private static readonly HashSet<string> ExpectedControllerNames =
    [
        "TrainerReportingController",
        "TraineeReportingController"
    ];

    private static readonly HashSet<string> ExpectedControllerFiles =
    [
        "LgymApi.Api/Features/Trainer/Controllers/TrainerReportingController.cs",
        "LgymApi.Api/Features/Trainer/Controllers/TrainerReportingController.Recurring.cs",
        "LgymApi.Api/Features/Trainer/Controllers/TrainerReportingController.Photos.cs",
        "LgymApi.Api/Features/Trainer/Controllers/TraineeReportingController.cs"
    ];

    private static readonly HashSet<string> ExpectedDtoFiles =
    [
        "LgymApi.Api/Features/Trainer/Contracts/ReportingDtos.cs",
        "LgymApi.Api/Features/Trainer/Contracts/PhotoDtos.cs",
        "LgymApi.Api/Features/Common/Contracts/ResponseMessageDto.cs"
    ];

    private static readonly string[] ExpectedDtoTypes =
    [
        "UpsertReportTemplateRequest", "ReportTemplateFieldRequest", "ReportTemplateDto", "ReportTemplateFieldDto",
        "CreateReportRequestRequest", "UpsertRecurringReportAssignmentRequest", "ReportRequestDto",
        "SubmitReportRequestRequest", "UpdateReportSubmissionFeedbackRequest", "ReportSubmissionDto",
        "RecurringReportAssignmentDto", "InitiatePhotoUploadRequest", "InitiatePhotoUploadResponse",
        "GetSignedReadUrlResponse", "CompletePhotoUploadRequest", "CompletePhotoUploadResponse",
        "PhotoHistoryItemResponse", "GetPhotoHistoryResponse", "ResponseMessageDto"
    ];

    private static readonly HashSet<string> ExpectedDtoTypeSet = ExpectedDtoTypes.ToHashSet(StringComparer.Ordinal);

    private static readonly ControllerRoute[] ExpectedControllerRoutes =
    [
        new("TrainerReportingController", "api/trainer"),
        new("TraineeReportingController", "api/trainee")
    ];

    private static readonly RouteContract[] ExpectedRoutes =
    [
        new("TrainerReportingController", "CreateTemplate", "POST", "api/trainer/report-templates"),
        new("TrainerReportingController", "GetTemplates", "GET", "api/trainer/report-templates"),
        new("TrainerReportingController", "GetTemplate", "GET", "api/trainer/report-templates/{templateId}"),
        new("TrainerReportingController", "UpdateTemplate", "POST", "api/trainer/report-templates/{templateId}/update"),
        new("TrainerReportingController", "DeleteTemplate", "POST", "api/trainer/report-templates/{templateId}/delete"),
        new("TrainerReportingController", "CreateReportRequest", "POST", "api/trainer/trainees/{traineeId}/report-requests"),
        new("TrainerReportingController", "GetTraineeSubmissions", "GET", "api/trainer/trainees/{traineeId}/report-submissions"),
        new("TrainerReportingController", "UpdateSubmissionFeedback", "POST", "api/trainer/trainees/{traineeId}/report-submissions/{submissionId}/feedback"),
        new("TrainerReportingController", "CreateRecurringReportAssignment", "POST", "api/trainer/trainees/{traineeId}/recurring-report-assignments"),
        new("TrainerReportingController", "GetRecurringReportAssignments", "GET", "api/trainer/trainees/{traineeId}/recurring-report-assignments"),
        new("TrainerReportingController", "UpdateRecurringReportAssignment", "POST", "api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/update"),
        new("TrainerReportingController", "PauseRecurringReportAssignment", "POST", "api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/pause"),
        new("TrainerReportingController", "ResumeRecurringReportAssignment", "POST", "api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/resume"),
        new("TrainerReportingController", "DeleteRecurringReportAssignment", "POST", "api/trainer/trainees/{traineeId}/recurring-report-assignments/{id}/delete"),
        new("TrainerReportingController", "InitiatePhotoUpload", "POST", "api/trainer/reporting/photos/upload-init"),
        new("TrainerReportingController", "GetPhotoSignedReadUrl", "GET", "api/trainer/reporting/photos/{photoId}/signed-url"),
        new("TrainerReportingController", "CompletePhotoUpload", "POST", "api/trainer/reporting/photos/complete-upload"),
        new("TrainerReportingController", "GetPhotoHistory", "GET", "api/trainer/reporting/photos/history"),
        new("TraineeReportingController", "GetPendingRequests", "GET", "api/trainee/report-requests"),
        new("TraineeReportingController", "SubmitRequest", "POST", "api/trainee/report-requests/{requestId}/submit"),
        new("TraineeReportingController", "GetOwnSubmissions", "GET", "api/trainee/report-submissions"),
        new("TraineeReportingController", "MarkFeedbackRead", "POST", "api/trainee/report-submissions/{submissionId}/mark-feedback-read"),
        new("TraineeReportingController", "InitiatePhotoUpload", "POST", "api/trainee/photos/initiate"),
        new("TraineeReportingController", "InitiatePhotoUpload", "POST", "api/trainee/reporting/photos/upload-init"),
        new("TraineeReportingController", "CompletePhotoUpload", "POST", "api/trainee/photos/complete-upload"),
        new("TraineeReportingController", "CompletePhotoUpload", "POST", "api/trainee/reporting/photos/complete-upload"),
        new("TraineeReportingController", "GetPhotoHistory", "GET", "api/trainee/reporting/photos/history")
    ];

    private static readonly ResponseContract[] ExpectedResponses =
    [
        new("TrainerReportingController", "CreateTemplate", "StatusCodes.Status201Created", "ReportTemplateDto"),
        new("TrainerReportingController", "GetTemplates", "StatusCodes.Status200OK", "List<ReportTemplateDto>"),
        new("TrainerReportingController", "GetTemplate", "StatusCodes.Status200OK", "ReportTemplateDto"),
        new("TrainerReportingController", "GetTemplate", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TrainerReportingController", "UpdateTemplate", "StatusCodes.Status200OK", "ReportTemplateDto"),
        new("TrainerReportingController", "UpdateTemplate", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TrainerReportingController", "DeleteTemplate", "StatusCodes.Status200OK", "ResponseMessageDto"),
        new("TrainerReportingController", "DeleteTemplate", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TrainerReportingController", "CreateReportRequest", "StatusCodes.Status201Created", "ReportRequestDto"),
        new("TrainerReportingController", "CreateReportRequest", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TrainerReportingController", "GetTraineeSubmissions", "StatusCodes.Status200OK", "List<ReportSubmissionDto>"),
        new("TrainerReportingController", "GetTraineeSubmissions", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TrainerReportingController", "UpdateSubmissionFeedback", "StatusCodes.Status200OK", "ReportSubmissionDto"),
        new("TrainerReportingController", "UpdateSubmissionFeedback", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TrainerReportingController", "CreateRecurringReportAssignment", "StatusCodes.Status201Created", "RecurringReportAssignmentDto"),
        new("TrainerReportingController", "CreateRecurringReportAssignment", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TrainerReportingController", "GetRecurringReportAssignments", "StatusCodes.Status200OK", "List<RecurringReportAssignmentDto>"),
        new("TrainerReportingController", "GetRecurringReportAssignments", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TrainerReportingController", "UpdateRecurringReportAssignment", "StatusCodes.Status200OK", "RecurringReportAssignmentDto"),
        new("TrainerReportingController", "UpdateRecurringReportAssignment", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TrainerReportingController", "PauseRecurringReportAssignment", "StatusCodes.Status200OK", "RecurringReportAssignmentDto"),
        new("TrainerReportingController", "PauseRecurringReportAssignment", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TrainerReportingController", "ResumeRecurringReportAssignment", "StatusCodes.Status200OK", "RecurringReportAssignmentDto"),
        new("TrainerReportingController", "ResumeRecurringReportAssignment", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TrainerReportingController", "DeleteRecurringReportAssignment", "StatusCodes.Status200OK", "ResponseMessageDto"),
        new("TrainerReportingController", "DeleteRecurringReportAssignment", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TrainerReportingController", "InitiatePhotoUpload", "StatusCodes.Status200OK", "InitiatePhotoUploadResponse"),
        new("TrainerReportingController", "InitiatePhotoUpload", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TrainerReportingController", "GetPhotoSignedReadUrl", "StatusCodes.Status200OK", "GetSignedReadUrlResponse"),
        new("TrainerReportingController", "GetPhotoSignedReadUrl", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TrainerReportingController", "CompletePhotoUpload", "StatusCodes.Status200OK", "CompletePhotoUploadResponse"),
        new("TrainerReportingController", "CompletePhotoUpload", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TrainerReportingController", "GetPhotoHistory", "StatusCodes.Status200OK", "GetPhotoHistoryResponse"),
        new("TrainerReportingController", "GetPhotoHistory", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TraineeReportingController", "GetPendingRequests", "StatusCodes.Status200OK", "List<ReportRequestDto>"),
        new("TraineeReportingController", "SubmitRequest", "StatusCodes.Status200OK", "ReportSubmissionDto"),
        new("TraineeReportingController", "SubmitRequest", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TraineeReportingController", "GetOwnSubmissions", "StatusCodes.Status200OK", "List<ReportSubmissionDto>"),
        new("TraineeReportingController", "MarkFeedbackRead", "StatusCodes.Status200OK", "ReportSubmissionDto"),
        new("TraineeReportingController", "MarkFeedbackRead", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TraineeReportingController", "InitiatePhotoUpload", "StatusCodes.Status200OK", "InitiatePhotoUploadResponse"),
        new("TraineeReportingController", "InitiatePhotoUpload", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TraineeReportingController", "CompletePhotoUpload", "StatusCodes.Status200OK", "CompletePhotoUploadResponse"),
        new("TraineeReportingController", "CompletePhotoUpload", "StatusCodes.Status400BadRequest", "ResponseMessageDto"),
        new("TraineeReportingController", "GetPhotoHistory", "StatusCodes.Status200OK", "GetPhotoHistoryResponse"),
        new("TraineeReportingController", "GetPhotoHistory", "StatusCodes.Status400BadRequest", "ResponseMessageDto")
    ];

    private static readonly DtoPropertyContract[] ExpectedDtoProperties =
    [
        new("UpsertReportTemplateRequest", "Name", "name"), new("UpsertReportTemplateRequest", "Description", "description"), new("UpsertReportTemplateRequest", "Fields", "fields"),
        new("ReportTemplateFieldRequest", "Key", "key"), new("ReportTemplateFieldRequest", "Label", "label"), new("ReportTemplateFieldRequest", "Type", "type"), new("ReportTemplateFieldRequest", "IsRequired", "isRequired"), new("ReportTemplateFieldRequest", "Order", "order"), new("ReportTemplateFieldRequest", "ModuleConfig", "moduleConfig"),
        new("ReportTemplateDto", "Id", "_id"), new("ReportTemplateDto", "TrainerId", "trainerId"), new("ReportTemplateDto", "Name", "name"), new("ReportTemplateDto", "Description", "description"), new("ReportTemplateDto", "CreatedAt", "createdAt"), new("ReportTemplateDto", "Fields", "fields"),
        new("ReportTemplateFieldDto", "Key", "key"), new("ReportTemplateFieldDto", "Label", "label"), new("ReportTemplateFieldDto", "Type", "type"), new("ReportTemplateFieldDto", "IsRequired", "isRequired"), new("ReportTemplateFieldDto", "Order", "order"), new("ReportTemplateFieldDto", "ModuleConfig", "moduleConfig"),
        new("CreateReportRequestRequest", "TemplateId", "templateId"), new("CreateReportRequestRequest", "DueAt", "dueAt"), new("CreateReportRequestRequest", "Note", "note"),
        new("UpsertRecurringReportAssignmentRequest", "TemplateId", "templateId"), new("UpsertRecurringReportAssignmentRequest", "IntervalValue", "intervalValue"), new("UpsertRecurringReportAssignmentRequest", "IntervalUnit", "intervalUnit"), new("UpsertRecurringReportAssignmentRequest", "StartsAt", "startsAt"), new("UpsertRecurringReportAssignmentRequest", "EndsAt", "endsAt"), new("UpsertRecurringReportAssignmentRequest", "Note", "note"),
        new("ReportRequestDto", "Id", "_id"), new("ReportRequestDto", "TrainerId", "trainerId"), new("ReportRequestDto", "TraineeId", "traineeId"), new("ReportRequestDto", "TemplateId", "templateId"), new("ReportRequestDto", "Status", "status"), new("ReportRequestDto", "DueAt", "dueAt"), new("ReportRequestDto", "Note", "note"), new("ReportRequestDto", "CreatedAt", "createdAt"), new("ReportRequestDto", "SubmittedAt", "submittedAt"), new("ReportRequestDto", "Template", "template"),
        new("SubmitReportRequestRequest", "Answers", "answers"),
        new("UpdateReportSubmissionFeedbackRequest", "TrainerOverallComment", "trainerOverallComment"), new("UpdateReportSubmissionFeedbackRequest", "TrainerFieldComments", "trainerFieldComments"),
        new("ReportSubmissionDto", "Id", "_id"), new("ReportSubmissionDto", "ReportRequestId", "reportRequestId"), new("ReportSubmissionDto", "TraineeId", "traineeId"), new("ReportSubmissionDto", "SubmittedAt", "submittedAt"), new("ReportSubmissionDto", "Answers", "answers"), new("ReportSubmissionDto", "TrainerOverallComment", "trainerOverallComment"), new("ReportSubmissionDto", "TrainerFieldComments", "trainerFieldComments"), new("ReportSubmissionDto", "TrainerFeedbackAddedAt", "trainerFeedbackAddedAt"), new("ReportSubmissionDto", "TrainerFeedbackReadAt", "trainerFeedbackReadAt"), new("ReportSubmissionDto", "Request", "request"),
        new("RecurringReportAssignmentDto", "Id", "_id"), new("RecurringReportAssignmentDto", "TrainerId", "trainerId"), new("RecurringReportAssignmentDto", "TraineeId", "traineeId"), new("RecurringReportAssignmentDto", "TemplateId", "templateId"), new("RecurringReportAssignmentDto", "IntervalValue", "intervalValue"), new("RecurringReportAssignmentDto", "IntervalUnit", "intervalUnit"), new("RecurringReportAssignmentDto", "StartsAt", "startsAt"), new("RecurringReportAssignmentDto", "EndsAt", "endsAt"), new("RecurringReportAssignmentDto", "IsActive", "isActive"), new("RecurringReportAssignmentDto", "Note", "note"), new("RecurringReportAssignmentDto", "CurrentReportRequestId", "currentReportRequestId"), new("RecurringReportAssignmentDto", "LastRequestCreatedAt", "lastRequestCreatedAt"), new("RecurringReportAssignmentDto", "NextEligibleAt", "nextEligibleAt"), new("RecurringReportAssignmentDto", "CreatedAt", "createdAt"), new("RecurringReportAssignmentDto", "Template", "template"), new("RecurringReportAssignmentDto", "CurrentReportRequest", "currentReportRequest"),
        new("InitiatePhotoUploadRequest", "ReportRequestId", "reportRequestId"), new("InitiatePhotoUploadRequest", "ViewType", "viewType"), new("InitiatePhotoUploadRequest", "MimeType", "mimeType"), new("InitiatePhotoUploadRequest", "SizeBytes", "sizeBytes"),
        new("InitiatePhotoUploadResponse", "UploadUrl", "uploadUrl"), new("InitiatePhotoUploadResponse", "StorageKey", "storageKey"), new("InitiatePhotoUploadResponse", "ExpiresAt", "expiresAt"),
        new("GetSignedReadUrlResponse", "ReadUrl", "readUrl"), new("GetSignedReadUrlResponse", "ExpiresAt", "expiresAt"),
        new("CompletePhotoUploadRequest", "StorageKey", "storageKey"), new("CompletePhotoUploadRequest", "MimeType", "mimeType"), new("CompletePhotoUploadRequest", "SizeBytes", "sizeBytes"), new("CompletePhotoUploadRequest", "Checksum", "checksum"), new("CompletePhotoUploadRequest", "ReportRequestId", "reportRequestId"), new("CompletePhotoUploadRequest", "ViewType", "viewType"),
        new("CompletePhotoUploadResponse", "PhotoId", "photoId"), new("CompletePhotoUploadResponse", "UploadedAt", "uploadedAt"),
        new("PhotoHistoryItemResponse", "Id", "_id"), new("PhotoHistoryItemResponse", "ViewType", "viewType"), new("PhotoHistoryItemResponse", "SizeBytes", "sizeBytes"), new("PhotoHistoryItemResponse", "ThumbnailUrl", "thumbnailUrl"), new("PhotoHistoryItemResponse", "ReadUrl", "readUrl"), new("PhotoHistoryItemResponse", "ReportRequestId", "reportRequestId"), new("PhotoHistoryItemResponse", "UploadedAt", "uploadedAt"),
        new("GetPhotoHistoryResponse", "Photos", "photos"),
        new("ResponseMessageDto", "Message", "msg"), new("ResponseMessageDto", "IsNew", "isNew")
    ];

    private static readonly HashSet<DtoPropertyContract> ExpectedLegacyFields =
    [
        new("ReportTemplateDto", "Id", "_id"),
        new("ReportRequestDto", "Id", "_id"),
        new("ReportSubmissionDto", "Id", "_id"),
        new("RecurringReportAssignmentDto", "Id", "_id"),
        new("PhotoHistoryItemResponse", "Id", "_id"),
        new("ResponseMessageDto", "Message", "msg")
    ];

    private sealed record ControllerAction(string Controller, string ControllerRoute, MethodDeclarationSyntax Method, IReadOnlyList<AttributeSyntax> HttpAttributes);
    private sealed record ControllerRoute(string Controller, string Template);
    private sealed record RouteContract(string Controller, string Action, string? Verb, string Template);
    private sealed record ResponseContract(string Controller, string Action, string Status, string ResponseType);
    private sealed record DtoPropertyContract(string Dto, string Property, string JsonName);
}
