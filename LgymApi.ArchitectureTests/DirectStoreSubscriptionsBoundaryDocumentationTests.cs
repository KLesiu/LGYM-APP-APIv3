using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

// allow: SIZE_OK — single-file plan scope keeps the private 45-row contract, parser, Roslyn scanner, and isolated fixtures together.
[TestFixture]
public sealed class DirectStoreSubscriptionsBoundaryDocumentationTests
{
    private const string DocumentPath = "docs/subscriptions/direct-store-subscriptions.md";
    private const string MissingDocumentDiagnostic =
        "Missing canonical direct-store subscription boundary document 'docs/subscriptions/direct-store-subscriptions.md'.";
    private const string ProviderCallPolicy =
        "Provider calls use a bounded, implementation-owned timeout and a bounded, implementation-owned retry policy.";

    private static readonly TableContract[] TableContracts =
    [
        new("subscriptions.boundary.",
        [
            "Boundary ID", "State", "Owner / authority", "Owner responsibility",
            "Allowed placement/dependencies", "Forbidden condition", "Source locator"
        ],
        [
            "subscriptions.boundary.current-state",
            "subscriptions.boundary.identity-owner",
            "subscriptions.boundary.api-transport",
            "subscriptions.boundary.worker-scheduling",
            "subscriptions.boundary.infrastructure-runtime",
            "subscriptions.boundary.common-closure",
            "subscriptions.boundary.project-graph"
        ]),
        new("subscriptions.contract.",
        [
            "Contract ID", "State", "Owner", "Provider-neutral contract",
            "Persistence/message rule", "Explicit exclusion"
        ],
        [
            "subscriptions.contract.grant",
            "subscriptions.contract.inbox",
            "subscriptions.contract.account-binding",
            "subscriptions.contract.current-access",
            "subscriptions.contract.provider-verification",
            "subscriptions.contract.provider-notification",
            "subscriptions.contract.processing",
            "subscriptions.contract.reconciliation",
            "subscriptions.contract.api-ingress",
            "subscriptions.contract.api-query",
            "subscriptions.contract.mapping",
            "subscriptions.contract.localization",
            "subscriptions.contract.persistence-topology"
        ]),
        new("subscriptions.provider.",
        [
            "Provider ID", "State", "Owner", "Fixed authority/trust input",
            "Verification/retry rule", "Public-contract exclusion", "Redaction class"
        ],
        [
            "subscriptions.provider.apple-production",
            "subscriptions.provider.apple-sandbox",
            "subscriptions.provider.apple-signed-data",
            "subscriptions.provider.google-play",
            "subscriptions.provider.google-rtdn",
            "subscriptions.provider.sanitized-errors"
        ]),
        new("subscriptions.configuration.",
        [
            "Configuration ID", "State", "Key/root", "Default", "Requires", "Enables", "Forbidden effect"
        ],
        [
            "subscriptions.configuration.root",
            "subscriptions.configuration.apple",
            "subscriptions.configuration.google-play",
            "subscriptions.configuration.processing",
            "subscriptions.configuration.reconciliation",
            "subscriptions.configuration.enabled",
            "subscriptions.configuration.apple-enabled",
            "subscriptions.configuration.google-play-enabled",
            "subscriptions.configuration.purchases-enabled",
            "subscriptions.configuration.projection-apply-enabled",
            "subscriptions.configuration.capability-enforcement-enabled"
        ]),
        new("subscriptions.policy.",
        ["Policy ID", "State", "Rule", "Evidence/guard", "Explicit non-goal"],
        [
            "subscriptions.policy.tiers",
            "subscriptions.policy.free-baseline",
            "subscriptions.policy.cross-store",
            "subscriptions.policy.server-authority",
            "subscriptions.policy.jwt",
            "subscriptions.policy.tests",
            "subscriptions.policy.rollout",
            "subscriptions.policy.rollback"
        ])
    ];

