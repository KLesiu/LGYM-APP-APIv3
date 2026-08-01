using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class CoachingPersistencePortGuardTests
{
    private static readonly string[] RequiredPortFiles =
    [
        "LgymApi.Application/Coaching/Persistence/ICoachingInvitationPersistence.cs",
        "LgymApi.Application/Coaching/Persistence/ICoachingActiveLinkPersistence.cs",
        "LgymApi.Application/Coaching/Persistence/ICoachingFactReader.cs",
        "LgymApi.Application/Coaching/Persistence/ICoachingTraineeNotePersistence.cs"
    ];

    private static readonly string[] RequiredRepositoryFiles =
    [
        "LgymApi.Infrastructure/Repositories/Coaching/CoachingInvitationPersistenceRepository.cs",
        "LgymApi.Infrastructure/Repositories/Coaching/CoachingActiveLinkPersistenceRepository.cs",
        "LgymApi.Infrastructure/Repositories/Coaching/CoachingFactReader.cs",
        "LgymApi.Infrastructure/Repositories/Coaching/CoachingTraineeNotePersistenceRepository.cs"
    ];

    [Test]
    public void Focused_Coaching_Persistence_Ports_Should_Exist_Separately()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var requiredFiles = RequiredPortFiles.Concat(RequiredRepositoryFiles).ToList();
        var missingFiles = requiredFiles
            .Where(path => !File.Exists(Path.Combine(repositoryRoot, path)))
            .ToList();

        Assert.That(missingFiles, Is.Empty, "Focused Coaching persistence seams are incomplete:" + Environment.NewLine + string.Join(Environment.NewLine, missingFiles));
    }

    [Test]
    public void Focused_Coaching_Persistence_Repositories_Should_Avoid_Identity_Joins_Writes_And_Early_Paging()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var violations = RequiredRepositoryFiles
            .Select(path => new { Path = path, FullPath = Path.Combine(repositoryRoot, path) })
            .Where(file => File.Exists(file.FullPath))
            .SelectMany(file => CollectForbiddenOperations(File.ReadAllText(file.FullPath))
                .Select(operation => $"{file.Path}: {operation}"))
            .ToList();

        Assert.That(
            violations,
            Is.Empty,
            "Focused Coaching persistence repositories must query Coaching DbSets only, remain stage-only, and return unpaged facts:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Test]
    public void Focused_Coaching_Persistence_Repositories_Should_Map_Fact_Results()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var violations = RequiredRepositoryFiles
            .Select(path => new { Path = path, FullPath = Path.Combine(repositoryRoot, path) })
            .Where(file => File.Exists(file.FullPath))
            .SelectMany(file => CollectManuallyConstructedFacts(File.ReadAllText(file.FullPath))
                .Select(fact => $"{file.Path}: {fact}"))
            .ToList();

        Assert.That(
            violations,
            Is.Empty,
            "Focused Coaching persistence repositories must transform fact results through registered mapping profiles:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [TestCase("_dbContext.Users.Any()", "Users")]
    [TestCase("context.Set<User>()", "Set<User>")]
    [TestCase("from link in links join user in users on link.Id equals user.Id select link", "join")]
    [TestCase("query.Include(item => item)", "Include")]
    [TestCase("_dbContext.SaveChangesAsync()", "SaveChangesAsync")]
    [TestCase("query.Skip(20)", "Skip")]
    [TestCase("query.Take(20)", "Take")]
    public void Forbidden_Focused_Coaching_Persistence_Repository_Syntax_Should_Be_Rejected(string statement, string expectedViolation)
    {
        var source = $$"""
            using System.Linq;

            public sealed class Repository
            {
                public object Query(dynamic query, dynamic links, dynamic users)
                {
                    return {{statement}};
                }
            }
            """;

        Assert.That(CollectForbiddenOperations(source), Is.EqualTo(new[] { expectedViolation }));
    }

    private static IReadOnlyList<string> CollectForbiddenOperations(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var violations = new List<string>();

        if (root.DescendantNodes().OfType<JoinClauseSyntax>().Any())
        {
            violations.Add("join");
        }

        foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            var memberName = memberAccess.Name.Identifier.ValueText;
            if (memberName is "Users" or "Include" or "SaveChanges" or "SaveChangesAsync" or "BeginTransaction" or "BeginTransactionAsync" or "Skip" or "Take")
            {
                violations.Add(memberName);
            }
        }

        foreach (var genericName in root.DescendantNodes().OfType<GenericNameSyntax>())
        {
            if (genericName.Identifier.ValueText == "Set" && genericName.TypeArgumentList.Arguments.Any(argument => argument.ToString() == "User"))
            {
                violations.Add("Set<User>");
            }
        }

        return violations.Distinct(StringComparer.Ordinal).ToList();
    }

    private static IReadOnlyList<string> CollectManuallyConstructedFacts(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();

        return root.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Select(creation => creation.Type.ToString())
            .Where(typeName => typeName.StartsWith("Coaching", StringComparison.Ordinal)
                && typeName.EndsWith("Fact", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LgymApi.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
