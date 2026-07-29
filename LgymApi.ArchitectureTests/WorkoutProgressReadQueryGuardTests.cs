using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class WorkoutProgressReadQueryGuardTests
{
    [TestCase("LgymApi.Identity/Persistence/Repositories/UserRepository.cs", "GetRankingEligibleAccountProfilesAsync", "AsNoTracking", "Select", "RankingAccountProfile")]
    [TestCase("LgymApi.Infrastructure/Repositories/EloRegistryRepository.cs", "GetLatestEloAsync", "AsNoTracking", "OrderByDescending", "Select")]
    [TestCase("LgymApi.Infrastructure/Repositories/TrainingRepository.cs", "GetDatesByUserIdAsync", "AsNoTracking", "OrderBy", "Select")]
    [TestCase("LgymApi.Infrastructure/Repositories/TrainingRepository.cs", "GetByUserIdAndDateAsync", "AsNoTracking", "Include", "PlanDay", "Gym")]
    [TestCase("LgymApi.Infrastructure/Repositories/ExerciseScoreRepository.cs", "GetByUserAndExerciseAsync", "AsNoTracking", "Include", "Exercise", "Training")]
    [TestCase("LgymApi.Infrastructure/Repositories/MainRecordRepository.cs", "GetBestByUserGroupedByExerciseAndUnitAsync", "AsNoTracking", "GroupBy", "Select", "OrderByDescending")]
    public void Performance_Sensitive_Workout_Progress_Reads_Should_Remain_NoTracking_And_Projected(
        string relativePath,
        string methodName,
        params string[] requiredFragments)
    {
        var methodSource = ReadMethodSource(relativePath, methodName);

        Assert.That(
            requiredFragments.Where(fragment => !methodSource.Contains(fragment, StringComparison.Ordinal)),
            Is.Empty,
            $"{relativePath}:{methodName} must retain its no-tracking/projection query shape.");
    }

    private static string ReadMethodSource(string relativePath, string methodName)
    {
        var source = ReadSource(relativePath);
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var method = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.ValueText == methodName);

        return method.ToFullString();
    }

    private static string ReadSource(string relativePath)
    {
        return File.ReadAllText(Path.Combine(ArchitectureTestHelpers.ResolveRepositoryRoot(), relativePath));
    }
}
