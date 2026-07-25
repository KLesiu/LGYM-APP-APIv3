using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ApplicationInputModelStringIdGuardTests
{
    private static readonly HashSet<string> OperationalStringPatterns = new()
    {
        "CorrelationId",
        "TraceId",
        "RequestId",
        "SessionId"
    };

    [Test]
    public void Application_Input_Models_Must_Not_Use_Raw_String_For_Entity_Ids()
    {
        var repoRoot = ResolveRepositoryRoot();
        var applicationRoot = Path.Combine(repoRoot, "LgymApi.Application");
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

        Assert.That(Directory.Exists(applicationRoot), Is.True, $"Application project directory '{applicationRoot}' does not exist.");

        var sourceFiles = Directory
            .EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsInBuildArtifacts(path))
            .ToList();

        Assert.That(sourceFiles, Is.Not.Empty, $"No source files found in '{applicationRoot}'.");

        var syntaxTrees = sourceFiles
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), options: parseOptions, path: path))
            .ToList();

        foreach (var tree in syntaxTrees)
        {
            var parseErrors = tree
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToList();

            Assert.That(
                parseErrors,
                Is.Empty,
                $"Failed to parse source file '{tree.FilePath}':{Environment.NewLine}{string.Join(Environment.NewLine, parseErrors)}");
        }

        var compilation = CSharpCompilation.Create(
            "ApplicationInputModelStringIdGuard",
            syntaxTrees,
            ResolveMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var violations = new List<Violation>();
        var inputModelTrees = syntaxTrees
            .Where(tree => IsInputModelPath(tree.FilePath))
            .ToList();

        foreach (var tree in inputModelTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var root = tree.GetCompilationUnitRoot();

            // Check property declarations
            foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                if (IsRawStringIdUsage(property.Type, property.Identifier.ValueText, semanticModel))
                {
                    var line = property.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var relativePath = Path.GetRelativePath(repoRoot, tree.FilePath);
                    violations.Add(new Violation(
                        relativePath,
                        line,
                        $"Property '{property.Identifier.ValueText}' uses raw string for entity ID; use Id<TEntity>"));
                }
            }

            // Check field declarations
            foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            {
                foreach (var variable in field.Declaration.Variables)
                {
                    if (IsRawStringIdUsage(field.Declaration.Type, variable.Identifier.ValueText, semanticModel))
                    {
                        var line = field.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        var relativePath = Path.GetRelativePath(repoRoot, tree.FilePath);
                        violations.Add(new Violation(
                            relativePath,
                            line,
                            $"Field '{variable.Identifier.ValueText}' uses raw string for entity ID; use Id<TEntity>"));
                    }
                }
            }

            // Check record primary constructor parameters
            foreach (var recordDeclaration in root.DescendantNodes().OfType<RecordDeclarationSyntax>())
            {
                if (recordDeclaration.ParameterList != null)
                {
                    foreach (var parameter in recordDeclaration.ParameterList.Parameters)
                    {
                        if (IsRawStringIdUsage(parameter.Type, parameter.Identifier.ValueText, semanticModel))
                        {
                            var line = parameter.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                            var relativePath = Path.GetRelativePath(repoRoot, tree.FilePath);
                            violations.Add(new Violation(
                                relativePath,
                                line,
                                $"Record parameter '{parameter.Identifier.ValueText}' uses raw string for entity ID; use Id<TEntity>"));
                        }
                    }
                }
            }

            // Check method parameters
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                foreach (var parameter in method.ParameterList.Parameters)
                {
                    if (IsRawStringIdUsage(parameter.Type, parameter.Identifier.ValueText, semanticModel))
                    {
                        var line = parameter.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        var relativePath = Path.GetRelativePath(repoRoot, tree.FilePath);
                        violations.Add(new Violation(
                            relativePath,
                            line,
                            $"Method parameter '{parameter.Identifier.ValueText}' uses raw string for entity ID; use Id<TEntity>"));
                    }
                }
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            "Application input models must not use raw string for entity IDs. Internal models must use Id<TEntity>." + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    private static bool IsRawStringIdUsage(TypeSyntax? type, string identifier, SemanticModel semanticModel)
    {
        if (type == null || string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        // Check if it's an entity ID pattern (ends with "Id" case-insensitive)
        if (!IsEntityIdPattern(identifier))
        {
            return false;
        }

        // Exclude operational string patterns
        if (IsOperationalPattern(identifier))
        {
            return false;
        }

        // Check if type is string or string?
        return IsRawStringType(type, semanticModel);
    }

    private static bool IsEntityIdPattern(string identifier)
    {
        // Must be exactly "Id" or end with "Id" (case-insensitive)
        return identifier.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
               identifier.EndsWith("Id", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOperationalPattern(string identifier)
    {
        // Exclude operational string identifiers that are not entity IDs
        foreach (var pattern in OperationalStringPatterns)
        {
            if (identifier.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRawStringType(TypeSyntax type, SemanticModel semanticModel)
    {
        // Check semantic type information
        var typeInfo = semanticModel.GetTypeInfo(type);
        if (typeInfo.Type != null)
        {
            var typeString = typeInfo.Type.ToDisplayString();
            if (typeString == "string" || typeString == "string?")
            {
                return true;
            }
        }

        // Fallback to syntax check
        var typeText = type.ToString();
        if (typeText == "string" || typeText == "string?")
        {
            return true;
        }

        // Check for nullable string syntax
        if (type is NullableTypeSyntax nullableType)
        {
            var elementText = nullableType.ElementType.ToString();
            if (elementText == "string")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInputModelPath(string path)
    {
        var normalized = path.Replace('\\', '/');

        // Must be under LgymApi.Application
        if (!normalized.Contains("/LgymApi.Application/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Must contain /Models/ in the path
        if (!normalized.Contains("/Models/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Filename must contain "Input"
        var fileName = Path.GetFileName(path);
        if (!fileName.Contains("Input", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
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

    private static bool IsInBuildArtifacts(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record Violation(string File, int Line, string Message)
    {
        public override string ToString() => $"{File}:{Line} {Message}";
    }
}
