using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ModuleContributionDocumentationTests
{
    private const string GuideRelativePath = "docs/MODULE_CONTRIBUTION_GUIDE.md";
    private const string PullRequestTemplateRelativePath = ".github/PULL_REQUEST_TEMPLATE.md";
    private const string PullRequestStartMarker = "<!-- module-contribution-checklist:start -->";
    private const string PullRequestEndMarker = "<!-- module-contribution-checklist:end -->";
    private const string AuthorityPrefix = "module-guide.authority.";
    private const string PolicyPrefix = "module-guide.policy.";
    private const string TrainingPathPrefix = "module-guide.path.training-planning-read.";
    private const string ReportingPathPrefix = "module-guide.path.reporting-write.";
    private const string ExceptionPrefix = "module-guide.exception.";

    private static readonly string[] AuthorityColumns = ["Authority ID", "Authority source", "Governs"];
    private static readonly string[] PolicyColumns = ["Policy ID", "Contract", "Evidence"];
    private static readonly string[] PathColumns = ["Path ID", "Step", "Canonical owner", "Source locator", "Verified invariant"];
    private static readonly string[] ExceptionColumns = ["Exception ID", "Scope", "Constraint"];

    private static readonly AuthorityExpectation[] Authorities =
    [
        new("module-guide.authority.workflow", GuideRelativePath, "workflow"),
        new("module-guide.authority.adr-decision", "docs/adr/006-lgym-evolves-as-modular-monolith.md", "architectural-decisions"),
        new("module-guide.authority.ownership", "LgymApi.ArchitectureTests/PersistedEntityOwnershipCatalog.cs#PersistedEntityOwnershipCatalog.CanonicalOwners", "persisted-ownership"),
        new("module-guide.authority.dependency-graph", "docs/modular-monolith/issue-380-project-reference-graph.md", "dependencies"),
        new("module-guide.authority.background-messaging", "docs/modular-monolith/issue-380-background-contract-ownership.md", "background-messaging"),
        new("module-guide.authority.reporting-boundary", "docs/modular-monolith/issue-392-reporting-boundary.md", "reporting-boundary"),
        new("module-guide.authority.platform-provider-boundary", "docs/modular-monolith/issue-393-platform-reference-data-boundary.md", "platform-provider-boundary"),
        new("module-guide.authority.final-compatibility", "docs/adr/007-final-modular-monolith-compatibility-commitments.md", "final-compatibility"),
        new("module-guide.authority.final-verification", "docs/modular-monolith/issue-395-final-verification.md", "final-verification")
    ];

    private static readonly PolicyExpectation[] Policies =
    [
        new("module-guide.policy.owner", OwnerPolicyContract()),
        new("module-guide.policy.placement", "owner-first=true; foreign-entities=false; foreign-repositories=false"),
        new("module-guide.policy.namespace-compatibility", "physical-path=owner; legacy-namespace=compatible"),
        new("module-guide.policy.public-surface", "focused-contracts=true; dependency-aggregate=false; high-arity=accepted"),
        new("module-guide.policy.vertical-slice", "owner-local=true; cosmetic-partial=false"),
        new("module-guide.policy.command-query", "query=read; command=write"),
        new("module-guide.policy.uow-transactions", "repository-save=false; one-save=default; transaction=multi-save-only"),
        new("module-guide.policy.read-models", "no-tracking=default; tracked-read=same-uow-mutation-only"),
        new("module-guide.policy.messaging-outbox", "reporting=stage; platform=envelope; worker=forward; workout-progress=consume"),
        new("module-guide.policy.ef-migrations", "AppDbContext=1; PostgreSQL database=1; migration stream=1; physical split=None"),
        new("module-guide.policy.di", "owner-facade=true; service-locator=false"),
        new("module-guide.policy.api-compatibility", "endpoint-specific=true"),
        new("module-guide.policy.api-adapters", "application=25; notifications=3; migration-clr-identities=removed"),
        new("module-guide.policy.localization", "resources=en,pl"),
        new("module-guide.policy.tactical-ddd", "invariants=required"),
        new("module-guide.policy.architecture-tests", "focused-guards=true"),
        new("module-guide.policy.prohibited-patterns", "ninth-module=false; application-to-worker=false; worker-common-feature-command=false")
    ];

    private static readonly PathExpectation[] TrainingPlanningPath =
    [
        new("module-guide.path.training-planning-read.controller", 1, PersistedEntityOwnershipCatalog.TrainingPlanningModuleName, "LgymApi.Api/Features/Plan/Controllers/PlanController.cs#PlanController.GetPlansList"),
        new("module-guide.path.training-planning-read.compatibility-adapter", 2, PersistedEntityOwnershipCatalog.TrainingPlanningModuleName, "LgymApi.Application/TrainingPlanning/ApiAdapters/PlanApiAdapter.cs#PlanApiAdapter.GetListAsync"),
        new("module-guide.path.training-planning-read.use-case-contract", 3, PersistedEntityOwnershipCatalog.TrainingPlanningModuleName, "LgymApi.TrainingPlanning/Plan/GetPlansList/Contracts/IGetPlansListUseCase.cs#IGetPlansListUseCase.ExecuteAsync"),
        new("module-guide.path.training-planning-read.use-case", 4, PersistedEntityOwnershipCatalog.TrainingPlanningModuleName, "LgymApi.TrainingPlanning/Plan/GetPlansList/GetPlansListUseCase.cs#GetPlansListUseCase.ExecuteAsync"),
        new("module-guide.path.training-planning-read.repository-contract", 5, PersistedEntityOwnershipCatalog.TrainingPlanningModuleName, "LgymApi.TrainingPlanning/Persistence/IPlanRepository.cs#IPlanRepository.GetReadModelsByUserIdAsync(Id<User>,CancellationToken)"),
        new("module-guide.path.training-planning-read.repository-projection", 6, PersistedEntityOwnershipCatalog.TrainingPlanningModuleName, "LgymApi.TrainingPlanning/Persistence/Repositories/PlanRepository.cs#PlanRepository.GetReadModelsByUserIdAsync"),
        new("module-guide.path.training-planning-read.api-mapping", 7, PersistedEntityOwnershipCatalog.TrainingPlanningModuleName, "LgymApi.Api/Mapping/Profiles/PlanProfile.cs#PlanProfile.Configure"),
        new("module-guide.path.training-planning-read.module-registration", 8, PersistedEntityOwnershipCatalog.TrainingPlanningModuleName, "LgymApi.TrainingPlanning/TrainingPlanningModule.cs#TrainingPlanningModule.AddTrainingPlanningModule")
    ];

    private static readonly PathExpectation[] ReportingPath =
    [
        new("module-guide.path.reporting-write.controller", 1, PersistedEntityOwnershipCatalog.ReportingModuleName, "LgymApi.Api/Features/Trainer/Controllers/TraineeReportingController.cs#TraineeReportingController.SubmitRequest"),
        new("module-guide.path.reporting-write.compatibility-adapter", 2, PersistedEntityOwnershipCatalog.ReportingModuleName, "LgymApi.Application/Reporting/ApiAdapters/ReportTemplateAndRequestApiAdapters.cs#TraineeReportRequestApiAdapter.SubmitAsync"),
        new("module-guide.path.reporting-write.service", 3, PersistedEntityOwnershipCatalog.ReportingModuleName, "LgymApi.Application/Features/Reporting/ReportingService.Submissions.cs#ReportingService.SubmitReportRequestAsync"),
        new("module-guide.path.reporting-write.persistence-add", 4, PersistedEntityOwnershipCatalog.ReportingModuleName, "LgymApi.Application/Reporting/Persistence/IReportRequestSubmissionPersistence.cs#IReportRequestSubmissionPersistence.AddSubmissionAsync"),
        new("module-guide.path.reporting-write.persistence-status", 5, PersistedEntityOwnershipCatalog.ReportingModuleName, "LgymApi.Application/Reporting/Persistence/IReportRequestSubmissionPersistence.cs#IReportRequestSubmissionPersistence.SetRequestSubmittedAsync"),
        new("module-guide.path.reporting-write.outbox-stage", 6, PersistedEntityOwnershipCatalog.PlatformModuleName, "LgymApi.Platform/Contracts/BackgroundCommands/ICommandOutboxWriter.cs#ICommandOutboxWriter.StageAsync"),
        new("module-guide.path.reporting-write.unit-of-work", 7, PersistedEntityOwnershipCatalog.PlatformModuleName, "LgymApi.Platform/Repositories/IUnitOfWork.cs#IUnitOfWork.SaveChangesAsync"),
        new("module-guide.path.reporting-write.post-commit-dispatch", 8, PersistedEntityOwnershipCatalog.PlatformModuleName, "LgymApi.Platform/Contracts/BackgroundCommands/ICommandDispatcher.cs#ICommandDispatcher.EnqueueAsync"),
        new("module-guide.path.reporting-write.worker-handler", 9, PersistedEntityOwnershipCatalog.PlatformModuleName, "LgymApi.BackgroundWorker/Actions/ReportSubmissionAcceptedProgressCommandHandler.cs#ReportSubmissionAcceptedProgressCommandHandler.ExecuteAsync"),
        new("module-guide.path.reporting-write.workout-action-port", 10, PersistedEntityOwnershipCatalog.WorkoutProgressModuleName, "LgymApi.Application/WorkoutProgress/BackgroundActions/ReportSubmissionAcceptedProgressActionExecutionPort.cs#ReportSubmissionAcceptedProgressActionExecutionPort.ExecuteAsync"),
        new("module-guide.path.reporting-write.workout-consumer", 11, PersistedEntityOwnershipCatalog.WorkoutProgressModuleName, "LgymApi.Application/WorkoutProgress/ReportingIntegration/ReportSubmissionAcceptedProgressConsumer.cs#ReportSubmissionAcceptedProgressConsumer.ConsumeAsync")
    ];

    private static readonly ExceptionExpectation[] Exceptions =
    [
        new("module-guide.exception.endpoint-specific-legacy-fields", "endpoint-specific", "legacy-fields=route-contract-only"),
        new("module-guide.exception.tracked-mutation-reads", "same-uow-mutation", "tracking=same-uow-mutation-only"),
        new("module-guide.exception.direct-high-arity-constructors", "focused-service", "dependency-aggregate=false; numeric-cap=none"),
        new("module-guide.exception.retained-generated-partial-classes", "retained-or-generated", "new-cosmetic-partial=false"),
        new("module-guide.exception.legacy-namespaces-command-ids", "wire-compatibility", "identity=preserve"),
        new("module-guide.exception.query-side-compatibility-cleanup", "compatibility-adapter-only", "owner-path=unchanged"),
        new("module-guide.exception.shared-physical-persistence", "single-context-shared-database", "AppDbContext=1; migration stream=1")
    ];

    private static readonly string[] PullRequestItemIds =
    [
        "owner",
        "dependencies",
        "public-surface",
        "vertical-slice",
        "persistence-topology",
        "uow-transactions",
        "messaging",
        "api-compatibility-localization",
        "mapping",
        "architecture-tests",
        "documentation",
        "project-topology-evidence"
    ];

    private static readonly string[] RetainedDependencyAggregates = [];

    private static readonly string[] StaleArchitectureContributionPatterns =
    [
        "Add repository interface in LgymApi.Application/Repositories",
        "Implement repository under LgymApi.Infrastructure/Repositories",
        "Register service in LgymApi.Application/ServiceCollectionExtensions.cs",
        "register service/repository in both service collection extension files",
        "LgymApi.Application/Repositories/IFeatureXRepository.cs"
    ];

    private static readonly string[] ForbiddenUsageAuthorityClaims =
    [
        "is the module-creation authority",
        "examples authorize creating a project-reference edge",
        "examples authorize creating a project",
        "examples authorize creating a context",
        "examples authorize creating a database",
        "examples authorize creating a schema",
        "examples authorize creating a migration"
    ];

    private static readonly string[] ForbiddenOwnershipAggregateClaims =
    [
        "all retained dependency aggregates are forbidden"
    ];

    private static readonly string[] SemanticFlowProjects =
    [
        "LgymApi.Api",
        "LgymApi.Application",
        "LgymApi.BackgroundWorker",
        "LgymApi.BackgroundWorker.Common",
        "LgymApi.Domain",
        "LgymApi.Identity",
        "LgymApi.Infrastructure",
        "LgymApi.Notifications",
        "LgymApi.Platform",
        "LgymApi.Resources",
        "LgymApi.TrainingPlanning"
    ];

    private static readonly ConcurrentDictionary<string, Lazy<SemanticProject>> SemanticProjects = new(StringComparer.Ordinal);

    [Test]
    public void Guide_Should_Publish_The_Current_Module_Contribution_Contract()
    {
        ValidateGuideTables(ReadRequiredArtifact(GuideRelativePath));
    }

    [Test]
    public void Guide_Source_Matrices_Should_Resolve_And_Match_Implemented_Flows()
    {
        var markdown = ReadRequiredArtifact(GuideRelativePath);
        var pathRows = ValidateGuideTables(markdown);
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();

        foreach (var row in pathRows.Values)
        {
            _ = ResolveSourceMember(repositoryRoot, row.GetField("Source locator"));
        }

        AssertTrainingPlanningFlow(repositoryRoot, pathRows);
        AssertReportingFlow(repositoryRoot, pathRows);
    }

    [Test]
    public void Parser_Should_Reject_Invalid_Rows_Locators_And_Unsafe_Fixtures()
    {
        var validGuide = CreateValidGuideFixture();
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var validPathRows = ValidateGuideTables(validGuide);
        foreach (var row in validPathRows.Values)
        {
            _ = ResolveSourceMember(repositoryRoot, row.GetField("Source locator"));
        }

        AssertTrainingPlanningFlow(repositoryRoot, validPathRows);
        AssertReportingFlow(repositoryRoot, validPathRows);
        AssertReportingWriteOrder(ParseFixtureMethod(ReportingOrderFixture(stageAfterSave: false, omitStage: false), "SubmitAsync"));
        AssertReportingWriteOrder(ParseFixtureMethod(ReportingReversePersistenceOrderFixture(), "SubmitAsync"));
        AssertReportingWriteOrder(ParseFixtureMethod(ReportingEarlyBranchSaveFixture(), "SubmitAsync"));
        AssertPublicOneMethodInterface(ParseFixtureMethod(PublicInterfaceFixture(), "ExecuteAsync"));
        var receiverFixture = CompileFixture(RenamedReceiverFixture());
        AssertReceiverInvocation(
            receiverFixture.Compilation.GetSemanticModel(receiverFixture.Trees.Single()),
            GetFixtureMethod(receiverFixture, "ExecuteAsync"),
            "IPlanRepository",
            "GetReadModelsByUserIdAsync");
        ValidatePullRequestChecklist(CreateValidPullRequestFixture());
        var siblingGuideLink = ResolveLink("docs/ARCHITECTURE.md", "MODULE_CONTRIBUTION_GUIDE.md");
        Ensure(siblingGuideLink == GuideRelativePath, $"Sibling Markdown links must resolve repository-relatively, but resolved as '{siblingGuideLink}'.");
        var parentGuideLink = ResolveLink("docs/modular-monolith/issue-376-ownership-map.md", "../MODULE_CONTRIBUTION_GUIDE.md");
        Ensure(parentGuideLink == GuideRelativePath, $"Parent-relative Markdown links must resolve repository-relatively, but resolved as '{parentGuideLink}'.");
        Ensure(ResolveLink("docs/ARCHITECTURE.md", "#contributing") == string.Empty, "Fragment-only Markdown links must remain excluded from repository link matching.");
        Ensure(ResolveLink("docs/ARCHITECTURE.md", "https://example.com/guide") == string.Empty, "External Markdown links must remain excluded from repository link matching.");
        ValidateArchitectureContributionSections(ArchitectureContributionFixture(legacyInventory: StaleArchitectureContributionPatterns[4]));
        ValidateContributorEntryPoints(ContributorReadmeFixture(), ContributorUsageFixture());
        ValidateRetainedDependencyAggregateRows(OwnershipMapFixture());

        var validNoTrackingFixture = CompileProjectFixture("LgymApi.TrainingPlanning", TrainingProjectionFixture(includeNoTracking: true));
        var missingNoTrackingFixture = CompileProjectFixture("LgymApi.TrainingPlanning", TrainingProjectionFixture(includeNoTracking: false));
        var disconnectedNoTrackingFixture = CompileProjectFixture("LgymApi.TrainingPlanning", TrainingProjectionDisconnectedNoTrackingFixture());
        var falsePositiveProjectionFixture = CompileProjectFixture("LgymApi.TrainingPlanning", TrainingProjectionFalsePositiveFixture());
        var wrongPlanReadModelFixture = CompileProjectFixture("LgymApi.TrainingPlanning", TrainingProjectionWrongPlanReadModelIdentityFixture());
        var wrongAsNoTrackingFixture = CompileProjectFixture("LgymApi.TrainingPlanning", TrainingProjectionWrongAsNoTrackingIdentityFixture());
        var wrongReceiverFixture = CompileFixture(WrongReceiverIdentityFixture());
        var wrongReceiverExpectedType = RequireTypeSymbol(wrongReceiverFixture.Compilation, "Expected.IPlanRepository");
        var directMeasurementFixture = CompileProjectFixture("LgymApi.Application", DirectMeasurementFixture());
        var directConsumerFixture = CompileProjectFixture("LgymApi.Application", DirectConsumerFixture());
        var unrelatedConsumerFixture = CompileProjectFixture("LgymApi.Application", UnrelatedConsumeAsyncFixture());
        var fakeForbiddenShortNameFixture = CompileProjectFixture("LgymApi.Application", FakeForbiddenReportingShortNameFixture());

        Assert.Multiple(() =>
        {
            Assert.That(
                () => AssertTrainingReadProjection(validNoTrackingFixture.Compilation.GetSemanticModel(validNoTrackingFixture.Trees.Single()), GetFixtureMethod(validNoTrackingFixture, "QueryAsync")),
                Throws.Nothing,
                "A legitimate Plans.AsNoTracking().Select(...) chain must remain valid.");
            AssertRejected(() => ValidateGuideTables(RemoveStableRow(validGuide, "module-guide.policy.owner")), "module-guide.policy.owner");
            AssertRejected(() => ValidateGuideTables(DuplicateStableRow(validGuide, "module-guide.authority.workflow")), "Duplicate stable row IDs");
            AssertRejected(() => ValidateGuideTables(AddUnknownStableRow(validGuide, "module-guide.exception.unknown")), "module-guide.exception.unknown");
            AssertRejected(() => ValidateGuideTables(AddUnknownStableRow(validGuide, "module-guide.path.future.experimental")), "module-guide.path.future.experimental");
            AssertRejected(() => ValidateGuideTables(RemoveLastCell(validGuide, "module-guide.path.training-planning-read.controller")), "has 4 cells; expected 5");
            AssertRejected(() => ValidateGuideTables(ReplaceStableCell(validGuide, "module-guide.policy.di", "Evidence", string.Empty)), "must define Evidence");
            AssertRejected(() => ValidateGuideTables(ReplaceStableCell(validGuide, "module-guide.path.reporting-write.service", "Canonical owner", "Worker")), "must define Canonical owner");
            AssertRejected(() => ResolveSourceMember(repositoryRoot, "C:/repo/File.cs#Type.Member"), "repository-relative");
            AssertRejected(() => ResolveSourceMember(repositoryRoot, "../File.cs#Type.Member"), "path traversal");
            AssertRejected(() => ResolveSourceMember(repositoryRoot, "LgymApi.Api/Program.cs:10#Program.Main"), "line numbers");
            AssertRejected(() => ResolveSourceMember(repositoryRoot, "missing.cs#Missing.Member"), "does not exist");
            AssertRejected(() => ResolveSourceMember(repositoryRoot, "LgymApi.Api/Features/Plan/Controllers/PlanController.cs#Missing.GetPlansList"), "type does not resolve exactly once");
            AssertRejected(() => ResolveSourceMember(repositoryRoot, "LgymApi.Api/Features/Plan/Controllers/PlanController.cs#PlanController.Missing"), "member does not resolve exactly once");
            AssertRejected(() => ResolveSourceMember(repositoryRoot, "LgymApi.TrainingPlanning/Persistence/IPlanRepository.cs#IPlanRepository.GetReadModelsByUserIdAsync"), "ambiguous");
            AssertRejected(() => ValidateGuideTables(ReplaceStableCell(validGuide, TrainingPlanningPath[3].Id, "Canonical owner", PersistedEntityOwnershipCatalog.ReportingModuleName)), TrainingPlanningPath[3].Id);
            AssertRejected(() => AssertTrainingReadProjection(missingNoTrackingFixture.Compilation.GetSemanticModel(missingNoTrackingFixture.Trees.Single()), GetFixtureMethod(missingNoTrackingFixture, "QueryAsync")), "AsNoTracking");
            AssertRejected(() => AssertTrainingReadProjection(disconnectedNoTrackingFixture.Compilation.GetSemanticModel(disconnectedNoTrackingFixture.Trees.Single()), GetFixtureMethod(disconnectedNoTrackingFixture, "QueryAsync")), "Select receiver chain must contain the exact EntityFrameworkQueryableExtensions.AsNoTracking invocation");
            AssertRejected(() => AssertTrainingReadProjection(falsePositiveProjectionFixture.Compilation.GetSemanticModel(falsePositiveProjectionFixture.Trees.Single()), GetFixtureMethod(falsePositiveProjectionFixture, "QueryAsync")), "Select selector");
            AssertRejected(() => AssertTrainingReadProjection(wrongPlanReadModelFixture.Compilation.GetSemanticModel(wrongPlanReadModelFixture.Trees.Single()), GetFixtureMethod(wrongPlanReadModelFixture, "QueryAsync")), "PlanReadModel symbol identity");
            AssertRejected(() => AssertTrainingReadProjection(wrongAsNoTrackingFixture.Compilation.GetSemanticModel(wrongAsNoTrackingFixture.Trees.Single()), GetFixtureMethod(wrongAsNoTrackingFixture, "QueryAsync")), "AsNoTracking symbol identity");
            AssertRejected(() => AssertReportingWriteOrder(ParseFixtureMethod(ReportingOrderFixture(stageAfterSave: true, omitStage: false), "SubmitAsync")), "StageAsync before SaveChangesAsync");
            AssertRejected(() => AssertReportingWriteOrder(ParseFixtureMethod(ReportingOrderFixture(stageAfterSave: false, omitStage: true), "SubmitAsync")), "StageAsync exactly once");
            AssertRejected(() => AssertReportingWriteOrder(ParseFixtureMethod(ReportingAcceptedSliceExtraSaveFixture(), "SubmitAsync")), "SaveChangesAsync exactly once in the accepted-submission operation slice");
            AssertRejected(() => AssertPublicOneMethodInterface(ParseFixtureMethod(BodylessClassContractFixture(), "ExecuteAsync")), "public interface");
            AssertRejected(() => AssertPublicOneMethodInterface(ParseFixtureMethod(NonPublicInterfaceFixture(), "ExecuteAsync")), "public interface");
            AssertRejected(() => AssertPublicOneMethodInterface(ParseFixtureMethod(PublicInterfaceWithExtraMemberFixture(), "ExecuteAsync")), "exactly one member");
            AssertRejected(() => AssertReceiverInvocation(
                wrongReceiverFixture.Compilation.GetSemanticModel(wrongReceiverFixture.Trees.Single()),
                GetFixtureMethod(wrongReceiverFixture, "ExecuteAsync"),
                wrongReceiverExpectedType,
                "GetReadModelsByUserIdAsync"), "receiver symbol identity");
            AssertRejected(() => AssertNoForbiddenReportingReferences(directMeasurementFixture, "IMeasurementRepository"), "IMeasurementRepository");
            AssertRejected(() => AssertNoForbiddenReportingReferences(directConsumerFixture, "IReportSubmissionAcceptedProgressConsumer"), "IReportSubmissionAcceptedProgressConsumer");
            Assert.That(() => AssertNoForbiddenReportingReferences(unrelatedConsumerFixture, "unrelated consumer fixture"), Throws.Nothing, "An unrelated ConsumeAsync method must not be rejected.");
            Assert.That(() => AssertNoForbiddenReportingReferences(fakeForbiddenShortNameFixture, "fake short-name fixture"), Throws.Nothing, "Unrelated short-name types must not be rejected.");
            AssertRejected(() => ValidateGuideTables(ReplaceStableCell(validGuide, "module-guide.policy.owner", "Contract", "canonical-owners=8; completed-training=Training Planning; api-persistence=false")), "module-guide.policy.owner");
            AssertRejected(() => ValidateGuideTables(ReplaceStableCell(validGuide, "module-guide.policy.owner", "Contract", "canonical-owners=8; completed-training=Workout & Progress; api-persistence=true")), "module-guide.policy.owner");
            AssertRejected(() => ValidateGuideTables(ReplaceStableCell(validGuide, "module-guide.policy.placement", "Contract", "owner-first=true; foreign-entities=true; foreign-repositories=true")), "module-guide.policy.placement");
            AssertRejected(() => ValidateGuideTables(ReplaceStableCell(validGuide, "module-guide.policy.ef-migrations", "Contract", "AppDbContext=2; PostgreSQL database=2; migration stream=2; physical split=schema")), "module-guide.policy.ef-migrations");
            AssertRejected(() => ValidateGuideTables(ReplaceStableCell(validGuide, "module-guide.policy.prohibited-patterns", "Contract", "ninth-module=true; application-to-worker=false; worker-common-feature-command=false")), "module-guide.policy.prohibited-patterns");
            AssertRejected(() => ValidateGuideTables(ReplaceStableCell(validGuide, "module-guide.policy.prohibited-patterns", "Contract", "ninth-module=false; application-to-worker=true; worker-common-feature-command=false")), "module-guide.policy.prohibited-patterns");
            AssertRejected(() => ValidateGuideTables(ReplaceStableCell(validGuide, "module-guide.policy.prohibited-patterns", "Contract", "ninth-module=false; application-to-worker=false; worker-common-feature-command=true")), "module-guide.policy.prohibited-patterns");
            AssertRejected(() => ValidateGuideTables(ReplaceStableCell(validGuide, "module-guide.policy.public-surface", "Contract", "focused-contracts=false; broad-dependency-aggregate=true")), "module-guide.policy.public-surface");
            AssertRejected(() => ValidateGuideTables(ReplaceStableCell(validGuide, "module-guide.policy.api-compatibility", "Contract", "endpoint-specific=false; legacy-fields=universal:_id,msg,req")), "module-guide.policy.api-compatibility");
            AssertRejected(() => ValidateGuideTables(ReplaceStableCell(validGuide, "module-guide.policy.read-models", "Contract", "no-tracking=optional; tracked-read=arbitrary")), "module-guide.policy.read-models");
            foreach (var stalePattern in StaleArchitectureContributionPatterns)
            {
                AssertRejected(() => ValidateArchitectureContributionSections(ArchitectureContributionFixture(stalePattern)), stalePattern);
            }

            foreach (var forbiddenClaim in ForbiddenUsageAuthorityClaims)
            {
                AssertRejected(() => ValidateContributorEntryPoints(ContributorReadmeFixture(), ContributorUsageFixture(forbiddenClaim)), forbiddenClaim);
            }

            foreach (var forbiddenClaim in ForbiddenOwnershipAggregateClaims)
            {
                AssertRejected(() => ValidateRetainedDependencyAggregateRows(OwnershipMapFixture(forbiddenClaim)), forbiddenClaim);
            }

            AssertRejected(() => ValidatePullRequestChecklist(CreateValidPullRequestFixture().Replace(PullRequestStartMarker, string.Empty, StringComparison.Ordinal)), "start delimiter");
            AssertRejected(() => ValidatePullRequestChecklist(CreateValidPullRequestFixture().Replace(PullRequestEndMarker, string.Empty, StringComparison.Ordinal)), "end delimiter");
            AssertRejected(() => ValidatePullRequestChecklist(CreateValidPullRequestFixture().Replace(PullRequestStartMarker, PullRequestStartMarker + Environment.NewLine + PullRequestStartMarker, StringComparison.Ordinal)), "start delimiter");
            AssertRejected(() => ValidatePullRequestChecklist(CreateValidPullRequestFixture().Replace(PullRequestEndMarker, PullRequestEndMarker + Environment.NewLine + PullRequestEndMarker, StringComparison.Ordinal)), "end delimiter");
            AssertRejected(() => ValidatePullRequestChecklist(PullRequestEndMarker + Environment.NewLine + PullRequestStartMarker), "ordered");
            AssertRejected(() => ValidatePullRequestChecklist(CreateValidPullRequestFixture().Replace(PullRequestEndMarker, PullRequestStartMarker + Environment.NewLine + PullRequestEndMarker, StringComparison.Ordinal)), "start delimiter");
            AssertRejected(() => ValidatePullRequestChecklist(CreateValidPullRequestFixture().Replace(PullRequestItemMarker("owner"), string.Empty, StringComparison.Ordinal)), "machine marker");
            AssertRejected(() => ValidatePullRequestChecklist(CreateValidPullRequestFixture().Replace(PullRequestItemMarker("owner"), PullRequestItemMarker("owner") + Environment.NewLine + "- [ ] " + PullRequestItemMarker("owner"), StringComparison.Ordinal)), "Duplicate PR checklist item IDs");
            AssertRejected(() => ValidatePullRequestChecklist(CreateValidPullRequestFixture().Replace(PullRequestItemMarker("owner"), PullRequestItemMarker("unknown"), StringComparison.Ordinal)), "Unknown: unknown");
            AssertRejected(() => ValidatePullRequestChecklist(CreateValidPullRequestFixture().Replace("- [ ] " + PullRequestItemMarker("mapping"), "- [x] " + PullRequestItemMarker("mapping"), StringComparison.Ordinal)), "unchecked checkbox");
            AssertRejected(() => ValidatePullRequestChecklist(CreateValidPullRequestFixture().Replace(PullRequestEndMarker, "- [ ] unmarked" + Environment.NewLine + PullRequestEndMarker, StringComparison.Ordinal)), "machine marker");
            AssertRejected(() => ValidatePullRequestChecklist(ReorderPullRequestItems(CreateValidPullRequestFixture(), "owner", "dependencies")), "order");
        });
    }

    [Test]
    public void Pull_Request_Template_Should_Contain_The_Exact_Module_Boundary_Checklist()
    {
        ValidatePullRequestChecklist(ReadRequiredArtifact(PullRequestTemplateRelativePath));
    }

    [Test]
    public void Architecture_Guide_Should_Point_To_The_Canonical_Workflow_Without_Stale_Skeletons()
    {
        _ = ReadRequiredArtifact(GuideRelativePath);
        ValidateArchitectureContributionSections(ReadRequiredArtifact("docs/ARCHITECTURE.md"));
    }

    [Test]
    public void Agent_Guidance_Should_Remain_Byte_Identical_And_Link_The_Canonical_Guide()
    {
        _ = ReadRequiredArtifact(GuideRelativePath);
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var agent = File.ReadAllBytes(Path.Combine(repositoryRoot, "AGENT.md"));
        var agents = File.ReadAllBytes(Path.Combine(repositoryRoot, "AGENTS.md"));
        Assert.That(agent.SequenceEqual(agents), Is.True, "AGENT.md and AGENTS.md must remain byte-identical.");
        var markdown = Encoding.UTF8.GetString(agent);
        Assert.That(CountLinksTo(markdown, "AGENT.md", GuideRelativePath), Is.EqualTo(1), "Root agent guidance must link the canonical workflow exactly once.");
    }

    [Test]
    public void Contributor_Entry_Points_Should_Distinguish_Contribution_From_Feature_Usage()
    {
        _ = ReadRequiredArtifact(GuideRelativePath);
        ValidateContributorEntryPoints(ReadRequiredArtifact("README.md"), ReadRequiredArtifact("docs/NEW_MODULES_USAGE.md"));
    }

    [Test]
    public void Ownership_Map_Should_Treat_Retained_Dependency_Aggregates_As_Non_Template_Exceptions()
    {
        _ = ReadRequiredArtifact(GuideRelativePath);
        var markdown = ReadRequiredArtifact("docs/modular-monolith/issue-376-ownership-map.md");
        ValidateRetainedDependencyAggregateRows(markdown);
        Ensure(CountLinksTo(markdown, "docs/modular-monolith/issue-376-ownership-map.md", GuideRelativePath) == 1, "The ownership map must link the canonical workflow exactly once.");
    }

    [Test]
    public void Architecture_Test_Project_Document_Should_Describe_The_Focused_Guard()
    {
        _ = ReadRequiredArtifact(GuideRelativePath);
        var markdown = ReadRequiredArtifact("LgymApi.ArchitectureTests/LgymApi.ArchitectureTests.md");
        var tokens = ExtractInlineCodeTokens(markdown);
        Assert.Multiple(() =>
        {
            foreach (var token in new[] { "ModuleContributionDocumentationTests", GuideRelativePath, "path#Type.Member", PullRequestStartMarker, PullRequestEndMarker })
            {
                Assert.That(tokens.Contains(token, StringComparer.Ordinal), Is.True, $"ArchitectureTests documentation is missing machine token {token}.");
            }

            foreach (var prefix in new[] { AuthorityPrefix, PolicyPrefix, "module-guide.path.", ExceptionPrefix })
            {
                Assert.That(tokens.Any(token => token.StartsWith(prefix, StringComparison.Ordinal)), Is.True, $"ArchitectureTests documentation is missing stable prefix {prefix}.");
            }
        });
    }

    [Test]
    public void Authority_Documents_Should_Remain_Consistent_With_The_Guide()
    {
        var markdown = ReadRequiredArtifact(GuideRelativePath);
        _ = ValidateGuideTables(markdown);
        var rows = RequireRows(
            PlatformReferenceDataBoundaryDocumentationTestHelpers.ParseRows(markdown),
            AuthorityPrefix,
            Authorities.Select(item => item.Id),
            AuthorityColumns);
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        foreach (var authority in Authorities)
        {
            var source = rows[authority.Id].GetField("Authority source");
            var path = source.Split('#', 2)[0];
            Ensure(File.Exists(Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar))), $"Authority source does not exist: {path}.");
            if (source.Contains('#', StringComparison.Ordinal))
            {
                _ = ResolveSourceMember(repositoryRoot, source);
            }
        }

        Ensure(PersistedEntityOwnershipCatalog.CanonicalOwners.Count == 8, "The canonical ownership authority must retain eight owners.");
        var projects = File.ReadLines(Path.Combine(repositoryRoot, "LgymApi.sln")).Where(line => line.StartsWith("Project(", StringComparison.Ordinal)).ToArray();
        var edges = projects.Select(line => line.Split('"')[5]).Select(path => Path.Combine(repositoryRoot, path)).SelectMany(ArchitectureTestHelpers.ParseProjectReferences).ToArray();
        Ensure(projects.Length == 18 && edges.Length == 90, "The dependency authority must retain the 18-project, 90-edge graph.");
    }

    private static IReadOnlyDictionary<string, BoundaryDocumentationRow> ValidateGuideTables(string markdown)
    {
        var rows = PlatformReferenceDataBoundaryDocumentationTestHelpers.ParseRows(markdown);
        _ = PlatformReferenceDataBoundaryDocumentationTestHelpers.RequireExactIds(
            rows,
            "module-guide.",
            Authorities.Select(item => item.Id)
                .Concat(Policies.Select(item => item.Id))
                .Concat(TrainingPlanningPath.Select(item => item.Id))
                .Concat(ReportingPath.Select(item => item.Id))
                .Concat(Exceptions.Select(item => item.Id)));
        var authorityRows = RequireRows(rows, AuthorityPrefix, Authorities.Select(item => item.Id), AuthorityColumns);
        var policyRows = RequireRows(rows, PolicyPrefix, Policies.Select(item => item.Id), PolicyColumns);
        var trainingRows = RequireRows(rows, TrainingPathPrefix, TrainingPlanningPath.Select(item => item.Id), PathColumns);
        var reportingRows = RequireRows(rows, ReportingPathPrefix, ReportingPath.Select(item => item.Id), PathColumns);
        var exceptionRows = RequireRows(rows, ExceptionPrefix, Exceptions.Select(item => item.Id), ExceptionColumns);

        foreach (var expectation in Authorities)
        {
            EnsureField(authorityRows[expectation.Id], "Authority source", expectation.Source);
            EnsureField(authorityRows[expectation.Id], "Governs", expectation.Governs);
        }

        foreach (var expectation in Policies)
        {
            EnsureField(policyRows[expectation.Id], "Contract", expectation.Contract);
        }

        foreach (var expectation in TrainingPlanningPath.Concat(ReportingPath))
        {
            var row = trainingRows.TryGetValue(expectation.Id, out var trainingRow) ? trainingRow : reportingRows[expectation.Id];
            EnsureField(row, "Step", expectation.Step.ToString(System.Globalization.CultureInfo.InvariantCulture));
            EnsureField(row, "Canonical owner", expectation.Owner);
            Ensure(PersistedEntityOwnershipCatalog.CanonicalOwners.Contains(row.GetField("Canonical owner"), StringComparer.Ordinal), $"{row.Id} must name a canonical owner from PersistedEntityOwnershipCatalog.CanonicalOwners.");
            EnsureField(row, "Source locator", expectation.Locator);
        }

        foreach (var expectation in Exceptions)
        {
            EnsureField(exceptionRows[expectation.Id], "Scope", expectation.Scope);
            EnsureField(exceptionRows[expectation.Id], "Constraint", expectation.Constraint);
        }

        return trainingRows.Concat(reportingRows).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, BoundaryDocumentationRow> RequireRows(
        IReadOnlyList<BoundaryDocumentationRow> rows,
        string prefix,
        IEnumerable<string> expectedIds,
        IReadOnlyList<string> expectedColumns)
    {
        var required = PlatformReferenceDataBoundaryDocumentationTestHelpers.RequireExactIds(rows, prefix, expectedIds);
        foreach (var row in required.Values)
        {
            Ensure(row.Fields.Keys.SequenceEqual(expectedColumns, StringComparer.Ordinal), $"{row.Id} must use columns: {string.Join(" | ", expectedColumns)}.");
            foreach (var column in expectedColumns)
            {
                Ensure(!string.IsNullOrWhiteSpace(row.GetField(column)), $"{row.Id} must define {column}.");
            }
        }

        return required;
    }

    private static void AssertTrainingPlanningFlow(string repositoryRoot, IReadOnlyDictionary<string, BoundaryDocumentationRow> rows)
    {
        var controller = ResolvePathMethod(repositoryRoot, rows, "module-guide.path.training-planning-read.controller");
        var adapter = ResolvePathMethod(repositoryRoot, rows, "module-guide.path.training-planning-read.compatibility-adapter");
        var contract = ResolvePathMethod(repositoryRoot, rows, "module-guide.path.training-planning-read.use-case-contract");
        var useCase = ResolvePathMethod(repositoryRoot, rows, "module-guide.path.training-planning-read.use-case");
        var repositoryContract = ResolvePathMethod(repositoryRoot, rows, "module-guide.path.training-planning-read.repository-contract");
        var repository = ResolvePathMethod(repositoryRoot, rows, "module-guide.path.training-planning-read.repository-projection");
        var profile = ResolvePathMethod(repositoryRoot, rows, "module-guide.path.training-planning-read.api-mapping");
        var module = ResolvePathMethod(repositoryRoot, rows, "module-guide.path.training-planning-read.module-registration");

        AssertReceiverInvocation(controller, "LgymApi.Application.TrainingPlanning.ApiAdapters.IPlanAccountApiAdapter", "GetListAsync");
        AssertReceiverInvocation(adapter, "LgymApi.Application.TrainingPlanning.Plan.GetPlansList.IGetPlansListUseCase", "ExecuteAsync");
        AssertPublicOneMethodInterface(contract);
        AssertReceiverInvocation(useCase, "LgymApi.Application.Repositories.IPlanRepository", "GetReadModelsByUserIdAsync");
        Ensure(!InvocationNames(useCase).Contains("SaveChangesAsync", StringComparer.Ordinal), "The Training Planning list query must not save a unit of work.");
        Ensure(repositoryContract.Body is null && repositoryContract.ExpressionBody is null, "The selected IPlanRepository overload must remain a contract declaration.");
        AssertTrainingReadProjection(repository);
        AssertCreateMap(profile, "PlanReadModel", "PlanFormDto");
        AssertExactScopedRegistration(module, "IGetPlansListUseCase", "GetPlansListUseCase");
        AssertExactScopedRegistration(module, "IPlanRepository", "PlanRepository");

        foreach (var id in new[] { TrainingPlanningPath[2].Id, TrainingPlanningPath[3].Id, TrainingPlanningPath[4].Id, TrainingPlanningPath[5].Id, TrainingPlanningPath[7].Id })
        {
            var path = rows[id].GetField("Source locator").Split('#')[0];
            Ensure(path.StartsWith("LgymApi.TrainingPlanning/", StringComparison.Ordinal), $"{id} must remain physically owned by LgymApi.TrainingPlanning.");
        }

        Ensure(GetNamespace(useCase).StartsWith("LgymApi.Application.", StringComparison.Ordinal), "The extracted use case must retain its compatible LgymApi.Application namespace.");
        Ensure(GetNamespace(repositoryContract).StartsWith("LgymApi.Application.", StringComparison.Ordinal), "The extracted repository contract must retain its compatible LgymApi.Application namespace.");
        Ensure(GetNamespace(repository).StartsWith("LgymApi.Infrastructure.", StringComparison.Ordinal), "The extracted repository must retain its compatible LgymApi.Infrastructure namespace.");
        var useCaseType = useCase.Ancestors().OfType<TypeDeclarationSyntax>().Single();
        Ensure(useCaseType.Modifiers.Any(SyntaxKind.InternalKeyword) && useCaseType.Modifiers.Any(SyntaxKind.SealedKeyword), "GetPlansListUseCase must remain an internal sealed implementation.");
        Ensure(useCaseType.BaseList?.Types.Any(type => type.Type.ToString() == "IGetPlansListUseCase") == true, "GetPlansListUseCase must implement IGetPlansListUseCase.");
        var repositoryType = repository.Ancestors().OfType<TypeDeclarationSyntax>().Single();
        Ensure(repositoryType.Modifiers.Any(SyntaxKind.InternalKeyword) && repositoryType.Modifiers.Any(SyntaxKind.SealedKeyword), "PlanRepository must remain an internal sealed implementation.");
        Ensure(repositoryType.BaseList?.Types.Any(type => type.Type.ToString() == "IPlanRepository") == true, "PlanRepository must implement IPlanRepository.");
    }

    private static void AssertReportingFlow(string repositoryRoot, IReadOnlyDictionary<string, BoundaryDocumentationRow> rows)
    {
        var controller = ResolvePathMethod(repositoryRoot, rows, "module-guide.path.reporting-write.controller");
        var adapter = ResolvePathMethod(repositoryRoot, rows, "module-guide.path.reporting-write.compatibility-adapter");
        var service = ResolvePathMethod(repositoryRoot, rows, "module-guide.path.reporting-write.service");
        var worker = ResolvePathMethod(repositoryRoot, rows, "module-guide.path.reporting-write.worker-handler");
        var actionPort = ResolvePathMethod(repositoryRoot, rows, "module-guide.path.reporting-write.workout-action-port");
        var consumer = ResolvePathMethod(repositoryRoot, rows, "module-guide.path.reporting-write.workout-consumer");
        var acceptedSubmissionSpan = GetAcceptedSubmissionOperationSpan(service);

        AssertReceiverInvocation(controller, "LgymApi.Application.Reporting.ApiAdapters.ITraineeReportRequestApiPort", "SubmitAsync");
        AssertReceiverInvocation(adapter, "LgymApi.Application.Features.Reporting.IReportingService", "SubmitReportRequestAsync");
        AssertReportingWriteOrder(service);
        AssertReceiverInvocation(service, "LgymApi.Application.Reporting.Persistence.IReportRequestSubmissionPersistence", "AddSubmissionAsync", acceptedSubmissionSpan);
        AssertReceiverInvocation(service, "LgymApi.Application.Reporting.Persistence.IReportRequestSubmissionPersistence", "SetRequestSubmittedAsync", acceptedSubmissionSpan);
        AssertReceiverInvocation(service, "LgymApi.Application.Platform.Contracts.BackgroundCommands.ICommandOutboxWriter", "StageAsync", acceptedSubmissionSpan);
        AssertReceiverInvocation(service, "LgymApi.Application.Repositories.IUnitOfWork", "SaveChangesAsync", acceptedSubmissionSpan);
        AssertReceiverInvocation(service, "LgymApi.Application.Platform.Contracts.BackgroundCommands.ICommandDispatcher", "EnqueueAsync", acceptedSubmissionSpan);
        AssertReceiverInvocation(worker, "LgymApi.Application.WorkoutProgress.Contracts.BackgroundActions.IReportSubmissionAcceptedProgressActionExecutionPort", "ExecuteAsync");
        AssertReceiverInvocation(actionPort, "LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration.IReportSubmissionAcceptedProgressConsumer", "ConsumeAsync");
        AssertReceiverInvocation(consumer, "LgymApi.Application.WorkoutProgress.ReportingIntegration.IReportSubmissionAcceptedProgressPersistence", "AddAsync");
        AssertReceiverInvocation(consumer, "LgymApi.Application.Repositories.IUnitOfWork", "SaveChangesAsync");

        var compilationInput = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Application");
        var reportingTrees = compilationInput.SyntaxTrees.Where(tree =>
        {
            var path = ArchitectureTestHelpers.NormalizePath(tree.FilePath);
            return path.Contains("/LgymApi.Application/Features/Reporting/", StringComparison.Ordinal) ||
                   path.Contains("/LgymApi.Application/Reporting/", StringComparison.Ordinal);
        }).ToArray();
        AssertNoForbiddenReportingReferences((compilationInput.Compilation, reportingTrees), "production Reporting sources");

        var registration = ResolveSourceMember(repositoryRoot, "LgymApi.Application/WorkoutProgress/ServiceCollectionExtensions.cs#ServiceCollectionExtensions.AddWorkoutAndProgressModule").AsMethod();
        AssertExactScopedRegistration(registration, "IReportSubmissionAcceptedProgressConsumer", "ReportSubmissionAcceptedProgressConsumer");
        AssertExactScopedRegistration(registration, "IReportSubmissionAcceptedProgressActionExecutionPort", "ReportSubmissionAcceptedProgressActionExecutionPort");
        var reportingRegistration = ResolveSourceMember(repositoryRoot, "LgymApi.Application/Reporting/ServiceCollectionExtensions.cs#ServiceCollectionExtensions.AddReportingModule").AsMethod();
        AssertExactScopedRegistration(reportingRegistration, "IReportingService", "ReportingService");
    }

    private static void AssertTrainingReadProjection(MethodDeclarationSyntax method)
    {
        var context = GetSemanticMethod(method);
        AssertTrainingReadProjection(context.SemanticModel, context.Method);
    }

    private static void AssertTrainingReadProjection(SemanticModel semanticModel, MethodDeclarationSyntax method)
    {
        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        var expectedPlanReadModel = RequireTypeSymbol(semanticModel.Compilation, "LgymApi.Application.TrainingPlanning.Plan.Models.PlanReadModel");
        var entityFrameworkExtensions = RequireTypeSymbol(semanticModel.Compilation, "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions");
        var noTrackingInvocations = invocations.Where(invocation => InvocationBelongsToType(semanticModel, invocation, entityFrameworkExtensions, "AsNoTracking")).ToArray();
        Ensure(noTrackingInvocations.Length == 1, "Training Planning read projection must invoke the EntityFrameworkQueryableExtensions.AsNoTracking symbol identity exactly once.");
        Ensure(!invocations.Any(invocation => GetInvokedMethod(semanticModel, invocation)?.Name == "SaveChangesAsync"), "Training Planning read projection must not save a unit of work.");
        var select = RequireInvocation(invocations, "Select", "Training Planning read projection must invoke exactly one Select selector.");
        var selectReceiverInvocations = new List<InvocationExpressionSyntax>();
        ExpressionSyntax? receiverExpression = select.Expression is MemberAccessExpressionSyntax selectMemberAccess
            ? selectMemberAccess.Expression
            : null;
        while (receiverExpression is not null)
        {
            while (receiverExpression is ParenthesizedExpressionSyntax parenthesized)
            {
                receiverExpression = parenthesized.Expression;
            }

            if (receiverExpression is not InvocationExpressionSyntax receiverInvocation)
            {
                break;
            }

            selectReceiverInvocations.Add(receiverInvocation);
            receiverExpression = receiverInvocation.Expression is MemberAccessExpressionSyntax receiverMemberAccess
                ? receiverMemberAccess.Expression
                : null;
        }

        var chainedNoTrackingInvocations = selectReceiverInvocations
            .Where(invocation => InvocationBelongsToType(semanticModel, invocation, entityFrameworkExtensions, "AsNoTracking"))
            .ToArray();
        Ensure(chainedNoTrackingInvocations.Length == 1,
            "Training Planning Select receiver chain must contain the exact EntityFrameworkQueryableExtensions.AsNoTracking invocation exactly once.");
        var selector = select.ArgumentList.Arguments.Select(argument => argument.Expression).OfType<AnonymousFunctionExpressionSyntax>().SingleOrDefault();
        Ensure(selector is not null, "Training Planning Select must have exactly one lambda or anonymous-function selector.");
        var selectorBody = selector switch
        {
            LambdaExpressionSyntax lambda => lambda.Body,
            AnonymousMethodExpressionSyntax anonymousMethod => anonymousMethod.Block,
            _ => null
        };
        Ensure(selectorBody is not null && selectorBody.DescendantNodesAndSelf().OfType<ObjectCreationExpressionSyntax>()
            .Any(creation => SymbolEqualityComparer.Default.Equals(semanticModel.GetTypeInfo(creation).Type, expectedPlanReadModel)),
            "Training Planning Select selector must construct the owner-local PlanReadModel symbol identity.");
    }

    private static void AssertPublicOneMethodInterface(MethodDeclarationSyntax method)
    {
        if (method.Parent is not InterfaceDeclarationSyntax contract || !contract.Modifiers.Any(SyntaxKind.PublicKeyword))
        {
            throw new InvalidOperationException("The selected one-method contract container must be a public interface.");
        }

        Ensure(contract.Members.Count == 1, "The public interface must declare exactly one member.");
        Ensure(method.Body is null && method.ExpressionBody is null, "The public interface method must remain a declaration without an implementation body.");
    }

    private static void AssertReportingWriteOrder(MethodDeclarationSyntax method)
    {
        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        var add = RequireInvocation(invocations, "AddSubmissionAsync");
        var set = RequireInvocation(invocations, "SetRequestSubmittedAsync");
        var stage = RequireInvocation(invocations, "StageAsync");
        var enqueue = RequireInvocation(invocations, "EnqueueAsync");
        var acceptedSubmissionSpan = GetAcceptedSubmissionOperationSpan(add, set, enqueue);
        var acceptedSubmissionInvocations = invocations.Where(invocation => acceptedSubmissionSpan.Contains(invocation.Span)).ToArray();
        var save = RequireInvocation(
            acceptedSubmissionInvocations,
            "SaveChangesAsync",
            "Reporting must invoke SaveChangesAsync exactly once in the accepted-submission operation slice.");
        Ensure(add.SpanStart < stage.SpanStart && set.SpanStart < stage.SpanStart && stage.SpanStart < save.SpanStart && save.SpanStart < enqueue.SpanStart,
            "Reporting order must place both persistence-stage calls before StageAsync, then StageAsync before SaveChangesAsync before EnqueueAsync.");
    }

    private static TextSpan GetAcceptedSubmissionOperationSpan(MethodDeclarationSyntax method)
    {
        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        return GetAcceptedSubmissionOperationSpan(
            RequireInvocation(invocations, "AddSubmissionAsync"),
            RequireInvocation(invocations, "SetRequestSubmittedAsync"),
            RequireInvocation(invocations, "EnqueueAsync"));
    }

    private static TextSpan GetAcceptedSubmissionOperationSpan(
        InvocationExpressionSyntax add,
        InvocationExpressionSyntax set,
        InvocationExpressionSyntax enqueue)
        => TextSpan.FromBounds(Math.Min(add.SpanStart, set.SpanStart), enqueue.Span.End);

    private static void AssertNoForbiddenReportingReferences((CSharpCompilation Compilation, IReadOnlyList<SyntaxTree> Trees) input, string sourceName)
    {
        var forbiddenTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default)
        {
            RequireTypeSymbol(input.Compilation, "LgymApi.Domain.Entities.Measurement"),
            RequireTypeSymbol(input.Compilation, "LgymApi.Application.Repositories.IMeasurementRepository"),
            RequireTypeSymbol(input.Compilation, "LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration.IReportSubmissionAcceptedProgressConsumer"),
            RequireTypeSymbol(input.Compilation, "LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration.ReportSubmissionAcceptedProgressConsumer"),
            RequireTypeSymbol(input.Compilation, "LgymApi.Application.WorkoutProgress.ReportingIntegration.IReportSubmissionAcceptedProgressPersistence")
        };
        var forbiddenConsumerTypes = forbiddenTypes.Where(type => type.Name is "IReportSubmissionAcceptedProgressConsumer" or "ReportSubmissionAcceptedProgressConsumer").ToArray();
        var observed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var tree in input.Trees)
        {
            var semanticModel = input.Compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var root = tree.GetCompilationUnitRoot();
            foreach (var typeSyntax in root.DescendantNodes().OfType<TypeSyntax>())
            {
                if (semanticModel.GetTypeInfo(typeSyntax).Type is INamedTypeSymbol type && forbiddenTypes.Contains(type.OriginalDefinition))
                {
                    observed.Add(type.ToDisplayString());
                }
            }

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var method = GetInvokedMethod(semanticModel, invocation);
                if (method?.Name == "ConsumeAsync" && forbiddenConsumerTypes.Any(type => SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, type)))
                {
                    observed.Add(method.ToDisplayString());
                }
            }
        }

        Ensure(observed.Count == 0, $"{sourceName} must not directly write Measurements or call the Workout & Progress consumer. Observed: {string.Join(", ", observed)}.");
    }

    private static ResolvedMember ResolveSourceMember(string repositoryRoot, string locator)
    {
        var match = Regex.Match(locator, "^(?<path>[^#]+)#(?<type>[A-Za-z_][A-Za-z0-9_]*)\\.(?<member>[A-Za-z_][A-Za-z0-9_]*)(?:\\((?<parameters>[^()]*)\\))?$", RegexOptions.CultureInvariant);
        Ensure(match.Success, $"Source locator '{locator}' must use path#Type.Member or path#Type.Member(Type,...).");
        var relativePath = match.Groups["path"].Value;
        Ensure(!Path.IsPathRooted(relativePath) && !Regex.IsMatch(relativePath, "^[A-Za-z]:", RegexOptions.CultureInvariant), $"Source locator path must be repository-relative: {relativePath}.");
        Ensure(!relativePath.Contains('\\'), $"Source locator must use '/' path separators: {relativePath}.");
        Ensure(!relativePath.Split('/').Contains("..", StringComparer.Ordinal), $"Source locator path traversal is forbidden: {relativePath}.");
        Ensure(!Regex.IsMatch(relativePath, @":\d+$", RegexOptions.CultureInvariant), $"Source locator line numbers are forbidden: {relativePath}.");
        Ensure(relativePath.EndsWith(".cs", StringComparison.Ordinal), $"Source locator path must identify a C# source file: {relativePath}.");

        var sourcePath = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Ensure(sourcePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase), $"Source locator escapes the repository: {relativePath}.");
        Ensure(File.Exists(sourcePath), $"Source locator path does not exist: {relativePath}.");

        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(sourcePath), CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest), sourcePath);
        var compilation = CSharpCompilation.Create("ModuleContributionLocator", [tree], ArchitectureTestHelpers.ResolveMetadataReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
        var root = tree.GetCompilationUnitRoot();
        var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Where(type => type.Identifier.ValueText == match.Groups["type"].Value).ToArray();
        Ensure(types.Length == 1, $"Source locator type does not resolve exactly once: {locator}.");
        Ensure(semanticModel.GetDeclaredSymbol(types[0]) is not null, $"Source locator type has no declared Roslyn symbol: {locator}.");

        var members = types[0].Members.Where(member => GetMemberName(member) == match.Groups["member"].Value).ToArray();
        if (match.Groups["parameters"].Success)
        {
            var parameterTypes = SplitParameterTypes(match.Groups["parameters"].Value);
            members = members.OfType<MethodDeclarationSyntax>()
                .Where(method => method.ParameterList.Parameters.Select(parameter => NormalizeType(parameter.Type?.ToString() ?? string.Empty)).SequenceEqual(parameterTypes, StringComparer.Ordinal))
                .Cast<MemberDeclarationSyntax>()
                .ToArray();
        }
        else if (members.Length > 1)
        {
            throw new InvalidOperationException($"Source locator member is ambiguous and requires a parameter signature: {locator}.");
        }

        Ensure(members.Length == 1, $"Source locator member does not resolve exactly once: {locator}.");
        Ensure(semanticModel.GetDeclaredSymbol(members[0]) is not null, $"Source locator member has no declared Roslyn symbol: {locator}.");
        return new ResolvedMember(relativePath, types[0], members[0]);
    }

    private static MethodDeclarationSyntax ResolvePathMethod(string repositoryRoot, IReadOnlyDictionary<string, BoundaryDocumentationRow> rows, string id)
        => ResolveSourceMember(repositoryRoot, rows[id].GetField("Source locator")).AsMethod();

    private static string[] SplitParameterTypes(string parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return [];
        }

        var result = new List<string>();
        var start = 0;
        var depth = 0;
        for (var index = 0; index < parameters.Length; index++)
        {
            depth += parameters[index] == '<' ? 1 : parameters[index] == '>' ? -1 : 0;
            if (parameters[index] == ',' && depth == 0)
            {
                result.Add(NormalizeType(parameters[start..index]));
                start = index + 1;
            }
        }

        result.Add(NormalizeType(parameters[start..]));
        return result.ToArray();
    }

    private static void AssertCreateMap(MethodDeclarationSyntax method, string sourceType, string destinationType)
    {
        var count = method.DescendantNodes().OfType<InvocationExpressionSyntax>().Count(invocation =>
            invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } &&
            generic.Identifier.ValueText == "CreateMap" &&
            generic.TypeArgumentList.Arguments.Select(argument => argument.ToString()).SequenceEqual([sourceType, destinationType], StringComparer.Ordinal));
        Ensure(count == 1, $"PlanProfile.Configure must register exactly one CreateMap<{sourceType}, {destinationType}> mapping.");
    }

    private static void AssertExactScopedRegistration(MethodDeclarationSyntax method, string contract, string implementation)
    {
        var count = method.DescendantNodes().OfType<InvocationExpressionSyntax>().Count(invocation =>
            invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } &&
            generic.Identifier.ValueText == "AddScoped" &&
            generic.TypeArgumentList.Arguments.Select(argument => argument.ToString()).SequenceEqual([contract, implementation], StringComparer.Ordinal));
        Ensure(count == 1, $"{method.Identifier.ValueText} must register AddScoped<{contract}, {implementation}> exactly once.");
    }

    private static void AssertReceiverInvocation(
        MethodDeclarationSyntax method,
        string expectedReceiverType,
        string invocationName,
        TextSpan? requiredSpan = null)
    {
        var context = GetSemanticMethod(method);
        AssertReceiverInvocation(context.SemanticModel, context.Method, expectedReceiverType, invocationName, requiredSpan);
    }

    private static void AssertReceiverInvocation(
        SemanticModel semanticModel,
        MethodDeclarationSyntax method,
        string expectedReceiverMetadataName,
        string invocationName,
        TextSpan? requiredSpan = null)
        => AssertReceiverInvocation(
            semanticModel,
            method,
            RequireTypeSymbol(semanticModel.Compilation, expectedReceiverMetadataName),
            invocationName,
            requiredSpan);

    private static void AssertReceiverInvocation(
        SemanticModel semanticModel,
        MethodDeclarationSyntax method,
        INamedTypeSymbol expectedReceiverType,
        string invocationName,
        TextSpan? requiredSpan = null)
    {
        var candidates = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(invocation => (requiredSpan is not { } span || span.Contains(invocation.Span)) && GetInvokedMethod(semanticModel, invocation)?.Name == invocationName)
            .ToArray();
        var matches = candidates.Where(invocation =>
        {
            var receiverType = GetReceiverType(semanticModel, invocation);
            var invokedMethod = GetInvokedMethod(semanticModel, invocation);
            return SymbolEqualityComparer.Default.Equals(receiverType?.OriginalDefinition, expectedReceiverType) &&
                SymbolEqualityComparer.Default.Equals(invokedMethod!.ContainingType.OriginalDefinition, expectedReceiverType);
        }).ToArray();
        var observed = candidates.Select(invocation =>
        {
            var receiverType = GetReceiverType(semanticModel, invocation)?.ToDisplayString() ?? "unresolved receiver";
            var invokedMethod = GetInvokedMethod(semanticModel, invocation)?.ToDisplayString() ?? "unresolved method";
            return $"{receiverType} -> {invokedMethod}";
        });
        Ensure(matches.Length == 1, $"{method.Identifier.ValueText} must invoke {invocationName} exactly once through the Roslyn receiver symbol identity {expectedReceiverType.ToDisplayString()}. Observed: {string.Join(" | ", observed)}.");
    }

    private static ITypeSymbol? GetReceiverType(SemanticModel semanticModel, InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        return semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            _ => null
        };
    }

    private static IMethodSymbol? GetInvokedMethod(SemanticModel semanticModel, InvocationExpressionSyntax invocation)
    {
        var symbolInfos = new List<SymbolInfo>
        {
            semanticModel.GetSymbolInfo(invocation),
            semanticModel.GetSymbolInfo(invocation.Expression)
        };
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            symbolInfos.Add(semanticModel.GetSymbolInfo(memberAccess.Name));
        }

        foreach (var symbolInfo in symbolInfos)
        {
            var methods = symbolInfo.Symbol is IMethodSymbol method
                ? new[] { method }
                : symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
            if (methods.Length == 1)
            {
                return methods[0].ReducedFrom?.OriginalDefinition ?? methods[0].OriginalDefinition;
            }
        }

        return null;
    }

    private static bool InvocationBelongsToType(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol expectedContainingType,
        string methodName)
    {
        var method = GetInvokedMethod(semanticModel, invocation);
        return method?.Name == methodName && SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, expectedContainingType);
    }

    private static INamedTypeSymbol RequireTypeSymbol(Compilation compilation, string metadataName)
        => compilation.GetTypeByMetadataName(metadataName)
            ?? throw new InvalidOperationException($"Roslyn compilation must resolve semantic type identity {metadataName}.");

    private static SemanticMethod GetSemanticMethod(MethodDeclarationSyntax method)
    {
        var semanticProject = SemanticProjects.GetOrAdd("module-contribution-flow", static _ => new Lazy<SemanticProject>(() =>
        {
            var prepared = ArchitectureTestHelpers.PrepareCompilation(SemanticFlowProjects);
            return new SemanticProject(prepared.Compilation, prepared.SyntaxTrees);
        })).Value;
        var semanticTree = semanticProject.SyntaxTrees.Single(tree =>
            string.Equals(Path.GetFullPath(tree.FilePath), Path.GetFullPath(method.SyntaxTree.FilePath), StringComparison.OrdinalIgnoreCase));
        var semanticMethod = semanticTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(candidate =>
            candidate.SpanStart == method.SpanStart && candidate.Identifier.ValueText == method.Identifier.ValueText);
        return new SemanticMethod(semanticProject.Compilation.GetSemanticModel(semanticTree, ignoreAccessibility: true), semanticMethod);
    }

    private static string[] InvocationNames(MethodDeclarationSyntax method)
        => method.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(GetInvocationName).Where(name => name.Length > 0).ToArray();

    private static InvocationExpressionSyntax RequireInvocation(IEnumerable<InvocationExpressionSyntax> invocations, string name, string? countMessage = null)
    {
        var matches = invocations.Where(invocation => GetInvocationName(invocation) == name).ToArray();
        Ensure(matches.Length == 1, countMessage ?? $"Reporting must invoke {name} exactly once in the accepted submission path.");
        return matches[0];
    }

    private static string GetInvocationName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        MemberAccessExpressionSyntax { Name: IdentifierNameSyntax identifier } => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } => generic.Identifier.ValueText,
        MemberBindingExpressionSyntax { Name: IdentifierNameSyntax identifier } => identifier.Identifier.ValueText,
        _ => string.Empty
    };

    private static string GetMemberName(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        EventDeclarationSyntax eventDeclaration => eventDeclaration.Identifier.ValueText,
        _ => string.Empty
    };

    private static string GetNamespace(SyntaxNode node)
        => node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().First().Name.ToString();

    private static string NormalizeType(string type)
        => Regex.Replace(type, @"\s+", string.Empty, RegexOptions.CultureInvariant).Replace("global::", string.Empty, StringComparison.Ordinal);

    private static void ValidatePullRequestChecklist(string markdown)
    {
        var lines = NormalizeLines(markdown);
        var starts = lines.Select((line, index) => (line, index)).Where(item => item.line.Trim() == PullRequestStartMarker).Select(item => item.index).ToArray();
        var ends = lines.Select((line, index) => (line, index)).Where(item => item.line.Trim() == PullRequestEndMarker).Select(item => item.index).ToArray();
        Ensure(starts.Length == 1, "The PR checklist must contain exactly one start delimiter.");
        Ensure(ends.Length == 1, "The PR checklist must contain exactly one end delimiter.");
        Ensure(starts[0] < ends[0], "The PR checklist delimiters must be ordered and non-nested.");

        var itemPattern = new Regex(@"<!--\s*module-contribution:(?<id>[a-z0-9-]+)\s*-->", RegexOptions.CultureInvariant);
        var items = new List<string>();
        foreach (var line in lines[(starts[0] + 1)..ends[0]])
        {
            var matches = itemPattern.Matches(line);
            var isCheckbox = Regex.IsMatch(line, @"^\s*-\s+\[[ xX]\]", RegexOptions.CultureInvariant);
            if (isCheckbox)
            {
                Ensure(matches.Count == 1, "Every PR checklist checkbox inside the delimiters must carry exactly one module-contribution machine marker.");
            }

            foreach (Match match in matches)
            {
                Ensure(Regex.IsMatch(line, @"^\s*-\s+\[\s\]\s+.*<!--\s*module-contribution:[a-z0-9-]+\s*-->\s*$", RegexOptions.CultureInvariant), $"module-contribution:{match.Groups["id"].Value} must be carried by one unchecked checkbox line.");
                items.Add(match.Groups["id"].Value);
            }
        }

        var duplicates = items.GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        Ensure(duplicates.Length == 0, $"Duplicate PR checklist item IDs: {string.Join(", ", duplicates)}.");
        var missing = PullRequestItemIds.Except(items, StringComparer.Ordinal).ToArray();
        var unknown = items.Except(PullRequestItemIds, StringComparer.Ordinal).ToArray();
        Ensure(missing.Length == 0 && unknown.Length == 0, $"PR checklist items drifted. Missing: {string.Join(", ", missing)}. Unknown: {string.Join(", ", unknown)}.");
        Ensure(items.SequenceEqual(PullRequestItemIds, StringComparer.Ordinal), $"PR checklist item order must be: {string.Join(", ", PullRequestItemIds)}.");
    }

    private static void ValidateArchitectureContributionSections(string markdown)
    {
        Ensure(CountLinksTo(markdown, "docs/ARCHITECTURE.md", GuideRelativePath) == 2, "docs/ARCHITECTURE.md must link the canonical workflow from both contribution entry sections.");
        var sections = FindSectionsContainingLink(markdown, "docs/ARCHITECTURE.md", GuideRelativePath);
        var codeTokens = ExtractInlineCodeTokens(sections);
        var staleSkeletonTokens = new[]
        {
            "LgymApi.Application/FeatureX/IFeatureXService.cs",
            "LgymApi.Application/FeatureX/FeatureXService.cs",
            "LgymApi.Application/Repositories/IFeatureXRepository.cs",
            "LgymApi.Infrastructure/Repositories/FeatureXRepository.cs",
            "LgymApi.Api/Features/FeatureX/Controllers/FeatureXController.cs",
            "LgymApi.Api/Features/FeatureX/Contracts/FeatureXDtos.cs"
        };
        Ensure(!codeTokens.Intersect(staleSkeletonTokens, StringComparer.Ordinal).Any(), "The active contribution sections retain the stale global FeatureX skeleton.");
        foreach (var stalePattern in StaleArchitectureContributionPatterns)
        {
            Ensure(!sections.Contains(stalePattern, StringComparison.OrdinalIgnoreCase), $"The active contribution sections retain stale imperative/template: {stalePattern}.");
        }
    }

    private static void ValidateContributorEntryPoints(string readme, string usage)
    {
        Ensure(CountLinksTo(readme, "README.md", GuideRelativePath) == 1, "README.md must link the canonical workflow exactly once.");
        Ensure(CountLinksTo(usage, "docs/NEW_MODULES_USAGE.md", GuideRelativePath) == 1, "The feature-usage document must link the canonical workflow exactly once.");
        Ensure(PlatformReferenceDataBoundaryDocumentationTestHelpers.ParseRows(usage).All(row => !row.Id.StartsWith("module-guide.", StringComparison.Ordinal)), "Feature usage must not duplicate the contribution contract tables.");
        foreach (var forbiddenClaim in ForbiddenUsageAuthorityClaims)
        {
            Ensure(!usage.Contains(forbiddenClaim, StringComparison.OrdinalIgnoreCase), $"Feature usage must not claim '{forbiddenClaim}'.");
        }
    }

    private static void ValidateRetainedDependencyAggregateRows(string markdown)
    {
        var tableRows = NormalizeLines(markdown).Select(SplitPipeCells).Where(cells => cells.Count > 0).ToArray();
        foreach (var aggregate in RetainedDependencyAggregates)
        {
            var matches = tableRows.Where(cells => cells.Select(UnwrapCode).Contains(aggregate, StringComparer.Ordinal)).ToArray();
            Ensure(matches.Length == 1, $"Ownership map must contain exactly one row for {aggregate}.");
            Ensure(matches[0].Select(UnwrapCode).Contains("Retained owner-aligned dependency aggregate", StringComparer.Ordinal), $"{aggregate} must be classified as a retained owner-aligned dependency aggregate.");
        }

        foreach (var forbiddenClaim in ForbiddenOwnershipAggregateClaims)
        {
            Ensure(!markdown.Contains(forbiddenClaim, StringComparison.OrdinalIgnoreCase), $"Ownership map must not claim '{forbiddenClaim}'.");
        }
    }

    private static int CountLinksTo(string markdown, string documentRelativePath, string targetRelativePath)
        => ExtractMarkdownLinkTargets(markdown).Count(target => ResolveLink(documentRelativePath, target) == targetRelativePath);

    private static string FindSectionsContainingLink(string markdown, string documentRelativePath, string targetRelativePath)
    {
        var lines = NormalizeLines(markdown);
        var linkLines = Enumerable.Range(0, lines.Length)
            .Where(index => ExtractMarkdownLinkTargets(lines[index]).Any(target => ResolveLink(documentRelativePath, target) == targetRelativePath))
            .ToArray();
        Ensure(linkLines.Length > 0, $"{documentRelativePath} has no canonical workflow link section.");
        var sections = new List<string>();
        foreach (var linkLine in linkLines)
        {
            var start = linkLine;
            while (start > 0 && !lines[start].TrimStart().StartsWith('#'))
            {
                start--;
            }

            var end = linkLine + 1;
            while (end < lines.Length && !lines[end].TrimStart().StartsWith('#'))
            {
                end++;
            }

            sections.Add(string.Join('\n', lines[start..end]));
        }

        return string.Join('\n', sections.Distinct(StringComparer.Ordinal));
    }

    private static string[] ExtractMarkdownLinkTargets(string markdown)
        => Regex.Matches(markdown, @"\[[^\]]*\]\((?<target>[^)\s]+)(?:\s+[^)]*)?\)", RegexOptions.CultureInvariant)
            .Select(match => match.Groups["target"].Value.Trim('<', '>')).ToArray();

    private static string[] ExtractInlineCodeTokens(string markdown)
        => Regex.Matches(markdown, @"`(?<token>[^`\r\n]+)`", RegexOptions.CultureInvariant).Select(match => match.Groups["token"].Value).ToArray();

    private static string ResolveLink(string documentRelativePath, string target)
    {
        if (target.StartsWith('#') || Uri.TryCreate(target, UriKind.Absolute, out _))
        {
            return string.Empty;
        }

        var documentDirectory = Path.GetDirectoryName(documentRelativePath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        var pathRoot = Path.GetFullPath(Path.DirectorySeparatorChar.ToString());
        var resolvedPath = Path.GetFullPath(Path.Combine(pathRoot, documentDirectory, target));
        return ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(pathRoot, resolvedPath));
    }

    private static string OwnerPolicyContract()
    {
        var completedTrainingOwner = PersistedEntityOwnershipCatalog.Entries
            .Single(entry => entry.EntityType == typeof(LgymApi.Domain.Entities.Training))
            .Owner;
        return $"canonical-owners={PersistedEntityOwnershipCatalog.CanonicalOwners.Count}; completed-training={completedTrainingOwner}; api-persistence=false";
    }

    private static string CreateValidGuideFixture()
    {
        var builder = new StringBuilder();
        AppendTableHeader(builder, AuthorityColumns);
        foreach (var item in Authorities)
        {
            builder.AppendLine($"| {item.Id} | {item.Source} | {item.Governs} |");
        }

        builder.AppendLine();
        AppendTableHeader(builder, PolicyColumns);
        foreach (var item in Policies)
        {
            builder.AppendLine($"| {item.Id} | {item.Contract} | source-bound |");
        }

        builder.AppendLine();
        AppendTableHeader(builder, PathColumns);
        foreach (var item in TrainingPlanningPath.Concat(ReportingPath))
        {
            builder.AppendLine($"| {item.Id} | {item.Step} | {item.Owner} | {item.Locator} | source-bound |");
        }

        builder.AppendLine();
        AppendTableHeader(builder, ExceptionColumns);
        foreach (var item in Exceptions)
        {
            builder.AppendLine($"| {item.Id} | {item.Scope} | {item.Constraint} |");
        }

        return builder.ToString();
    }

    private static void AppendTableHeader(StringBuilder builder, IReadOnlyList<string> columns)
    {
        builder.AppendLine($"| {string.Join(" | ", columns)} |");
        builder.AppendLine($"| {string.Join(" | ", columns.Select(_ => "---"))} |");
    }

    private static string CreateValidPullRequestFixture()
    {
        var builder = new StringBuilder().AppendLine(PullRequestStartMarker);
        foreach (var id in PullRequestItemIds)
        {
            builder.AppendLine("- [ ] " + PullRequestItemMarker(id));
        }

        return builder.AppendLine(PullRequestEndMarker).ToString();
    }

    private static string ReorderPullRequestItems(string markdown, string firstId, string secondId)
    {
        var lines = NormalizeLines(markdown);
        var firstIndex = Array.FindIndex(lines, line => line.Contains(PullRequestItemMarker(firstId), StringComparison.Ordinal));
        var secondIndex = Array.FindIndex(lines, line => line.Contains(PullRequestItemMarker(secondId), StringComparison.Ordinal));
        (lines[firstIndex], lines[secondIndex]) = (lines[secondIndex], lines[firstIndex]);
        return string.Join('\n', lines);
    }

    private static string PullRequestItemMarker(string id) => $"<!-- module-contribution:{id} -->";

    private static string ArchitectureContributionFixture(string? stalePattern = null, string? legacyInventory = null) => $$"""
        ## Contributing a Use Case
        Follow the [Module Contribution Guide](MODULE_CONTRIBUTION_GUIDE.md).
        {{stalePattern}}

        ## Contribution References
        Use the [Module Contribution Guide](MODULE_CONTRIBUTION_GUIDE.md).

        ## Historical Inventory
        {{legacyInventory}}
        """;

    private static string ContributorReadmeFixture() => "[Module Contribution Guide](docs/MODULE_CONTRIBUTION_GUIDE.md)";

    private static string ContributorUsageFixture(string? forbiddenClaim = null) => $$"""
        # Feature usage
        See the [Module Contribution Guide](MODULE_CONTRIBUTION_GUIDE.md).
        {{forbiddenClaim}}
        """;

    private static string OwnershipMapFixture(string? forbiddenClaim = null)
    {
        var builder = new StringBuilder("| Artifact type | Artifact |\n| --- | --- |\n");
        foreach (var aggregate in RetainedDependencyAggregates)
        {
            builder.AppendLine($"| Retained owner-aligned dependency aggregate | `{aggregate}` |");
        }

        return builder.AppendLine(forbiddenClaim).ToString();
    }

    private static string RemoveStableRow(string markdown, string id)
        => string.Join('\n', NormalizeLines(markdown).Where(line => SplitPipeCells(line).FirstOrDefault() != id));

    private static string DuplicateStableRow(string markdown, string id)
    {
        var lines = NormalizeLines(markdown).ToList();
        var index = lines.FindIndex(line => SplitPipeCells(line).FirstOrDefault() == id);
        lines.Insert(index + 1, lines[index]);
        return string.Join('\n', lines);
    }

    private static string AddUnknownStableRow(string markdown, string unknownId)
    {
        var sourceId = Exceptions[0].Id;
        var lines = NormalizeLines(markdown).ToList();
        var index = lines.FindIndex(line => SplitPipeCells(line).FirstOrDefault() == sourceId);
        lines.Insert(index + 1, lines[index].Replace(sourceId, unknownId, StringComparison.Ordinal));
        return string.Join('\n', lines);
    }

    private static string RemoveLastCell(string markdown, string id)
    {
        var lines = NormalizeLines(markdown);
        var index = Array.FindIndex(lines, line => SplitPipeCells(line).FirstOrDefault() == id);
        var cells = SplitPipeCells(lines[index]);
        cells.RemoveAt(cells.Count - 1);
        lines[index] = "| " + string.Join(" | ", cells) + " |";
        return string.Join('\n', lines);
    }

    private static string ReplaceStableCell(string markdown, string id, string field, string value)
    {
        var columns = id.StartsWith(AuthorityPrefix, StringComparison.Ordinal) ? AuthorityColumns :
            id.StartsWith(PolicyPrefix, StringComparison.Ordinal) ? PolicyColumns :
            id.StartsWith("module-guide.path.", StringComparison.Ordinal) ? PathColumns : ExceptionColumns;
        var lines = NormalizeLines(markdown);
        var index = Array.FindIndex(lines, line => SplitPipeCells(line).FirstOrDefault() == id);
        var cells = SplitPipeCells(lines[index]);
        cells[Array.IndexOf(columns, field)] = value;
        lines[index] = "| " + string.Join(" | ", cells) + " |";
        return string.Join('\n', lines);
    }

    private static List<string> SplitPipeCells(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '|' && trimmed[^1] == '|'
            ? trimmed[1..^1].Split('|').Select(cell => cell.Trim()).ToList()
            : [];
    }

    private static string UnwrapCode(string value)
        => value.Length >= 2 && value[0] == '`' && value[^1] == '`' ? value[1..^1] : value;

    private static string[] NormalizeLines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static MethodDeclarationSyntax ParseFixtureMethod(string source, string methodName)
    {
        var root = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest)).GetCompilationUnitRoot();
        return root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == methodName);
    }

    private static MethodDeclarationSyntax GetFixtureMethod((CSharpCompilation Compilation, IReadOnlyList<SyntaxTree> Trees) fixture, string methodName)
        => fixture.Trees.Single().GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == methodName);

    private static (CSharpCompilation Compilation, IReadOnlyList<SyntaxTree> Trees) CompileFixture(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create("ModuleContributionFixture", [tree], ArchitectureTestHelpers.ResolveMetadataReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return (compilation, new[] { tree });
    }

    private static (CSharpCompilation Compilation, IReadOnlyList<SyntaxTree> Trees) CompileProjectFixture(string projectName, string source)
    {
        var semanticProject = SemanticProjects.GetOrAdd(projectName, static name => new Lazy<SemanticProject>(() =>
        {
            var prepared = ArchitectureTestHelpers.PrepareCompilation(name);
            return new SemanticProject(prepared.Compilation, prepared.SyntaxTrees);
        })).Value;
        var tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
        var compilation = semanticProject.Compilation;
        var queryableAssemblyPath = typeof(Queryable).Assembly.Location;
        if (!compilation.References.OfType<PortableExecutableReference>().Any(reference => string.Equals(reference.FilePath, queryableAssemblyPath, StringComparison.OrdinalIgnoreCase)))
        {
            compilation = compilation.AddReferences(MetadataReference.CreateFromFile(queryableAssemblyPath));
        }

        compilation = compilation.AddSyntaxTrees(tree);
        var errors = compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Location.SourceTree == tree).ToArray();
        Ensure(errors.Length == 0, $"Semantic fixture must compile without errors: {string.Join(" | ", errors.Select(error => error.ToString()))}.");
        return (compilation, new[] { tree });
    }

    private static string TrainingProjectionFixture(bool includeNoTracking)
        => $$"""
            using System.Linq;
            using LgymApi.Domain.ValueObjects;
            using LgymApi.Identity.Contracts;
            using LgymApi.TrainingPlanning.Contracts;
            using LgymApi.Application.TrainingPlanning.Plan.Models;
            using Microsoft.EntityFrameworkCore;

            namespace MissingTrackingFixture;

            sealed class Fixture
            {
                IQueryable<string> Plans => throw null!;
                object QueryAsync() => Plans{{(includeNoTracking ? ".AsNoTracking()" : string.Empty)}}.Select(plan => new PlanReadModel(
                    default(Id<PlanReference>),
                    default(Id<AccountReference>),
                    string.Empty,
                    false,
                    null));
            }
            """;

    private static string TrainingProjectionDisconnectedNoTrackingFixture() => """
        using System.Linq;
        using LgymApi.Application.TrainingPlanning.Plan.Models;
        using LgymApi.Domain.ValueObjects;
        using LgymApi.Identity.Contracts;
        using LgymApi.TrainingPlanning.Contracts;
        using Microsoft.EntityFrameworkCore;

        namespace DisconnectedNoTrackingFixture;

        sealed class Fixture
        {
            IQueryable<string> Plans => throw null!;
            IQueryable<string> Other => throw null!;

            object QueryAsync()
            {
                _ = Other.AsNoTracking();
                return Plans.Select(plan => new PlanReadModel(
                    default(Id<PlanReference>),
                    default(Id<AccountReference>),
                    string.Empty,
                    false,
                    null));
            }
        }
        """;

    private static string TrainingProjectionFalsePositiveFixture() => """
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        namespace FalsePositiveProjectionFixture;

        sealed class PlanReadModel { }

        sealed class Fixture
        {
            IQueryable<string> Plans => throw null!;
            object QueryAsync()
            {
                var unrelated = new PlanReadModel();
                return Plans.AsNoTracking().Select(plan => plan);
            }
        }
        """;

    private static string ReportingOrderFixture(bool stageAfterSave, bool omitStage)
    {
        var stage = omitStage ? string.Empty : "await StageAsync();";
        var ordered = stageAfterSave ? $"await SaveChangesAsync(); {stage}" : $"{stage} await SaveChangesAsync();";
        return $$"""
            class Fixture
            {
                async Task SubmitAsync()
                {
                    await AddSubmissionAsync();
                    await SetRequestSubmittedAsync();
                    {{ordered}}
                    await EnqueueAsync();
                }
            }
            """;
    }

    private static string ReportingReversePersistenceOrderFixture() => """
        class Fixture
        {
            async Task SubmitAsync()
            {
                await SetRequestSubmittedAsync();
                await AddSubmissionAsync();
                await StageAsync();
                await SaveChangesAsync();
                await EnqueueAsync();
            }
        }
        """;

    private static string ReportingEarlyBranchSaveFixture() => """
        class Fixture
        {
            async Task SubmitAsync()
            {
                if (ShouldExpire())
                {
                    await SaveChangesAsync();
                    return;
                }

                await AddSubmissionAsync();
                await SetRequestSubmittedAsync();
                await StageAsync();
                await SaveChangesAsync();
                await EnqueueAsync();
            }
        }
        """;

    private static string ReportingAcceptedSliceExtraSaveFixture() => """
        class Fixture
        {
            async Task SubmitAsync()
            {
                if (ShouldExpire())
                {
                    await SaveChangesAsync();
                    return;
                }

                await AddSubmissionAsync();
                await SaveChangesAsync();
                await SetRequestSubmittedAsync();
                await StageAsync();
                await SaveChangesAsync();
                await EnqueueAsync();
            }
        }
        """;

    private static string PublicInterfaceFixture() => """
        public interface IFixtureContract
        {
            object ExecuteAsync();
        }
        """;

    private static string PublicInterfaceWithExtraMemberFixture() => """
        public interface IFixtureContract
        {
            object ExecuteAsync();
            string Name { get; }
        }
        """;

    private static string BodylessClassContractFixture() => """
        public abstract class FixtureContract
        {
            public abstract object ExecuteAsync();
        }
        """;

    private static string NonPublicInterfaceFixture() => """
        interface IFixtureContract
        {
            object ExecuteAsync();
        }
        """;

    private static string RenamedReceiverFixture() => """
        interface IPlanRepository
        {
            object GetReadModelsByUserIdAsync();
        }

        class Fixture
        {
            private readonly IPlanRepository renamedRepository = default!;
            object ExecuteAsync() => renamedRepository.GetReadModelsByUserIdAsync();
        }
        """;

    private static string WrongReceiverIdentityFixture() => """
        namespace Expected
        {
            interface IPlanRepository
            {
                object GetReadModelsByUserIdAsync();
            }
        }

        namespace Impostor
        {
            interface IPlanRepository
            {
                object GetReadModelsByUserIdAsync();
            }

            class Fixture
            {
                private readonly IPlanRepository repository = default!;
                object ExecuteAsync() => repository.GetReadModelsByUserIdAsync();
            }
        }
        """;

    private static string TrainingProjectionWrongPlanReadModelIdentityFixture() => """
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        namespace FixtureNamespace;

        sealed class PlanReadModel { }

        sealed class Fixture
        {
            IQueryable<string> Plans => throw null!;
            object QueryAsync() => Plans.AsNoTracking().Select(plan => new PlanReadModel());
        }
        """;

    private static string TrainingProjectionWrongAsNoTrackingIdentityFixture() => """
        using System.Linq;

        namespace FixtureNamespace;

        static class FakeTrackingExtensions
        {
            public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
        }

        sealed class PlanReadModel { }

        sealed class Fixture
        {
            IQueryable<string> Plans => throw null!;
            object QueryAsync() => Plans.AsNoTracking().Select(plan => new PlanReadModel());
        }
        """;

    private static string DirectMeasurementFixture() => """
        using System.Threading.Tasks;
        using LgymApi.Application.Repositories;

        namespace DirectMeasurementFixtureNamespace;

        sealed class Fixture
        {
            IMeasurementRepository Measurements => throw null!;
            Task Submit() => Measurements.AddAsync(default!);
        }
        """;

    private static string DirectConsumerFixture() => """
        using System.Threading.Tasks;
        using LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration;

        namespace DirectConsumerFixtureNamespace;

        sealed class Fixture
        {
            IReportSubmissionAcceptedProgressConsumer Consumer => throw null!;
            Task Submit() => Consumer.ConsumeAsync(default!);
        }
        """;

    private static string UnrelatedConsumeAsyncFixture() => """
        interface IUnrelatedConsumer { void ConsumeAsync(); }
        class Fixture
        {
            IUnrelatedConsumer Consumer { get; }
            void Submit() => Consumer.ConsumeAsync();
        }
        """;

    private static string FakeForbiddenReportingShortNameFixture() => """
        namespace FixtureNamespace;
        interface IMeasurementRepository { void AddAsync(); }
        class Fixture
        {
            IMeasurementRepository Measurements { get; }
            void Submit() => Measurements.AddAsync();
        }
        """;

    private static string ReadRequiredArtifact(string relativePath)
    {
        var path = Path.Combine(ArchitectureTestHelpers.ResolveRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required issue #394 contract artifact is missing: {relativePath}.");
        }

        return File.ReadAllText(path);
    }

    private static void EnsureField(BoundaryDocumentationRow row, string field, string expected)
        => Ensure(row.GetField(field) == expected, $"{row.Id} must define {field} as '{expected}'.");

    private static void AssertRejected(Action action, string messageFragment)
        => Assert.That(action, Throws.InvalidOperationException.With.Message.Contains(messageFragment));

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record AuthorityExpectation(string Id, string Source, string Governs);
    private sealed record PolicyExpectation(string Id, string Contract);
    private sealed record PathExpectation(string Id, int Step, string Owner, string Locator);
    private sealed record ExceptionExpectation(string Id, string Scope, string Constraint);
    private sealed record SemanticProject(CSharpCompilation Compilation, IReadOnlyList<SyntaxTree> SyntaxTrees);
    private sealed record SemanticMethod(SemanticModel SemanticModel, MethodDeclarationSyntax Method);
    private sealed record ResolvedMember(string RelativePath, TypeDeclarationSyntax Type, MemberDeclarationSyntax Member)
    {
        internal MethodDeclarationSyntax AsMethod()
            => Member as MethodDeclarationSyntax ?? throw new InvalidOperationException($"Source locator must resolve to a method: {RelativePath}#{Type.Identifier.ValueText}.{GetMemberName(Member)}.");
    }
}
