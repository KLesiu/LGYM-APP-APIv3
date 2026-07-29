namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ProjectReferenceImportGuardTests
{
    [Test]
    public void Solution_Direct_Imports_Should_Justify_Every_ProjectReference_Without_Transitive_Reliance()
    {
        var analysis = ProjectReferenceImportGuard.AnalyzeSolution(ResolveRepositoryRoot());

        Assert.Multiple(() =>
        {
            Assert.That(analysis.Violations, Is.Empty, string.Join(Environment.NewLine, analysis.Violations));
            Assert.That(analysis.SemanticEvidenceByEdge, Has.Count.EqualTo(89));
            Assert.That(analysis.AnalyzerEdgeIdentities, Is.EquivalentTo(new[]
            {
                "LgymApi.Resources -> LgymApi.Resources.Generator"
            }));
            Assert.That(analysis.TopologicalOrder, Is.EqualTo(ProjectReferenceGraphManifest.TopologicalOrder));
        });
    }

    [Test]
    public void Direct_Import_Fixture_With_Complete_Evidence_Should_Pass()
    {
        var analysis = ProjectReferenceImportGuard.AnalyzeFixture(CreateValidFixture());

        Assert.That(analysis.Violations, Is.Empty);
        Assert.That(analysis.TopologicalOrder, Is.EqualTo(new[] { "D", "C", "B", "A" }));
    }

    [Test]
    public void Unused_Edge_Fixture_Should_Fail()
    {
        var fixture = CreateValidFixture() with
        {
            SymbolUses = CreateValidFixture().SymbolUses
                .Where(use => use.EdgeIdentity != "A -> B")
                .ToArray()
        };

        var analysis = ProjectReferenceImportGuard.AnalyzeFixture(fixture);

        Assert.That(analysis.Violations, Does.Contain("Unused project-reference edge: A -> B"));
    }

    [Test]
    public void Omitted_Needed_Edge_Fixture_Should_Fail()
    {
        var fixture = CreateValidFixture() with
        {
            SymbolUses =
            [
                .. CreateValidFixture().SymbolUses,
                new ProjectImportUse("D", "A", "D/Consumer.cs", 7, "A.Contract")
            ]
        };

        var analysis = ProjectReferenceImportGuard.AnalyzeFixture(fixture);

        Assert.That(analysis.Violations, Does.Contain("Missing project-reference edge required by source import: D -> A"));
    }

    [Test]
    public void Transitive_Reliance_Fixture_Should_Fail()
    {
        var fixture = CreateValidFixture() with
        {
            SymbolUses =
            [
                .. CreateValidFixture().SymbolUses,
                new ProjectImportUse("A", "C", "A/Consumer.cs", 8, "C.Contract")
            ]
        };

        var analysis = ProjectReferenceImportGuard.AnalyzeFixture(fixture);

        Assert.That(
            analysis.Violations,
            Does.Contain("Transitive project-reference reliance: A -> C via A -> B -> C"));
    }

    [Test]
    public void Forbidden_Edge_Fixture_Should_Fail()
    {
        var fixture = CreateValidFixture() with
        {
            EdgeIdentities = [.. CreateValidFixture().EdgeIdentities, "A -> D"],
            SymbolUses =
            [
                .. CreateValidFixture().SymbolUses,
                new ProjectImportUse("A", "D", "A/Consumer.cs", 9, "D.Contract")
            ],
            ForbiddenEdgeIdentities = ["A -> D"]
        };

        var analysis = ProjectReferenceImportGuard.AnalyzeFixture(fixture);

        Assert.That(analysis.Violations, Does.Contain("Forbidden project-reference edge: A -> D"));
    }

    [Test]
    public void Duplicate_Edge_Fixture_Should_Fail()
    {
        var fixture = CreateValidFixture() with
        {
            EdgeIdentities = [.. CreateValidFixture().EdgeIdentities, "A -> B"]
        };

        var analysis = ProjectReferenceImportGuard.AnalyzeFixture(fixture);

        Assert.That(analysis.Violations, Does.Contain("Duplicate project-reference edge: A -> B"));
    }

    [Test]
    public void Cyclic_Edge_Fixture_Should_Fail()
    {
        var fixture = CreateValidFixture() with
        {
            EdgeIdentities = [.. CreateValidFixture().EdgeIdentities, "D -> A"],
            SymbolUses =
            [
                .. CreateValidFixture().SymbolUses,
                new ProjectImportUse("D", "A", "D/Consumer.cs", 10, "A.Contract")
            ]
        };

        var analysis = ProjectReferenceImportGuard.AnalyzeFixture(fixture);

        Assert.That(analysis.Violations, Does.Contain("Project-reference cycle: A -> B -> C -> D -> A"));
    }

    [Test]
    public void Topological_Order_Drift_Fixture_Should_Fail()
    {
        var fixture = CreateValidFixture() with
        {
            ExpectedTopologicalOrder = ["C", "D", "B", "A"]
        };

        var analysis = ProjectReferenceImportGuard.AnalyzeFixture(fixture);

        Assert.That(
            analysis.Violations,
            Does.Contain("Topological order drift: expected C -> D -> B -> A; actual D -> C -> B -> A"));
    }

    private static ProjectImportFixture CreateValidFixture()
    {
        return new ProjectImportFixture(
            ProjectNames: ["A", "B", "C", "D"],
            EdgeIdentities: ["A -> B", "B -> C", "C -> D"],
            SymbolUses:
            [
                new ProjectImportUse("A", "B", "A/Consumer.cs", 3, "B.Contract"),
                new ProjectImportUse("B", "C", "B/Consumer.cs", 4, "C.Contract")
            ],
            AnalyzerEdgeIdentities: ["C -> D"],
            ForbiddenEdgeIdentities: [],
            ExpectedTopologicalOrder: ["D", "C", "B", "A"]);
    }

    private static string ResolveRepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        return Directory.GetParent(Path.GetDirectoryName(sourceFilePath)!)!.FullName;
    }
}
