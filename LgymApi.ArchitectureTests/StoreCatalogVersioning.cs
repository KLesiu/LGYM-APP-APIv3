using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LgymApi.ArchitectureTests;

internal static class StoreCatalogVersioning
{
    private const string V0RevisionId = "v0-restricted-test";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static string ComputeV0SemanticHash(StoreCatalogDocument document)
    {
        var semantics = new V0CatalogSemantics(
            document.SchemaVersion,
            document.CatalogId,
            document.Approval,
            document.Revisions.Single(revision => revision.Id == V0RevisionId),
            document.Profiles,
            document.BenefitStatus,
            document.Periods,
            document.TerritoryPrices.Where(price => price.RevisionId == V0RevisionId).ToArray(),
            document.Applications,
            new AppleV0Semantics(
                document.Apple.Group.Reference,
                document.Apple.Group.Localizations,
                document.Apple.Products.Select(product => new AppleProductV0Semantics(
                    product.Id,
                    product.ProfileId,
                    product.PeriodId,
                    product.Level)).ToArray()),
            new GoogleV0Semantics(document.Google.Products.Select(product => new GoogleProductV0Semantics(
                product.Id,
                product.ProfileId,
                product.Role,
                product.BasePlans.Select(plan => new GoogleBasePlanV0Semantics(
                    plan.Id,
                    plan.PeriodId,
                    plan.AutoRenewing)).ToArray())).ToArray()),
            document.Transitions,
            document.Exclusions,
            document.Governance);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(semantics, JsonOptions));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    internal static void AssertV0SemanticHash(StoreCatalogDocument document, string expectedHash)
    {
        if (!string.Equals(ComputeV0SemanticHash(document), expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Store catalog validation failed for the pinned V0 semantic hash.");
        }
    }

    private sealed record V0CatalogSemantics(
        int SchemaVersion,
        string CatalogId,
        CatalogApproval Approval,
        CatalogRevision Revision,
        IReadOnlyList<CatalogProfile> Profiles,
        BenefitStatus BenefitStatus,
        IReadOnlyList<BillingPeriod> Periods,
        IReadOnlyList<TerritoryPrice> TerritoryPrices,
        StoreApplications Applications,
        AppleV0Semantics Apple,
        GoogleV0Semantics Google,
        IReadOnlyList<CatalogTransition> Transitions,
        CatalogExclusions Exclusions,
        CatalogGovernance Governance);

    private sealed record AppleV0Semantics(
        string GroupReference,
        IReadOnlyList<NameLocalization> GroupLocalizations,
        IReadOnlyList<AppleProductV0Semantics> Products);

    private sealed record AppleProductV0Semantics(string Id, string ProfileId, string PeriodId, int Level);

    private sealed record GoogleV0Semantics(IReadOnlyList<GoogleProductV0Semantics> Products);

    private sealed record GoogleProductV0Semantics(
        string Id,
        string ProfileId,
        string Role,
        IReadOnlyList<GoogleBasePlanV0Semantics> BasePlans);

    private sealed record GoogleBasePlanV0Semantics(string Id, string PeriodId, bool AutoRenewing);
}
