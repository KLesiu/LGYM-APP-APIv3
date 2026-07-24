namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ModuleBoundaryDebtAllowlistTests
{
    private const string GuardId = "ModuleDependencyGuardTests";

    [Test]
    public void Allowlist_Registry_Remains_Centralized_And_Populated_With_Explicit_Current_Debt_Entries()
    {
        var entries = ModuleBoundaryDebtAllowlistRegistry.AllEntries;

        Assert.Multiple(() =>
        {
            Assert.That(entries, Is.Not.Empty, "The shrink-only allowlist should contain the currently approved explicit debt entries.");
            Assert.That(entries, Has.Count.EqualTo(ModuleBoundaryDebtAllowlistRegistry.MaximumAllowedEntryCount), "The approved debt baseline must remain exact.");
            Assert.That(entries.Select(entry => entry.Key.GuardId).Distinct(StringComparer.Ordinal).Count(), Is.GreaterThanOrEqualTo(1));
            Assert.That(entries.All(entry => !string.IsNullOrWhiteSpace(entry.Key.Rationale)), Is.True, "Every centralized debt entry must stay reviewable with a rationale.");
            Assert.That(entries.Select(entry => entry.IdentityKey).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(entries.Count), "Centralized debt entries must stay exact and non-duplicated.");
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

        var evaluation = ModuleBoundaryDebtAllowlistEvaluator.Evaluate([entry], [observedViolation]);

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

        var evaluation = ModuleBoundaryDebtAllowlistEvaluator.Evaluate([entry], [], GuardId);

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

        var act = () => ModuleBoundaryDebtAllowlistEvaluator.Evaluate([firstEntry, speculativeDuplicate], [], GuardId);

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

        var act = () => ModuleBoundaryDebtAllowlistEvaluator.Evaluate([broadEntry], [], GuardId);

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
    public void Owner_Rekey_Changes_Only_Owner_Metadata_For_An_Exact_Current_Violation()
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

        var rekeyedEntry = ModuleBoundaryDebtOwnerRekey.FromCurrentViolation(originalEntry, currentViolation).ToEntry();
        var evaluation = ModuleBoundaryDebtAllowlistEvaluator.Evaluate([rekeyedEntry], [currentViolation], GuardId);

        Assert.Multiple(() =>
        {
            Assert.That(rekeyedEntry.Key.GuardId, Is.EqualTo(originalEntry.Key.GuardId));
            Assert.That(rekeyedEntry.Key.SourceModule, Is.EqualTo(currentViolation.SourceModule));
            Assert.That(rekeyedEntry.Key.TargetModule, Is.EqualTo(currentViolation.TargetModule));
            Assert.That(rekeyedEntry.Key.SourceSymbolOrPath, Is.EqualTo(originalEntry.Key.SourceSymbolOrPath));
            Assert.That(rekeyedEntry.Key.TargetSymbolOrPath, Is.EqualTo(originalEntry.Key.TargetSymbolOrPath));
            Assert.That(rekeyedEntry.Key.Rationale, Is.EqualTo(originalEntry.Key.Rationale));
            Assert.That(evaluation.IsSuccess, Is.True, evaluation.BuildFailureMessage());
        });
    }

    [TestCase(GuardId, "LgymApi.Application/Nutrition/Plans/ChangedPlanService.cs", "LgymApi.Application/User/IUserService.cs", "source symbol")]
    [TestCase(GuardId, "LgymApi.Application/Nutrition/Plans/PlanService.cs", "LgymApi.Application/User/IChangedUserService.cs", "target symbol")]
    [TestCase("ChangedGuard", "LgymApi.Application/Nutrition/Plans/PlanService.cs", "LgymApi.Application/User/IUserService.cs", "kind")]
    public void Owner_Rekey_Fails_When_The_Current_Violation_Changes_A_Nonowner_Identity_Field(
        string guardId,
        string sourceSymbolOrPath,
        string targetSymbolOrPath,
        string changedField)
    {
        var originalEntry = new ModuleBoundaryDebtEntry(
            ModuleBoundaryDebtKey.Create(
                GuardId,
                "Nutrition",
                "User",
                "LgymApi.Application/Nutrition/Plans/PlanService.cs",
                "LgymApi.Application/User/IUserService.cs",
                "approved debt"));
        var changedViolation = new ModuleBoundaryObservedViolation(
            guardId,
            "Training Planning",
            "Identity & Accounts",
            sourceSymbolOrPath,
            targetSymbolOrPath);

        var act = () => ModuleBoundaryDebtOwnerRekey.FromCurrentViolation(originalEntry, changedViolation);

        Assert.That(
            act,
            Throws.TypeOf<AssertionException>()
                .With.Message.Contains(changedField));
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
}
