using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class DirectGuidUsageGuardTests
{
    private static readonly HashSet<string> ForbiddenGuidMethods = new(StringComparer.Ordinal)
    {
        "NewGuid",
        "TryParse",
        "Parse"
    };

    [Test]
    public void Direct_Guid_Usage_Must_Be_Limited_To_Domain_Id_ValueObject()
    {
        var (repoRoot, compilation, syntaxTrees) = PrepareCompilation();

        var violations = new List<Violation>();

        foreach (var tree in syntaxTrees)
        {
            if (IsExcludedPath(tree.FilePath))
            {
                continue;
            }

            if (IsGeneratedFile(tree))
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var root = tree.GetCompilationUnitRoot();

            CollectGuidTypeViolations(root, tree, semanticModel, repoRoot, violations);
            CollectGuidMethodInvocationViolations(root, tree, semanticModel, repoRoot, violations);
            CollectGuidEmptyViolations(root, tree, semanticModel, repoRoot, violations);
        }

        Assert.That(
            violations,
            Is.Empty,
            "Direct Guid usage is forbidden outside LgymApi.Domain/ValueObjects/Id.cs. "
            + "Use domain IDs instead of handwritten Guid handling." + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    private static void CollectGuidTypeViolations(
        CompilationUnitSyntax root,
        SyntaxTree tree,
        SemanticModel semanticModel,
        string repoRoot,
        List<Violation> violations)
    {
        foreach (var typeSyntax in root.DescendantNodes().OfType<TypeSyntax>())
        {
            if (!ContainsGuidType(typeSyntax, semanticModel))
            {
                continue;
            }

            var reason = DescribeGuidTypeUsage(typeSyntax);
            AddViolation(violations, repoRoot, tree.FilePath, typeSyntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1, reason);
        }

        foreach (var cast in root.DescendantNodes().OfType<CastExpressionSyntax>())
        {
            if (!ContainsGuidType(cast.Type, semanticModel))
            {
                continue;
            }

            AddViolation(
                violations,
                repoRoot,
                tree.FilePath,
                cast.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                "(Guid) cast detected; replace with Id<TEntity> conversion flow");
        }

        foreach (var typeOfExpression in root.DescendantNodes().OfType<TypeOfExpressionSyntax>())
        {
            if (!ContainsGuidType(typeOfExpression.Type, semanticModel))
            {
                continue;
            }

            AddViolation(
                violations,
                repoRoot,
                tree.FilePath,
                typeOfExpression.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                "typeof(Guid) detected; avoid direct Guid metadata usage outside Id.cs");
        }

        foreach (var objectCreation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            if (!ContainsGuidType(objectCreation.Type, semanticModel))
            {
                continue;
            }

            AddViolation(
                violations,
                repoRoot,
                tree.FilePath,
                objectCreation.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                "new Guid(...) construction detected; use Id<TEntity> APIs");
        }
    }

    private static void CollectGuidMethodInvocationViolations(
        CompilationUnitSyntax root,
        SyntaxTree tree,
        SemanticModel semanticModel,
        string repoRoot,
        List<Violation> violations)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            var methodSymbol = symbolInfo.Symbol as IMethodSymbol;

            if (methodSymbol == null || !IsGuidSymbol(methodSymbol.ContainingType))
            {
                continue;
            }

            if (!ForbiddenGuidMethods.Contains(methodSymbol.Name))
            {
                continue;
            }

            AddViolation(
                violations,
                repoRoot,
                tree.FilePath,
                invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                $"Guid.{methodSymbol.Name}(...) call detected; route through Id<TEntity> factory/parse methods");
        }
    }

    private static void CollectGuidEmptyViolations(
        CompilationUnitSyntax root,
        SyntaxTree tree,
        SemanticModel semanticModel,
        string repoRoot,
        List<Violation> violations)
    {
        foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
            var symbol = symbolInfo.Symbol;

            if (symbol is not IFieldSymbol fieldSymbol)
            {
                continue;
            }

            if (!string.Equals(fieldSymbol.Name, "Empty", StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsGuidSymbol(fieldSymbol.ContainingType))
            {
                continue;
            }

            AddViolation(
                violations,
                repoRoot,
                tree.FilePath,
                memberAccess.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                "Guid.Empty usage detected; use domain Id empty/none semantics instead");
        }
    }

    private static void AddViolation(List<Violation> violations, string repoRoot, string filePath, int line, string message)
    {
        var relativePath = Path.GetRelativePath(repoRoot, filePath);
        var violation = new Violation(relativePath, line, message);

        if (!violations.Contains(violation))
        {
            violations.Add(violation);
        }
    }

    private static string DescribeGuidTypeUsage(TypeSyntax typeSyntax)
    {
        if (typeSyntax.Parent is TypeArgumentListSyntax)
        {
            return "Guid used as a generic type argument; use Id<TEntity> in generic contracts";
        }

        if (typeSyntax.Parent is CastExpressionSyntax)
        {
            return "(Guid) cast detected; replace with Id<TEntity> conversion flow";
        }

        if (typeSyntax.Parent is TypeOfExpressionSyntax)
        {
            return "typeof(Guid) detected; avoid direct Guid metadata usage outside Id.cs";
        }

        if (typeSyntax.Parent is ObjectCreationExpressionSyntax)
        {
            return "new Guid(...) construction detected; use Id<TEntity> APIs";
        }

        return "Guid type usage detected; replace raw Guid declarations/usages with Id<TEntity>";
    }

    private static bool ContainsGuidType(TypeSyntax typeSyntax, SemanticModel semanticModel)
    {
        var typeInfo = semanticModel.GetTypeInfo(typeSyntax);
        return ContainsGuidType(typeInfo.Type) || ContainsGuidType(typeInfo.ConvertedType);
    }

    private static bool ContainsGuidType(ITypeSymbol? symbol)
    {
        if (symbol == null)
        {
            return false;
        }

        if (IsGuidSymbol(symbol))
        {
            return true;
        }

        if (symbol is IArrayTypeSymbol arrayType)
        {
            return ContainsGuidType(arrayType.ElementType);
        }

        if (symbol is INamedTypeSymbol namedType)
        {
            foreach (var typeArgument in namedType.TypeArguments)
            {
                if (ContainsGuidType(typeArgument))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsGuidSymbol(ITypeSymbol? symbol)
    {
        return symbol is INamedTypeSymbol namedType
            && string.Equals(namedType.Name, "Guid", StringComparison.Ordinal)
            && string.Equals(namedType.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal);
    }

    private static bool IsExcludedPath(string path)
    {
        var normalized = Normalize(path);

        if (normalized.Contains("/migrations/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.EndsWith("/LgymApi.Domain/ValueObjects/Id.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.EndsWith("/LgymApi.Infrastructure/Data/TypedIdValueConverter.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.EndsWith("/LgymApi.UnitTests/UnitOfWorkCommittedDispatchTests.cs", StringComparison.OrdinalIgnoreCase))
        {
            // EF Core IDbContextTransaction.TransactionId contract is raw Guid and unavoidable in the test double.
            return true;
        }

        if (normalized.EndsWith("/LgymApi.Platform/Pagination/FilterToGridifyAdapter.cs", StringComparison.OrdinalIgnoreCase))
        {
            // Gridify field type resolution requires typeof(Guid) and Guid.TryParse to properly map Guid-typed fields
            // in filter expressions. This is unavoidable for a generic filter-to-Gridify adapter.
            return true;
        }

        if (normalized.Contains("/LgymApi.UnitTests/Pagination/", StringComparison.OrdinalIgnoreCase))
        {
            // Pagination unit tests use Guid for EF Core InMemory database setup and test data seeding.
            // Guid.Parse/Guid.NewGuid are unavoidable for creating deterministic test fixtures.
            return true;
        }

        if (normalized.Contains("/LgymApi.IntegrationTests/Pagination/", StringComparison.OrdinalIgnoreCase))
        {
            // Pagination integration tests use Guid for EF Core InMemory database setup and test data seeding.
            return true;
        }

        if (normalized.EndsWith("/LgymApi.UnitTests/InAppNotifications/NotificationHubTests.cs", StringComparison.OrdinalIgnoreCase))
        {
            // NotificationHub tests use Id<User>.New().GetValue() which returns Guid.
            // The Id<TEntity> API is the approved abstraction; the Guid is an implementation detail of GetValue().
            return true;
        }

        if (normalized.EndsWith("/LgymApi.IntegrationTests/PostgreSqlDatabaseLease.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.EndsWith("/LgymApi.IntegrationTests/PostgreSqlPersistenceTests.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("/LgymApi.UnitTests/TypedIdValueConverterTests.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalized.EndsWith("/LgymApi.UnitTests/TypedIdTests.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedFile(SyntaxTree tree)
    {
        try
        {
            var normalized = Normalize(tree.FilePath);
            if (normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var fileContent = File.ReadAllText(tree.FilePath);
            var lines = fileContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            for (int i = 0; i < Math.Min(5, lines.Length); i++)
            {
                if (lines[i].Contains("<auto-generated", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // If the file cannot be read, keep it in scope.
        }

        return false;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static (string RepoRoot, CSharpCompilation Compilation, IReadOnlyList<SyntaxTree> SyntaxTrees) PrepareCompilation()
    {
        var repoRoot = ResolveRepositoryRoot();
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var sourceFiles = Directory
            .EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsInBuildArtifacts(path))
            .ToList();

        Assert.That(sourceFiles, Is.Not.Empty, "No C# source files found for direct Guid usage guard.");

        var syntaxTrees = sourceFiles
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), options: parseOptions, path: path))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "DirectGuidUsageGuard",
            syntaxTrees,
            ResolveMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return (repoRoot, compilation, syntaxTrees);
    }

    private static bool IsInBuildArtifacts(string path)
    {
        var normalized = Normalize(path);
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static List<MetadataReference> ResolveMetadataReferences()
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(assembly => !assembly.IsDynamic)
            .Select(assembly => assembly.Location)
            .Where(location => !string.IsNullOrWhiteSpace(location) && File.Exists(location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
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

    private sealed record Violation(string File, int Line, string Message)
    {
        public override string ToString() => $"{File}:{Line} {Message}";
    }
}
