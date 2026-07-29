namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ModuleBoundaryDebtAllowlistTests
{
    private const string GuardId = "ModuleDependencyGuardTests";
    private const string CrossModuleGuardId = "CrossModuleEntityLeakage";

    private static readonly IReadOnlyList<ModuleBoundaryObservedViolation> RetiredTrainingPlanningDebtBaseline =
    [
        RetiredPlanningViolation(GuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "LgymApi.Application.Features.PlanDay.IPlanDayServiceDependencies @ LgymApi.Application/PlanDay/IPlanDayServiceDependencies.cs", "LgymApi.Application.Repositories.IExerciseRepository @ LgymApi.Application/Repositories/IExerciseRepository.cs"),
        RetiredPlanningViolation(GuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "LgymApi.Application.Features.PlanDay.IPlanDayServiceDependencies @ LgymApi.Application/PlanDay/IPlanDayServiceDependencies.cs", "LgymApi.Application.Repositories.ITrainingRepository @ LgymApi.Application/Repositories/ITrainingRepository.cs"),
        RetiredPlanningViolation(GuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "LgymApi.Application.Features.PlanDay.PlanDayService @ LgymApi.Application/PlanDay/PlanDayService.cs", "LgymApi.Application.Repositories.IExerciseRepository @ LgymApi.Application/Repositories/IExerciseRepository.cs"),
        RetiredPlanningViolation(GuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "LgymApi.Application.Features.PlanDay.PlanDayService @ LgymApi.Application/PlanDay/PlanDayService.cs", "LgymApi.Application.Repositories.ITrainingRepository @ LgymApi.Application/Repositories/ITrainingRepository.cs"),
        RetiredPlanningViolation(GuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "LgymApi.Application.Features.PlanDay.PlanDayServiceDependencies @ LgymApi.Application/PlanDay/IPlanDayServiceDependencies.cs", "LgymApi.Application.Repositories.IExerciseRepository @ LgymApi.Application/Repositories/IExerciseRepository.cs"),
        RetiredPlanningViolation(GuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "LgymApi.Application.Features.PlanDay.PlanDayServiceDependencies @ LgymApi.Application/PlanDay/IPlanDayServiceDependencies.cs", "LgymApi.Application.Repositories.ITrainingRepository @ LgymApi.Application/Repositories/ITrainingRepository.cs"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.IdentityModuleName, "CanAccessPlanAsync", "LgymApi.Domain.Entities.User"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.IdentityModuleName, "CreatePlanDayAsync", "LgymApi.Domain.Entities.User"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.IdentityModuleName, "DeletePlanDayAsync", "LgymApi.Domain.Entities.User"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.IdentityModuleName, "GetPlanDayAsync", "LgymApi.Domain.Entities.User"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.IdentityModuleName, "GetPlanDaysAsync", "LgymApi.Domain.Entities.User"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.IdentityModuleName, "GetPlanDaysInfoAsync", "LgymApi.Domain.Entities.User"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.IdentityModuleName, "GetPlanDaysTypesAsync", "LgymApi.Domain.Entities.User"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.IdentityModuleName, "LgymApi.Application/PlanDay/IPlanDayService.cs", "LgymApi.Domain.Entities.User"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.IdentityModuleName, "LgymApi.Application/PlanDay/PlanDayService.Mutations.cs", "LgymApi.Domain.Entities.User"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.IdentityModuleName, "LgymApi.Application/PlanDay/PlanDayService.Queries.cs", "LgymApi.Domain.Entities.User"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.IdentityModuleName, "LgymApi.Application/PlanDay/PlanDayService.cs", "LgymApi.Domain.Entities.User"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.IdentityModuleName, "UpdatePlanDayAsync", "LgymApi.Domain.Entities.User"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "ExerciseMap", "LgymApi.Domain.Entities.Exercise"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "ExerciseRepository", "LgymApi.Application.Repositories.IExerciseRepository"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "GetPlanDayAsync", "LgymApi.Application.Repositories.IExerciseRepository"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "GetPlanDayAsync", "LgymApi.Domain.Entities.Exercise"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "GetPlanDaysAsync", "LgymApi.Application.Repositories.IExerciseRepository"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "GetPlanDaysAsync", "LgymApi.Domain.Entities.Exercise"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "GetPlanDaysInfoAsync", "LgymApi.Application.Repositories.ITrainingRepository"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "LgymApi.Application/PlanDay/Models/PlanDayDetailsContext.cs", "LgymApi.Domain.Entities.Exercise"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "LgymApi.Application/PlanDay/Models/PlanDaysContext.cs", "LgymApi.Domain.Entities.Exercise"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "PlanDayServiceDependencies", "LgymApi.Application.Repositories.IExerciseRepository"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "PlanDayServiceDependencies", "LgymApi.Application.Repositories.ITrainingRepository"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "PlanDayService", "LgymApi.Application.Repositories.IExerciseRepository"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "PlanDayService", "LgymApi.Application.Repositories.ITrainingRepository"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "TrainingRepository", "LgymApi.Application.Repositories.ITrainingRepository"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "_exerciseRepository", "LgymApi.Application.Repositories.IExerciseRepository"),
        RetiredPlanningViolation(CrossModuleGuardId, ArchitectureTestHelpers.WorkoutProgressModuleName, "_trainingRepository", "LgymApi.Application.Repositories.ITrainingRepository")
    ];

    [Test]
    public void Allowlist_Registry_Remains_Centralized_And_Contains_No_Approved_Debt_Entries()
    {
        var entries = ModuleBoundaryDebtAllowlistRegistry.AllEntries;

        Assert.Multiple(() =>
        {
            Assert.That(ModuleBoundaryDebtAllowlistRegistry.MaximumAllowedEntryCount, Is.Zero, "The approved debt maximum must remain a literal zero baseline.");
            Assert.That(entries, Is.Empty, "All currently approved module-boundary debt must be retired.");
            Assert.That(entries, Has.Count.EqualTo(ModuleBoundaryDebtAllowlistRegistry.MaximumAllowedEntryCount), "The approved debt baseline must remain exact.");
            Assert.That(entries.All(entry => !string.IsNullOrWhiteSpace(entry.Key.Rationale)), Is.True, "Every centralized debt entry must stay reviewable with a rationale.");
            Assert.That(entries.Select(entry => entry.IdentityKey).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(entries.Count), "Centralized debt entries must stay exact and non-duplicated.");
        });
    }

    [Test]
    public void Retired_TrainingPlanning_Baseline_Should_Account_For_Exactly_22_Workout_And_12_Identity_Entries()
    {
        var exactKeys = RetiredTrainingPlanningDebtBaseline
            .Select(violation => ModuleBoundaryDebtKey.Create(
                violation.GuardId,
                violation.SourceModule,
                violation.TargetModule,
                violation.SourceSymbolOrPath,
                violation.TargetSymbolOrPath,
                "Task 20 retired baseline evidence"))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(RetiredTrainingPlanningDebtBaseline, Has.Count.EqualTo(34));
            Assert.That(exactKeys.Select(key => key.IdentityKey), Is.Unique);
            Assert.That(RetiredTrainingPlanningDebtBaseline, Has.All.Matches<ModuleBoundaryObservedViolation>(violation =>
                violation.SourceModule == ArchitectureTestHelpers.TrainingPlanningModuleName));
            Assert.That(RetiredTrainingPlanningDebtBaseline.Count(violation => violation.TargetModule == ArchitectureTestHelpers.WorkoutProgressModuleName), Is.EqualTo(22));
            Assert.That(RetiredTrainingPlanningDebtBaseline.Count(violation => violation.TargetModule == ArchitectureTestHelpers.IdentityModuleName), Is.EqualTo(12));
            Assert.That(RetiredTrainingPlanningDebtBaseline.Count(violation => violation.GuardId == GuardId), Is.EqualTo(6));
            Assert.That(RetiredTrainingPlanningDebtBaseline.Count(violation => violation.GuardId == CrossModuleGuardId && violation.TargetModule == ArchitectureTestHelpers.WorkoutProgressModuleName), Is.EqualTo(16));
            Assert.That(RetiredTrainingPlanningDebtBaseline.Count(violation => violation.GuardId == CrossModuleGuardId && violation.TargetModule == ArchitectureTestHelpers.IdentityModuleName), Is.EqualTo(12));
        });
    }

    [Test]
    public void Every_Retired_TrainingPlanning_Baseline_Entry_Should_Remain_Unexpected_At_Zero_Debt()
    {
        var evaluations = RetiredTrainingPlanningDebtBaseline
            .Select(violation => ModuleBoundaryDebtAllowlistEvaluator.Evaluate([], [violation], violation.GuardId))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(evaluations, Has.All.Matches<ModuleBoundaryDebtAllowlistEvaluation>(evaluation => !evaluation.IsSuccess));
            Assert.That(evaluations, Has.All.Matches<ModuleBoundaryDebtAllowlistEvaluation>(evaluation => evaluation.UnexpectedViolations.Count == 1));
            Assert.That(
                ModuleBoundaryDebtAllowlistRegistry.AllEntries.Where(entry => entry.Key.SourceModule == ArchitectureTestHelpers.TrainingPlanningModuleName),
                Is.Empty);
        });
    }

    [Test]
    public void Allowlist_Evaluation_Allows_Exact_Normalized_Current_Match()
    {
        var entry = new ModuleBoundaryDebtEntry(
            ModuleBoundaryDebtKey.Create(
                GuardId,
                " Nutrition ",
                " User ",
                " LgymApi.Application\\Nutrition\\Plans\\PlanService.cs ",
                " LgymApi.Application\\User\\IUserService.cs ",
                "issue-379 debt"));

        var observedViolation = new ModuleBoundaryObservedViolation(
            GuardId,
            "Nutrition",
            "User",
            "LgymApi.Application/Nutrition/Plans/PlanService.cs",
            "LgymApi.Application/User/IUserService.cs");

        var evaluation = ModuleBoundaryDebtAllowlistEvaluator.EvaluateForTesting(
            [entry],
            [observedViolation],
            null,
            maximumAllowedEntryCount: 1);

        Assert.That(evaluation.IsSuccess, Is.True, evaluation.BuildFailureMessage());
    }

    [Test]
    public void Allowlist_Evaluation_Fails_When_A_Live_Violation_Is_Not_Exactly_Allowlisted()
    {
        var observedViolation = new ModuleBoundaryObservedViolation(
            GuardId,
            "Nutrition",
            "User",
            "LgymApi.Application/Nutrition/Plans/PlanService.cs",
            "LgymApi.Application/User/IUserService.cs");

        var evaluation = ModuleBoundaryDebtAllowlistEvaluator.Evaluate([], [observedViolation], GuardId);

        Assert.Multiple(() =>
        {
            Assert.That(evaluation.IsSuccess, Is.False);
            Assert.That(evaluation.UnexpectedViolations, Has.Count.EqualTo(1));
            Assert.That(evaluation.BuildFailureMessage(), Does.Contain("New module-boundary violations"));
            Assert.That(evaluation.BuildFailureMessage(), Does.Contain("Rule: ModuleDependencyGuardTests"));
            Assert.That(evaluation.BuildFailureMessage(), Does.Contain("Source module: Nutrition"));
            Assert.That(evaluation.BuildFailureMessage(), Does.Contain("Target module: User"));
            Assert.That(evaluation.BuildFailureMessage(), Does.Contain("Source symbol/file: LgymApi.Application/Nutrition/Plans/PlanService.cs"));
        });
    }

    [TestCase("CrossModuleEntityLeakage", "Identity & Accounts", "Workout & Progress", "BuildAsync", "LgymApi.Application.Repositories.IEloRegistryRepository")]
    [TestCase("CrossModuleEntityLeakage", "Identity & Accounts", "Workout & Progress", "CheckTokenAsync", "LgymApi.Application.Repositories.IEloRegistryRepository")]
    [TestCase("CrossModuleEntityLeakage", "Identity & Accounts", "Workout & Progress", "GetUserEloAsync", "LgymApi.Application.Repositories.IEloRegistryRepository")]
    [TestCase("CrossModuleEntityLeakage", "Identity & Accounts", "Workout & Progress", "LoginCoreAsync", "LgymApi.Application.Repositories.IEloRegistryRepository")]
    [TestCase("CrossModuleEntityLeakage", "Identity & Accounts", "Workout & Progress", "LoginResultBuilder", "LgymApi.Application.Repositories.IEloRegistryRepository")]
    [TestCase("CrossModuleEntityLeakage", "Identity & Accounts", "Workout & Progress", "RegisterCoreAsync", "LgymApi.Application.Repositories.IEloRegistryRepository")]
    [TestCase("CrossModuleEntityLeakage", "Identity & Accounts", "Workout & Progress", "UserServiceDependencies", "LgymApi.Application.Repositories.IEloRegistryRepository")]
    [TestCase("CrossModuleEntityLeakage", "Identity & Accounts", "Workout & Progress", "UserService", "LgymApi.Application.Repositories.IEloRegistryRepository")]
    [TestCase("CrossModuleEntityLeakage", "Workout & Progress", "Identity & Accounts", "EloRegistryService", "LgymApi.Application.Repositories.IUserRepository")]
    [TestCase("CrossModuleEntityLeakage", "Workout & Progress", "Identity & Accounts", "GetChartAsync", "LgymApi.Application.Repositories.IUserRepository")]
    [TestCase("ModuleDependencyGuardTests", "Identity & Accounts", "Workout & Progress", "LgymApi.Application.ExternalAuth.LoginResultBuilder @ LgymApi.Application/ExternalAuth/LoginResultBuilder.cs", "LgymApi.Application.Repositories.IEloRegistryRepository @ LgymApi.Application/Repositories/IEloRegistryRepository.cs")]
    [TestCase("ModuleDependencyGuardTests", "Identity & Accounts", "Workout & Progress", "LgymApi.Application.Features.User.IUserServiceDependencies @ LgymApi.Application/User/IUserServiceDependencies.cs", "LgymApi.Application.Repositories.IEloRegistryRepository @ LgymApi.Application/Repositories/IEloRegistryRepository.cs")]
    [TestCase("ModuleDependencyGuardTests", "Identity & Accounts", "Workout & Progress", "LgymApi.Application.Features.User.UserService @ LgymApi.Application/User/UserService.cs", "LgymApi.Application.Repositories.IEloRegistryRepository @ LgymApi.Application/Repositories/IEloRegistryRepository.cs")]
    [TestCase("ModuleDependencyGuardTests", "Identity & Accounts", "Workout & Progress", "LgymApi.Application.Features.User.UserServiceDependencies @ LgymApi.Application/User/UserServiceDependencies.cs", "LgymApi.Application.Repositories.IEloRegistryRepository @ LgymApi.Application/Repositories/IEloRegistryRepository.cs")]
    public void Allowlist_Evaluation_Rejects_Each_Eliminated_Canonical_Dependency(
        string guardId,
        string sourceModule,
        string targetModule,
        string sourceSymbolOrPath,
        string targetSymbolOrPath)
    {
        var observedViolation = new ModuleBoundaryObservedViolation(
            guardId,
            sourceModule,
            targetModule,
            sourceSymbolOrPath,
            targetSymbolOrPath);

        var evaluation = ModuleBoundaryDebtAllowlistEvaluator.Evaluate([], [observedViolation], guardId);

        Assert.Multiple(() =>
        {
            Assert.That(evaluation.IsSuccess, Is.False);
            Assert.That(evaluation.UnexpectedViolations, Has.Count.EqualTo(1));
            Assert.That(evaluation.UnexpectedViolations[0].IdentityKey, Is.EqualTo(observedViolation.IdentityKey));
        });
    }

    [Test]
    public void Allowlist_Evaluation_Fails_When_An_Allowlist_Entry_Becomes_Stale()
    {
        var entry = new ModuleBoundaryDebtEntry(
            ModuleBoundaryDebtKey.Create(
                GuardId,
                "Nutrition",
                "User",
                "LgymApi.Application/Nutrition/Plans/PlanService.cs",
                "LgymApi.Application/User/IUserService.cs",
                "issue-379 debt"));

        var evaluation = ModuleBoundaryDebtAllowlistEvaluator.EvaluateForTesting(
            [entry],
            [],
            GuardId,
            maximumAllowedEntryCount: 1);

        Assert.Multiple(() =>
        {
            Assert.That(evaluation.IsSuccess, Is.False);
            Assert.That(evaluation.StaleEntries, Has.Count.EqualTo(1));
            Assert.That(evaluation.BuildFailureMessage(), Does.Contain("Stale module-boundary allowlist entries"));
            Assert.That(evaluation.BuildFailureMessage(), Does.Contain("Rule: ModuleDependencyGuardTests"));
            Assert.That(evaluation.BuildFailureMessage(), Does.Contain("Source module: Nutrition"));
            Assert.That(evaluation.BuildFailureMessage(), Does.Contain("Rationale: issue-379 debt"));
        });
    }

    [Test]
    public void Allowlist_Registry_Excludes_Retired_Coaching_Boundary_Debt()
    {
        var retiredReferences = ModuleBoundaryDebtAllowlistRegistry.AllEntries
            .Where(entry => entry.Key.SourceModule == "Coaching"
                || entry.Key.TargetSymbolOrPath.Contains("ITrainerRelationshipRepository", StringComparison.Ordinal)
                || entry.Key.TargetSymbolOrPath.Contains("ITraineeNoteRepository", StringComparison.Ordinal))
            .ToArray();

        Assert.That(retiredReferences, Is.Empty);
    }

    [Test]
    public void Allowlist_Registry_Excludes_Retired_Reporting_To_Identity_Debt()
    {
        var retiredReferences = ModuleBoundaryDebtAllowlistRegistry.AllEntries
            .Where(entry => entry.Key.SourceModule == ArchitectureTestHelpers.ReportingModuleName
                && entry.Key.TargetModule == ArchitectureTestHelpers.IdentityModuleName)
            .ToArray();

        Assert.That(retiredReferences, Is.Empty);
    }

    [Test]
    public void Allowlist_Evaluation_Fails_For_Speculative_Or_Duplicate_Identity_Entries()
    {
        var firstEntry = new ModuleBoundaryDebtEntry(
            ModuleBoundaryDebtKey.Create(
                GuardId,
                "Nutrition",
                "User",
                "LgymApi.Application/Nutrition/Plans/PlanService.cs",
                "LgymApi.Application/User/IUserService.cs",
                "issue-379 debt"));

        var speculativeDuplicate = new ModuleBoundaryDebtEntry(
            ModuleBoundaryDebtKey.Create(
                GuardId,
                "Nutrition",
                "User",
                "LgymApi.Application/Nutrition/Plans/PlanService.cs",
                "LgymApi.Application/User/IUserService.cs",
                "future debt"));

        var act = () => ModuleBoundaryDebtAllowlistEvaluator.EvaluateForTesting(
            [firstEntry, speculativeDuplicate],
            [],
            GuardId,
            maximumAllowedEntryCount: 2);

        Assert.That(
            act,
            Throws.TypeOf<AssertionException>()
                .With.Message.Contains("duplicate identity matches"));
    }

    [Test]
    public void Allowlist_Evaluation_Fails_For_A_Broad_Wildcard_Entry()
    {
        var broadEntry = new ModuleBoundaryDebtEntry(
            new ModuleBoundaryDebtKey(
                GuardId,
                "Nutrition",
                "User",
                "LgymApi.Application/Nutrition/Plans/*.cs",
                "LgymApi.Application/User/IUserService.cs",
                "broad exemption"));

        var act = () => ModuleBoundaryDebtAllowlistEvaluator.EvaluateForTesting(
            [broadEntry],
            [],
            GuardId,
            maximumAllowedEntryCount: 1);

        Assert.That(
            act,
            Throws.TypeOf<AssertionException>()
                .With.Message.Contains("wildcard"));
    }

    [Test]
    public void Allowlist_Evaluation_Fails_When_Entries_Grow_Beyond_The_Approved_Baseline()
    {
        var entries = Enumerable.Range(0, ModuleBoundaryDebtAllowlistRegistry.MaximumAllowedEntryCount + 1)
            .Select(index => new ModuleBoundaryDebtEntry(
                ModuleBoundaryDebtKey.Create(
                    GuardId,
                    "Nutrition",
                    "User",
                    $"LgymApi.Application/Nutrition/Plans/PlanService{index}.cs",
                    "LgymApi.Application/User/IUserService.cs",
                    "approved debt")))
            .ToList();

        var observedViolations = entries
            .Select(entry => new ModuleBoundaryObservedViolation(
                entry.Key.GuardId,
                entry.Key.SourceModule,
                entry.Key.TargetModule,
                entry.Key.SourceSymbolOrPath,
                entry.Key.TargetSymbolOrPath))
            .ToList();

        var act = () => ModuleBoundaryDebtAllowlistEvaluator.Evaluate(entries, observedViolations, GuardId);

        Assert.That(
            act,
            Throws.TypeOf<AssertionException>()
                .With.Message.Contains("must not grow"));
    }

    [Test]
    public void Owner_Rekey_Should_Remain_Stale_And_Unexpected_Even_When_Source_And_Target_Are_Unchanged()
    {
        var originalEntry = new ModuleBoundaryDebtEntry(
            ModuleBoundaryDebtKey.Create(
                GuardId,
                "Nutrition",
                "User",
                "LgymApi.Application/Nutrition/Plans/PlanService.cs",
                "LgymApi.Application/User/IUserService.cs",
                "approved debt"));
        var currentViolation = new ModuleBoundaryObservedViolation(
            GuardId,
            "Training Planning",
            "Identity & Accounts",
            originalEntry.Key.SourceSymbolOrPath,
            originalEntry.Key.TargetSymbolOrPath);

        var evaluation = ModuleBoundaryDebtAllowlistEvaluator.EvaluateForTesting(
            [originalEntry],
            [currentViolation],
            GuardId,
            maximumAllowedEntryCount: 1);

        Assert.Multiple(() =>
        {
            Assert.That(evaluation.IsSuccess, Is.False);
            Assert.That(evaluation.StaleEntries, Is.EqualTo(new[] { originalEntry }));
            Assert.That(evaluation.UnexpectedViolations, Is.EqualTo(new[] { currentViolation }));
        });
    }

    [Test]
    public void Allowlist_Registry_Assertion_Uses_The_Centralized_Exact_Match_Path()
    {
        var act = () => ModuleBoundaryDebtAllowlistRegistry.AssertNoUnexpectedViolations(
            GuardId,
            [
                new ModuleBoundaryObservedViolation(
                    GuardId,
                    "Nutrition",
                    "User",
                    "LgymApi.Application/Nutrition/Plans/PlanService.cs",
                    "LgymApi.Application/User/IUserService.cs")
            ]);

        Assert.That(
            act,
            Throws.TypeOf<AssertionException>()
                .With.Message.Contains("Module-boundary shrink-only debt allowlist failed"));
    }

    private static ModuleBoundaryObservedViolation RetiredPlanningViolation(
        string guardId,
        string targetModule,
        string sourceSymbolOrPath,
        string targetSymbolOrPath)
        => new(
            guardId,
            ArchitectureTestHelpers.TrainingPlanningModuleName,
            targetModule,
            sourceSymbolOrPath,
            targetSymbolOrPath);
}
