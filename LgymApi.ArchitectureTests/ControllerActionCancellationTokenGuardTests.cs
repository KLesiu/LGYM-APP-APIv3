using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ControllerActionCancellationTokenGuardTests
{
    [Test]
    public void Controller_Actions_Returning_Task_Should_Have_CancellationToken_Parameter()
    {
        var repoRoot = ResolveRepositoryRoot();
        var controllersRoot = Path.Combine(repoRoot, "LgymApi.Api", "Features");

        Assert.That(Directory.Exists(controllersRoot), Is.True, $"Controllers root '{controllersRoot}' not found.");

        var controllerFiles = Directory
            .EnumerateFiles(controllersRoot, "*Controller.cs", SearchOption.AllDirectories)
            .Where(path => !IsInBuildArtifacts(path))
            .ToList();

        Assert.That(controllerFiles, Is.Not.Empty, "No controller files found for CancellationToken guard test.");

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var violations = new List<Violation>();

        foreach (var file in controllerFiles)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file);
            var root = tree.GetCompilationUnitRoot();

            var controllerClasses = root
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(cls => cls.Identifier.ValueText.EndsWith("Controller", StringComparison.Ordinal));

            foreach (var controllerClass in controllerClasses)
            {
                foreach (var method in controllerClass.Members.OfType<MethodDeclarationSyntax>())
                {
                    if (!method.Modifiers.Any(m => Microsoft.CodeAnalysis.CSharpExtensions.IsKind(m, SyntaxKind.PublicKeyword)))
                    {
                        continue;
                    }

                    if (!IsAsyncActionResultReturnType(method))
                    {
                        continue;
                    }

                    if (HasCancellationTokenParameter(method.ParameterList))
                    {
                        continue;
                    }

                    var lineSpan = tree.GetLineSpan(method.Span);
                    violations.Add(new Violation(
                        Path.GetRelativePath(repoRoot, file),
                        lineSpan.StartLinePosition.Line + 1,
                        controllerClass.Identifier.ValueText,
                        method.Identifier.ValueText));
                }
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            "Every public controller action returning Task<IActionResult> or Task<ActionResult<T>> must declare a CancellationToken parameter." + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    private static bool IsAsyncActionResultReturnType(MethodDeclarationSyntax method)
    {
        var returnType = method.ReturnType.ToString();

        if (returnType == "Task<IActionResult>")
        {
            return true;
        }

        if (returnType == "Task<ActionResult>")
        {
            return true;
        }

        if (returnType.StartsWith("Task<ActionResult<", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool HasCancellationTokenParameter(ParameterListSyntax parameterList)
    {
        return parameterList.Parameters.Any(parameter =>
            parameter.Type is IdentifierNameSyntax { Identifier.ValueText: "CancellationToken" }
            || parameter.Type is QualifiedNameSyntax { Right: IdentifierNameSyntax { Identifier.ValueText: "CancellationToken" } });
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

    private sealed record Violation(string File, int Line, string Controller, string Action)
    {
        public override string ToString() => $"{File}:{Line} -> {Controller}.{Action} is missing CancellationToken parameter";
    }
}
