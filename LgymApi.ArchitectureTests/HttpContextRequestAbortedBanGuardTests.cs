using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class HttpContextRequestAbortedBanGuardTests
{
    [Test]
    public void Controllers_Should_Not_Use_HttpContext_RequestAborted()
    {
        var repoRoot = ResolveRepositoryRoot();
        var controllersRoot = Path.Combine(repoRoot, "LgymApi.Api", "Features");

        Assert.That(Directory.Exists(controllersRoot), Is.True, $"Controllers root '{controllersRoot}' not found.");

        var controllerFiles = Directory
            .EnumerateFiles(controllersRoot, "*Controller.cs", SearchOption.AllDirectories)
            .Where(path => !IsInBuildArtifacts(path))
            .ToList();

        Assert.That(controllerFiles, Is.Not.Empty, "No controller files found for HttpContext.RequestAborted ban guard test.");

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
                foreach (var member in controllerClass.Members)
                {
                    MethodDeclarationSyntax? method = member switch
                    {
                        MethodDeclarationSyntax m => m,
                        _ => null
                    };

                    if (method is null)
                    {
                        continue;
                    }

                    if (!method.Modifiers.Any(m => Microsoft.CodeAnalysis.CSharpExtensions.IsKind(m, SyntaxKind.PublicKeyword)))
                    {
                        continue;
                    }

                    foreach (var memberAccess in method.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
                    {
                        if (!IsHttpContextRequestAborted(memberAccess))
                        {
                            continue;
                        }

                        var lineSpan = tree.GetLineSpan(memberAccess.Span);
                        violations.Add(new Violation(
                            Path.GetRelativePath(repoRoot, file),
                            lineSpan.StartLinePosition.Line + 1,
                            controllerClass.Identifier.ValueText,
                            method.Identifier.ValueText));
                    }
                }
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            "Controllers must not use HttpContext.RequestAborted. Use CancellationToken parameter instead." + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    private static bool IsHttpContextRequestAborted(MemberAccessExpressionSyntax memberAccess)
    {
        if (memberAccess.Name.Identifier.ValueText != "RequestAborted")
        {
            return false;
        }

        if (memberAccess.Expression is IdentifierNameSyntax { Identifier.ValueText: "HttpContext" })
        {
            return true;
        }

        if (memberAccess.Expression is PostfixUnaryExpressionSyntax { Operand: IdentifierNameSyntax { Identifier.ValueText: "HttpContext" }, RawKind: (int)SyntaxKind.SuppressNullableWarningExpression })
        {
            return true;
        }

        if (memberAccess.Expression is ConditionalAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "HttpContext" } })
        {
            return true;
        }

        return false;
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
        public override string ToString() => $"{File}:{Line} -> {Controller}.{Action} uses HttpContext.RequestAborted instead of CancellationToken parameter";
    }
}
