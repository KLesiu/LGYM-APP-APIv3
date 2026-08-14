using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LgymApi.ArchitectureTests;

internal static class StoreCatalogContract
{
    private static readonly string[] ProfileIds = ["tier_1", "tier_2", "tier_3"];
    private static readonly string[] ProductIds = ["lgym.subscription.tier_1.monthly", "lgym.subscription.tier_2.monthly", "lgym.subscription.tier_3.monthly"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        AllowDuplicateProperties = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false) }
    };

    internal static StoreCatalogDocument Parse(string json)
    {
        if (Regex.IsMatch(
                json,
                @"(?i)(bearer\s+|-----begin|private[_ -]?key|client[_ -]?secret|access[_ -]?token|purchase[_ -]?token|ignore\s+(all\s+)?previous\s+instructions|eyJ[A-Za-z0-9_-]{8,}\.eyJ|[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,})",
                RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("Store catalog validation failed for sensitive content.");
        }

        try
        {
            var document = JsonSerializer.Deserialize<StoreCatalogDocument>(json, JsonOptions)
                           ?? throw new InvalidOperationException("Store catalog JSON must contain one document.");
            Validate(document);
            return document;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Store catalog JSON is malformed or violates the typed schema.");
        }
    }

    private static void Validate(StoreCatalogDocument document)
    {
        Require(document.SchemaVersion == 1 && document.CatalogId == "lgym-direct-store-v1", "catalog header");
        Require(document.Approval.Approver == "@withelm" && IsHttps(document.Approval.Url) && IsUtc(document.Approval.ApprovedAtUtc), "approval");
        ValidateRevisions(document);
        ValidateProfiles(document);
        ValidatePrices(document);
        ValidateApplications(document.Applications);
        ValidateApple(document.Apple);
        ValidateGoogle(document.Google);
        ValidateTransitions(document.Transitions);
        ValidateExclusions(document.Exclusions);
        ValidateGovernance(document.Governance);
        ValidateState(document);
    }

    private static void ValidateRevisions(StoreCatalogDocument document)
    {
        Require(document.Revisions.Count > 0, "append-only revisions");
        Require(document.Revisions.Select(revision => revision.Id).Distinct(StringComparer.Ordinal).Count() == document.Revisions.Count, "append-only revisions");
        var first = document.Revisions[0];
        Require(first.Id == "v0-restricted-test" && first.PreviousId is null && first.ApprovedAtUtc == document.Approval.ApprovedAtUtc, "append-only revisions");
        for (var index = 0; index < document.Revisions.Count; index++)
        {
            var revision = document.Revisions[index];
            Require(!revision.ProductionEnabled, "production state");
            Require(IsUtc(revision.ApprovedAtUtc), "append-only revisions");
            if (index > 0)
            {
                var previous = document.Revisions[index - 1];
                Require(revision.PreviousId == previous.Id && revision.ApprovedAtUtc > previous.ApprovedAtUtc, "append-only revisions");
            }
        }
    }

    private static void ValidateProfiles(StoreCatalogDocument document)
    {
        RequireIds(document.Profiles.Select(profile => profile.Id), ProfileIds, "profiles/ranks");
        Require(document.Profiles.Select(profile => profile.Rank).SequenceEqual([1, 2, 3]), "profiles/ranks");
        var expectedNames = new[] { ("Basic", "Podstawowy"), ("Plus", "Plus"), ("Pro", "Pro") };
        for (var index = 0; index < document.Profiles.Count; index++)
        {
            ValidateNames(document.Profiles[index].Localizations, expectedNames[index].Item1, expectedNames[index].Item2, "profile localizations");
        }

        Require(document.BenefitStatus.Id == "pending_paid_benefits", "benefit status");
        RequireIds(document.BenefitStatus.Localizations.Select(item => item.Locale), ["en", "pl"], "benefit localizations");
        Require(document.BenefitStatus.Localizations[0].Text == "Paid benefits pending approval; unavailable for production sale.", "benefit localizations");
        Require(document.BenefitStatus.Localizations[1].Text == "Płatne korzyści oczekują na zatwierdzenie; sprzedaż produkcyjna jest niedostępna.", "benefit localizations");
        Require(document.Periods.Count == 1 && document.Periods[0] == new BillingPeriod("monthly", "one-month", "P1M"), "billing period");
    }

    private static void ValidatePrices(StoreCatalogDocument document)
    {
        Require(document.TerritoryPrices.Count == document.Revisions.Count * ProfileIds.Length, "territory prices");
        Require(document.TerritoryPrices.All(price => Regex.IsMatch(price.GrossAmount, @"^(0|[1-9]\d*)\.\d{2}$", RegexOptions.CultureInvariant)
            && decimal.TryParse(price.GrossAmount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) && amount > 0), "money");
        Require(document.TerritoryPrices.All(price => price.Currency == "PLN"), "currency");
        foreach (var revision in document.Revisions)
        {
            var prices = document.TerritoryPrices.Where(price => price.RevisionId == revision.Id).ToArray();
            RequireIds(prices.Select(price => price.ProfileId), ProfileIds, "territory prices");
            Require(prices.All(price => price.Territory == "POL" && price.VatDisplay == "vat-inclusive"), "territory prices");
        }

        var v0 = document.TerritoryPrices.Where(price => price.RevisionId == "v0-restricted-test").Select(price => price.GrossAmount);
        Require(v0.SequenceEqual(["4.99", "9.99", "19.99"], StringComparer.Ordinal), "V0 prices");
    }

    private static void ValidateApplications(StoreApplications applications)
    {
        Require(applications.Apple.AppId == "6753204527" && applications.Apple.Identity == "com.lesiuuu.lgymappmobile"
            && applications.Google.PackageId == "com.lesiuuu.lgymappmobile" && applications.Google.Identity == "com.lesiuuu.lgymappmobile", "application identity");
        ValidateEnvironments(applications.Apple.Environments, ["sandbox", "testflight", "production"], "Apple environments");
        ValidateEnvironments(applications.Google.Environments, ["license-test", "internal", "production"], "Google environments");
    }

    private static void ValidateApple(AppleCatalog apple)
    {
        Require(apple.Group.Reference == "LGYM Paid Profiles V1", "Apple group");
        ValidateNames(apple.Group.Localizations, "LGYM Subscriptions", "Subskrypcje LGYM", "Apple group localizations");
        RequireIds(apple.Products.Select(product => product.Id), ProductIds, "Apple products");
        RequireIds(apple.Products.Select(product => product.ProfileId), ProfileIds, "Apple products");
        Require(apple.Products.Select(product => product.Level).SequenceEqual([3, 2, 1])
            && apple.Products.All(product => product.PeriodId == "monthly"), "Apple products");
    }

    private static void ValidateGoogle(GoogleCatalog google)
    {
        RequireIds(google.Products.Select(product => product.Id), ProductIds, "Google products");
        RequireIds(google.Products.Select(product => product.ProfileId), ProfileIds, "Google products");
        foreach (var product in google.Products)
        {
            Require(product.BasePlans.Count == 1, "Google base plans");
            var plan = product.BasePlans[0];
            Require(product.Role == "primary" && plan.Id == "primary-monthly" && plan.Id != product.Role, "role/base-plan separation");
            Require(plan.PeriodId == "monthly" && plan.AutoRenewing, "Google base plans");
        }
    }

    private static void ValidateTransitions(IReadOnlyList<CatalogTransition> transitions)
    {
        string[] expected =
        [
            "tier_1>tier_2|immediate|CHARGE_PRORATED_PRICE", "tier_1>tier_3|immediate|CHARGE_PRORATED_PRICE",
            "tier_2>tier_3|immediate|CHARGE_PRORATED_PRICE", "tier_3>tier_2|next-renewal|DEFERRED",
            "tier_3>tier_1|next-renewal|DEFERRED", "tier_2>tier_1|next-renewal|DEFERRED"
        ];
        var actual = transitions.Select(item => $"{item.FromProfileId}>{item.ToProfileId}|{item.Timing}|{item.GoogleMode}");
        Require(actual.SequenceEqual(expected, StringComparer.Ordinal), "transitions");
    }

    private static void ValidateExclusions(CatalogExclusions exclusions)
        => Require(!(exclusions.Trials || exclusions.Offers || exclusions.Coupons || exclusions.FamilySharing
            || exclusions.PromotedIap || exclusions.OfferCodes || exclusions.WinBack || exclusions.PlayResubscribe
            || exclusions.OutsideAppAcceptance), "forbidden promotions");

    private static void ValidateGovernance(CatalogGovernance governance)
    {
        Require(governance.Owner == "@withelm", "catalog owner");
        RequireIds(governance.TestAliases, ["apple-sandbox-v1-primary", "google-license-v1-primary"], "test aliases");
        Require(governance.RevisionPolicy == "append-only-git-reviewed"
            && governance.ApplePricePreservation == "preserve-existing-subscriber-price-on-increase"
            && governance.GooglePricePreservation == "retain-legacy-cohort-opt-in-migration"
            && governance.EvidenceManifestPath == "docs/subscriptions/evidence/issue-444-store-console-evidence.json", "price governance");
    }

    private static void ValidateState(StoreCatalogDocument document)
    {
        var evidence = document.Apple.Products.Select(product => product.Evidence)
            .Concat(document.Google.Products.Select(product => product.Evidence))
            .Concat(document.Google.Products.SelectMany(product => product.BasePlans).Select(plan => plan.Evidence))
            .Append(document.Apple.Group.Evidence).ToArray();
        switch (document.State)
        {
            case StoreCatalogState.ApprovedPrecreation:
                Require(document.Apple.Group.GeneratedId is null && evidence.All(item => item is null)
                    && !document.Apple.Group.SandboxTestable && document.Apple.Products.All(product => !product.SandboxTestable)
                    && document.Google.Products.SelectMany(product => product.BasePlans).All(plan => !plan.Active), "precreation state");
                break;
            case StoreCatalogState.RestrictedTestConfigured:
                var appleEvidence = HasEvidence(document.Apple.Group.Evidence, document.Apple.Group.GeneratedId, document.Approval.ApprovedAtUtc)
                    && document.Apple.Products.All(product => HasEvidence(product.Evidence, product.Id, document.Approval.ApprovedAtUtc));
                var googleEvidence = document.Google.Products.All(product => HasEvidence(product.Evidence, product.Id, document.Approval.ApprovedAtUtc)
                    && product.BasePlans.All(plan => HasEvidence(plan.Evidence, $"{product.Id}/{plan.Id}", document.Approval.ApprovedAtUtc)));
                Require(!IsPlaceholder(document.Apple.Group.GeneratedId) && appleEvidence && googleEvidence && document.Apple.Group.SandboxTestable
                    && document.Apple.Products.All(product => product.SandboxTestable)
                    && document.Google.Products.SelectMany(product => product.BasePlans).All(plan => plan.Active), "configured-state evidence");
                break;
            default:
                throw new InvalidOperationException("Store catalog validation failed for catalog state.");
        }
    }

    private static void ValidateNames(IReadOnlyList<NameLocalization> values, string english, string polish, string category)
    {
        RequireIds(values.Select(value => value.Locale), ["en", "pl"], category);
        Require(values[0].Name == english && values[1].Name == polish, category);
    }

    private static void ValidateEnvironments(IReadOnlyList<StoreEnvironment> values, string[] ids, string category)
    {
        RequireIds(values.Select(value => value.Id), ids, category);
        Require(values[0] == new StoreEnvironment(ids[0], true, false)
            && values[1] == new StoreEnvironment(ids[1], true, false)
            && values[2] == new StoreEnvironment(ids[2], false, false), category);
    }

    private static bool HasEvidence(StoreObjectEvidence? evidence, string? expectedReadBackId, DateTimeOffset approvedAtUtc)
        => evidence is not null && evidence.ReadBackId == expectedReadBackId && !IsPlaceholder(evidence.ReadBackId) && IsHttps(evidence.Reference)
           && IsUtc(evidence.ObservedAtUtc) && evidence.ObservedAtUtc > approvedAtUtc;

    private static bool IsPlaceholder(string? value)
        => string.IsNullOrWhiteSpace(value) || Regex.IsMatch(value, "(?i)(placeholder|pending|todo|tbd)", RegexOptions.CultureInvariant);

    private static bool IsHttps(Uri value) => value.IsAbsoluteUri && value.Scheme == Uri.UriSchemeHttps;

    private static bool IsUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;

    private static void RequireIds(IEnumerable<string> actual, IEnumerable<string> expected, string category)
        => Require(actual.SequenceEqual(expected, StringComparer.Ordinal), category);

    private static void Require(bool condition, string category)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Store catalog validation failed for {category}.");
        }
    }
}

