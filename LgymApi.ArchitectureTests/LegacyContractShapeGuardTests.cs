using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using LgymApi.Api.Interfaces;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class LegacyContractShapeGuardTests
{
    [Test]
    public void Legacy_Response_Dtos_Should_Preserve_Required_Property_Naming()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var apiRoot = Path.Combine(repoRoot, "LgymApi.Api");
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

        Assert.That(Directory.Exists(apiRoot), Is.True, $"Api project directory '{apiRoot}' does not exist.");

        var sourceFiles = Directory
            .EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !ArchitectureTestHelpers.IsInBuildArtifacts(path))
            .ToList();

        Assert.That(sourceFiles, Is.Not.Empty, $"No source files found in '{apiRoot}'.");

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

        var compilation = ArchitectureTestHelpers.CreateCompilation(syntaxTrees);

        var resultDtoSymbol = compilation.GetTypeByMetadataName(typeof(IResultDto).FullName!);
        Assert.That(resultDtoSymbol, Is.Not.Null, "Unable to resolve IResultDto symbol.");

        var violations = new List<Violation>();
        var contractTrees = syntaxTrees
            .Where(tree => IsContractsPath(tree.FilePath))
            .ToList();

        foreach (var tree in contractTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var root = tree.GetCompilationUnitRoot();

            // Find all type declarations that implement IResultDto
            foreach (var typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration) as INamedTypeSymbol;
                if (typeSymbol == null)
                {
                    continue;
                }

                if (!IsProjectType(typeSymbol, compilation.Assembly))
                {
                    continue;
                }

                // Check if this type implements IResultDto
                if (!ImplementsIResultDto(typeSymbol, resultDtoSymbol!))
                {
                    continue;
                }

                // Validate properties of this response DTO
                var typeViolations = ValidateLegacyPropertyNaming(
                    typeSymbol,
                    typeDeclaration,
                    tree.FilePath,
                    repoRoot,
                    semanticModel);

                violations.AddRange(typeViolations);
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            "Legacy response DTOs must preserve required property naming conventions (e.g., _id for IDs, msg for messages)." +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    private static List<Violation> ValidateLegacyPropertyNaming(
        INamedTypeSymbol typeSymbol,
        TypeDeclarationSyntax typeDeclaration,
        string filePath,
        string repoRoot,
        SemanticModel semanticModel)
    {
        var violations = new List<Violation>();

        // First pass: collect all properties that explicitly use legacy naming patterns
        var hasLegacyIdPattern = false;
        var hasMessagePattern = false;

        foreach (var property in typeDeclaration.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            if (HasJsonPropertyNameAttribute(property, out var jsonName))
            {
                if (jsonName == "_id")
                {
                    hasLegacyIdPattern = true;
                }
                if (jsonName == "msg")
                {
                    hasMessagePattern = true;
                }
            }
        }

        // Second pass: validate consistency
        // If a DTO uses _id pattern, all ID properties should use it
        if (hasLegacyIdPattern)
        {
            foreach (var property in typeDeclaration.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                var propertySymbol = semanticModel.GetDeclaredSymbol(property) as IPropertySymbol;
                if (propertySymbol == null || !IsIdProperty(propertySymbol))
                {
                    continue;
                }

                if (HasJsonPropertyNameAttribute(property, out var jsonName))
                {
                    // If this DTO uses _id pattern, validate ID properties match
                    if (jsonName != "_id" && jsonName != null)
                    {
                        var line = property.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        var relativePath = Path.GetRelativePath(repoRoot, filePath);
                        violations.Add(new Violation(
                            relativePath,
                            line,
                            $"DTO uses legacy '_id' pattern but property '{propertySymbol.Name}' uses JSON name '{jsonName}' instead of '_id'"));
                    }
                }
                else
                {
                    // ID property without JsonPropertyName in a legacy-pattern DTO
                    var line = property.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var relativePath = Path.GetRelativePath(repoRoot, filePath);
                    violations.Add(new Violation(
                        relativePath,
                        line,
                        $"DTO uses legacy '_id' pattern but property '{propertySymbol.Name}' lacks [JsonPropertyName] attribute"));
                }
            }
        }

        // Similarly for message pattern
        if (hasMessagePattern)
        {
            foreach (var property in typeDeclaration.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                var propertySymbol = semanticModel.GetDeclaredSymbol(property) as IPropertySymbol;
                if (propertySymbol == null || !IsMessageProperty(propertySymbol))
                {
                    continue;
                }

                if (HasJsonPropertyNameAttribute(property, out var jsonName))
                {
                    if (jsonName != "msg" && jsonName != null)
                    {
                        var line = property.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        var relativePath = Path.GetRelativePath(repoRoot, filePath);
                        violations.Add(new Violation(
                            relativePath,
                            line,
                            $"DTO uses legacy 'msg' pattern but property '{propertySymbol.Name}' uses JSON name '{jsonName}' instead of 'msg'"));
                    }
                }
            }
        }

        return violations;
    }

    private static bool HasJsonPropertyNameAttribute(PropertyDeclarationSyntax property, out string? jsonName)
    {
        jsonName = null;

        foreach (var attr in property.AttributeLists.SelectMany(list => list.Attributes))
        {
            var attrName = attr.Name.ToString();
            if (attrName == "JsonPropertyName" || attrName.EndsWith(".JsonPropertyName"))
            {
                if (attr.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal)
                {
                    // Extract the string value (including quotes removal)
                    var token = literal.Token;
                    jsonName = token.ValueText;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsIdProperty(IPropertySymbol property)
    {
        // Only consider the primary "Id" property as requiring legacy "_id" naming
        // Relationship IDs (like ExerciseId, UserId, etc.) can use semantic names
        return property.Name == "Id";
    }

    private static bool IsMessageProperty(IPropertySymbol property)
    {
        // Property is considered a message if it contains "Message" in the name
        // and is of type string
        var isStringType = property.Type.SpecialType == SpecialType.System_String;
        var isMessageNamed = property.Name.Contains("Message") || property.Name == "Msg";

        return isStringType && isMessageNamed;
    }

    private static bool ImplementsIResultDto(INamedTypeSymbol typeSymbol, INamedTypeSymbol resultDtoSymbol)
    {
        return typeSymbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, resultDtoSymbol));
    }

    private static bool IsProjectType(INamedTypeSymbol typeSymbol, IAssemblySymbol projectAssembly)
    {
        return SymbolEqualityComparer.Default.Equals(typeSymbol.ContainingAssembly, projectAssembly);
    }

    private static bool IsContractsPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/Contracts/", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record Violation(string File, int Line, string Message)
    {
        public override string ToString() => $"{File}:{Line} {Message}";
    }
}
