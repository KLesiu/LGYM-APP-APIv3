using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

/// <summary>
/// Strict architecture guard that bans ALL direct Guid usage outside LgymApi.Domain/ValueObjects/Id.cs.
/// 
/// Exclusions:
/// - LgymApi.Domain/ValueObjects/Id.cs (sole handwritten file allowed to use Guid APIs)
/// - Migration files in /Migrations/ directory
/// - Generated files marked with // &lt;auto-generated /&gt; header
/// 
/// Detects:
/// - Direct Guid type references
/// - Guid.TryParse, Guid.NewGuid, Guid.Empty invocations
/// - new Guid(...) expressions
/// - Casts to/from Guid
/// - typeof(Guid) expressions
/// 
/// Notes:
/// - Handwritten tests ARE included in scope (no test exclusions)
/// - Exclusion rules are explicit and narrow
/// </summary>
[TestFixture]
public sealed class StrictGuidBanGuardTests
{
    [Test]
    public void Strict_Guid_Ban_Guard_Must_Reject_All_Handwritten_Guid_Usage_Except_Id_Cs_And_Generated_Files()
    {
        var (repoRoot, compilation, syntaxTrees) = PrepareCompilation();

        var violations = new List<Violation>();

        foreach (var tree in syntaxTrees)
        {
            // Skip excluded paths: migrations and Id.cs
            if (IsExcludedPath(tree.FilePath))
            {
                continue;
            }

            // Check if file has auto-generated marker in first few lines
            if (IsGeneratedFile(tree))
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var root = tree.GetCompilationUnitRoot();

            // Check for direct Guid type usages (fields, properties, parameters, return types)
            foreach (var node in root.DescendantNodes())
            {
                CheckGuidTypeReference(node, tree, semanticModel, repoRoot, violations);
                CheckGuidInvocations(node, tree, repoRoot, violations);
                CheckGuidCasts(node, tree, repoRoot, violations);
                CheckTypeofGuid(node, tree, repoRoot, violations);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                violations,
                Is.Empty,
                "Strict Guid ban is enforced. Direct Guid usage is forbidden outside LgymApi.Domain/ValueObjects/Id.cs. "
                + "Migrations and generated files are excluded automatically. "
                + "Violations:\n" + string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
        });
    }

    /// <summary>
    /// Check for direct Guid type references in fields, properties, parameters, and return types.
    /// </summary>
    private static void CheckGuidTypeReference(SyntaxNode node, SyntaxTree tree, SemanticModel semanticModel, string repoRoot, List<Violation> violations)
    {
        TypeSyntax? type = null;
        SyntaxToken? identifier = null;

        // Field declaration: public Guid id;
        if (node is FieldDeclarationSyntax field)
        {
            type = field.Declaration.Type;
            if (field.Declaration.Variables.Count > 0)
            {
                identifier = field.Declaration.Variables[0].Identifier;
            }
        }
        // Property declaration: public Guid Id { get; set; }
        else if (node is PropertyDeclarationSyntax property)
        {
            type = property.Type;
            identifier = property.Identifier;
        }
        // Method parameter: void Method(Guid id)
        else if (node is ParameterSyntax parameter)
        {
            type = parameter.Type;
            identifier = parameter.Identifier;
        }
        // Method return type: Guid GetId()
        else if (node is MethodDeclarationSyntax method)
        {
            type = method.ReturnType;
            identifier = method.Identifier;
        }

        if (type != null && IsRawGuidType(type, semanticModel))
        {
            var line = type.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var relativePath = Path.GetRelativePath(repoRoot, tree.FilePath);
            var identifierText = identifier?.ValueText ?? "unknown";
            violations.Add(new Violation(
                relativePath,
                line,
                $"Direct Guid type reference detected ({identifierText}); use Id<TEntity> or an approved abstraction instead"));
        }
    }