internal enum StoreCatalogState { ApprovedPrecreation, RestrictedTestConfigured }
internal sealed record StoreCatalogDocument(int SchemaVersion, string CatalogId, CatalogApproval Approval, StoreCatalogState State, IReadOnlyList<CatalogRevision> Revisions, IReadOnlyList<CatalogProfile> Profiles, BenefitStatus BenefitStatus, IReadOnlyList<BillingPeriod> Periods, IReadOnlyList<TerritoryPrice> TerritoryPrices, StoreApplications Applications, AppleCatalog Apple, GoogleCatalog Google, IReadOnlyList<CatalogTransition> Transitions, CatalogExclusions Exclusions, CatalogGovernance Governance);
internal sealed record CatalogApproval(string Approver, Uri Url, DateTimeOffset ApprovedAtUtc);
internal sealed record CatalogRevision(string Id, string? PreviousId, DateTimeOffset ApprovedAtUtc, bool ProductionEnabled);
internal sealed record CatalogProfile(string Id, int Rank, IReadOnlyList<NameLocalization> Localizations);
internal sealed record NameLocalization(string Locale, string Name);
internal sealed record BenefitStatus(string Id, IReadOnlyList<TextLocalization> Localizations);
internal sealed record TextLocalization(string Locale, string Text);
internal sealed record BillingPeriod(string Id, string ApplePeriod, string GooglePeriod);
internal sealed record TerritoryPrice(string RevisionId, string ProfileId, string Territory, string Currency, string GrossAmount, string VatDisplay);
internal sealed record StoreApplications(AppleApplication Apple, GoogleApplication Google);
internal sealed record AppleApplication(string AppId, string Identity, IReadOnlyList<StoreEnvironment> Environments);
internal sealed record GoogleApplication(string PackageId, string Identity, IReadOnlyList<StoreEnvironment> Environments);
internal sealed record StoreEnvironment(string Id, bool Usable, bool ProductionEnabled);
internal sealed record AppleCatalog(AppleSubscriptionGroup Group, IReadOnlyList<AppleProduct> Products);
internal sealed record AppleSubscriptionGroup(string Reference, IReadOnlyList<NameLocalization> Localizations, string? GeneratedId, bool SandboxTestable, StoreObjectEvidence? Evidence);
internal sealed record AppleProduct(string Id, string ProfileId, string PeriodId, int Level, bool SandboxTestable, StoreObjectEvidence? Evidence);
internal sealed record GoogleCatalog(IReadOnlyList<GoogleProduct> Products);
internal sealed record GoogleProduct(string Id, string ProfileId, string Role, StoreObjectEvidence? Evidence, IReadOnlyList<GoogleBasePlan> BasePlans);
internal sealed record GoogleBasePlan(string Id, string PeriodId, bool AutoRenewing, bool Active, StoreObjectEvidence? Evidence);
internal sealed record StoreObjectEvidence(string ReadBackId, Uri Reference, DateTimeOffset ObservedAtUtc);
internal sealed record CatalogTransition(string FromProfileId, string ToProfileId, string Timing, string GoogleMode);
internal sealed record CatalogExclusions(bool Trials, bool Offers, bool Coupons, bool FamilySharing, bool PromotedIap, bool OfferCodes, bool WinBack, bool PlayResubscribe, bool OutsideAppAcceptance);
internal sealed record CatalogGovernance(string Owner, IReadOnlyList<string> TestAliases, string RevisionPolicy, string ApplePricePreservation, string GooglePricePreservation, string EvidenceManifestPath);
