using Microsoft.CodeAnalysis.CSharp;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ModuleBoundaryDebtProductionScanGuardTests
{
    private static readonly string[] ExactProductionProjects =
    [
        "LgymApi.Application",
        "LgymApi.Domain",
        "LgymApi.Platform",
        "LgymApi.Identity",
        "LgymApi.TrainingPlanning",
        "LgymApi.Notifications"
    ];

    [Test]
    public void Production_Scan_Should_Compile_Every_Source_From_The_Exact_Six_Assemblies()
    {
        var scan = ModuleBoundaryProductionScan.Prepare();

        TestContext.Progress.WriteLine($"Canonical module-boundary production scan: {scan.DescribeSourceTreeCounts()}; total={scan.SyntaxTrees.Count}.");

        Assert.Multiple(() =>
        {
            Assert.That(scan.SourceTreeCounts.Keys, Is.EqualTo(ExactProductionProjects));
            Assert.That(scan.SourceTreeCounts.Values, Has.All.GreaterThan(0));
            Assert.That(scan.SourceTreeCounts.Values.Sum(), Is.EqualTo(scan.SyntaxTrees.Count));
        });
    }

    [Test]
    public void Production_Scan_Should_Reject_An_Omitted_Assembly()
    {
        var omittedNotifications = ExactProductionProjects
            .Where(project => project != "LgymApi.Notifications")
            .ToArray();

        Assert.That(
            () => ModuleBoundaryProductionScan.AssertExactProjectCoverage(omittedNotifications),
            Throws.TypeOf<AssertionException>()
                .With.Message.Contains("LgymApi.Notifications"));
    }

    [Test]
    public void Production_Scan_Should_Reject_Wildcard_Assembly_Inputs()
    {
        var wildcardProjects = ExactProductionProjects
            .Take(ExactProductionProjects.Length - 1)
            .Append("LgymApi.*")
            .ToArray();

        Assert.That(
            () => ModuleBoundaryProductionScan.AssertExactProjectCoverage(wildcardProjects),
            Throws.TypeOf<AssertionException>()
                .With.Message.Contains("LgymApi.Notifications"));
    }

    [Test]
    public void Empty_Registry_Should_Not_Hide_A_Relocated_Production_Violation()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var relocatedTree = CSharpSyntaxTree.ParseText(
            "namespace LgymApi.Application.Features.Reporting.Relocated; internal sealed class HiddenReportingDebt { }",
            path: Path.Combine(repoRoot, "LgymApi.Application", "Relocated", "HiddenReportingDebt.cs"));
        var observedViolation = new ModuleBoundaryObservedViolation(
            nameof(ModuleDependencyGuardTests),
            ArchitectureTestHelpers.ReportingModuleName,
            ArchitectureTestHelpers.WorkoutProgressModuleName,
            "LgymApi.Application/Relocated/HiddenReportingDebt.cs",
            "LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration.FormerWorkoutDependency");

        Assert.Multiple(() =>
        {
            Assert.That(ModuleBoundaryDebtAllowlistRegistry.AllEntries, Is.Empty);
            Assert.That(
                ModuleBoundaryProductionScan.ResolveCanonicalModule(relocatedTree, repoRoot),
                Is.EqualTo(ArchitectureTestHelpers.ReportingModuleName));
            Assert.That(
                () => ModuleBoundaryDebtAllowlistRegistry.AssertNoUnexpectedViolations(
                    nameof(ModuleDependencyGuardTests),
                    [observedViolation]),
                Throws.TypeOf<AssertionException>());
        });
    }

    [Test]
    public void Helper_Named_Production_Path_Should_Not_Be_A_Classifier_Exception()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var helperPath = Path.Combine(
            repoRoot,
            "LgymApi.Application",
            "Features",
            "Reporting",
            "Helpers",
            "HiddenReportingDebt.cs");

        Assert.That(
            ArchitectureTestHelpers.ClassifyModuleBoundaryFile(helperPath, repoRoot).IsProductionCode,
            Is.True);
    }
}