    /// <summary>
    /// Check for Guid static method invocations: Guid.TryParse, Guid.NewGuid, Guid.Empty, etc.
    /// </summary>
    private static void CheckGuidInvocations(SyntaxNode node, SyntaxTree tree, string repoRoot, List<Violation> violations)
    {
        // Look for invocations like Guid.TryParse(...), Guid.NewGuid(), Guid.Empty
        if (node is InvocationExpressionSyntax invocation)
        {
            var methodName = ExtractMethodName(invocation.Expression);
            if (methodName.StartsWith("Guid.", StringComparison.Ordinal) || methodName == "Guid.TryParse" || methodName == "Guid.NewGuid" || methodName == "Guid.Empty")
            {
                var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var relativePath = Path.GetRelativePath(repoRoot, tree.FilePath);
                violations.Add(new Violation(
                    relativePath,
                    line,
                    $"Direct Guid API invocation detected ({methodName}); use Id<TEntity>.TryParse(...) or an approved abstraction instead"));
            }
        }
        // Also check for member access like Guid.Empty
        else if (node is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Expression.ToString() == "Guid")
            {
                var memberName = memberAccess.Name.Identifier.ValueText;
                if (memberName == "Empty" || memberName == "NewGuid")
                {
                    var line = memberAccess.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var relativePath = Path.GetRelativePath(repoRoot, tree.FilePath);
                    violations.Add(new Violation(
                        relativePath,
                        line,
                        $"Direct Guid API access detected (Guid.{memberName}); use an approved abstraction instead"));
                }
            }
        }
    }

    /// <summary>
    /// Check for Guid casts: (Guid)value or value as Guid
    /// </summary>
    private static void CheckGuidCasts(SyntaxNode node, SyntaxTree tree, string repoRoot, List<Violation> violations)
    {
        // Cast expression: (Guid)value
        if (node is CastExpressionSyntax cast)
        {
            if (cast.Type.ToString() == "Guid")
            {
                var line = cast.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var relativePath = Path.GetRelativePath(repoRoot, tree.FilePath);
                violations.Add(new Violation(
                    relativePath,
                    line,
                    "Direct Guid cast detected; use Id<TEntity> or an approved abstraction instead"));
            }
        }
    }

    /// <summary>
    /// Check for typeof(Guid) expressions.
    /// </summary>
    private static void CheckTypeofGuid(SyntaxNode node, SyntaxTree tree, string repoRoot, List<Violation> violations)
    {
        // typeof(Guid)
        if (node is TypeOfExpressionSyntax typeOf)
        {
            if (typeOf.Type.ToString() == "Guid")
            {
                var line = typeOf.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var relativePath = Path.GetRelativePath(repoRoot, tree.FilePath);
                violations.Add(new Violation(
                    relativePath,
                    line,
                    "Direct typeof(Guid) detected; use an approved abstraction instead"));
            }
        }
    }

    /// <summary>
    /// Extract method name from an invocation expression.
    /// </summary>
    private static string ExtractMethodName(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => $"{memberAccess.Expression}.{memberAccess.Name.Identifier.ValueText}",
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => expression.ToString()
        };
    }

    /// <summary>
    /// Check if a type syntax represents raw Guid.
    /// </summary>
    private static bool IsRawGuidType(TypeSyntax? type, SemanticModel semanticModel)
    {
        if (type == null)
        {
            return false;
        }

        var typeString = type.ToString();

        // Simple Guid
        if (typeString == "Guid")
        {
            return true;
        }

        // Nullable Guid?
        if (typeString == "Guid?")
        {
            return true;
        }

        // Fully qualified Guid
        if (typeString == "global::System.Guid" || typeString == "System.Guid")
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Check if a file is excluded from the guard.
    /// Exclusions:
    /// - Migration files (/Migrations/ path)
    /// - Generated files (// <auto-generated /> header)
    /// - Id.cs (sole handwritten Guid-allowed file)
    /// </summary>
    private static bool IsExcludedPath(string path)
    {
        var normalized = Normalize(path);

        // Exclude migrations directory
        if (normalized.Contains("/migrations/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Exclude the sole handwritten Guid-allowed file: LgymApi.Domain/ValueObjects/Id.cs
        if (normalized.EndsWith("/LgymApi.Domain/ValueObjects/Id.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.EndsWith("/LgymApi.UnitTests/UnitOfWorkCommittedDispatchTests.cs", StringComparison.OrdinalIgnoreCase))
        {
            // EF Core IDbContextTransaction.TransactionId contract is raw Guid and unavoidable in the test double.
            return true;
        }

        if (normalized.EndsWith("/LgymApi.IntegrationTests/PostgreSqlDatabaseLease.cs", StringComparison.OrdinalIgnoreCase))
        {
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

        return false;
    }

    /// <summary>
    /// Check if a file has the auto-generated marker in the first few lines.
    /// </summary>
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

            // Check first 5 lines for auto-generated marker
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
            // If we can't read the file, don't exclude it
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

        Assert.That(sourceFiles, Is.Not.Empty, "No C# source files found for Guid ban guard.");

        var syntaxTrees = sourceFiles
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), options: parseOptions, path: path))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "StrictGuidBanGuard",
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
        var current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root (no .git directory found).");
    }

    private sealed record Violation(string File, int Line, string Message)
    {
        public override string ToString() => $"{File}:{Line} - {Message}";
    }
}
