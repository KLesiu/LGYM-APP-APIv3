using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ServiceTransactionHeuristicGuardTests
{
    private static readonly HashSet<string> AllowlistedMethods = new(StringComparer.Ordinal)
    {
        // Keep this list intentionally small and local.
        // Add "ServiceName.MethodName" entries only when the heuristic
        // would otherwise flag a proven-safe multi-write flow.
    };

    [Test]
    public void Multi_Write_Service_Methods_Should_Use_A_Commit_Boundary_Or_Be_Allowlisted()
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

                    var allowlistKey = GetAllowlistKey(serviceClass.Identifier.ValueText, method.Identifier.ValueText);
                    if (AllowlistedMethods.Contains(allowlistKey))
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
            "Multi-write service methods must either commit through SaveChanges/transaction boundaries or be explicitly allowlisted." + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
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

    private static string GetAllowlistKey(string serviceName, string methodName)
    {
        return $"{serviceName}.{methodName}";
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
