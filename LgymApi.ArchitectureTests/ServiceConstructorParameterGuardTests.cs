using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ServiceConstructorParameterGuardTests
{
    private const int MaxConstructorParameters = 5;

    [Test]
    public void Application_Service_Constructors_Should_Not_Exceed_Maximum_Parameter_Count()
    {
        var repoRoot = ResolveRepositoryRoot();
        var applicationRoot = Path.Combine(repoRoot, "LgymApi.Application");

        Assert.That(Directory.Exists(applicationRoot), Is.True, $"Application root '{applicationRoot}' not found.");

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest);
        var serviceFiles = Directory
            .EnumerateFiles(applicationRoot, "*Service.cs", SearchOption.AllDirectories)
            .Where(path => !IsInBuildArtifacts(path))
            .ToList();

        Assert.That(serviceFiles, Is.Not.Empty, "No service files found for constructor parameter guard test.");

        var violations = new List<Violation>();
        foreach (var serviceFile in serviceFiles)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(serviceFile), parseOptions, serviceFile);
            var root = tree.GetCompilationUnitRoot();

            var serviceClasses = root
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(type => IsConcreteService(type));

            foreach (var serviceClass in serviceClasses)
            {
                foreach (var constructor in serviceClass.Members.OfType<ConstructorDeclarationSyntax>())
                {
                    var parameterCount = constructor.ParameterList.Parameters.Count;
                    if (parameterCount > MaxConstructorParameters)
                    {
                        var lineSpan = tree.GetLineSpan(constructor.Span);
                        violations.Add(new Violation(
                            Path.GetRelativePath(repoRoot, serviceFile),
                            lineSpan.StartLinePosition.Line + 1,
                            serviceClass.Identifier.ValueText,
                            parameterCount));
                    }
                }
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            $"Every concrete *Service class in LgymApi.Application must have constructor parameter count <= {MaxConstructorParameters}." + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    private static bool IsConcreteService(ClassDeclarationSyntax typeDeclaration)
    {
        if (!typeDeclaration.Identifier.ValueText.EndsWith("Service", StringComparison.Ordinal))
        {
            return false;
        }

        return !typeDeclaration.Modifiers.Any(modifier => Microsoft.CodeAnalysis.CSharpExtensions.IsKind(modifier, SyntaxKind.AbstractKeyword));
    }

    private static bool IsInBuildArtifacts(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase);
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

    private sealed record Violation(string File, int Line, string ServiceName, int ParameterCount)
    {
        public override string ToString() => $"{File}:{Line} -> {ServiceName} has {ParameterCount} constructor parameters";
    }
}
