using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ServiceTransactionHeuristicGuardTests
{
    [Test]
    public void Multi_Write_Service_Methods_Should_Use_A_Commit_Boundary()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var serviceFiles = ArchitectureTestHelpers.EnumerateProductionSourceFiles("LgymApi.Application");

        Assert.That(serviceFiles, Is.Not.Empty, "No application source files found for transaction heuristic guard test.");

        var violations = new List<Violation>();

        foreach (var serviceFile in serviceFiles)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(serviceFile), parseOptions, serviceFile);
            var root = tree.GetCompilationUnitRoot();

            foreach (var serviceClass in root.DescendantNodes().OfType<ClassDeclarationSyntax>().Where(IsConcreteService))
            {
                foreach (var method in serviceClass.Members.OfType<MethodDeclarationSyntax>().Where(IsPublicMethod))
                {
                    var analysis = Analyze(method);
                    if (!analysis.IsMultiWriteCandidate)
                    {
                        continue;
                    }

                    if (analysis.HasCommitBoundary)
                    {
                        continue;
                    }

                    var lineSpan = tree.GetLineSpan(method.Span);
                    violations.Add(new Violation(
                        Path.GetRelativePath(repoRoot, serviceFile),
                        lineSpan.StartLinePosition.Line + 1,
                        serviceClass.Identifier.ValueText,
                        method.Identifier.ValueText,
                        analysis.RepositoryWriteCount,
                        analysis.SaveChangesCount,
                        analysis.BeginTransactionCount,
                        string.Join(", ", analysis.RepositoryWriteCalls)));
                }
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            "Multi-write service methods must commit through SaveChanges or transaction boundaries." + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    [TestCase("firstRepository.Add(); secondRepository.Update();", 1, 2, 0, 0)]
    [TestCase("firstRepository.Add(); secondRepository.Update(); unitOfWork.SaveChangesAsync();", 0, 0, 0, 0)]
    [TestCase("firstRepository.Add(); secondRepository.Update(); unitOfWork.BeginTransactionAsync();", 0, 0, 0, 0)]
    [TestCase("repository.Add();", 0, 0, 0, 0)]
    public void Transaction_Boundary_Fixtures_Should_Produce_Deterministic_Results(
        string methodBody,
        int expectedViolationCount,
        int expectedWriteCount,
        int expectedSaveCount,
        int expectedTransactionCount)
    {
        var source = $$"""
            namespace Example;

            public sealed class FixtureService
            {
                public void Execute()
                {
                    {{methodBody}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
            path: "TransactionFixture.cs");
        var method = tree.GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();
        var analysis = Analyze(method);
        var isViolation = analysis.IsMultiWriteCandidate && !analysis.HasCommitBoundary;

        Assert.Multiple(() =>
        {
            Assert.That(isViolation ? 1 : 0, Is.EqualTo(expectedViolationCount));
            if (expectedViolationCount == 1)
            {
                Assert.That(analysis.RepositoryWriteCount, Is.EqualTo(expectedWriteCount));
                Assert.That(analysis.SaveChangesCount, Is.EqualTo(expectedSaveCount));
                Assert.That(analysis.BeginTransactionCount, Is.EqualTo(expectedTransactionCount));
                Assert.That(analysis.RepositoryWriteCalls, Is.EqualTo(new[] { "Add", "Update" }));
            }
        });
    }

    [Test]
    public void Legacy_Exception_Hook_Should_Be_Absent()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var guardSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "LgymApi.ArchitectureTests",
            "ServiceTransactionHeuristicGuardTests.cs"));
        var forbiddenTerm = string.Concat("allow", "list");

        Assert.That(guardSource.Contains(forbiddenTerm, StringComparison.OrdinalIgnoreCase), Is.False);
    }

    private static MethodAnalysis Analyze(MethodDeclarationSyntax method)
    {
        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        var repositoryWriteCalls = new List<string>();
        var saveChangesCount = 0;
        var beginTransactionCount = 0;

        foreach (var invocation in invocations)
        {
            if (IsSaveChangesInvocation(invocation))
            {
                saveChangesCount++;
            }

            if (IsBeginTransactionInvocation(invocation))
            {
                beginTransactionCount++;
            }

            if (IsRepositoryWriteInvocation(invocation))
            {
                repositoryWriteCalls.Add(GetInvocationName(invocation));
            }
        }

        return new MethodAnalysis(
            repositoryWriteCalls.Count >= 2,
            repositoryWriteCalls.Count,
            saveChangesCount,
            beginTransactionCount,
            repositoryWriteCalls);
    }

    private static bool IsRepositoryWriteInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        var receiver = memberAccess.Expression.ToString();
        if (!receiver.Contains("Repository", StringComparison.Ordinal))
        {
            return false;
        }

        var methodName = memberAccess.Name.Identifier.ValueText;
        return IsWriteMethodName(methodName);
    }

    private static bool IsWriteMethodName(string methodName)
    {
        return methodName.StartsWith("Add", StringComparison.Ordinal)
            || methodName.StartsWith("Update", StringComparison.Ordinal)
            || methodName.StartsWith("Delete", StringComparison.Ordinal)
            || methodName.StartsWith("Remove", StringComparison.Ordinal)
            || methodName.StartsWith("Mark", StringComparison.Ordinal)
            || methodName.StartsWith("Set", StringComparison.Ordinal)
            || methodName.StartsWith("Clear", StringComparison.Ordinal)
            || methodName.StartsWith("Upsert", StringComparison.Ordinal)
            || methodName.StartsWith("Revoke", StringComparison.Ordinal)
            || methodName.StartsWith("Complete", StringComparison.Ordinal)
            || methodName.StartsWith("Assign", StringComparison.Ordinal)
            || methodName.StartsWith("Create", StringComparison.Ordinal)
            || methodName.StartsWith("Register", StringComparison.Ordinal)
            || methodName.StartsWith("Block", StringComparison.Ordinal)
            || methodName.StartsWith("Unblock", StringComparison.Ordinal)
            || methodName.StartsWith("Copy", StringComparison.Ordinal)
            || methodName.StartsWith("Generate", StringComparison.Ordinal);
    }

    private static bool IsSaveChangesInvocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name.Identifier.ValueText == "SaveChangesAsync";
    }

    private static bool IsBeginTransactionInvocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name.Identifier.ValueText == "BeginTransactionAsync";
    }

    private static string GetInvocationName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Name.Identifier.ValueText
            : invocation.Expression.ToString();
    }

    private static bool IsPublicMethod(MethodDeclarationSyntax method)
    {
        return method.Modifiers.Any(modifier => Microsoft.CodeAnalysis.CSharpExtensions.IsKind(modifier, Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword));
    }

    private static bool IsConcreteService(ClassDeclarationSyntax typeDeclaration)
    {
        return typeDeclaration.Identifier.ValueText.EndsWith("Service", StringComparison.Ordinal)
            && !typeDeclaration.Modifiers.Any(modifier => Microsoft.CodeAnalysis.CSharpExtensions.IsKind(modifier, Microsoft.CodeAnalysis.CSharp.SyntaxKind.AbstractKeyword));
    }

    private sealed record MethodAnalysis(
        bool IsMultiWriteCandidate,
        int RepositoryWriteCount,
        int SaveChangesCount,
        int BeginTransactionCount,
        IReadOnlyList<string> RepositoryWriteCalls)
    {
        public bool HasCommitBoundary => SaveChangesCount > 0 || BeginTransactionCount > 0;
    }

    private sealed record Violation(
        string File,
        int Line,
        string ServiceName,
        string MethodName,
        int RepositoryWriteCount,
        int SaveChangesCount,
        int BeginTransactionCount,
        string RepositoryWriteCalls)
    {
        public override string ToString()
            => $"{File}:{Line} -> {ServiceName}.{MethodName} has {RepositoryWriteCount} repository writes ({RepositoryWriteCalls}) but only {SaveChangesCount} SaveChangesAsync and {BeginTransactionCount} BeginTransactionAsync calls";
    }
}
