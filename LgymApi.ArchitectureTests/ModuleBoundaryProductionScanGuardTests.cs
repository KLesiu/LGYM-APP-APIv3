using Microsoft.CodeAnalysis.CSharp;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ModuleBoundaryProductionScanGuardTests
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
    public void Relocated_Production_Source_Should_Be_Classified_And_Fail_Directly()
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
        var assertionFailure = Assert.Throws<AssertionException>(() => Assert.That(
            new[] { observedViolation },
            Is.Empty,
            ModuleBoundaryObservedViolation.DescribeAll([observedViolation])));

        Assert.Multiple(() =>
        {
            Assert.That(
                ModuleBoundaryProductionScan.ResolveCanonicalModule(relocatedTree, repoRoot),
                Is.EqualTo(ArchitectureTestHelpers.ReportingModuleName));
            Assert.That(assertionFailure!.Message, Does.Contain("Source module: Reporting"));
            Assert.That(assertionFailure.Message, Does.Contain("Target module: Workout & Progress"));
            Assert.That(assertionFailure.Message, Does.Contain("LgymApi.Application/Relocated/HiddenReportingDebt.cs"));
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

    [Test]
    public void Api_Adapter_Dependency_Contracts_Should_Match_Only_Their_Exact_Source_And_Target()
    {
        const string identityAdapter = "LgymApi.Application/Identity/ApiAdapters/IdentityApiAdapters.cs";
        const string appConfigAdapter = "LgymApi.Application/Platform/ReferenceData/ApiAdapters/AppConfigApiAdapter.cs";

        Assert.Multiple(() =>
        {
            Assert.That(
                ArchitectureTestHelpers.MatchesApiAdapterDependencyContract(identityAdapter, "LgymApi.Application.Features.EloRegistry.IEloRegistryService"),
                Is.True);
            Assert.That(
                ArchitectureTestHelpers.MatchesApiAdapterDependencyContract(appConfigAdapter, "LgymApi.Identity.Contracts.AccountReference"),
                Is.True);
            Assert.That(
                ArchitectureTestHelpers.MatchesApiAdapterDependencyContract(identityAdapter, "LgymApi.Application.Repositories.IEloRegistryRepository"),
                Is.False);
            Assert.That(
                ArchitectureTestHelpers.MatchesApiAdapterDependencyContract("LgymApi.Application/Identity/Profile/UserProfileService.cs", "LgymApi.Application.Features.EloRegistry.IEloRegistryService"),
                Is.False);
        });
    }
}
