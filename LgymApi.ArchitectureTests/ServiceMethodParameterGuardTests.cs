using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ServiceMethodParameterGuardTests
{
    private const int MaxPublicMethodParameters = 4;
    private const int MaxNonPublicMethodParameters = 5;

    [Test]
    public void Public_Service_Methods_Should_Not_Exceed_Maximum_Parameter_Count()
    {
        var repoRoot = ResolveRepositoryRoot();
        var applicationRoot = Path.Combine(repoRoot, "LgymApi.Application");

        Assert.That(Directory.Exists(applicationRoot), Is.True, $"Application root '{applicationRoot}' not found.");

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest);
        var serviceFiles = Directory
            .EnumerateFiles(applicationRoot, "*Service.cs", SearchOption.AllDirectories)
            .Where(path => !IsInBuildArtifacts(path))
            .ToList();

        Assert.That(serviceFiles, Is.Not.Empty, "No service files found for method parameter guard test.");

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
                foreach (var method in serviceClass.Members.OfType<MethodDeclarationSyntax>())
                {
                    if (!method.Modifiers.Any(modifier => Microsoft.CodeAnalysis.CSharpExtensions.IsKind(modifier, SyntaxKind.PublicKeyword)))
                    {
                        continue;
                    }

                    var parameterCount = method.ParameterList.Parameters.Count(parameter => !IsCancellationTokenParameter(parameter));
                    if (parameterCount > MaxPublicMethodParameters)
                    {
                        var lineSpan = tree.GetLineSpan(method.Span);
                        violations.Add(new Violation(
                            Path.GetRelativePath(repoRoot, serviceFile),
                            lineSpan.StartLinePosition.Line + 1,
                            serviceClass.Identifier.ValueText,
                            method.Identifier.ValueText,
                            parameterCount));
                    }
                }
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            $"Every public method in concrete *Service classes in LgymApi.Application must have parameter count <= {MaxPublicMethodParameters} (excluding CancellationToken)." + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    [Test]
    public void NonPublic_Service_Methods_Should_Not_Exceed_Maximum_Parameter_Count()
    {
        var repoRoot = ResolveRepositoryRoot();
        var applicationRoot = Path.Combine(repoRoot, "LgymApi.Application");

        Assert.That(Directory.Exists(applicationRoot), Is.True, $"Application root '{applicationRoot}' not found.");

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest);
        var serviceFiles = Directory
            .EnumerateFiles(applicationRoot, "*Service.cs", SearchOption.AllDirectories)
            .Where(path => !IsInBuildArtifacts(path))
            .ToList();

        Assert.That(serviceFiles, Is.Not.Empty, "No service files found for method parameter guard test.");

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
                foreach (var method in serviceClass.Members.OfType<MethodDeclarationSyntax>())
                {
                    if (method.Modifiers.Any(modifier => Microsoft.CodeAnalysis.CSharpExtensions.IsKind(modifier, SyntaxKind.PublicKeyword)))
                    {
                        continue;
                    }

                    var parameterCount = method.ParameterList.Parameters.Count(parameter => !IsCancellationTokenParameter(parameter));
                    if (parameterCount > MaxNonPublicMethodParameters)
                    {
                        var lineSpan = tree.GetLineSpan(method.Span);
                        violations.Add(new Violation(
                            Path.GetRelativePath(repoRoot, serviceFile),
                            lineSpan.StartLinePosition.Line + 1,
                            serviceClass.Identifier.ValueText,
                            method.Identifier.ValueText,
                            parameterCount));
                    }
                }
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            $"Every non-public method in concrete *Service classes in LgymApi.Application must have parameter count <= {MaxNonPublicMethodParameters} (excluding CancellationToken)." + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    private static bool IsCancellationTokenParameter(ParameterSyntax parameter)
    {
        var typeName = parameter.Type?.ToString();
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        return string.Equals(typeName, "CancellationToken", StringComparison.Ordinal) ||
               typeName.EndsWith(".CancellationToken", StringComparison.Ordinal);
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

    private sealed record Violation(string File, int Line, string ServiceName, string MethodName, int ParameterCount)
    {
        public override string ToString() => $"{File}:{Line} -> {ServiceName}.{MethodName} has {ParameterCount} method parameters";
    }
}