    private static readonly IReadOnlyDictionary<string, string> BoundaryLocators =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["subscriptions.boundary.current-state"] = "LgymApi.ArchitectureTests/PersistedEntityOwnershipCatalog.cs#PersistedEntityOwnershipCatalog",
            ["subscriptions.boundary.identity-owner"] = "LgymApi.Identity/IdentityModule.cs#IdentityModule",
            ["subscriptions.boundary.api-transport"] = "LgymApi.Api/Features/Account/Controllers/AccountController.cs#AccountController",
            ["subscriptions.boundary.worker-scheduling"] = "LgymApi.BackgroundWorker/BackgroundWorkerRecurringJobs.cs#BackgroundWorkerRecurringJobs",
            ["subscriptions.boundary.infrastructure-runtime"] = "LgymApi.Infrastructure/Data/AppDbContext.cs#AppDbContext",
            ["subscriptions.boundary.common-closure"] = "LgymApi.ArchitectureTests/BackgroundWorkerCommonSurfaceGuardTests.cs#BackgroundWorkerCommonSurfaceGuardTests",
            ["subscriptions.boundary.project-graph"] = "LgymApi.ArchitectureTests/ProjectReferenceGraphManifest.cs#ProjectReferenceGraphManifest"
        };

    private static readonly DiagramEdge[] RequiredDiagramEdges =
    [
        new("Client", "API", "authenticated purchase/query"),
        new("Apple", "API", "verified ingress"),
        new("GooglePlay", "API", "verified ingress"),
        new("API", "Identity", "focused contracts"),
        new("Worker", "Identity", "scheduling"),
        new("Identity", "AppleAdapter", "provider call"),
        new("AppleAdapter", "Apple", "provider call"),
        new("Identity", "GoogleAdapter", "provider call"),
        new("GoogleAdapter", "GooglePlay", "provider call"),
        new("Identity", "Infrastructure", "UoW"),
        new("Infrastructure", "PostgreSQL", "persistence"),
        new("Worker", "Infrastructure", "scheduler"),
        new("Infrastructure", "Hangfire", "persistence"),
        new("Identity", "CurrentAccess", "projection read")
    ];

    private static readonly string[] PublicSurfaceRoots =
    [
        "LgymApi.Identity/Contracts/Subscriptions/",
        "LgymApi.Api/Features/Account/Subscriptions/",
        "LgymApi.Api/Features/Webhooks/Subscriptions/"
    ];

    private static readonly string[] PublicSurfaceOwnerProjects =
    [
        "LgymApi.Identity",
        "LgymApi.Api"
    ];

    private static readonly string[] ProviderCallPolicyRowIds =
    [
        "subscriptions.provider.apple-production",
        "subscriptions.provider.apple-sandbox",
        "subscriptions.provider.google-play",
        "subscriptions.provider.google-rtdn"
    ];

    private static readonly string[] FutureFocusedContractIdentities =
    [
        "IAccountSubscriptionGrantRepository",
        "ISubscriptionInboxEventRepository",
        "IAccountPaidAccessProjectionRepository",
        "IAppleSubscriptionProvider",
        "IGooglePlaySubscriptionProvider",
        "IVerifyAppleSubscriptionPurchaseUseCase.VerifyAsync",
        "IVerifyGooglePlaySubscriptionPurchaseUseCase.VerifyAsync",
        "IIngestAppleSubscriptionNotificationUseCase.IngestAsync",
        "IIngestGooglePlayNotificationUseCase.IngestAsync",
        "ICurrentPaidAccessQuery.GetAsync",
        "ISubscriptionInboxProcessingUseCase.ProcessBatchAsync",
        "ISubscriptionProviderReconciliationUseCase.ReconcileBatchAsync"
    ];

    private static readonly HashSet<string> ExplicitRawProviderWrapperNames = new(StringComparer.Ordinal)
    {
        "RawProviderPayload",
        "SignedPayload",
        "PurchaseToken",
        "ProviderResponseBody"
    };

    [Test]
    public void Canonical_Document_Should_Match_The_Executable_Subscription_Boundary()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var path = Path.Combine(repositoryRoot, DocumentPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            Assert.Fail(MissingDocumentDiagnostic);
        }

        var markdown = File.ReadAllText(path);
        var document = ParseAndValidateDocument(markdown);

        AssertCurrentExecutableAuthorities(repositoryRoot, document.Rows);
        AssertSourceLocators(repositoryRoot, document.Rows);
        Assert.That(ScanRepositoryPublicSurfaces(), Is.Empty);
    }

    [Test]
    public void Complete_Synthetic_Document_And_Provider_Neutral_Public_Surface_Should_Pass()
    {
        Assert.That(() => ParseAndValidateDocument(CreateValidMarkdown()), Throws.Nothing);

        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            namespace LgymApi.Identity.Contracts.Subscriptions;
            public sealed class AccountReference { }
            public readonly struct Id<T> { }
            public readonly struct Result<TValue, TError> { }
            public sealed record SanitizedSubscriptionError(string Code);
            public sealed record AccountBindingToken(string Value);
            public sealed record PurchaseReference(string Value);
            public sealed record CurrentPaidAccess(Id<AccountReference> AccountId, string Tier, PurchaseReference Purchase);
            public interface ICurrentPaidAccessQuery
            {
                Task<Result<CurrentPaidAccess, SanitizedSubscriptionError>> GetAsync(
                    AccountBindingToken binding,
                    CancellationToken cancellationToken);
            }
            """;

        Assert.That(ScanFixturePublicSurface(source), Is.Empty);
    }

    [Test]
    public void Synthetic_Document_Should_Reject_A_Ninth_Owner()
    {
        var markdown = CreateValidMarkdown().Replace(
            "| `subscriptions.boundary.identity-owner` | future | Identity & Accounts |",
            "| `subscriptions.boundary.identity-owner` | future | Commerce |",
            StringComparison.Ordinal);

        Assert.That(
            () => ParseAndValidateDocument(markdown),
            Throws.InvalidOperationException.With.Message.Contains("subscriptions.boundary.identity-owner").And.Message.Contains("Identity & Accounts"));
    }

    [Test]
    public void Synthetic_Document_Should_Reject_A_Common_Subscription_Job()
    {
        var markdown = CreateValidMarkdown().Replace(
            "no subscription additions to Common",
            "add ISubscriptionProcessingJob to Common",
            StringComparison.Ordinal);

        Assert.That(
            () => ParseAndValidateDocument(markdown),
            Throws.InvalidOperationException.With.Message.Contains("subscriptions.boundary.common-closure").And.Message.Contains("no subscription additions to Common"));
    }

    [Test]
    public void Synthetic_Document_Should_Reject_A_Changed_Provider_Host()
    {
        var markdown = CreateValidMarkdown().Replace(
            "https://api.storekit.apple.com",
            "https://store.example.invalid",
            StringComparison.Ordinal);

        Assert.That(
            () => ParseAndValidateDocument(markdown),
            Throws.InvalidOperationException.With.Message.Contains("https://api.storekit.apple.com"));
    }

    [Test]
    public void Synthetic_Document_Should_Reject_An_Inverted_Launch_Gate_Dependency()
    {
        var markdown = CreateValidMarkdown().Replace(
            "global enabled plus projection apply plus separately approved and shipped paid-benefit release",
            "capability enforcement enables projection apply",
            StringComparison.Ordinal);

        Assert.That(
            () => ParseAndValidateDocument(markdown),
            Throws.InvalidOperationException.With.Message.Contains("subscriptions.configuration.capability-enforcement-enabled").And.Message.Contains("Requires"));
    }

    [Test]
    public void Synthetic_Public_Surface_Should_Reject_A_Provider_Sdk_Type()
    {
        const string source = """
            namespace Google.Apis.AndroidPublisher.v3.Data
            {
                public sealed class SubscriptionPurchaseV2 { }
            }
            namespace LgymApi.Identity.Contracts.Subscriptions
            {
                public sealed record LeakedPurchase(Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2 Response);
            }
            """;

        Assert.That(
            ScanFixturePublicSurface(source),
            Has.Some.Matches<PublicSurfaceViolation>(violation =>
                violation.Category == "provider SDK" && violation.Dependency.Contains("SubscriptionPurchaseV2", StringComparison.Ordinal)));
    }

    [TestCase(
        "public Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2 Get() => null!;",
        "provider SDK",
        "Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2")]
    [TestCase(
        "protected Google.Apis.Auth.OAuth2.ServiceAccountCredential Credential { get; } = null!;",
        "credential family",
        "Google.Apis.Auth.OAuth2.ServiceAccountCredential")]
    [TestCase(
        "public System.Collections.Generic.List<Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2[]> Values { get; } = [];",
        "provider SDK",
        "Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2")]
    public void Unresolved_Provider_Types_In_Exposed_Signatures_Should_Be_Rejected(
        string member,
        string category,
        string dependency)
    {
        var source = $$"""
            namespace LgymApi.Identity.Contracts.Subscriptions;
            public sealed class Exposure { {{member}} }
            """;

        Assert.That(
            ScanFixturePublicSurface(source),
            Has.Some.Matches<PublicSurfaceViolation>(violation =>
                violation.Category == category && violation.Dependency == dependency));
    }

    [Test]
    public void Unresolved_Provider_Neutral_Type_Should_Remain_Allowed()
    {
        const string source = """
            namespace LgymApi.Identity.Contracts.Subscriptions;
            public sealed record Exposure(
                Future.Contracts.NeutralReference Value,
                Neutral.Google.Apis.AndroidPublisher.Reference ProviderNamedValue);
            """;

        Assert.That(ScanFixturePublicSurface(source), Is.Empty);
    }

    [TestCase("public Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2 Value;")]
    [TestCase("protected Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2 Value;")]
    [TestCase("public event Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2 Value;")]
    [TestCase("protected event Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2 Value;")]
    public void Unresolved_Provider_Fields_And_Field_Like_Events_Should_Be_Rejected(string member)
    {
        var source = $$"""
            namespace LgymApi.Identity.Contracts.Subscriptions;
            public class Exposure { {{member}} }
            """;

        Assert.That(
            ScanFixturePublicSurface(source),
            Has.Some.Matches<PublicSurfaceViolation>(violation =>
                violation.Category == "provider SDK"
                && violation.Dependency == "Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2"));
    }

    [TestCase(
        "using Google.Apis.AndroidPublisher.v3.Data;",
        "SubscriptionPurchaseV2")]
    [TestCase(
        "using Purchase = Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2;",
        "Purchase")]
    [TestCase(
        "using Provider = Google.Apis.AndroidPublisher.v3.Data;",
        "Provider.SubscriptionPurchaseV2")]
    [TestCase(
        "global using Purchase = Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2;",
        "Purchase")]
    public void Unresolved_Imported_Or_Aliased_Provider_Types_Should_Be_Rejected(
        string usingDirective,
        string exposedType)
    {
        var source = $$"""
            {{usingDirective}}
            namespace LgymApi.Identity.Contracts.Subscriptions;
            public class Exposure { public {{exposedType}} Get() => null!; }
            """;

        Assert.That(
            ScanFixturePublicSurface(source),
            Has.Some.Matches<PublicSurfaceViolation>(violation =>
                violation.Category == "provider SDK"
                && violation.Dependency == "Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2"));
    }

    [Test]
    public void Unresolved_Delegate_Generic_Constraint_Should_Be_Rejected()
    {
        const string source = """
            namespace LgymApi.Identity.Contracts.Subscriptions;
            public delegate void Exposure<T>()
                where T : Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2;
            """;

        Assert.That(
            ScanFixturePublicSurface(source),
            Has.Some.Matches<PublicSurfaceViolation>(violation =>
                violation.Category == "provider SDK"
                && violation.Dependency == "Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2"));
    }

    [Test]
    public void Unresolved_Imported_And_Aliased_Near_Misses_Should_Remain_Allowed()
    {
        const string source = """
            using Google.Apis.AndroidPublisherFake;
            using AppleReference = Apple.AppStoreServerFake.NeutralReference;
            using NeutralGoogle = Neutral.Google.Apis.AndroidPublisher;
            namespace LgymApi.Identity.Contracts.Subscriptions;
            public sealed record Exposure(
                NeutralReference Imported,
                AppleReference Aliased,
                NeutralGoogle.Reference NamespaceAliased);
            """;

        Assert.That(ScanFixturePublicSurface(source), Is.Empty);
    }

    [Test]
    public void Canonical_Document_Should_Require_Bounded_Implementation_Owned_Provider_Timeout_And_Retry()
    {
        Assert.That(
            () => AssertProviderCallPolicy(ReadCanonicalDocument()),
            Throws.Nothing);
    }

    [Test]
    public void Canonical_Document_Should_Require_The_Complete_Future_Focused_Contract_Catalog()
    {
        Assert.That(
            () => AssertFutureFocusedContractCatalog(ReadCanonicalDocument()),
            Throws.Nothing);
    }

    [Test]
    public void Out_Of_Root_Global_Provider_Alias_Should_Be_Rejected_In_The_Owning_Project()
    {
        var violations = ScanFixtureProjectPublicSurfaces(
            ("LgymApi.Identity/GlobalUsings.cs", "global using ProviderPurchase = Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2;"),
            ("LgymApi.Identity/Contracts/Subscriptions/Exposure.cs", """
                namespace LgymApi.Identity.Contracts.Subscriptions;
                public sealed class Exposure { public ProviderPurchase Get() => null!; }
                """));

        Assert.That(
            violations,
            Has.Some.Matches<PublicSurfaceViolation>(violation =>
                violation.Category == "provider SDK"
                && violation.Dependency == "Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2"));
    }

    [Test]
    public void Out_Of_Root_Global_Neutral_Alias_Should_Remain_Allowed()
    {
        var violations = ScanFixtureProjectPublicSurfaces(
            ("LgymApi.Identity/GlobalUsings.cs", "global using NeutralPurchase = Neutral.Contracts.SubscriptionReference;"),
            ("LgymApi.Identity/Contracts/Subscriptions/Exposure.cs", """
                namespace LgymApi.Identity.Contracts.Subscriptions;
                public sealed record Exposure(NeutralPurchase Purchase);
                """));

        Assert.That(violations, Is.Empty);
    }

    [Test]
    public void Out_Of_Root_Global_Provider_Alias_Should_Not_Leak_Between_Owning_Projects()
    {
        var identityViolations = ScanFixtureProjectPublicSurfaces(
            ("LgymApi.Identity/GlobalUsings.cs", "global using ProviderPurchase = Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2;"));
        var apiViolations = ScanFixtureProjectPublicSurfaces(
            ("LgymApi.Api/Features/Account/Subscriptions/Exposure.cs", """
                namespace LgymApi.Api.Features.Account.Subscriptions;
                public sealed class Exposure { public ProviderPurchase Get() => null!; }
                """));

        Assert.That(identityViolations, Is.Empty);
        Assert.That(apiViolations, Is.Empty);
    }

    [Test]
    public void Synthetic_Document_Should_Reject_A_Missing_Provider_Call_Policy()
    {
        var markdown = CreateValidMarkdown().Replace(ProviderCallPolicy, "Provider policy is deferred.", StringComparison.Ordinal);

        Assert.That(
            () => ParseAndValidateDocument(markdown),
            Throws.InvalidOperationException.With.Message.Contains("provider-call timeout/retry policy"));
    }

    [TestCaseSource(nameof(FutureFocusedContractIdentities))]
    public void Synthetic_Document_Should_Reject_Each_Missing_Future_Focused_Identity(string identity)
    {
        var markdown = CreateValidMarkdown().Replace($"`{identity}`", "`RemovedFutureIdentity`", StringComparison.Ordinal);

        Assert.That(
            () => ParseAndValidateDocument(markdown),
            Throws.InvalidOperationException.With.Message.Contains(identity));
    }

    [TestCase(
        "provider-private sensitive handling and storage",
        "public-contract/log/analytics/response/evidence exclusion")]
    [TestCase(
        "metadata-only observability and evidence",
        "sensitive values in observability")]
    [TestCase(
        "CancellationToken is the only Hangfire batch argument",
        "Hangfire sensitive arguments")]
    public void Canonical_Security_And_Worker_Rules_Should_Be_Parser_Backed(
        string requiredRule,
        string replacement)
    {
        var markdown = CreateValidMarkdown().Replace(requiredRule, replacement, StringComparison.Ordinal);

        Assert.That(
            () => ParseAndValidateDocument(markdown),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Synthetic_Document_Should_Reject_A_Removed_Mermaid_Edge()
    {
        var markdown = CreateValidMarkdown().Replace(
            "    Identity -->|projection read| CurrentAccess\n",
            string.Empty,
            StringComparison.Ordinal);

        Assert.That(
            () => ParseAndValidateDocument(markdown),
            Throws.InvalidOperationException.With.Message.Contains("Identity -> CurrentAccess"));
    }

    [Test]
    public void Unrelated_Existing_Markdown_Should_Not_Satisfy_The_Contract()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var markdown = File.ReadAllText(Path.Combine(repositoryRoot, "README.md"));

        Assert.That(
            () => ParseAndValidateDocument(markdown),
            Throws.InvalidOperationException.With.Message.Contains("Stable table 'Boundary ID' must appear exactly once; found 0"));
    }

    [Test]
    public void Stable_Table_Should_Reject_An_Extra_Row_Outside_The_Approved_Prefix()
    {
        var markdown = CreateValidMarkdown().Replace(
            "| `subscriptions.boundary.project-graph`",
            "| `subscriptions.unapproved-row` | future | Fixture | Fixture | Fixture | Fixture | Fixture |\n| `subscriptions.boundary.project-graph`",
            StringComparison.Ordinal);

        Assert.That(
            () => ParseAndValidateDocument(markdown),
            Throws.InvalidOperationException.With.Message.Contains("unapproved stable row 'subscriptions.unapproved-row'"));
    }

    [Test]
    public void Stable_Row_Should_Reject_An_Unapproved_State()
    {
        var markdown = CreateValidMarkdown().Replace(
            "| `subscriptions.contract.grant` | future |",
            "| `subscriptions.contract.grant` | current |",
            StringComparison.Ordinal);

        Assert.That(
            () => ParseAndValidateDocument(markdown),
            Throws.InvalidOperationException.With.Message.Contains("subscriptions.contract.grant").And.Message.Contains("State"));
    }

    [Test]
    public void Configuration_Row_Should_Reject_Contradictory_Enable_Semantics()
    {
        var markdown = CreateValidMarkdown().Replace(
            "new client purchase verification | does not disable restore or lifecycle repair",
            "restore and lifecycle repair | disables new client purchase verification",
            StringComparison.Ordinal);

        Assert.That(
            () => ParseAndValidateDocument(markdown),
            Throws.InvalidOperationException.With.Message.Contains("subscriptions.configuration.purchases-enabled").And.Message.Contains("Enables"));
    }

    [Test]
    public void Mermaid_Should_Reject_An_Extra_Logical_Edge()
    {
        var markdown = CreateValidMarkdown().Replace(
            "    Identity -->|projection read| CurrentAccess\n",
            "    Identity -->|projection read| CurrentAccess\n    API -->|unapproved| PostgreSQL\n",
            StringComparison.Ordinal);

        Assert.That(
            () => ParseAndValidateDocument(markdown),
            Throws.InvalidOperationException.With.Message.Contains("Unexpected Mermaid edge 'API -> PostgreSQL'"));
    }

    [Test]
    public void Neutral_Apple_And_Google_Named_Contracts_Should_Be_Allowed()
    {
        const string source = """
            namespace LgymApi.Identity.Contracts.Subscriptions;
            public sealed record ApplePurchaseReference(string Value);
            public sealed record GoogleAccountBinding(string Value);
            public sealed record AppleDisplayOptions(string Locale);
            """;

        Assert.That(ScanFixturePublicSurface(source), Is.Empty);
    }

    [TestCase("Google.Apis.Auth.OAuth2", "ServiceAccountCredential", "credential family")]
    [TestCase("Apple.AppStoreServer.Library", "JWSTransactionDecodedPayload", "provider SDK")]
    [TestCase("LgymApi.Identity.Subscriptions.Providers.Apple", "AppleAdapter", "provider implementation namespace")]
    [TestCase("LgymApi.Identity.Contracts.Subscriptions", "RawProviderPayload", "provider raw wrapper")]
    public void Public_Surface_Should_Reject_Each_Explicit_Provider_Family(
        string dependencyNamespace,
        string dependencyType,
        string category)
    {
        var source = $$"""
            namespace {{dependencyNamespace}} { public sealed class {{dependencyType}} { } }
            namespace LgymApi.Identity.Contracts.Subscriptions
            {
                public sealed record Exposure({{dependencyNamespace}}.{{dependencyType}} Value);
            }
            """;

        Assert.That(
            ScanFixturePublicSurface(source),
            Has.Some.Matches<PublicSurfaceViolation>(violation =>
                violation.Category == category && violation.Dependency.Contains(dependencyType, StringComparison.Ordinal)));
    }

    [TestCase("Field", "public Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2 Value;")]
    [TestCase("Property", "public Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2 Value { get; init; }")]
    [TestCase("Parameter", "public void Use(Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2 value) { }")]
    [TestCase("Return", "public Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2 Get() => null!;")]
    [TestCase("Base", "", " : Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2")]
    [TestCase("NestedGeneric", "public System.Collections.Generic.List<Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2[]> Values { get; } = [];")]
    [TestCase("Indexer", "public string this[Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2 value] => string.Empty;")]
    public void Public_Surface_Should_Recursively_Reject_Provider_Types(
        string caseName,
        string member,
        string baseClause = "")
    {
        var source = $$"""
            namespace Google.Apis.AndroidPublisher.v3.Data { public class SubscriptionPurchaseV2 { } }
            namespace LgymApi.Identity.Contracts.Subscriptions
            {
                public sealed class Exposure{{baseClause}} { {{member}} }
            }
            """;

        Assert.That(
            ScanFixturePublicSurface(source),
            Has.Some.Matches<PublicSurfaceViolation>(violation => violation.Category == "provider SDK"),
            caseName);
    }

    [Test]
    public void Delegate_Signature_Should_Reject_Provider_Return_And_Parameter_Types()
    {
        const string source = """
            namespace Google.Apis.AndroidPublisher.v3.Data { public sealed class SubscriptionPurchaseV2 { } }
            namespace Google.Apis.Auth.OAuth2 { public sealed class ServiceAccountCredential { } }
            namespace LgymApi.Identity.Contracts.Subscriptions
            {
                public delegate Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchaseV2 Exposure(
                    Google.Apis.Auth.OAuth2.ServiceAccountCredential credential);
            }
            """;

        var categories = ScanFixturePublicSurface(source).Select(violation => violation.Category);
        Assert.That(categories, Does.Contain("provider SDK"));
        Assert.That(categories, Does.Contain("credential family"));
    }

    [Test]
    public void Near_Miss_Provider_Namespaces_Should_Remain_Allowed()
    {
        const string source = """
            namespace Google.Apis.AndroidPublisherFake { public sealed class NeutralReference { } }
            namespace Apple.AppStoreServerFake { public sealed class NeutralReference { } }
            namespace LgymApi.Identity.Contracts.Subscriptions
            {
                public sealed record Exposure(
                    Google.Apis.AndroidPublisherFake.NeutralReference Google,
                    Apple.AppStoreServerFake.NeutralReference Apple);
            }
            """;

        Assert.That(ScanFixturePublicSurface(source), Is.Empty);
    }

    [TestCase("RawProviderPayload", "provider raw wrapper")]
    [TestCase("PurchaseToken", "provider raw wrapper")]
    public void Standalone_Forbidden_Public_Declaration_Should_Be_Rejected(string typeName, string category)
    {
        var source = $$"""
            namespace LgymApi.Identity.Contracts.Subscriptions;
            public sealed record {{typeName}}(string Value);
            """;

        Assert.That(
            ScanFixturePublicSurface(source),
            Has.Some.Matches<PublicSurfaceViolation>(violation =>
                violation.Category == category && violation.Dependency.EndsWith(typeName, StringComparison.Ordinal)));
    }

    [TestCase("Google.Apis.AndroidPublisher.v3.Data", "SubscriptionPurchaseV2", "provider SDK")]
    [TestCase("Google.Apis.Auth.OAuth2", "ServiceAccountCredential", "credential family")]
    public void Standalone_Sdk_Or_Credential_Declaration_Should_Be_Rejected(
        string declarationNamespace,
        string typeName,
        string category)
    {
        var source = $$"""
            namespace {{declarationNamespace}};
            public sealed class {{typeName}} { }
            """;

        Assert.That(
            ScanFixturePublicSurface(source),
            Has.Some.Matches<PublicSurfaceViolation>(violation =>
                violation.Category == category && violation.Dependency.EndsWith(typeName, StringComparison.Ordinal)));
    }

    [Test]
    public void Provider_Implementation_Root_And_Descendant_Declarations_Should_Be_Rejected()
    {
        const string rootSource = """
            namespace LgymApi.Identity.Subscriptions.Providers;
            public sealed class PublicProviderRoot { }
            """;
        const string descendantSource = """
            namespace LgymApi.Identity.Subscriptions.Providers.Apple.Internal;
            public sealed class PublicAppleAdapter { }
            """;

        Assert.That(
            ScanFixturePublicSurface(rootSource),
            Has.Some.Matches<PublicSurfaceViolation>(violation => violation.Category == "provider implementation namespace"));
        Assert.That(
            ScanFixturePublicSurface(descendantSource),
            Has.Some.Matches<PublicSurfaceViolation>(violation => violation.Category == "provider implementation namespace"));
    }

    [Test]
    public void Unapproved_Subscription_Id_In_A_Foreign_Table_Should_Be_Rejected()
    {
        var markdown = CreateValidMarkdown() + """

            | Note | Value |
            | --- | --- |
            | `subscriptions.unapproved.foreign-table` | fixture |
            """;

        Assert.That(
            () => ParseAndValidateDocument(markdown),
            Throws.InvalidOperationException.With.Message.Contains("subscriptions.unapproved.foreign-table").And.Message.Contains("unapproved table"));
    }

    [Test]
    public void Mermaid_Should_Reject_Standalone_Node_And_Alternative_Connector_Statements()
    {
        var standalone = CreateValidMarkdown().Replace(
            "flowchart LR\n",
            "flowchart LR\n    RogueNode[Unapproved]\n",
            StringComparison.Ordinal);
        var alternativeConnector = CreateValidMarkdown().Replace(
            "Client -->|authenticated purchase/query| API",
            "Client ---|authenticated purchase/query| API",
            StringComparison.Ordinal);

        Assert.That(
            () => ParseAndValidateDocument(standalone),
            Throws.InvalidOperationException.With.Message.Contains("RogueNode").And.Message.Contains("standalone Mermaid node"));
        Assert.That(
            () => ParseAndValidateDocument(alternativeConnector),
            Throws.InvalidOperationException.With.Message.Contains("unsupported Mermaid statement"));
    }

    [Test]
    public void Mermaid_Should_Require_The_Structured_Future_Project_Graph_Marker()
    {
        var markdown = CreateValidMarkdown().Replace(
            "%% subscriptions-graph: future-state logical flow; project-graph: existing %%\n",
            string.Empty,
            StringComparison.Ordinal) + "\nFuture-state logical flow uses the existing project graph.";

        Assert.That(
            () => ParseAndValidateDocument(markdown),
            Throws.InvalidOperationException.With.Message.Contains("structured future-state/project-graph marker"));
    }

    [Test]
    public void Mermaid_Fixture_Mutations_Should_Reject_Windows_Line_Endings()
    {
        var markdown = CreateValidMarkdown()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal)
            .Replace(
                "    Identity -->|projection read| CurrentAccess\r\n",
                string.Empty,
                StringComparison.Ordinal);

        Assert.That(
            () => ParseAndValidateDocument(markdown),
            Throws.InvalidOperationException.With.Message.Contains("Identity -> CurrentAccess"));
    }

    private static SubscriptionDocument ParseAndValidateDocument(string markdown)
    {
        var tables = ParseTables(markdown);
        var stableTables = new List<DocumentationTable>();
        foreach (var contract in TableContracts)
        {
            var matchingTables = tables
                .Where(table => table.Columns.SequenceEqual(contract.Columns, StringComparer.Ordinal))
                .ToList();
            if (matchingTables.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Stable table '{contract.Columns[0]}' must appear exactly once; found {matchingTables.Count}.");
            }

            var table = matchingTables[0];
            if (!table.Columns.SequenceEqual(contract.Columns, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Stable table '{contract.Columns[0]}' columns drifted. Expected: {string.Join(" | ", contract.Columns)}.");
            }

            RequireExactIds(table.Rows, contract);
            stableTables.Add(table);
        }

        var approvedTables = stableTables.ToHashSet();
        var misplacedSubscriptionId = tables
            .Where(table => !approvedTables.Contains(table))
            .SelectMany(table => table.Rows)
            .SelectMany(row => row.Fields.Values)
            .FirstOrDefault(value => value.StartsWith("subscriptions.", StringComparison.Ordinal));
        if (misplacedSubscriptionId != null)
        {
            throw new InvalidOperationException(
                $"Subscription ID '{misplacedSubscriptionId}' appears in an unapproved table schema.");
        }

        var rows = stableTables.SelectMany(table => table.Rows).ToList();
        var duplicateIds = rows.GroupBy(row => row.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (duplicateIds.Count > 0)
        {
            throw new InvalidOperationException($"Duplicate global stable row IDs: {string.Join(", ", duplicateIds)}.");
        }

        foreach (var row in rows)
        {
            foreach (var field in row.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Value))
                {
                    throw new InvalidOperationException($"Stable row '{row.Id}' has a blank '{field.Key}' cell.");
                }
            }
        }

        var rowsById = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        AssertExactApprovedRows(rowsById);
        AssertProviderAuthorities(rowsById);
        AssertProviderCallPolicy(markdown);
        AssertFutureFocusedContractCatalog(markdown);
        AssertMermaidContract(markdown);
        return new SubscriptionDocument(rowsById);
    }

    private static string ReadCanonicalDocument()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        return File.ReadAllText(Path.Combine(repositoryRoot, DocumentPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static void AssertProviderCallPolicy(string markdown)
    {
        var rows = ParseTables(markdown)
            .SelectMany(table => table.Rows)
            .ToDictionary(row => row.Id, StringComparer.Ordinal);
        foreach (var id in ProviderCallPolicyRowIds)
        {
            RequireContains(
                rows,
                id,
                "Verification/retry rule",
                "bounded, implementation-owned timeout",
                "bounded, implementation-owned retry policy");
        }

        RequireExactOccurrence(markdown, ProviderCallPolicy, "provider-call timeout/retry policy");
    }

    private static void AssertFutureFocusedContractCatalog(string markdown)
    {
        foreach (var identity in FutureFocusedContractIdentities)
        {
            RequireExactOccurrence(markdown, $"`{identity}`", $"future focused identity '{identity}'");
        }
    }

    private static IReadOnlyList<DocumentationTable> ParseTables(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var tables = new List<DocumentationTable>();
        for (var index = 0; index < lines.Length - 1; index++)
        {
            var headers = ParseCells(lines[index]);
            if (headers.Count == 0 || !IsSeparator(lines[index + 1], headers.Count))
            {
                continue;
            }

            var rows = new List<DocumentationRow>();
            for (index += 2; index < lines.Length; index++)
            {
                var cells = ParseCells(lines[index]);
                if (cells.Count == 0)
                {
                    index--;
                    break;
                }

                if (cells.Count != headers.Count)
                {
                    throw new InvalidOperationException(
                        $"Stable documentation table '{headers[0]}' has {cells.Count} cells; expected {headers.Count}.");
                }

                var values = cells.Select(UnwrapCode).ToArray();
                rows.Add(new DocumentationRow(
                    values[0],
                    headers.Zip(values).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal)));
            }

            tables.Add(new DocumentationTable(headers, rows));
        }

        return tables;
    }

    private static void RequireExactIds(IEnumerable<DocumentationRow> rows, TableContract contract)
    {
        var actual = rows.ToList();
        var duplicates = actual.GroupBy(row => row.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException($"Duplicate stable row IDs: {string.Join(", ", duplicates)}.");
        }

        var expectedIds = contract.Ids.ToHashSet(StringComparer.Ordinal);
        var actualIds = actual.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        var missing = expectedIds.Except(actualIds).OrderBy(id => id, StringComparer.Ordinal).ToList();
        var unknown = actualIds.Except(expectedIds).OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (missing.Count > 0 || unknown.Count > 0)
        {
            if (unknown.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Stable table '{contract.Columns[0]}' contains unapproved stable row '{unknown[0]}'.");
            }

            throw new InvalidOperationException(
                $"Stable row IDs drifted for '{contract.Prefix}'. Missing stable row IDs: {Format(missing)}. " +
                $"Unknown stable row IDs: {Format(unknown)}.");
        }
    }

    private static void AssertExactApprovedRows(IReadOnlyDictionary<string, DocumentationRow> rows)
    {
        var expectedRows = ParseTables(CreateValidMarkdown())
            .SelectMany(table => table.Rows)
            .ToDictionary(row => row.Id, StringComparer.Ordinal);
        foreach (var expected in expectedRows)
        {
            foreach (var field in expected.Value.Fields)
            {
                RequireExactField(rows, expected.Key, field.Key, field.Value);
            }
        }
    }

    private static void AssertProviderAuthorities(IReadOnlyDictionary<string, DocumentationRow> rows)
    {
        RequireContains(rows, "subscriptions.provider.apple-production", "Fixed authority/trust input", "https://api.storekit.apple.com");
        RequireContains(rows, "subscriptions.provider.apple-sandbox", "Fixed authority/trust input", "https://api.storekit-sandbox.apple.com");
        RequireContains(rows, "subscriptions.provider.apple-signed-data", "Fixed authority/trust input", "JWS", "certificate", "app", "environment", "account binding");
        RequireContains(rows, "subscriptions.provider.google-play", "Fixed authority/trust input", "https://androidpublisher.googleapis.com", "purchases.subscriptionsv2.get");
        RequireContains(rows, "subscriptions.provider.google-rtdn", "Fixed authority/trust input", "Pub/Sub OIDC", "provider re-query", "signature", "issuer", "audience", "expiry", "email_verified");
        RequireContains(rows, "subscriptions.provider.sanitized-errors", "Fixed authority/trust input", "authentication", "validation", "throttled", "transient", "unavailable");

        var authorities = rows.Values
            .Where(row => row.Id.StartsWith("subscriptions.provider.", StringComparison.Ordinal))
            .SelectMany(row => ExtractHttpsAuthorities(row.GetField("Fixed authority/trust input")))
            .ToList();
        AssertExactSet(authorities,
        [
            "https://api.storekit.apple.com",
            "https://api.storekit-sandbox.apple.com",
            "https://androidpublisher.googleapis.com"
        ], "provider authorities");

    }

    private static void AssertMermaidContract(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var mermaidBlocks = ExtractFencedBlocks(normalized, "mermaid").ToList();
        if (mermaidBlocks.Count != 1)
        {
            throw new InvalidOperationException($"Subscription boundary must contain exactly one Mermaid graph; found {mermaidBlocks.Count}.");
        }

        var diagram = ParseDiagram(mermaidBlocks[0]);
        if (diagram.Marker != "subscriptions-graph: future-state logical flow; project-graph: existing")
        {
            throw new InvalidOperationException("Mermaid contract requires the structured future-state/project-graph marker.");
        }

        if (diagram.Declaration != "flowchart LR")
        {
            throw new InvalidOperationException($"Mermaid graph declaration must be exactly 'flowchart LR', but was '{diagram.Declaration}'.");
        }

        var actualEdges = diagram.Edges;
        var duplicateEdges = actualEdges.GroupBy(edge => (edge.Source, edge.Target))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Source} -> {group.Key.Target}")
            .ToList();
        if (duplicateEdges.Count > 0)
        {
            throw new InvalidOperationException($"Duplicate Mermaid edges: {string.Join(", ", duplicateEdges)}.");
        }

        var expectedEdges = RequiredDiagramEdges.ToHashSet();
        var observedEdges = actualEdges.ToHashSet();
        var unexpected = observedEdges.Except(expectedEdges).FirstOrDefault();
        if (unexpected != null)
        {
            throw new InvalidOperationException(
                $"Unexpected Mermaid edge '{unexpected.Source} -> {unexpected.Target}' with label '{unexpected.LabelToken}'.");
        }

        var missing = expectedEdges.Except(observedEdges).FirstOrDefault();
        if (missing != null)
        {
            throw new InvalidOperationException(
                $"Mermaid graph is missing required logical edge '{missing.Source} -> {missing.Target}' with label '{missing.LabelToken}'.");
        }

        var expectedNodes = RequiredDiagramEdges.SelectMany(edge => new[] { edge.Source, edge.Target }).ToHashSet(StringComparer.Ordinal);
        var observedNodes = actualEdges.SelectMany(edge => new[] { edge.Source, edge.Target }).ToHashSet(StringComparer.Ordinal);
        AssertExactSet(observedNodes, expectedNodes, "Mermaid logical nodes");
    }

    private static void AssertCurrentExecutableAuthorities(
        string repositoryRoot,
        IReadOnlyDictionary<string, DocumentationRow> rows)
    {
        var owners = PersistedEntityOwnershipCatalog.Entries
            .GroupBy(entry => entry.Owner, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Assert.That(owners.Keys, Is.EquivalentTo(PersistedEntityOwnershipCatalog.CanonicalOwners));
        RequireContainsOneOf(
            rows,
            "subscriptions.boundary.current-state",
            "Owner / authority",
            $"{owners.Count}-owner",
            "eight-owner");
        RequireContains(rows, "subscriptions.boundary.current-state", "Owner / authority",
            $"{PersistedEntityOwnershipCatalog.Entries.Count}-entity");

        var solutionProjects = File.ReadLines(Path.Combine(repositoryRoot, "LgymApi.sln"))
            .Where(line => line.StartsWith("Project(", StringComparison.Ordinal))
            .Select(line => line.Split('"'))
            .Where(parts => parts.Length > 5 && parts[5].EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(parts => Path.GetFullPath(Path.Combine(repositoryRoot, ArchitectureTestHelpers.ToHostPath(parts[5]))))
            .ToList();
        var edges = solutionProjects.SelectMany(ArchitectureTestHelpers.ParseProjectReferences).ToList();
        Assert.That(solutionProjects.Select(Path.GetFileNameWithoutExtension), Is.EquivalentTo(ProjectReferenceGraphManifest.ProjectNames));
        Assert.That(edges.Select(edge => $"{edge.SourceProject} -> {edge.TargetProject}"), Is.EquivalentTo(ProjectReferenceGraphManifest.EdgeIdentities));
        RequireContains(rows, "subscriptions.boundary.project-graph", "Owner / authority",
            $"{solutionProjects.Count}-project", $"{edges.Count}-edge");

        var topology = PersistenceTopologyGuardTestHelpers.Analyze(
            PersistenceTopologyGuardTestHelpers.LoadProductionSources(repositoryRoot));
        PersistenceTopologyGuardTestHelpers.EnsureSingleDbContext(
            topology, PersistenceIdentityContract.DbContextTypeName, PersistenceIdentityContract.DbContextSourcePath);
        PersistenceTopologyGuardTestHelpers.EnsureSingleMigrationRoot(topology, PersistenceIdentityContract.MigrationRoot);
        Assert.That(topology.DbSets, Has.Count.EqualTo(PersistedEntityOwnershipCatalog.Entries.Count));

        new BackgroundWorkerCommonSurfaceGuardTests().Repository_Common_Surface_Matches_The_Exact_Manifest();
        var commonSubscriptionPaths = ArchitectureTestHelpers.EnumerateProjectSourceFiles("LgymApi.BackgroundWorker.Common")
            .Select(path => ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repositoryRoot, path)))
            .Where(path => path.Contains("Subscription", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.That(commonSubscriptionPaths, Is.Empty, "The exact Common closure must not contain subscription paths.");
    }

    private static void AssertSourceLocators(
        string repositoryRoot,
        IReadOnlyDictionary<string, DocumentationRow> rows)
    {
        foreach (var expected in BoundaryLocators)
        {
            RequireExactField(rows, expected.Key, "Source locator", expected.Value);
            PlatformReferenceDataBoundaryDocumentationTestHelpers.AssertLocatorResolves(repositoryRoot, expected.Value);
        }
    }

    private static IReadOnlyList<PublicSurfaceViolation> ScanRepositoryPublicSurfaces()
    {
        return PublicSurfaceOwnerProjects
            .SelectMany(project => ScanOwningProjectPublicSurfaces(ArchitectureTestHelpers.ParseProjectSources(project)))
            .OrderBy(violation => violation.Identity, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<PublicSurfaceViolation> ScanFixturePublicSurface(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "LgymApi.Identity/Contracts/Subscriptions/Fixture.cs");
        return ScanPublicSurfaces(ArchitectureTestHelpers.CreateCompilation([tree]), [tree]);
    }

    private static IReadOnlyList<PublicSurfaceViolation> ScanFixtureProjectPublicSurfaces(
        params (string Path, string Source)[] sources)
    {
        var trees = sources
            .Select(source => (SyntaxTree)CSharpSyntaxTree.ParseText(source.Source, path: source.Path))
            .ToList();
        return ScanOwningProjectPublicSurfaces(trees);
    }

    private static IReadOnlyList<PublicSurfaceViolation> ScanOwningProjectPublicSurfaces(IEnumerable<SyntaxTree> syntaxTrees)
    {
        var trees = syntaxTrees.ToList();
        return ScanPublicSurfaces(ArchitectureTestHelpers.CreateCompilation(trees), trees);
    }

    private static IReadOnlyList<PublicSurfaceViolation> ScanPublicSurfaces(
        CSharpCompilation compilation,
        IEnumerable<SyntaxTree> syntaxTrees)
    {
        var trees = syntaxTrees.ToList();
        var globalUsings = trees
            .SelectMany(candidateTree => candidateTree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>())
            .Where(usingDirective => usingDirective.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword))
            .ToList();
        var violations = new Dictionary<string, PublicSurfaceViolation>(StringComparer.Ordinal);
        foreach (var tree in trees.Where(tree => IsPublicSurfaceRoot(tree.FilePath)))
        {
            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MemberDeclarationSyntax>()
                         .Where(declaration => declaration is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
            {
                if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type || !IsEffectivelyPublic(type))
                {
                    continue;
                }

                if (TryClassifyForbiddenType(type, out var declarationCategory))
                {
                    AddViolation(
                        violations,
                        tree,
                        type,
                        declarationCategory,
                        type.ToDisplayString());
                }

                foreach (var exposedType in GetDeclaredTypeDependencies(type)
                             .Concat(type.GetMembers().Where(IsPublicSurfaceMember).SelectMany(GetMemberTypes)))
                {
                    foreach (var namedType in EnumerateNamedTypes(exposedType))
                    {
                        if (TryClassifyForbiddenType(namedType, out var category))
                        {
                            AddViolation(violations, tree, type, category, namedType.ToDisplayString());
                        }
                    }
                }

                foreach (var exposedSyntax in GetExposedTypeSyntax(type))
                {
                    foreach (var unresolvedSyntax in exposedSyntax.DescendantNodesAndSelf().OfType<TypeSyntax>()
                                 .Where(candidate => model.GetTypeInfo(candidate).Type is null or IErrorTypeSymbol))
                    {
                        if (TryClassifyForbiddenUnresolvedType(
                                unresolvedSyntax,
                                GetApplicableUsings(unresolvedSyntax, globalUsings),
                                out var category,
                                out var dependency))
                        {
                            AddViolation(violations, tree, type, category, dependency);
                        }
                    }
                }
            }
        }

        return violations.Values.OrderBy(violation => violation.Identity, StringComparer.Ordinal).ToList();
    }

    private static void AddViolation(
        IDictionary<string, PublicSurfaceViolation> violations,
        SyntaxTree tree,
        INamedTypeSymbol type,
        string category,
        string dependency)
    {
        var violation = new PublicSurfaceViolation(
            ArchitectureTestHelpers.NormalizePath(tree.FilePath),
            type.ToDisplayString(),
            category,
            dependency);
        violations.TryAdd(violation.Identity, violation);
    }

    private static bool TryClassifyForbiddenType(INamedTypeSymbol type, out string category)
    {
        var namespaceName = type.ContainingNamespace.ToDisplayString();
        if (IsNamespace(namespaceName, "Google.Apis.AndroidPublisher")
            || IsNamespace(namespaceName, "Google.Apis.Auth.OAuth2")
            || IsNamespace(namespaceName, "Apple.AppStoreServer"))
        {
            category = IsNamespace(namespaceName, "Google.Apis.Auth.OAuth2")
                ? "credential family"
                : "provider SDK";
            return true;
        }

        if (IsProviderImplementationNamespace(namespaceName))
        {
            category = "provider implementation namespace";
            return true;
        }

        if (ExplicitRawProviderWrapperNames.Contains(type.Name))
        {
            category = "provider raw wrapper";
            return true;
        }

        category = string.Empty;
        return false;
    }

    private static bool TryClassifyForbiddenUnresolvedType(
        TypeSyntax type,
        IEnumerable<UsingDirectiveSyntax> usingDirectives,
        out string category,
        out string dependency)
    {
        var typeName = type.ToString().Replace("global::", string.Empty, StringComparison.Ordinal);
        var candidates = new List<string> { typeName };
        foreach (var usingDirective in usingDirectives)
        {
            var importedName = usingDirective.Name?.ToString().Replace("global::", string.Empty, StringComparison.Ordinal);
            if (string.IsNullOrEmpty(importedName))
            {
                continue;
            }

            if (usingDirective.Alias == null)
            {
                candidates.Add($"{importedName}.{typeName}");
                continue;
            }

            var alias = usingDirective.Alias.Name.Identifier.ValueText;
            if (typeName.Equals(alias, StringComparison.Ordinal))
            {
                candidates.Add(importedName);
            }
            else if (typeName.StartsWith($"{alias}.", StringComparison.Ordinal))
            {
                candidates.Add(importedName + typeName[alias.Length..]);
            }
        }

        (string Namespace, string Category)[] forbiddenNamespaces =
        [
            ("Google.Apis.Auth.OAuth2", "credential family"),
            ("Google.Apis.AndroidPublisher", "provider SDK"),
            ("Apple.AppStoreServer", "provider SDK")
        ];
        foreach (var qualifiedType in candidates)
        {
            foreach (var forbidden in forbiddenNamespaces)
            {
                var start = qualifiedType.IndexOf($"{forbidden.Namespace}.", StringComparison.Ordinal);
                if (start < 0 || start > 0 && qualifiedType[start - 1] is not ('<' or '(' or '[' or ',' or ' '))
                {
                    continue;
                }

                var end = qualifiedType.IndexOfAny(['<', '>', '[', ']', ',', '?'], start);
                category = forbidden.Category;
                dependency = end < 0 ? qualifiedType[start..] : qualifiedType[start..end];
                return true;
            }
        }

        category = string.Empty;
        dependency = string.Empty;
        return false;
    }

    private static IEnumerable<UsingDirectiveSyntax> GetApplicableUsings(
        TypeSyntax type,
        IEnumerable<UsingDirectiveSyntax> globalUsings)
    {
        foreach (var globalUsing in globalUsings)
        {
            yield return globalUsing;
        }

        foreach (var usingDirective in type.SyntaxTree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>()
                     .Where(usingDirective => !usingDirective.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)))
        {
            if (usingDirective.Parent is CompilationUnitSyntax
                || usingDirective.Parent is BaseNamespaceDeclarationSyntax namespaceDeclaration
                && namespaceDeclaration.Span.Contains(type.Span))
            {
                yield return usingDirective;
            }
        }
    }

    private static bool IsNamespace(string actual, string expected)
        => actual.Equals(expected, StringComparison.Ordinal)
            || actual.StartsWith($"{expected}.", StringComparison.Ordinal);

    private static bool IsProviderImplementationNamespace(string namespaceName)
    {
        var segments = namespaceName.Split('.');
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index].Equals("Subscriptions", StringComparison.Ordinal)
                && segments[index + 1].Equals("Providers", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<ITypeSymbol> GetDeclaredTypeDependencies(INamedTypeSymbol type)
    {
        if (type.BaseType != null)
        {
            yield return type.BaseType;
        }

        foreach (var @interface in type.Interfaces)
        {
            yield return @interface;
        }

        foreach (var typeParameter in type.TypeParameters)
        {
            foreach (var constraint in typeParameter.ConstraintTypes)
            {
                yield return constraint;
            }
        }

        if (type.TypeKind == TypeKind.Delegate && type.DelegateInvokeMethod != null)
        {
            foreach (var dependency in GetMemberTypes(type.DelegateInvokeMethod))
            {
                yield return dependency;
            }
        }
    }

    private static IEnumerable<ITypeSymbol> GetMemberTypes(ISymbol member)
    {
        switch (member)
        {
            case IFieldSymbol field:
                yield return field.Type;
                break;
            case IPropertySymbol property:
                yield return property.Type;
                foreach (var parameter in property.Parameters)
                {
                    yield return parameter.Type;
                }
                break;
            case IEventSymbol @event:
                yield return @event.Type;
                break;
            case IMethodSymbol method:
                yield return method.ReturnType;
                foreach (var parameter in method.Parameters)
                {
                    yield return parameter.Type;
                }

                foreach (var typeParameter in method.TypeParameters)
                {
                    foreach (var constraint in typeParameter.ConstraintTypes)
                    {
                        yield return constraint;
                    }
                }

                break;
        }
    }

    private static IEnumerable<TypeSyntax> GetExposedTypeSyntax(INamedTypeSymbol type)
    {
        foreach (var syntaxReference in type.DeclaringSyntaxReferences)
        {
            foreach (var syntax in GetSignatureTypeSyntax(syntaxReference.GetSyntax()))
            {
                yield return syntax;
            }
        }

        foreach (var member in type.GetMembers().Where(IsPublicSurfaceMember))
        {
            foreach (var syntaxReference in member.DeclaringSyntaxReferences)
            {
                var declaration = syntaxReference.GetSyntax();
                if (declaration is VariableDeclaratorSyntax variable
                    && variable.FirstAncestorOrSelf<BaseFieldDeclarationSyntax>() is { } field)
                {
                    declaration = field;
                }

                foreach (var syntax in GetSignatureTypeSyntax(declaration))
                {
                    yield return syntax;
                }
            }
        }
    }

    private static IEnumerable<TypeSyntax> GetSignatureTypeSyntax(SyntaxNode declaration)
    {
        switch (declaration)
        {
            case BaseTypeDeclarationSyntax type:
                foreach (var baseType in type.BaseList?.Types ?? [])
                {
                    yield return baseType.Type;
                }

                if (type is TypeDeclarationSyntax typeDeclaration)
                {
                    foreach (var constraint in typeDeclaration.ConstraintClauses.SelectMany(clause => clause.Constraints).OfType<TypeConstraintSyntax>())
                    {
                        yield return constraint.Type;
                    }

                    if (typeDeclaration.ParameterList != null)
                    {
                        foreach (var parameterType in typeDeclaration.ParameterList.Parameters.Select(parameter => parameter.Type).OfType<TypeSyntax>())
                        {
                            yield return parameterType;
                        }
                    }
                }

                break;
            case DelegateDeclarationSyntax @delegate:
                yield return @delegate.ReturnType;
                foreach (var parameterType in @delegate.ParameterList.Parameters.Select(parameter => parameter.Type).OfType<TypeSyntax>())
                {
                    yield return parameterType;
                }

                foreach (var constraint in @delegate.ConstraintClauses.SelectMany(clause => clause.Constraints).OfType<TypeConstraintSyntax>())
                {
                    yield return constraint.Type;
                }

                break;
            case FieldDeclarationSyntax field:
                yield return field.Declaration.Type;
                break;
            case EventFieldDeclarationSyntax eventField:
                yield return eventField.Declaration.Type;
                break;
            case PropertyDeclarationSyntax property:
                yield return property.Type;
                break;
            case IndexerDeclarationSyntax indexer:
                yield return indexer.Type;
                foreach (var parameterType in indexer.ParameterList.Parameters.Select(parameter => parameter.Type).OfType<TypeSyntax>())
                {
                    yield return parameterType;
                }

                break;
            case EventDeclarationSyntax @event:
                yield return @event.Type;
                break;
            case MethodDeclarationSyntax method:
                yield return method.ReturnType;
                foreach (var parameterType in method.ParameterList.Parameters.Select(parameter => parameter.Type).OfType<TypeSyntax>())
                {
                    yield return parameterType;
                }

                foreach (var constraint in method.ConstraintClauses.SelectMany(clause => clause.Constraints).OfType<TypeConstraintSyntax>())
                {
                    yield return constraint.Type;
                }

                break;
            case ConstructorDeclarationSyntax constructor:
                foreach (var parameterType in constructor.ParameterList.Parameters.Select(parameter => parameter.Type).OfType<TypeSyntax>())
                {
                    yield return parameterType;
                }

                break;
            case OperatorDeclarationSyntax @operator:
                yield return @operator.ReturnType;
                foreach (var parameterType in @operator.ParameterList.Parameters.Select(parameter => parameter.Type).OfType<TypeSyntax>())
                {
                    yield return parameterType;
                }

                break;
            case ConversionOperatorDeclarationSyntax conversion:
                yield return conversion.Type;
                foreach (var parameterType in conversion.ParameterList.Parameters.Select(parameter => parameter.Type).OfType<TypeSyntax>())
                {
                    yield return parameterType;
                }

                break;
            case ParameterSyntax { Type: not null } parameter:
                yield return parameter.Type;
                break;
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol namedType)
        {
            yield return namedType;
            if (GetMetadataName(namedType.OriginalDefinition) == "LgymApi.Domain.ValueObjects.Id`1")
            {
                yield break;
            }

            foreach (var argument in namedType.TypeArguments)
            {
                foreach (var nested in EnumerateNamedTypes(argument))
                {
                    yield return nested;
                }
            }
        }

        if (type is IArrayTypeSymbol array)
        {
            foreach (var nested in EnumerateNamedTypes(array.ElementType))
            {
                yield return nested;
            }
        }
    }

    private static bool IsPublicSurfaceRoot(string path)
    {
        var normalized = ArchitectureTestHelpers.NormalizePath(path);
        return PublicSurfaceRoots.Any(root => normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"/{root}", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsEffectivelyPublic(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPublicSurfaceMember(ISymbol member)
    {
        if (member is INamedTypeSymbol || member.IsImplicitlyDeclared && member is not IMethodSymbol)
        {
            return false;
        }

        if (member is IMethodSymbol { AssociatedSymbol: not null })
        {
            return false;
        }

        return member.DeclaredAccessibility is Accessibility.Public
            or Accessibility.Protected
            or Accessibility.ProtectedOrInternal;
    }

    private static string GetMetadataName(INamedTypeSymbol type)
    {
        var names = new Stack<string>();
        for (var current = type; current != null; current = current.ContainingType)
        {
            names.Push(current.MetadataName);
        }

        var namespaceName = type.ContainingNamespace.ToDisplayString();
        return string.IsNullOrEmpty(namespaceName) ? string.Join('.', names) : $"{namespaceName}.{string.Join('.', names)}";
    }

    private static void RequireExactField(
        IReadOnlyDictionary<string, DocumentationRow> rows,
        string id,
        string field,
        string expected,
        string? diagnostic = null)
    {
        var actual = rows[id].GetField(field);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(diagnostic ?? $"Stable row '{id}' field '{field}' must be exactly '{expected}', but was '{actual}'.");
        }
    }

    private static void RequireContains(
        IReadOnlyDictionary<string, DocumentationRow> rows,
        string id,
        string field,
        params string[] tokens)
    {
        var value = rows[id].GetField(field);
        foreach (var token in tokens)
        {
            if (!value.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                var diagnostic = id == "subscriptions.boundary.common-closure" && field == "Forbidden condition"
                    ? "Common closure must forbid subscription additions to Common."
                    : $"Stable row '{id}' field '{field}' must contain '{token}'.";
                throw new InvalidOperationException(diagnostic);
            }
        }
    }

    private static void RequireContainsOneOf(
        IReadOnlyDictionary<string, DocumentationRow> rows,
        string id,
        string field,
        params string[] alternatives)
    {
        var value = rows[id].GetField(field);
        if (!alternatives.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Stable row '{id}' field '{field}' must contain one of: {string.Join(", ", alternatives)}.");
        }
    }

    private static IEnumerable<string> ExtractHttpsAuthorities(string value)
    {
        return value.Split([' ', ';', ',', ')', '('], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.TrimEnd('.', ':'))
            .Where(token => token.StartsWith("https://", StringComparison.Ordinal));
    }

    private static void AssertExactSet(IEnumerable<string> actual, IEnumerable<string> expected, string name)
    {
        var actualValues = actual.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var expectedValues = expected.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (!actualValues.SequenceEqual(expectedValues, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Exact {name} drifted. Expected: {string.Join(", ", expectedValues)}. Actual: {string.Join(", ", actualValues)}.");
        }
    }

    private static void RequireExactOccurrence(string value, string expected, string name)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(expected, index, StringComparison.Ordinal)) >= 0; index += expected.Length)
        {
            count++;
        }

        if (count != 1)
        {
            throw new InvalidOperationException(
                $"Canonical subscription document must contain {name} exactly once; found {count}.");
        }
    }

    private static IEnumerable<string> ExtractFencedBlocks(string markdown, string language)
    {
        var marker = $"```{language}";
        var offset = 0;
        while (true)
        {
            var start = markdown.IndexOf(marker, offset, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                yield break;
            }

            var contentStart = markdown.IndexOf('\n', start + marker.Length);
            var end = contentStart < 0 ? -1 : markdown.IndexOf("```", contentStart, StringComparison.Ordinal);
            if (contentStart < 0 || end < 0)
            {
                throw new InvalidOperationException("Mermaid code fence is not closed.");
            }

            yield return markdown[(contentStart + 1)..end];
            offset = end + 3;
        }
    }

    private static ParsedDiagram ParseDiagram(string mermaid)
    {
        var edges = new List<DiagramEdge>();
        string? marker = null;
        string? declaration = null;
        foreach (var line in mermaid.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("%%", StringComparison.Ordinal) && line.EndsWith("%%", StringComparison.Ordinal))
            {
                var comment = line[2..^2].Trim();
                if (comment.StartsWith("subscriptions-graph:", StringComparison.Ordinal))
                {
                    if (marker != null)
                    {
                        throw new InvalidOperationException("Mermaid graph contains duplicate structured markers.");
                    }

                    marker = comment;
                }

                continue;
            }

            if (line.StartsWith("flowchart ", StringComparison.Ordinal))
            {
                if (declaration != null)
                {
                    throw new InvalidOperationException("Mermaid graph contains duplicate declarations.");
                }

                declaration = line;
                continue;
            }

            var arrow = line.IndexOf("-->", StringComparison.Ordinal);
            if (arrow < 0)
            {
                if (line.Contains("---", StringComparison.Ordinal)
                    || line.Contains("-.->", StringComparison.Ordinal)
                    || line.Contains("==>", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Mermaid graph contains unsupported Mermaid statement '{line}'.");
                }

                var standaloneNode = NormalizeDiagramNode(line);
                if (standaloneNode.Length > 0 && standaloneNode != line)
                {
                    throw new InvalidOperationException(
                        $"Mermaid graph contains unsupported standalone Mermaid node '{standaloneNode}'.");
                }

                throw new InvalidOperationException($"Mermaid graph contains unsupported Mermaid statement '{line}'.");
            }

            var source = NormalizeDiagramNode(line[..arrow]);
            var targetPart = line[(arrow + 3)..].Trim();
            var label = string.Empty;
            if (targetPart.StartsWith('|'))
            {
                var labelEnd = targetPart.IndexOf('|', 1);
                if (labelEnd < 0)
                {
                    throw new InvalidOperationException($"Malformed Mermaid edge label in '{line}'.");
                }

                label = targetPart[1..labelEnd].Trim();
                targetPart = targetPart[(labelEnd + 1)..].Trim();
            }

            edges.Add(new DiagramEdge(source, NormalizeDiagramNode(targetPart), label));
        }

        return new ParsedDiagram(marker, declaration, edges);
    }

    private static string NormalizeDiagramNode(string value)
    {
        var node = value.Trim();
        var shapeIndex = node.IndexOfAny(['[', '(', '{']);
        return shapeIndex < 0 ? node : node[..shapeIndex].Trim();
    }

    private static List<string> ParseCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '|' || trimmed[^1] != '|')
        {
            return [];
        }

        return trimmed[1..^1].Split('|').Select(cell => cell.Trim()).ToList();
    }

    private static bool IsSeparator(string line, int expectedCells)
    {
        var cells = ParseCells(line);
        return cells.Count == expectedCells && cells.All(cell => cell.Length >= 3 && cell.All(character => character == '-'));
    }

    private static string UnwrapCode(string value)
        => value.Length >= 2 && value[0] == '`' && value[^1] == '`' ? value[1..^1] : value;

    private static string Format(IReadOnlyCollection<string> values)
        => values.Count == 0 ? "none" : string.Join(", ", values);

    private static string CreateValidMarkdown()
    {
        return """
            # Direct store subscriptions

            This is a future-state logical contract over the existing project graph.

            | Boundary ID | State | Owner / authority | Owner responsibility | Allowed placement/dependencies | Forbidden condition | Source locator |
            | --- | --- | --- | --- | --- | --- | --- |
            | `subscriptions.boundary.current-state` | current | executable eight-owner/48-entity authority | current persisted ownership roster | current executable ownership catalog | subscription implementation is not current state | `LgymApi.ArchitectureTests/PersistedEntityOwnershipCatalog.cs#PersistedEntityOwnershipCatalog` |
            | `subscriptions.boundary.identity-owner` | future | Identity & Accounts | all subscription business, write, and provider ownership | Identity with current Domain, Platform, and Resources dependencies | no subscription ownership in API, Worker, Infrastructure, or Common | `LgymApi.Identity/IdentityModule.cs#IdentityModule` |
            | `subscriptions.boundary.api-transport` | future | API | HTTP transport only | existing API-to-Identity edge and authenticated account context | no business policy or provider handling in controllers | `LgymApi.Api/Features/Account/Controllers/AccountController.cs#AccountController` |
            | `subscriptions.boundary.worker-scheduling` | future | Worker | recurring scheduling for Identity public use cases | existing Worker-to-Identity edge and Infrastructure scheduler composition | no subscription job, payload, or provider type in Common | `LgymApi.BackgroundWorker/BackgroundWorkerRecurringJobs.cs#BackgroundWorkerRecurringJobs` |
            | `subscriptions.boundary.infrastructure-runtime` | current/future | Infrastructure | unchanged shared technical roots only | one shared AppDbContext, UoW, migrations, and Hangfire persistence | no subscription business policy or provider adapter ownership | `LgymApi.Infrastructure/Data/AppDbContext.cs#AppDbContext` |
            | `subscriptions.boundary.common-closure` | current/durable | BackgroundWorker.Common | exact closed persisted-job and email-wire surface | existing Common contract surface only | no subscription additions to Common | `LgymApi.ArchitectureTests/BackgroundWorkerCommonSurfaceGuardTests.cs#BackgroundWorkerCommonSurfaceGuardTests` |
            | `subscriptions.boundary.project-graph` | current/durable | executable 18-project/90-edge authority | preserve current project topology | existing project-reference graph only | no added, removed, duplicated, or cyclic project edge | `LgymApi.ArchitectureTests/ProjectReferenceGraphManifest.cs#ProjectReferenceGraphManifest` |

            | Contract ID | State | Owner | Provider-neutral contract | Persistence/message rule | Explicit exclusion |
            | --- | --- | --- | --- | --- | --- |
            | `subscriptions.contract.grant` | future | Identity & Accounts | durable subscription grant contract | owner-local persistence with one service-owned UoW boundary | provider SDK/response/credential/raw payload, foreign entity/repository, direct repository save, Common/Hangfire type |
            | `subscriptions.contract.inbox` | future | Identity & Accounts | durable provider-event inbox contract | owner-local inbox persistence with one service-owned UoW boundary | provider SDK/response/credential/raw payload, foreign entity/repository, direct repository save, Common/Hangfire type |
            | `subscriptions.contract.account-binding` | future | Identity & Accounts | account/store binding contract | owner-local binding persistence with one service-owned UoW boundary | provider SDK/response/credential/raw payload, foreign entity/repository, direct repository save, Common/Hangfire type |
            | `subscriptions.contract.current-access` | future | Identity & Accounts | effective current paid-access projection contract | owner-local projection persistence with one service-owned UoW boundary | provider SDK/response/credential/raw payload, foreign entity/repository, direct repository save, Common/Hangfire type |
            | `subscriptions.contract.provider-verification` | future | Identity & Accounts | internal provider-verification port returning normalized results | internal Identity adapter boundary; no provider payload persistence in the contract | provider SDK/response/credential/raw payload, foreign entity/repository, direct repository save, Common/Hangfire type |
            | `subscriptions.contract.provider-notification` | future | Identity & Accounts | internal provider-notification port returning normalized results | internal Identity adapter boundary; durable inbox state remains owner-local | provider SDK/response/credential/raw payload, foreign entity/repository, direct repository save, Common/Hangfire type |
            | `subscriptions.contract.processing` | future | Identity & Accounts | public Worker-facing inbox processing use case | CancellationToken is the only Hangfire batch argument; owner persistence selects records and keeps record IDs/cursors | Hangfire receives no record IDs/cursors, credentials, raw/provider payloads, receipts/JWS, purchase tokens, or account-binding tokens; no Common/Hangfire type |
            | `subscriptions.contract.reconciliation` | future | Identity & Accounts | public Worker-facing provider reconciliation use case | CancellationToken is the only Hangfire batch argument; owner persistence selects records and keeps record IDs/cursors | Hangfire receives no record IDs/cursors, credentials, raw/provider payloads, receipts/JWS, purchase tokens, or account-binding tokens; no Common/Hangfire type |
            | `subscriptions.contract.api-ingress` | future | API | thin provider-ingress transport adapter | inject focused Identity contracts and use authenticated account context where applicable | provider SDK/response/credential/raw payload, foreign entity/repository, direct repository save, Common/Hangfire type |
            | `subscriptions.contract.api-query` | future | API | thin current-access query transport adapter | inject focused Identity query contract and use authenticated account context | provider SDK/response/credential/raw payload, foreign entity/repository, direct repository save, Common/Hangfire type |
            | `subscriptions.contract.mapping` | future | API | registered custom IMapper profiles | cross-layer model conversion remains in API mapping profiles | provider SDK/response/credential/raw payload, foreign entity/repository, direct repository save, Common/Hangfire type |
            | `subscriptions.contract.localization` | future | Resources | EN/PL resource-backed user-facing messages | localized messages remain in the Resources boundary | provider SDK/response/credential/raw payload, foreign entity/repository, direct repository save, Common/Hangfire type |
            | `subscriptions.contract.persistence-topology` | future | Identity and Infrastructure | provider-neutral logical write and physical persistence seam | logical writes belong to Identity; context, UoW, and migrations remain in Infrastructure | provider SDK/response/credential/raw payload, foreign entity/repository, direct repository save, Common/Hangfire type |

            | Provider ID | State | Owner | Fixed authority/trust input | Verification/retry rule | Public-contract exclusion | Redaction class |
            | --- | --- | --- | --- | --- | --- | --- |
            | `subscriptions.provider.apple-production` | future | Identity & Accounts | Apple production authority https://api.storekit.apple.com; verified JWS/certificate/app/environment/account binding before normalization | fixed authority; bounded, implementation-owned timeout and bounded, implementation-owned retry policy; cancellation-aware and idempotent; leaves are deferred | no provider SDK/response/credential/raw payload | metadata-only |
            | `subscriptions.provider.apple-sandbox` | future | Identity & Accounts | Apple sandbox authority https://api.storekit-sandbox.apple.com; verified JWS/certificate/app/environment/account binding before normalization | fixed authority; bounded, implementation-owned timeout and bounded, implementation-owned retry policy; cancellation-aware and idempotent; leaves are deferred | no provider SDK/response/credential/raw payload | metadata-only |
            | `subscriptions.provider.apple-signed-data` | future | Identity & Accounts | verified JWS/certificate/app/environment/account binding before normalization | provider-private sensitive handling and storage for receipts/JWS and account-binding tokens; bounded, cancellation-aware, idempotent retry | receipts/JWS, account-binding tokens, credentials, provider bodies, and SDK responses are forbidden from public contracts, logs, analytics, responses, and evidence | signed-payload;account-binding-token |
            | `subscriptions.provider.google-play` | future | Identity & Accounts | Google Android Publisher authority https://androidpublisher.googleapis.com; authoritative purchases.subscriptionsv2.get current-state re-query | provider-private sensitive handling and storage for purchase tokens, account-binding tokens, credentials, and provider bodies; bounded, implementation-owned timeout and bounded, implementation-owned retry policy; cancellation-aware and idempotent; honor Retry-After and transient guidance | purchase tokens, account-binding tokens, credentials, provider bodies, and SDK responses are forbidden from public contracts, logs, analytics, responses, and evidence | purchase-token;account-binding-token;provider-body;credential |
            | `subscriptions.provider.google-rtdn` | future | Identity & Accounts | verified Pub/Sub OIDC identity/envelope bounds then provider re-query via purchases.subscriptionsv2.get; OIDC checks include signature, issuer, audience, expiry, expected service-account email, and email_verified | provider-private sensitive handling and storage for credentials and provider bodies; bounded, implementation-owned timeout and bounded, implementation-owned retry policy for provider re-query; cancellation-aware and idempotent; never trust notification order or body alone | credentials, provider bodies, raw payloads, purchase tokens, and SDK responses are forbidden from public contracts, logs, analytics, responses, and evidence | provider-body;credential |
            | `subscriptions.provider.sanitized-errors` | future | Identity & Accounts | provider-neutral authentication, validation, throttled, transient, and unavailable outcomes | metadata-only observability and evidence; sanitize provider bodies and exceptions | sensitive values in observability are forbidden; no receipt/JWS, purchase token, account-binding token, credential, provider body, personal data, exception, SDK response, or raw payload | metadata-only |

            | Configuration ID | State | Key/root | Default | Requires | Enables | Forbidden effect |
            | --- | --- | --- | --- | --- | --- | --- |
            | `subscriptions.configuration.root` | future | `Subscriptions:*` | no value/default | none | Identity-owned subscription configuration namespace | no binding or runtime value change in #443 |
            | `subscriptions.configuration.apple` | future | `Subscriptions:Apple:*` | no value/default | Subscriptions:Enabled | Apple provider child leaves | no runtime host override or public provider contract |
            | `subscriptions.configuration.google-play` | future | `Subscriptions:GooglePlay:*` | no value/default | Subscriptions:Enabled | Google Play provider child leaves | no runtime host override or public provider contract |
            | `subscriptions.configuration.processing` | future | `Subscriptions:Processing:*` | no value/default | Subscriptions:Enabled | processing child leaves | no runtime registration or value file change |
            | `subscriptions.configuration.reconciliation` | future | `Subscriptions:Reconciliation:*` | no value/default | Subscriptions:Enabled | reconciliation child leaves | no runtime registration or value file change |
            | `subscriptions.configuration.enabled` | future | `Subscriptions:Enabled` | false; missing or unparseable is false | none | provider ingress/calls and lifecycle processing | never hides durable current access, grants access, erases state, or changes free baseline |
            | `subscriptions.configuration.apple-enabled` | future | `Subscriptions:Apple:Enabled` | false; missing or unparseable is false | Subscriptions:Enabled | only the Apple adapter | disabling does not cancel, refund, or delete its grant |
            | `subscriptions.configuration.google-play-enabled` | future | `Subscriptions:GooglePlay:Enabled` | false; missing or unparseable is false | Subscriptions:Enabled | only the Google Play adapter | disabling does not cancel, refund, or delete its grant |
            | `subscriptions.configuration.purchases-enabled` | future | `Subscriptions:PurchasesEnabled` | false; missing or unparseable is false | global enabled plus relevant provider enabled | new client purchase verification | does not disable restore or lifecycle repair |
            | `subscriptions.configuration.projection-apply-enabled` | future | `Subscriptions:ProjectionApplyEnabled` | false; missing or unparseable is false | global enabled plus relevant provider enabled | paid-projection mutation while allowing observe-only durable metadata | does not grant paid access or erase durable inbox/reconciliation state |
            | `subscriptions.configuration.capability-enforcement-enabled` | future | `Subscriptions:CapabilityEnforcementEnabled` | false; missing or unparseable is false | global enabled plus projection apply plus separately approved and shipped paid-benefit release | paid-benefit capability enforcement only after approved release | no effect before release and no module lock in #443 |

            | Policy ID | State | Rule | Evidence/guard | Explicit non-goal |
            | --- | --- | --- | --- | --- |
            | `subscriptions.policy.tiers` | future | exactly tier_1 rank 1, tier_2 rank 2, and tier_3 rank 3 | stable policy row parser and focused architecture guard | no catalog, pricing, or billing-period implementation |
            | `subscriptions.policy.free-baseline` | future | unchanged free baseline and not a fourth profile | focused policy assertion | no paid capability enforcement in #443 |
            | `subscriptions.policy.cross-store` | future | independent grants; highest currently valid tier wins; no automatic cross-store cancel or refund | focused cross-store policy assertion | no automatic Apple/Google coupling |
            | `subscriptions.policy.server-authority` | future | durable inbox is processing authority; verified provider re-query plus durable grant/projection is access authority | focused authority and source-of-truth assertion | no unverified notification or client success flag as authority |
            | `subscriptions.policy.jwt` | future | no long-lived paid claim, role, or permission authority in JWT | focused JWT exclusion assertion | no migration to JWT-paid entitlements |
            | `subscriptions.policy.tests` | future | parser, provider-surface, topology/Common/persistence parity, fixtures, and targeted/full Release evidence | Todo 2 focused guard and later Release evidence | no provider call or cryptography proof in Todo 1 |
            | `subscriptions.policy.rollout` | future | docs and guards first; all controls false; no sale or module lock | focused rollout/control-state assertion | no production activation or paid benefit release |
            | `subscriptions.policy.rollback` | future | remove contract only before dependent child implementation; otherwise supersede without deleting commerce state | focused rollback rule assertion | no durable commerce-state deletion as rollback |

            Provider calls use a bounded, implementation-owned timeout and a bounded, implementation-owned retry policy.

            Future focused identities are internal `IAccountSubscriptionGrantRepository`, `ISubscriptionInboxEventRepository`, `IAccountPaidAccessProjectionRepository`, `IAppleSubscriptionProvider`, and `IGooglePlaySubscriptionProvider`; public `IVerifyAppleSubscriptionPurchaseUseCase.VerifyAsync`, `IVerifyGooglePlaySubscriptionPurchaseUseCase.VerifyAsync`, `IIngestAppleSubscriptionNotificationUseCase.IngestAsync`, `IIngestGooglePlayNotificationUseCase.IngestAsync`, `ICurrentPaidAccessQuery.GetAsync`, `ISubscriptionInboxProcessingUseCase.ProcessBatchAsync`, and `ISubscriptionProviderReconciliationUseCase.ReconcileBatchAsync`.

            ```mermaid
            %% subscriptions-graph: future-state logical flow; project-graph: existing %%
            flowchart LR
                Client -->|authenticated purchase/query| API
                Apple -->|verified ingress| API
                GooglePlay -->|verified ingress| API
                API -->|focused contracts| Identity
                Worker -->|scheduling| Identity
                Identity -->|provider call| AppleAdapter
                AppleAdapter -->|provider call| Apple
                Identity -->|provider call| GoogleAdapter
                GoogleAdapter -->|provider call| GooglePlay
                Identity -->|UoW| Infrastructure
                Infrastructure -->|persistence| PostgreSQL
                Worker -->|scheduler| Infrastructure
                Infrastructure -->|persistence| Hangfire
                Identity -->|projection read| CurrentAccess
            ```
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private sealed record TableContract(string Prefix, IReadOnlyList<string> Columns, IReadOnlyList<string> Ids);

    private sealed record DocumentationTable(IReadOnlyList<string> Columns, IReadOnlyList<DocumentationRow> Rows);

    private sealed record DocumentationRow(string Id, IReadOnlyDictionary<string, string> Fields)
    {
        public string GetField(string name) => Fields.TryGetValue(name, out var value)
            ? value
            : throw new InvalidOperationException($"Stable row '{Id}' is missing expected field '{name}'.");
    }

    private sealed record SubscriptionDocument(IReadOnlyDictionary<string, DocumentationRow> Rows);

    private sealed record DiagramEdge(string Source, string Target, string LabelToken);

    private sealed record ParsedDiagram(
        string? Marker,
        string? Declaration,
        IReadOnlyList<DiagramEdge> Edges);

    private sealed record PublicSurfaceViolation(string Path, string Symbol, string Category, string Dependency)
    {
        public string Identity => $"{Path}|{Symbol}|{Category}|{Dependency}";

        public override string ToString() => Identity;
    }
}
