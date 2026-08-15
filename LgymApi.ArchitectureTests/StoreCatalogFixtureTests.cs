namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class StoreCatalogFixtureTests
{
    private const string ApprovalUrl = "https://github.com/withelm/LGYM-APP-APIv3/issues/444#issuecomment-5296486807";
    private const string ExpectedV0SemanticHash = "d322fa880a912d5cee7484077f031046dd0f8a5698395e9fed9bfca9c36b2cc3";
    private const string SecretMarker = "TOP_SECRET_MARKER";

    [Test]
    public void Repository_V0_Approved_Precreation_Catalog_Should_Load()
    {
        var document = StoreCatalogContract.Parse(LoadFixtureJson());

        Assert.Multiple(() =>
        {
            Assert.That(document.SchemaVersion, Is.EqualTo(1));
            Assert.That(document.CatalogId, Is.EqualTo("lgym-direct-store-v1"));
            Assert.That(document.State, Is.EqualTo(StoreCatalogState.ApprovedPrecreation));
            Assert.That(document.Revisions, Has.Count.EqualTo(1));
            Assert.That(document.Approval.Approver, Is.EqualTo("@withelm"));
            Assert.That(document.Approval.Url, Is.EqualTo(new Uri(ApprovalUrl)));
            Assert.That(document.Approval.ApprovedAtUtc, Is.EqualTo(new DateTimeOffset(2026, 8, 14, 17, 52, 55, TimeSpan.Zero)));
        });
    }

    [Test]
    public void Repository_V0_Semantics_Should_Match_Pinned_Hash()
    {
        var document = StoreCatalogContract.Parse(LoadFixtureJson());

        Assert.That(StoreCatalogVersioning.ComputeV0SemanticHash(document), Is.EqualTo(ExpectedV0SemanticHash));
    }

    [Test]
    public void V0_Semantic_Hash_Should_Ignore_Later_State_Evidence_And_Revisions()
    {
        var document = StoreCatalogContract.Parse(LoadFixtureJson());
        var observedAtUtc = document.Approval.ApprovedAtUtc.AddMinutes(1);
        var laterState = document with
        {
            State = StoreCatalogState.RestrictedTestConfigured,
            Revisions = [.. document.Revisions, new CatalogRevision("v1-restricted-test", "v0-restricted-test", observedAtUtc, false)],
            TerritoryPrices =
            [
                .. document.TerritoryPrices,
                new TerritoryPrice("v1-restricted-test", "tier_1", "POL", "PLN", "6.99", "vat-inclusive"),
                new TerritoryPrice("v1-restricted-test", "tier_2", "POL", "PLN", "11.99", "vat-inclusive"),
                new TerritoryPrice("v1-restricted-test", "tier_3", "POL", "PLN", "21.99", "vat-inclusive")
            ],
            Apple = document.Apple with
            {
                Group = document.Apple.Group with
                {
                    GeneratedId = "apple-group-42",
                    SandboxTestable = true,
                    Evidence = Evidence("apple-group-42", observedAtUtc)
                },
                Products = document.Apple.Products.Select(product => product with
                {
                    SandboxTestable = true,
                    Evidence = Evidence(product.Id, observedAtUtc)
                }).ToArray()
            },
            Google = document.Google with
            {
                Products = document.Google.Products.Select(product => product with
                {
                    Evidence = Evidence(product.Id, observedAtUtc),
                    BasePlans = product.BasePlans.Select(plan => plan with
                    {
                        Active = true,
                        Evidence = Evidence($"{product.Id}/{plan.Id}", observedAtUtc)
                    }).ToArray()
                }).ToArray()
            }
        };

        Assert.That(StoreCatalogVersioning.ComputeV0SemanticHash(laterState), Is.EqualTo(ExpectedV0SemanticHash));
    }

    [TestCaseSource(nameof(V0DriftCases))]
    public void V0_Semantic_Drift_Should_Fail_Verification(object mutationValue)
    {
        var mutation = (MutationCase)mutationValue;
        var json = mutation.ReplaceAll
            ? LoadFixtureJson().Replace(mutation.OldValue, mutation.NewValue, StringComparison.Ordinal)
            : ReplaceOnce(LoadFixtureJson(), mutation.OldValue, mutation.NewValue);

        var exception = Assert.Throws<InvalidOperationException>(() => VerifyV0(json));

        Assert.That(exception!.Message, Does.Not.Contain(SecretMarker));
    }

    private static IEnumerable<TestCaseData> V0DriftCases()
    {
        yield return Case("Rejects_Malformed_Json", "\"schemaVersion\": 1,", "\"schemaVersion\":,", false);
        yield return Case("Rejects_V0_Price_Rewrite", "\"grossAmount\": \"4.99\"", "\"grossAmount\": \"5.99\"", false);
        yield return Case("Rejects_V0_Product_Id_Rewrite", "\"id\": \"lgym.subscription.tier_1.monthly\"", "\"id\": \"lgym.subscription.unknown.monthly\"", false);
        yield return Case("Rejects_V0_Rank_Rewrite", "\"rank\": 1", "\"rank\": 2", false);
        yield return Case("Rejects_V0_Locale_Rewrite", "\"locale\": \"en\", \"name\": \"Basic\"", "\"locale\": \"en\", \"name\": \"Starter\"", false);
        yield return Case("Rejects_V0_Exclusion_Rewrite", "\"trials\": false", "\"trials\": true", false);
        yield return Case("Rejects_Unapproved_Object", "\"profiles\": [", "\"profiles\": [{ \"id\": \"tier_4\", \"rank\": 4, \"localizations\": [] },", false);
        yield return Case("Rejects_Stale_Approval_Url", ApprovalUrl, "https://github.com/withelm/LGYM-APP-APIv3/issues/444#issuecomment-1", false);
        yield return Case("Rejects_Stale_Approval_Timestamp", "2026-08-14T17:52:55Z", "2026-08-14T17:52:56Z", true);
        yield return Case("Rejects_Secret_Like_Value", "\"owner\": \"@withelm\"", $"\"owner\": \"Bearer {SecretMarker}\"", false);
    }

    private static TestCaseData Case(string name, string oldValue, string newValue, bool replaceAll)
        => new(new MutationCase(oldValue, newValue, replaceAll)) { TestName = name };

    private static StoreObjectEvidence Evidence(string readBackId, DateTimeOffset observedAtUtc)
        => new(readBackId, new Uri("https://github.com/withelm/LGYM-APP-APIv3/issues/444#evidence"), observedAtUtc);

    private static void VerifyV0(string json)
    {
        var document = StoreCatalogContract.Parse(json);
        StoreCatalogVersioning.AssertV0SemanticHash(document, ExpectedV0SemanticHash);
    }

    private static string LoadFixtureJson()
        => File.ReadAllText(Path.Combine(
            ArchitectureTestHelpers.ResolveRepositoryRoot(),
            "LgymApi.ArchitectureTests",
            "Inventories",
            "issue-444-store-catalog.json"));

    private static string ReplaceOnce(string source, string oldValue, string newValue)
    {
        var index = source.IndexOf(oldValue, StringComparison.Ordinal);
        Assert.That(index, Is.GreaterThanOrEqualTo(0), $"Mutation source token was not found: {oldValue}");
        return string.Concat(source.AsSpan(0, index), newValue, source.AsSpan(index + oldValue.Length));
    }

    private sealed record MutationCase(string OldValue, string NewValue, bool ReplaceAll);
}
