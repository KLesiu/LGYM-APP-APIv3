using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class SerializationOptionsGuardTests
{
    private const string CanonicalOwnerMetadataName = "LgymApi.Application.Platform.Contracts.Serialization.SharedSerializationOptions";

    [Test]
    public void JsonSerializer_Must_Use_SharedSerializationOptions_In_Production_Paths()
    {
        var (repoRoot, compilation, syntaxTrees) = PrepareCompilation();
        var violations = CollectViolations(
            compilation,
            syntaxTrees.Where(tree => !IsTestPath(tree.FilePath) && IsTargetedProductionPath(tree.FilePath)),
            CanonicalOwnerMetadataName,
            repoRoot);

        Assert.That(
            violations,
            Is.Empty,
            "JsonSerializer.Serialize/Deserialize must use SharedSerializationOptions.Current in BackgroundWorker and Infrastructure production paths to ensure consistent cross-module contract serialization." + Environment.NewLine +
            "Violations found:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    [Test]
    public void Future_Platform_Owner_Fixture_Is_Accepted()
    {
        var compilation = CreateFixtureCompilation(
            ("LgymApi.Platform/Contracts/Serialization/SharedSerializationOptions.cs", """
                using System.Text.Json;

                namespace LgymApi.Application.Platform.Contracts.Serialization;

                public static class SharedSerializationOptions
                {
                    public static JsonSerializerOptions Current { get; } = new();
                }
                """),
            ("LgymApi.BackgroundWorker/Fixtures/FutureOwnerConsumer.cs", """
                using System.Text.Json;
                using LgymApi.Application.Platform.Contracts.Serialization;

                namespace LgymApi.BackgroundWorker.Fixtures;

                public sealed class FutureOwnerConsumer
                {
                    public string Serialize(object value) => JsonSerializer.Serialize(value, SharedSerializationOptions.Current);
                }
                """));

        var violations = CollectViolations(
            compilation,
            compilation.SyntaxTrees.Where(tree => tree.FilePath.EndsWith("FutureOwnerConsumer.cs", StringComparison.Ordinal)),
            CanonicalOwnerMetadataName,
            repoRoot: null);

        Assert.That(violations, Is.Empty);
    }

    [Test]
    public void Future_Platform_Owner_Fixture_Rejects_Default_JsonSerializerOptions()
    {
        var compilation = CreateFixtureCompilation(("LgymApi.BackgroundWorker/Fixtures/DefaultOptionsConsumer.cs", """
            using System.Text.Json;

            namespace LgymApi.BackgroundWorker.Fixtures;

            public sealed class DefaultOptionsConsumer
            {
                public string Serialize(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions());
            }
            """));

        var violation = CollectViolations(
                compilation,
                compilation.SyntaxTrees,
            CanonicalOwnerMetadataName,
                repoRoot: null)
            .Single();

        Assert.That(violation.Message, Is.EqualTo("JsonSerializer.Serialize must use LgymApi.Application.Platform.Contracts.Serialization.SharedSerializationOptions.Current."));
    }

    [Test]
    public void Future_Platform_Owner_Fixture_Rejects_Legacy_Common_Owner()
    {
        var compilation = CreateFixtureCompilation(
            ("LgymApi.BackgroundWorker.Common/Serialization/SharedSerializationOptions.cs", """
                using System.Text.Json;

                namespace LgymApi.BackgroundWorker.Common.Serialization;

                public static class SharedSerializationOptions
                {
                    public static JsonSerializerOptions Current { get; } = new();
                }
                """),
            ("LgymApi.BackgroundWorker/Fixtures/LegacyOwnerConsumer.cs", """
                using System.Text.Json;
                using LgymApi.BackgroundWorker.Common.Serialization;

                namespace LgymApi.BackgroundWorker.Fixtures;

                public sealed class LegacyOwnerConsumer
                {
                    public string Serialize(object value) => JsonSerializer.Serialize(value, SharedSerializationOptions.Current);
                }
                """));

        var violation = CollectViolations(
                compilation,
                compilation.SyntaxTrees.Where(tree => tree.FilePath.EndsWith("LegacyOwnerConsumer.cs", StringComparison.Ordinal)),
                CanonicalOwnerMetadataName,
                repoRoot: null)
            .Single();

        Assert.That(violation.Message, Is.EqualTo("JsonSerializer.Serialize must use LgymApi.Application.Platform.Contracts.Serialization.SharedSerializationOptions.Current."));
    }

    private static bool IsJsonSerializerMethod(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.ContainingType?.Name != "JsonSerializer")
        {
            return false;
        }

        if (methodSymbol.ContainingNamespace?.ToDisplayString() != "System.Text.Json")
        {
            return false;
        }

        return methodSymbol.Name is "Serialize" or "Deserialize";
    }

    private static IReadOnlyList<Violation> CollectViolations(
        CSharpCompilation compilation,
        IEnumerable<SyntaxTree> syntaxTrees,
        string ownerMetadataName,
        string? repoRoot)
    {
        var violations = new List<Violation>();

        foreach (var tree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            foreach (var invocation in tree.GetCompilationUnitRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol
                    || !IsJsonSerializerMethod(methodSymbol)
                    || UsesSharedSerializationOptions(invocation, semanticModel, ownerMetadataName))
                {
                    continue;
                }

                var relativePath = repoRoot == null
                    ? Normalize(tree.FilePath)
                    : Normalize(Path.GetRelativePath(repoRoot, tree.FilePath));
                var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                violations.Add(new Violation(
                    relativePath,
                    line,
                    $"JsonSerializer.{methodSymbol.Name} must use {ownerMetadataName}.Current."));
            }
        }

        return violations;
    }

    private static bool UsesSharedSerializationOptions(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string ownerMetadataName)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is not MemberAccessExpressionSyntax memberAccess
                || semanticModel.GetSymbolInfo(memberAccess).Symbol is not IPropertySymbol propertySymbol
                || propertySymbol.Name != "Current"
                || propertySymbol.ContainingType is null)
            {
                continue;
            }

            if (string.Equals(GetMetadataName(propertySymbol.ContainingType), ownerMetadataName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static CSharpCompilation CreateFixtureCompilation(params (string Path, string Source)[] sources)
    {
        var syntaxTrees = sources
            .Select(source => CSharpSyntaxTree.ParseText(source.Source, path: source.Path))
            .ToList();
        var compilation = CSharpCompilation.Create(
            "SerializationOptionsFixture",
            syntaxTrees,
            ResolveMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.That(
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "The serializer ownership fixture must compile before semantic ownership is evaluated.");

        return compilation;
    }

    private static string GetMetadataName(INamedTypeSymbol symbol)
    {
        var typeNames = new Stack<string>();
        for (var current = symbol; current != null; current = current.ContainingType)
        {
            typeNames.Push(current.MetadataName);
        }

        var namespaceName = symbol.ContainingNamespace.ToDisplayString();
        return string.IsNullOrEmpty(namespaceName)
            ? string.Join(".", typeNames)
            : $"{namespaceName}.{string.Join(".", typeNames)}";
    }

    private static bool IsTargetedProductionPath(string path)
    {
        var normalized = Normalize(path);
        return normalized.Contains("/LgymApi.BackgroundWorker/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/LgymApi.Infrastructure/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTestPath(string path)
    {
        var normalized = Normalize(path);
        return normalized.Contains("/tests/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/unittests/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/integrationtests/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/architecturetests/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/lgymapi.unittests/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/lgymapi.integrationtests/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/lgymapi.architecturetests/", StringComparison.OrdinalIgnoreCase);
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

        Assert.That(sourceFiles, Is.Not.Empty, "No C# source files found for serialization options guard.");

        var syntaxTrees = sourceFiles
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), options: parseOptions, path: path))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "SerializationOptionsGuard",
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
