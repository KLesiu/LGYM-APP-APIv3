using Microsoft.CodeAnalysis.CSharp;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ModuleBoundaryArchitectureTestHelpersTests
{
    private static readonly object[] PlatformSubBoundaryPathCases =
    {
        new object[]
        {
            Path.Combine("LgymApi.Application", "Common", "Results", "Result.cs"),
            PlatformSubBoundary.BuildingBlocks
        },
        new object[]
        {
            Path.Combine("LgymApi.Application", "BuildingBlocks", "Errors", "AppError.cs"),
            PlatformSubBoundary.BuildingBlocks
        },
        new object[]
        {
            Path.Combine("LgymApi.Application", "Platform", "Contracts", "Serialization", "SharedSerializationOptions.cs"),
            PlatformSubBoundary.TechnicalPlatform
        },
        new object[]
        {
            Path.Combine("LgymApi.Application", "Platform", "ServiceCollectionExtensions.cs"),
            PlatformSubBoundary.TechnicalPlatform
        },
        new object[]
        {
            Path.Combine("LgymApi.Application", "Repositories", "IAppConfigRepository.cs"),
            PlatformSubBoundary.ReferenceData
        },
        new object[]
        {
            Path.Combine("LgymApi.Application", "Platform", "ReferenceData", "AppConfig", "AppConfigService.cs"),
            PlatformSubBoundary.ReferenceData
        },
        new object[]
        {
            Path.Combine("LgymApi.Application", "Platform", "ReferenceData", "Enums", "EnumService.cs"),
            PlatformSubBoundary.ReferenceData
        },
        new object[]
        {
            Path.Combine("LgymApi.Application", "Platform", "ReferenceData", "Errors", "AppConfigErrors.cs"),
            PlatformSubBoundary.ReferenceData
        },
        new object[]
        {
            Path.Combine("LgymApi.Application", "Platform", "ReferenceData", "Errors", "EnumErrors.cs"),
            PlatformSubBoundary.ReferenceData
        }
    };

    private static readonly object[] IdentityErrorPathCases =
    {
        Path.Combine("LgymApi.Application", "Identity", "Errors", "UserErrors.cs"),
        Path.Combine("LgymApi.Application", "Identity", "Errors", "AdminUserErrors.cs"),
        Path.Combine("LgymApi.Application", "Identity", "Errors", "RoleErrors.cs"),
        Path.Combine("LgymApi.Application", "Identity", "Errors", "TutorialErrors.cs")
    };

    private static readonly object[] TrainingPlanningErrorPathCases =
    {
        Path.Combine("LgymApi.Application", "TrainingPlanning", "Errors", "PlanErrors.cs"),
        Path.Combine("LgymApi.Application", "TrainingPlanning", "Errors", "PlanDayErrors.cs")
    };

    private static readonly object[] MisplacedTrainingPlanningErrorPathCases =
    {
        Path.Combine("LgymApi.Application", "BuildingBlocks", "Errors", "PlanErrors.cs"),
        Path.Combine("LgymApi.Application", "WorkoutProgress", "Errors", "PlanErrors.cs")
    };

    private static readonly object[] CoachingErrorPathCases =
    {
        Path.Combine("LgymApi.Application", "Coaching", "Errors", "TrainerRelationshipErrors.cs")
    };

    private static readonly object[] ReportingErrorPathCases =
    {
        Path.Combine("LgymApi.Application", "Reporting", "Errors", "ReportingErrors.cs")
    };

    private static readonly object[] MisplacedReportingErrorPathCases =
    {
        Path.Combine("LgymApi.Application", "BuildingBlocks", "Errors", "ReportingErrors.cs"),
        Path.Combine("LgymApi.Application", "Platform", "Contracts", "Errors", "ReportingErrors.cs")
    };

    private static readonly object[] MisplacedWorkoutProgressEloPathCases =
    {
        Path.Combine("LgymApi.Application", "Common", "Training", "Elo", "IExerciseEloCalculator.cs"),
        Path.Combine("LgymApi.Application", "BuildingBlocks", "Training", "Elo", "IExerciseEloCalculator.cs")
    };

    private static readonly object[] NonProductionPathCases =
    {
        new object[]
        {
            Path.Combine("LgymApi.Api", "bin", "Debug", "net10.0", "Generated.cs"),
            ModuleBoundaryExclusionKind.BuildArtifact
        },
        new object[]
        {
            Path.Combine("LgymApi.UnitTests", "Users", "UsersServiceTests.cs"),
            ModuleBoundaryExclusionKind.TestProject
        },
        new object[]
        {
            Path.Combine("LgymApi.Application", "Users", "Helpers", "UsersModuleHelper.cs"),
            ModuleBoundaryExclusionKind.Helper
        },
        new object[]
        {
            Path.Combine("LgymApi.Infrastructure", "Users", "Migrations", "202607140001_Init.cs"),
            ModuleBoundaryExclusionKind.GeneratedCode
        }
    };

    [Test]
    public void ClassifyModuleBoundaryFile_Recognizes_Api_Feature_Production_File()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var filePath = Path.Combine(repoRoot, "LgymApi.Api", "Features", "User", "Controllers", "UserController.cs");

        var classification = ArchitectureTestHelpers.ClassifyModuleBoundaryFile(filePath, repoRoot);

        Assert.Multiple(() =>
        {
            Assert.That(classification.IsProductionCode, Is.True);
            Assert.That(classification.ExclusionKind, Is.Null);
            Assert.That(classification.ModuleName, Is.EqualTo("User"));
            Assert.That(classification.RelativePath, Is.EqualTo("LgymApi.Api/Features/User/Controllers/UserController.cs"));
        });
    }

    [TestCaseSource(nameof(NonProductionPathCases))]
    public void ClassifyModuleBoundaryFile_Excludes_NonProduction_Paths(string relativePath, ModuleBoundaryExclusionKind expected)
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var filePath = Path.Combine(repoRoot, relativePath);

        var classification = ArchitectureTestHelpers.ClassifyModuleBoundaryFile(filePath, repoRoot);

        Assert.Multiple(() =>
        {
            Assert.That(classification.IsExcluded, Is.True);
            Assert.That(classification.IsProductionCode, Is.False);
            Assert.That(classification.ExclusionKind, Is.EqualTo(expected));
        });
    }

    [Test]
    public void PrepareCompilation_Uses_Only_Production_Source_Files_For_ModuleBoundary_Guards()
    {
        var (_, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Api");

        Assert.Multiple(() =>
        {
            Assert.That(syntaxTrees, Is.Not.Empty);
            Assert.That(compilation, Is.TypeOf<CSharpCompilation>());
            Assert.That(syntaxTrees.All(tree => ArchitectureTestHelpers.ClassifyModuleBoundaryFile(tree.FilePath).IsProductionCode), Is.True);
        });
    }

    [TestCaseSource(nameof(PlatformSubBoundaryPathCases))]
    public void Platform_SubBoundary_Paths_Should_Retain_The_Canonical_Platform_Owner(
        string relativePath,
        PlatformSubBoundary expectedSubBoundary)
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var filePath = Path.Combine(repoRoot, relativePath);

        var classification = ArchitectureTestHelpers.ClassifyModuleBoundaryFile(filePath, repoRoot);

        Assert.Multiple(() =>
        {
            Assert.That(classification.PlatformSubBoundary, Is.EqualTo(expectedSubBoundary));
            Assert.That(
                ArchitectureTestHelpers.GetCanonicalModuleNameFromPath(filePath),
                Is.EqualTo(ArchitectureTestHelpers.PlatformModuleName));
        });
    }

    [TestCaseSource(nameof(IdentityErrorPathCases))]
    public void Identity_Owned_Error_Paths_Should_Resolve_To_Identity_Accounts(string relativePath)
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var filePath = Path.Combine(repoRoot, relativePath);

        Assert.That(
            ArchitectureTestHelpers.GetCanonicalModuleNameFromPath(filePath),
            Is.EqualTo(ArchitectureTestHelpers.IdentityModuleName));
    }

    [TestCaseSource(nameof(TrainingPlanningErrorPathCases))]
    public void Training_Planning_Owned_Error_Paths_Should_Resolve_To_Training_Planning(string relativePath)
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var filePath = Path.Combine(repoRoot, relativePath);

        AssertTrainingPlanningErrorOwnership(filePath);
    }

    [TestCaseSource(nameof(MisplacedTrainingPlanningErrorPathCases))]
    public void Training_Planning_Error_Ownership_Fixtures_Outside_Training_Planning_Are_Rejected(string relativePath)
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var filePath = Path.Combine(repoRoot, relativePath);

        Assert.That(() => AssertTrainingPlanningErrorOwnership(filePath), Throws.TypeOf<AssertionException>());
    }

    [TestCaseSource(nameof(CoachingErrorPathCases))]
    public void Coaching_Owned_Error_Paths_Should_Resolve_To_Coaching(string relativePath)
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var filePath = Path.Combine(repoRoot, relativePath);

        AssertCoachingErrorOwnership(filePath);
    }

    [TestCaseSource(nameof(ReportingErrorPathCases))]
    public void Reporting_Owned_Error_Paths_Should_Resolve_To_Reporting(string relativePath)
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var filePath = Path.Combine(repoRoot, relativePath);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(filePath), Is.True, "Reporting errors must exist at their Reporting-owned path.");
            AssertReportingErrorOwnership(filePath);
            Assert.That(ArchitectureTestHelpers.GetPlatformSubBoundaryFromPath(filePath), Is.Null);
        });
    }

    [TestCaseSource(nameof(MisplacedReportingErrorPathCases))]
    public void Reporting_Error_Ownership_Fixtures_Outside_Reporting_Are_Rejected(string relativePath)
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var filePath = Path.Combine(repoRoot, relativePath);

        Assert.That(() => AssertReportingErrorOwnership(filePath), Throws.TypeOf<AssertionException>());
    }

    [Test]
    public void Reporting_Error_Source_Should_Not_Remain_In_Common()
    {
        var path = Path.Combine(
            ArchitectureTestHelpers.ResolveRepositoryRoot(),
            "LgymApi.Application",
            "Common",
            "Errors",
            "ReportingErrors.cs");

        Assert.That(File.Exists(path), Is.False, "Reporting errors must not retain Common compatibility sources.");
    }

    [TestCase("AppConfigErrors.cs")]
    [TestCase("EnumErrors.cs")]
    public void Reference_Data_Error_Sources_Should_Not_Remain_In_Common(string fileName)
    {
        var path = Path.Combine(
            ArchitectureTestHelpers.ResolveRepositoryRoot(),
            "LgymApi.Application",
            "Common",
            "Errors",
            fileName);

        Assert.That(File.Exists(path), Is.False, "Reference Data errors must not retain Common compatibility sources.");
    }

    [Test]
    public void Supplementation_Error_Path_Should_Resolve_To_Nutrition()
    {
        var path = Path.Combine(
            ArchitectureTestHelpers.ResolveRepositoryRoot(),
            "LgymApi.Application",
            "Nutrition",
            "Errors",
            "SupplementationErrors.cs");

        AssertNutritionErrorOwnership(path);
    }

    [TestCase("BuildingBlocks")]
    [TestCase("Reporting")]
    public void Supplementation_Error_Placement_Outside_Nutrition_Is_Rejected(string module)
    {
        var path = Path.Combine(
            ArchitectureTestHelpers.ResolveRepositoryRoot(),
            "LgymApi.Application",
            module,
            "Errors",
            "SupplementationErrors.cs");

        Assert.That(() => AssertNutritionErrorOwnership(path), Throws.TypeOf<AssertionException>());
    }

    [Test]
    public void Supplementation_Error_Source_Should_Not_Remain_In_Common()
    {
        var path = Path.Combine(
            ArchitectureTestHelpers.ResolveRepositoryRoot(),
            "LgymApi.Application",
            "Common",
            "Errors",
            "SupplementationErrors.cs");

        Assert.That(File.Exists(path), Is.False, "Nutrition errors must not retain Common compatibility sources.");
    }

    [Test]
    public void Coaching_Error_Under_Platform_ReferenceData_Is_Rejected()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var filePath = Path.Combine(
            repoRoot,
            "LgymApi.Application",
            "Platform",
            "ReferenceData",
            "Errors",
            "TrainerRelationshipErrors.cs");

        Assert.That(() => AssertCoachingErrorOwnership(filePath), Throws.TypeOf<AssertionException>());
    }

    [Test]
    public void Coaching_Error_Source_Should_Not_Remain_In_Common()
    {
        var path = Path.Combine(
            ArchitectureTestHelpers.ResolveRepositoryRoot(),
            "LgymApi.Application",
            "Common",
            "Errors",
            "TrainerRelationshipErrors.cs");

        Assert.That(File.Exists(path), Is.False, "Coaching errors must not retain Common compatibility sources.");
    }

    [Test]
    public void Workout_Progress_Elo_Path_Should_Resolve_To_Workout_Progress()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var filePath = Path.Combine(
            repoRoot,
            "LgymApi.Application",
            "WorkoutProgress",
            "Scoring",
            "Elo",
            "IExerciseEloCalculator.cs");

        AssertWorkoutProgressEloOwnership(filePath);
    }

    [TestCaseSource(nameof(MisplacedWorkoutProgressEloPathCases))]
    public void Workout_Progress_Elo_Ownership_Fixtures_Outside_Workout_Progress_Are_Rejected(string relativePath)
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var filePath = Path.Combine(repoRoot, relativePath);

        Assert.That(() => AssertWorkoutProgressEloOwnership(filePath), Throws.TypeOf<AssertionException>());
    }

    [Test]
    public void Canonical_Module_Catalog_Should_Reject_A_Ninth_SubBoundary_Module()
    {
        var canonicalModules = ArchitectureTestHelpers.GetCanonicalModuleCatalog();
        var ninthModuleFixture = canonicalModules.Append("Reference Data").ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(canonicalModules, Has.Count.EqualTo(8));
            Assert.That(
                () => ArchitectureTestHelpers.ValidateCanonicalModuleCatalog(ninthModuleFixture),
                Throws.InvalidOperationException.With.Message.Contains("exactly the established eight modules"));
        });
    }

    private static void AssertTrainingPlanningErrorOwnership(string filePath)
    {
        Assert.That(
            ArchitectureTestHelpers.GetCanonicalModuleNameFromPath(filePath),
            Is.EqualTo(ArchitectureTestHelpers.TrainingPlanningModuleName));
    }

    private static void AssertWorkoutProgressEloOwnership(string filePath)
    {
        Assert.That(
            ArchitectureTestHelpers.GetCanonicalModuleNameFromPath(filePath),
            Is.EqualTo(ArchitectureTestHelpers.WorkoutProgressModuleName));
    }

    private static void AssertCoachingErrorOwnership(string filePath)
    {
        Assert.That(
            ArchitectureTestHelpers.GetCanonicalModuleNameFromPath(filePath),
            Is.EqualTo(ArchitectureTestHelpers.CoachingModuleName));
    }

    private static void AssertReportingErrorOwnership(string filePath)
    {
        Assert.That(
            ArchitectureTestHelpers.GetCanonicalModuleNameFromPath(filePath),
            Is.EqualTo(ArchitectureTestHelpers.ReportingModuleName));
    }

    private static void AssertNutritionErrorOwnership(string filePath)
    {
        Assert.That(
            ArchitectureTestHelpers.GetCanonicalModuleNameFromPath(filePath),
            Is.EqualTo(ArchitectureTestHelpers.NutritionModuleName));
    }
}
