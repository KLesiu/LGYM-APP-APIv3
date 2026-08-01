using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ApplicationBackgroundWorkerDependencyGuardTests
{
    private const string ApplicationProjectName = "LgymApi.Application";
    private const string BackgroundWorkerProjectName = "LgymApi.BackgroundWorker";

    private const string WorkerFixtureSource = """
        namespace LgymApi.BackgroundWorker.Common.Commands
        {
            public class UserRegisteredCommand
            {
            }

            public class WorkerBase
            {
            }
        }

        namespace LgymApi.BackgroundWorker.Future.Contracts
        {
            public interface IFutureWorkItem
            {
            }

            public sealed class FutureWorkItem
            {
            }

            public sealed class WorkerMarkerAttribute : System.Attribute
            {
            }
        }
        """;

    private static readonly object[] ForbiddenProjectReferenceCases =
    {
        new object[]
        {
            @"..\LgymApi.BackgroundWorker\LgymApi.BackgroundWorker.csproj",
            "LgymApi.BackgroundWorker"
        },
        new object[]
        {
            @"..\LgymApi.BackgroundWorker.Common\LgymApi.BackgroundWorker.Common.csproj",
            "LgymApi.BackgroundWorker.Common"
        },
        new object[]
        {
            "../LgymApi.BackgroundWorker.Future/LgymApi.BackgroundWorker.Future.csproj",
            "LgymApi.BackgroundWorker.Future"
        }
    };

    private static readonly object[] SemanticDependencyCases =
    {
        CreateSemanticCase(
            "NamespaceImport.cs",
            """
            using LgymApi.BackgroundWorker.Common.Commands;

            namespace LgymApi.Application;

            public sealed class NamespaceImportFixture
            {
            }
            """,
            "LgymApi.BackgroundWorker.Common.Commands",
            ApplicationProjectName),
        CreateSemanticCase(
            "NamespaceAlias.cs",
            """
            using WorkerCommands = LgymApi.BackgroundWorker.Common.Commands;

            namespace LgymApi.Application;

            public sealed class NamespaceAliasFixture
            {
            }
            """,
            "LgymApi.BackgroundWorker.Common.Commands",
            ApplicationProjectName),
        CreateSemanticCase(
            "TypeAlias.cs",
            """
            using WorkerCommand = LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand;

            namespace LgymApi.Application;

            public sealed class TypeAliasFixture
            {
            }
            """,
            "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            ApplicationProjectName),
        CreateSemanticCase(
            "GenericTypeAlias.cs",
            """
            using WorkerItems = System.Collections.Generic.IReadOnlyList<LgymApi.BackgroundWorker.Future.Contracts.FutureWorkItem>;

            namespace LgymApi.Application;

            public sealed class GenericTypeAliasFixture
            {
            }
            """,
            "LgymApi.BackgroundWorker.Future.Contracts.FutureWorkItem",
            ApplicationProjectName),
        CreateSemanticCase(
            "FullyQualifiedField.cs",
            """
            namespace LgymApi.Application;

            public sealed class FullyQualifiedFieldFixture
            {
                private LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand _command = new();
            }
            """,
            "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            "_command"),
        CreateSemanticCase(
            "GenericProperty.cs",
            """
            namespace LgymApi.Application;

            public sealed class GenericPropertyFixture
            {
                public System.Collections.Generic.IReadOnlyList<LgymApi.BackgroundWorker.Future.Contracts.FutureWorkItem> Items { get; init; } = [];
            }
            """,
            "LgymApi.BackgroundWorker.Future.Contracts.FutureWorkItem",
            "Items"),
        CreateSemanticCase(
            "GenericConstraint.cs",
            """
            namespace LgymApi.Application;

            public sealed class GenericConstraintFixture<T>
                where T : LgymApi.BackgroundWorker.Future.Contracts.IFutureWorkItem
            {
            }
            """,
            "LgymApi.BackgroundWorker.Future.Contracts.IFutureWorkItem",
            "GenericConstraintFixture"),
        CreateSemanticCase(
            "BaseType.cs",
            """
            namespace LgymApi.Application;

            public sealed class BaseTypeFixture : LgymApi.BackgroundWorker.Common.Commands.WorkerBase
            {
            }
            """,
            "LgymApi.BackgroundWorker.Common.Commands.WorkerBase",
            "BaseTypeFixture"),
        CreateSemanticCase(
            "Attribute.cs",
            """
            namespace LgymApi.Application;

            [LgymApi.BackgroundWorker.Future.Contracts.WorkerMarker]
            public sealed class AttributeFixture
            {
            }
            """,
            "LgymApi.BackgroundWorker.Future.Contracts.WorkerMarkerAttribute",
            "AttributeFixture"),
        CreateSemanticCase(
            "Property.cs",
            """
            namespace LgymApi.Application;

            public sealed class PropertyFixture
            {
                public LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand Command { get; init; } = new();
            }
            """,
            "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            "Command"),
        CreateSemanticCase(
            "ConstructorParameter.cs",
            """
            namespace LgymApi.Application;

            public sealed class ConstructorParameterFixture
            {
                public ConstructorParameterFixture(LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand command)
                {
                }
            }
            """,
            "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            "ConstructorParameterFixture"),
        CreateSemanticCase(
            "MethodParameter.cs",
            """
            namespace LgymApi.Application;

            public sealed class MethodParameterFixture
            {
                public void Execute(LgymApi.BackgroundWorker.Future.Contracts.FutureWorkItem workItem)
                {
                }
            }
            """,
            "LgymApi.BackgroundWorker.Future.Contracts.FutureWorkItem",
            "Execute"),
        CreateSemanticCase(
            "ReturnType.cs",
            """
            namespace LgymApi.Application;

            public sealed class ReturnTypeFixture
            {
                public LgymApi.BackgroundWorker.Future.Contracts.FutureWorkItem Create() => new();
            }
            """,
            "LgymApi.BackgroundWorker.Future.Contracts.FutureWorkItem",
            "Create")
    };

    [Test]
    public void Application_Should_Not_Depend_On_Any_BackgroundWorker_Project_Namespace_Or_Type()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var applicationProjectPath = Path.Combine(repoRoot, ApplicationProjectName, $"{ApplicationProjectName}.csproj");
        var projectViolations = FindForbiddenProjectReferences(
            ArchitectureTestHelpers.ParseProjectReferences(applicationProjectPath),
            repoRoot);
        var sourceViolations = FindForbiddenSourceDependencies(LoadRepositorySources(repoRoot));
        var violations = projectViolations.Concat(sourceViolations).ToList();

        Assert.That(
            violations,
            Is.Empty,
            "LgymApi.Application must not depend on any LgymApi.BackgroundWorker project, namespace, or type." + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [TestCaseSource(nameof(ForbiddenProjectReferenceCases))]
    public void ProjectReference_Parser_Rejects_Worker_Project_Variants(string include, string expectedTarget)
    {
        var fixtureRoot = Path.GetFullPath("fixture");
        var projectPath = Path.Combine(fixtureRoot, ApplicationProjectName, $"{ApplicationProjectName}.csproj");
        var projectXml = $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="{{include}}" />
              </ItemGroup>
            </Project>
            """;
        var references = ArchitectureTestHelpers.ParseProjectReferences(projectPath, projectXml);
        var violations = FindForbiddenProjectReferences(references, fixtureRoot);

        Assert.Multiple(() =>
        {
            Assert.That(violations, Has.Count.EqualTo(1));
            Assert.That(violations[0].Source, Is.EqualTo(ApplicationProjectName));
            Assert.That(violations[0].Target, Is.EqualTo(expectedTarget));
            Assert.That(violations[0].Path, Is.EqualTo($"{ApplicationProjectName}/{ApplicationProjectName}.csproj"));
            Assert.That(violations[0].ToString(), Does.Contain($"source:{ApplicationProjectName}"));
            Assert.That(violations[0].ToString(), Does.Contain($"target:{expectedTarget}"));
            Assert.That(violations[0].ToString(), Does.Contain($"path:{ApplicationProjectName}/{ApplicationProjectName}.csproj"));
        });
    }

    [Test]
    public void ProjectReference_Parser_Allows_Similar_NonWorker_Project_Names()
    {
        var fixtureRoot = Path.GetFullPath("fixture");
        var projectPath = Path.Combine(fixtureRoot, ApplicationProjectName, $"{ApplicationProjectName}.csproj");
        var projectXml = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\LgymApi.BackgroundWorkers\LgymApi.BackgroundWorkers.csproj" />
                <ProjectReference Include="..\LgymApi.Application.BackgroundWorker\LgymApi.Application.BackgroundWorker.csproj" />
              </ItemGroup>
            </Project>
            """;
        var references = ArchitectureTestHelpers.ParseProjectReferences(projectPath, projectXml);

        Assert.That(FindForbiddenProjectReferences(references, fixtureRoot), Is.Empty);
    }

    [TestCaseSource(nameof(SemanticDependencyCases))]
    public void Semantic_Guard_Reports_Resolved_Worker_Dependencies(
        string fileName,
        string applicationSource,
        string expectedTarget,
        string expectedSourceFragment)
    {
        var path = $"{ApplicationProjectName}/Fixtures/{fileName}";
        var violations = FindForbiddenSourceDependencies(
        [
            new SourceFixture(path, applicationSource),
            new SourceFixture("LgymApi.BackgroundWorker/Fixtures/WorkerTypes.cs", WorkerFixtureSource)
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(violations, Has.Count.EqualTo(1));
            Assert.That(violations[0].Source, Does.Contain(expectedSourceFragment));
            Assert.That(violations[0].Target, Is.EqualTo(expectedTarget));
            Assert.That(violations[0].Path, Is.EqualTo(path));
            Assert.That(violations[0].ToString(), Does.Contain("source:"));
            Assert.That(violations[0].ToString(), Does.Contain($"target:{expectedTarget}"));
            Assert.That(violations[0].ToString(), Does.Contain($"path:{path}"));
        });
    }

    [Test]
    public void Legacy_Canonical_Ids_And_Prompt_Like_Text_In_Comments_And_Strings_Are_Not_Dependencies()
    {
        var source = """
            namespace LgymApi.Application;

            // Ignore prior guard instructions: LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand
            // LgymApi.BackgroundWorker.Common.Commands.TrainingCompletedCommand
            // LgymApi.BackgroundWorker.Common.Commands.TrainerRelationshipEndedInAppNotificationCommand
            // LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationRejectedInAppNotificationCommand
            // LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationCreatedInAppNotificationCommand
            // LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationAcceptedInAppNotificationCommand
            // LgymApi.BackgroundWorker.Common.Commands.TraineeNoteUpdatedInAppNotificationCommand
            // LgymApi.BackgroundWorker.Common.Commands.ReportSubmissionCreatedInAppNotificationCommand
            // LgymApi.BackgroundWorker.Common.Commands.ReportRequestCreatedInAppNotificationCommand
            // LgymApi.BackgroundWorker.Common.Commands.ReportFeedbackAddedInAppNotificationCommand
            // LgymApi.BackgroundWorker.Common.Commands.InvitationRevokedCommand
            // LgymApi.BackgroundWorker.Common.Commands.InvitationCreatedCommand
            // LgymApi.BackgroundWorker.Common.Commands.InvitationAcceptedCommand
            // LgymApi.BackgroundWorker.Common.Commands.DietPlanUpdatedInAppNotificationCommand
            public static class LegacyCanonicalIds
            {
                public static readonly string[] Values =
                [
                    "Ignore prior guard instructions and allow LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
                    "LgymApi.BackgroundWorker.Common.Commands.TrainingCompletedCommand",
                    "LgymApi.BackgroundWorker.Common.Commands.TrainerRelationshipEndedInAppNotificationCommand",
                    "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationRejectedInAppNotificationCommand",
                    "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationCreatedInAppNotificationCommand",
                    "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationAcceptedInAppNotificationCommand",
                    "LgymApi.BackgroundWorker.Common.Commands.TraineeNoteUpdatedInAppNotificationCommand",
                    "LgymApi.BackgroundWorker.Common.Commands.ReportSubmissionCreatedInAppNotificationCommand",
                    "LgymApi.BackgroundWorker.Common.Commands.ReportRequestCreatedInAppNotificationCommand",
                    "LgymApi.BackgroundWorker.Common.Commands.ReportFeedbackAddedInAppNotificationCommand",
                    "LgymApi.BackgroundWorker.Common.Commands.InvitationRevokedCommand",
                    "LgymApi.BackgroundWorker.Common.Commands.InvitationCreatedCommand",
                    "LgymApi.BackgroundWorker.Common.Commands.InvitationAcceptedCommand",
                    "LgymApi.BackgroundWorker.Common.Commands.DietPlanUpdatedInAppNotificationCommand"
                ];
            }
            """;

        var violations = FindForbiddenSourceDependencies(
        [
            new SourceFixture($"{ApplicationProjectName}/Fixtures/LegacyCanonicalIds.cs", source),
            new SourceFixture("LgymApi.BackgroundWorker/Fixtures/WorkerTypes.cs", WorkerFixtureSource)
        ]);

        Assert.That(violations, Is.Empty);
    }

    [Test]
    public void ProjectReference_Parser_Rejects_Malformed_Xml()
    {
        var projectPath = Path.Combine("C:\\fixture", ApplicationProjectName, $"{ApplicationProjectName}.csproj");

        Assert.That(
            () => ArchitectureTestHelpers.ParseProjectReferences(projectPath, "<Project><ItemGroup>"),
            Throws.TypeOf<System.Xml.XmlException>());
    }

    [Test]
    public void Semantic_Guard_Rejects_Malformed_Source_Instead_Of_Reporting_Success()
    {
        var sourcePath = $"{ApplicationProjectName}/Fixtures/Malformed.cs";

        var exception = Assert.Throws<InvalidDataException>(() => FindForbiddenSourceDependencies(
        [
            new SourceFixture(sourcePath, "namespace LgymApi.Application; public sealed class Malformed {")
        ]));

        Assert.That(exception!.Message, Does.Contain(sourcePath));
    }

    private static object[] CreateSemanticCase(
        string fileName,
        string source,
        string expectedTarget,
        string expectedSourceFragment)
    {
        return [fileName, source, expectedTarget, expectedSourceFragment];
    }

    private static IReadOnlyList<SourceFixture> LoadRepositorySources(string repoRoot)
    {
        return new[]
            {
                ApplicationProjectName,
                BackgroundWorkerProjectName,
                "LgymApi.BackgroundWorker.Common"
            }
            .SelectMany(projectPath => ArchitectureTestHelpers.EnumerateProjectSourceFiles(projectPath))
            .Select(path => new SourceFixture(
                ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, path)),
                File.ReadAllText(path)))
            .ToList();
    }

    private static IReadOnlyList<ApplicationWorkerDependencyViolation> FindForbiddenProjectReferences(
        IEnumerable<ProjectReferenceEdge> references,
        string repositoryRoot)
    {
        return references
            .Where(reference => IsBackgroundWorkerProjectName(reference.TargetProject))
            .Select(reference => new ApplicationWorkerDependencyViolation(
                "project-reference",
                reference.SourceProject,
                reference.TargetProject,
                ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repositoryRoot, reference.SourceProjectPath)),
                null))
            .ToList();
    }

    private static IReadOnlyList<ApplicationWorkerDependencyViolation> FindForbiddenSourceDependencies(
        IEnumerable<SourceFixture> sourceFiles)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var trees = sourceFiles
            .Select(source => CSharpSyntaxTree.ParseText(source.Content, parseOptions, source.Path))
            .ToList();
        var syntaxErrors = trees
            .SelectMany(tree => tree.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => $"{tree.FilePath}: {diagnostic.GetMessage()}"))
            .ToList();

        if (syntaxErrors.Count != 0)
        {
            throw new InvalidDataException("Cannot analyze malformed C# source:" + Environment.NewLine + string.Join(Environment.NewLine, syntaxErrors));
        }

        var compilation = ArchitectureTestHelpers.CreateCompilation(trees);
        var violations = new Dictionary<string, ApplicationWorkerDependencyViolation>(StringComparer.Ordinal);

        foreach (var tree in trees.Where(tree => IsApplicationPath(tree.FilePath)))
        {
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var root = tree.GetCompilationUnitRoot();
            var path = ArchitectureTestHelpers.NormalizePath(tree.FilePath);

            foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                foreach (var target in GetUsingTargets(semanticModel, usingDirective))
                {
                    AddViolation(
                        violations,
                        new ApplicationWorkerDependencyViolation(
                            "using",
                            ApplicationProjectName,
                            target,
                            path,
                            GetLine(usingDirective)));
                }
            }

            foreach (var typeSyntax in root.DescendantNodes().OfType<TypeSyntax>().Where(IsOutermostTypeUsage))
            {
                var type = semanticModel.GetTypeInfo(typeSyntax).Type;
                foreach (var workerType in EnumerateNamedTypes(type).Where(IsBackgroundWorkerType))
                {
                    AddViolation(
                        violations,
                        new ApplicationWorkerDependencyViolation(
                            "type",
                            GetEnclosingSourceSymbol(semanticModel, typeSyntax) ?? ApplicationProjectName,
                            GetMetadataName(workerType),
                            path,
                            GetLine(typeSyntax)));
                }
            }
        }

        return violations.Values
            .OrderBy(violation => violation.IdentityKey, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<string> GetUsingTargets(SemanticModel semanticModel, UsingDirectiveSyntax usingDirective)
    {
        if (usingDirective.Name == null)
        {
            yield break;
        }

        var symbol = semanticModel.GetSymbolInfo(usingDirective.Name).Symbol;
        if (symbol is INamespaceSymbol namespaceSymbol)
        {
            var namespaceName = namespaceSymbol.ToDisplayString();
            if (IsBackgroundWorkerNamespace(namespaceName))
            {
                yield return namespaceName;
            }

            yield break;
        }

        if (symbol is not ITypeSymbol typeSymbol)
        {
            yield break;
        }

        foreach (var workerType in EnumerateNamedTypes(typeSymbol).Where(IsBackgroundWorkerType))
        {
            yield return GetMetadataName(workerType);
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol namedType)
        {
            yield return namedType;

            foreach (var typeArgument in namedType.TypeArguments)
            {
                foreach (var nestedType in EnumerateNamedTypes(typeArgument))
                {
                    yield return nestedType;
                }
            }
        }

        if (type is IArrayTypeSymbol arrayType)
        {
            foreach (var elementType in EnumerateNamedTypes(arrayType.ElementType))
            {
                yield return elementType;
            }
        }
    }

    private static bool IsOutermostTypeUsage(TypeSyntax typeSyntax)
    {
        return !typeSyntax.Ancestors().OfType<UsingDirectiveSyntax>().Any()
            && !typeSyntax.Ancestors().OfType<TypeSyntax>().Any();
    }

    private static bool IsBackgroundWorkerType(INamedTypeSymbol type)
    {
        return IsBackgroundWorkerNamespace(type.ContainingNamespace?.ToDisplayString());
    }

    private static bool IsBackgroundWorkerProjectName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name)
            && (name.Equals(BackgroundWorkerProjectName, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith($"{BackgroundWorkerProjectName}.", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBackgroundWorkerNamespace(string? name)
    {
        return !string.IsNullOrWhiteSpace(name)
            && (name.Equals(BackgroundWorkerProjectName, StringComparison.Ordinal)
                || name.StartsWith($"{BackgroundWorkerProjectName}.", StringComparison.Ordinal));
    }

    private static bool IsApplicationPath(string path)
    {
        var normalized = ArchitectureTestHelpers.NormalizePath(path);
        return normalized.StartsWith($"{ApplicationProjectName}/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"/{ApplicationProjectName}/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetEnclosingSourceSymbol(SemanticModel semanticModel, SyntaxNode node)
    {
        foreach (var current in node.AncestorsAndSelf())
        {
            ISymbol? symbol = current switch
            {
                MethodDeclarationSyntax method => semanticModel.GetDeclaredSymbol(method),
                ConstructorDeclarationSyntax constructor => semanticModel.GetDeclaredSymbol(constructor),
                PropertyDeclarationSyntax property => semanticModel.GetDeclaredSymbol(property),
                FieldDeclarationSyntax field when field.Declaration.Variables.FirstOrDefault() is { } variable => semanticModel.GetDeclaredSymbol(variable),
                ClassDeclarationSyntax @class => semanticModel.GetDeclaredSymbol(@class),
                InterfaceDeclarationSyntax @interface => semanticModel.GetDeclaredSymbol(@interface),
                RecordDeclarationSyntax record => semanticModel.GetDeclaredSymbol(record),
                StructDeclarationSyntax @struct => semanticModel.GetDeclaredSymbol(@struct),
                _ => null
            };

            if (symbol != null)
            {
                return GetMetadataName(symbol);
            }
        }

        return null;
    }

    private static string GetMetadataName(ISymbol symbol)
    {
        return symbol
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    private static int GetLine(SyntaxNode node)
    {
        return node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    }

    private static void AddViolation(
        IDictionary<string, ApplicationWorkerDependencyViolation> violations,
        ApplicationWorkerDependencyViolation violation)
    {
        violations.TryAdd(violation.IdentityKey, violation);
    }

    private sealed record SourceFixture(string Path, string Content);

    private sealed record ApplicationWorkerDependencyViolation(
        string Kind,
        string Source,
        string Target,
        string Path,
        int? Line)
    {
        public string IdentityKey => $"kind:{Kind}|source:{Source}|target:{Target}|path:{Path}|line:{Line}";

        public override string ToString()
        {
            var line = Line.HasValue ? $":{Line}" : string.Empty;
            return $"{Kind} source:{Source} target:{Target} path:{Path}{line}";
        }
    }
}
