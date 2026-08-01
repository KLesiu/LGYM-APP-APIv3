using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class BackgroundWorkerCommonSurfaceGuardTests
{
    private const string CommonProjectPath = "LgymApi.BackgroundWorker.Common";

    private static readonly CommonSurfaceEntry[] AllowedSurface =
    {
        new("Jobs/IActionMessageJob.cs", "LgymApi.BackgroundWorker.Common.Jobs.IActionMessageJob", TypeKind.Interface, Accessibility.Public, false, "public interface IActionMessageJob {}"),
        new("Jobs/ICommittedIntentDispatchJob.cs", "LgymApi.BackgroundWorker.Common.Jobs.ICommittedIntentDispatchJob", TypeKind.Interface, Accessibility.Public, false, "public interface ICommittedIntentDispatchJob {}"),
        new("Jobs/IEmailJob.cs", "LgymApi.BackgroundWorker.Common.Jobs.IEmailJob", TypeKind.Interface, Accessibility.Public, false, "public interface IEmailJob {}"),
        new("Jobs/IExpiredPhotoUploadCleanupJob.cs", "LgymApi.BackgroundWorker.Common.Jobs.IExpiredPhotoUploadCleanupJob", TypeKind.Interface, Accessibility.Public, false, "public interface IExpiredPhotoUploadCleanupJob {}"),
        new("Jobs/IInvitationEmailJob.cs", "LgymApi.BackgroundWorker.Common.Jobs.IInvitationEmailJob", TypeKind.Interface, Accessibility.Public, false, "public interface IInvitationEmailJob {}"),
        new("Jobs/IPushNotificationJob.cs", "LgymApi.BackgroundWorker.Common.Jobs.IPushNotificationJob", TypeKind.Interface, Accessibility.Public, false, "public interface IPushNotificationJob {}"),
        new("Jobs/IRecurringReportAssignmentProcessingJob.cs", "LgymApi.BackgroundWorker.Common.Jobs.IRecurringReportAssignmentProcessingJob", TypeKind.Interface, Accessibility.Public, false, "public interface IRecurringReportAssignmentProcessingJob {}"),
        new("Jobs/IStalePushInstallationCleanupJob.cs", "LgymApi.BackgroundWorker.Common.Jobs.IStalePushInstallationCleanupJob", TypeKind.Interface, Accessibility.Public, false, "public interface IStalePushInstallationCleanupJob {}"),
        new("Jobs/IWelcomeEmailJob.cs", "LgymApi.BackgroundWorker.Common.Jobs.IWelcomeEmailJob", TypeKind.Interface, Accessibility.Public, false, "public interface IWelcomeEmailJob {}"),
        new("IActionMessageScheduler.cs", "LgymApi.BackgroundWorker.Common.IActionMessageScheduler", TypeKind.Interface, Accessibility.Public, false, "public interface IActionMessageScheduler {}"),
        new("IEmailBackgroundScheduler.cs", "LgymApi.BackgroundWorker.Common.IEmailBackgroundScheduler", TypeKind.Interface, Accessibility.Public, false, "public interface IEmailBackgroundScheduler {}"),
        new("IdempotencyKeyPolicy.cs", "LgymApi.BackgroundWorker.Common.IdempotencyKeyPolicy", TypeKind.Class, Accessibility.Public, true, "public static class IdempotencyKeyPolicy {}"),
        new("Notifications/IEmailJobHandler.cs", "LgymApi.BackgroundWorker.Common.Notifications.IEmailJobHandler", TypeKind.Interface, Accessibility.Public, false, "public interface IEmailJobHandler {}"),
        new("Notifications/IEmailMetrics.cs", "LgymApi.BackgroundWorker.Common.Notifications.IEmailMetrics", TypeKind.Interface, Accessibility.Public, false, "public interface IEmailMetrics {}"),
        new("Notifications/IEmailNotificationsFeature.cs", "LgymApi.BackgroundWorker.Common.Notifications.IEmailNotificationsFeature", TypeKind.Interface, Accessibility.Public, false, "public interface IEmailNotificationsFeature {}"),
        new("Notifications/IEmailPayload.cs", "LgymApi.BackgroundWorker.Common.Notifications.IEmailPayload", TypeKind.Interface, Accessibility.Public, false, "public interface IEmailPayload {}"),
        new("Notifications/IEmailScheduler.cs", "LgymApi.BackgroundWorker.Common.Notifications.IEmailScheduler`1", TypeKind.Interface, Accessibility.Public, false, "public interface IEmailScheduler<TPayload> {}"),
        new("Notifications/IEmailSender.cs", "LgymApi.BackgroundWorker.Common.Notifications.IEmailSender", TypeKind.Interface, Accessibility.Public, false, "public interface IEmailSender {}"),
        new("Notifications/IEmailTemplateComposer.cs", "LgymApi.BackgroundWorker.Common.Notifications.IEmailTemplateComposer", TypeKind.Interface, Accessibility.Public, false, "public interface IEmailTemplateComposer {}"),
        new("Notifications/IEmailTemplateComposerFactory.cs", "LgymApi.BackgroundWorker.Common.Notifications.IEmailTemplateComposerFactory", TypeKind.Interface, Accessibility.Public, false, "public interface IEmailTemplateComposerFactory {}"),
        new("Notifications/Models/EmailMessage.cs", "LgymApi.BackgroundWorker.Common.Notifications.Models.EmailMessage", TypeKind.Class, Accessibility.Public, false, "public sealed class EmailMessage {}"),
        new("Notifications/Models/InvitationAcceptedEmailPayload.cs", "LgymApi.BackgroundWorker.Common.Notifications.Models.InvitationAcceptedEmailPayload", TypeKind.Class, Accessibility.Public, false, "public sealed class InvitationAcceptedEmailPayload {}"),
        new("Notifications/Models/InvitationEmailPayload.cs", "LgymApi.BackgroundWorker.Common.Notifications.Models.InvitationEmailPayload", TypeKind.Class, Accessibility.Public, false, "public sealed class InvitationEmailPayload {}"),
        new("Notifications/Models/InvitationRevokedEmailPayload.cs", "LgymApi.BackgroundWorker.Common.Notifications.Models.InvitationRevokedEmailPayload", TypeKind.Class, Accessibility.Public, false, "public sealed class InvitationRevokedEmailPayload {}"),
        new("Notifications/Models/PasswordRecoveryEmailPayload.cs", "LgymApi.BackgroundWorker.Common.Notifications.Models.PasswordRecoveryEmailPayload", TypeKind.Class, Accessibility.Public, false, "public sealed class PasswordRecoveryEmailPayload {}"),
        new("Notifications/Models/TrainingCompletedEmailPayload.cs", "LgymApi.BackgroundWorker.Common.Notifications.Models.TrainingCompletedEmailPayload", TypeKind.Class, Accessibility.Public, false, "public sealed class TrainingCompletedEmailPayload {}"),
        new("Notifications/Models/TrainingCompletedEmailPayload.cs", "LgymApi.BackgroundWorker.Common.Notifications.Models.TrainingExerciseSummary", TypeKind.Class, Accessibility.Public, false, "public sealed class TrainingExerciseSummary {}"),
        new("Notifications/Models/WelcomeEmailPayload.cs", "LgymApi.BackgroundWorker.Common.Notifications.Models.WelcomeEmailPayload", TypeKind.Class, Accessibility.Public, false, "public sealed class WelcomeEmailPayload {}")
    };

    private static readonly HashSet<string> ForbiddenRootSymbols = new(StringComparer.Ordinal)
    {
        "IActionCommand",
        "ICommandDispatcher",
        "IBackgroundAction",
        "IActionExecutionScopeProvider",
        "CommandDescriptor",
        "CommandTypeDiscriminatorPolicy"
    };

    [Test]
    public void Final_Closed_Common_Surface_Fixture_Allows_Only_The_Exact_Manifest()
    {
        var compilation = CreateFixtureCompilation(CreateAllowedFixtureSources());

        Assert.That(CollectViolations(compilation), Is.Empty);
    }

    [Test]
    public void Repository_Common_Surface_Matches_The_Exact_Manifest()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var sources = CreateImplicitUsingSource().Concat(
            ArchitectureTestHelpers
                .EnumerateProjectSourceFiles(CommonProjectPath)
                .Select(path => (
                    ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repositoryRoot, path)),
                    File.ReadAllText(path))));
        var compilation = CreateFixtureCompilation(sources);

        Assert.That(
            CollectViolations(compilation),
            Is.Empty,
            "LgymApi.BackgroundWorker.Common must retain only its exact job and email wire-contract surface.");
    }

    private static IEnumerable<(string Path, string Source)> CreateImplicitUsingSource()
    {
        yield return (
            "Fixture/ImplicitUsings.g.cs",
            """
            global using System;
            global using System.Collections.Generic;
            global using System.IO;
            global using System.Linq;
            global using System.Net.Http;
            global using System.Threading;
            global using System.Threading.Tasks;
            """);
    }

    [Test]
    public void Arbitrary_Added_Common_File_Fixture_Is_Rejected()
    {
        var compilation = CreateFixtureCompilation(
            CreateAllowedFixtureSources().Append((
                $"{CommonProjectPath}/AdditionalContract.cs",
                "namespace LgymApi.BackgroundWorker.Common; public interface IAdditionalContract {}")));

        var violations = CollectViolations(compilation);

        Assert.Multiple(() =>
        {
            Assert.That(
                violations,
                Has.Some.Matches<CommonSurfaceViolation>(candidate =>
                    candidate.RelativePath == $"{CommonProjectPath}/AdditionalContract.cs"
                    && candidate.Message.Contains("Unexpected Common source file", StringComparison.Ordinal)));
            Assert.That(
                violations,
                Has.Some.Matches<CommonSurfaceViolation>(candidate =>
                    candidate.RelativePath == $"{CommonProjectPath}/AdditionalContract.cs"
                    && candidate.Message.Contains("Unexpected Common symbol 'LgymApi.BackgroundWorker.Common.IAdditionalContract'", StringComparison.Ordinal)));
        });
    }

    [TestCase("Commands/UnexpectedCommand.cs", "namespace LgymApi.BackgroundWorker.Common.Commands; public sealed class UnexpectedCommand {}", "Commands")]
    [TestCase("Serialization/UnexpectedSerializer.cs", "namespace LgymApi.BackgroundWorker.Common.Serialization; public sealed class UnexpectedSerializer {}", "Serialization")]
    [TestCase("Push/UnexpectedPushContract.cs", "namespace LgymApi.BackgroundWorker.Common.Push; public sealed class UnexpectedPushContract {}", "Push")]
    public void Forbidden_Common_Family_Fixture_Is_Rejected(string relativePath, string source, string family)
    {
        var compilation = CreateFixtureCompilation(
            CreateAllowedFixtureSources().Append(($"{CommonProjectPath}/{relativePath}", source)));

        Assert.That(
            CollectViolations(compilation),
            Has.Some.Matches<CommonSurfaceViolation>(candidate =>
                candidate.RelativePath == $"{CommonProjectPath}/{relativePath}"
                && candidate.Message.Contains($"Forbidden Common path family '{family}'", StringComparison.Ordinal)));
    }

    [Test]
    public void Explicitly_Forbidden_Common_Root_Symbols_Are_Rejected()
    {
        Assert.Multiple(() =>
        {
            foreach (var symbolName in ForbiddenRootSymbols)
            {
                var declaration = symbolName is "CommandDescriptor"
                    ? $"public sealed class {symbolName} {{}}"
                    : symbolName == "CommandTypeDiscriminatorPolicy"
                        ? $"public static class {symbolName} {{}}"
                        : $"public interface {symbolName} {{}}";
                var compilation = CreateFixtureCompilation(
                    CreateAllowedFixtureSources().Append((
                        $"{CommonProjectPath}/{symbolName}.cs",
                        $"namespace LgymApi.BackgroundWorker.Common; {declaration}")));

                Assert.That(
                    CollectViolations(compilation),
                    Has.Some.Matches<CommonSurfaceViolation>(candidate =>
                        candidate.Message.Contains($"Forbidden Common symbol '{symbolName}'", StringComparison.Ordinal)));
            }
        });
    }

    [Test]
    public void Allowed_Interface_Changed_To_Class_Fixture_Is_Rejected()
    {
        var compilation = CreateFixtureCompilation(CreateAllowedFixtureSources(
            "Jobs/IEmailJob.cs",
            "public sealed class IEmailJob {}"));

        Assert.That(
            CollectViolations(compilation),
            Has.Some.Matches<CommonSurfaceViolation>(candidate =>
                candidate.RelativePath == $"{CommonProjectPath}/Jobs/IEmailJob.cs"
                && candidate.Message.Contains("expected type kind 'Interface' but was 'Class'", StringComparison.Ordinal)));
    }

    [Test]
    public void Allowed_Public_Interface_Changed_To_Internal_Fixture_Is_Rejected()
    {
        var compilation = CreateFixtureCompilation(CreateAllowedFixtureSources(
            "Notifications/IEmailSender.cs",
            "internal interface IEmailSender {}"));

        Assert.That(
            CollectViolations(compilation),
            Has.Some.Matches<CommonSurfaceViolation>(candidate =>
                candidate.RelativePath == $"{CommonProjectPath}/Notifications/IEmailSender.cs"
                && candidate.Message.Contains("expected accessibility 'Public' but was 'Internal'", StringComparison.Ordinal)));
    }

    [Test]
    public void Allowed_Static_Policy_Changed_To_Instance_Class_Fixture_Is_Rejected()
    {
        var compilation = CreateFixtureCompilation(CreateAllowedFixtureSources(
            "IdempotencyKeyPolicy.cs",
            "public sealed class IdempotencyKeyPolicy {}"));

        Assert.That(
            CollectViolations(compilation),
            Has.Some.Matches<CommonSurfaceViolation>(candidate =>
                candidate.RelativePath == $"{CommonProjectPath}/IdempotencyKeyPolicy.cs"
                && candidate.Message.Contains("expected staticness 'True' but was 'False'", StringComparison.Ordinal)));
    }

    [Test]
    public void Namespace_Level_Delegate_Drift_Fixture_Is_Rejected()
    {
        var compilation = CreateFixtureCompilation(CreateAllowedFixtureSources(
            "Notifications/IEmailJobHandler.cs",
            "public delegate void IEmailJobHandler();"));

        Assert.That(
            CollectViolations(compilation),
            Has.Some.Matches<CommonSurfaceViolation>(candidate =>
                candidate.RelativePath == $"{CommonProjectPath}/Notifications/IEmailJobHandler.cs"
                && candidate.Message.Contains("Unsupported Common delegate 'LgymApi.BackgroundWorker.Common.Notifications.IEmailJobHandler'", StringComparison.Ordinal)));
    }

    private static IReadOnlyList<CommonSurfaceViolation> CollectViolations(CSharpCompilation compilation)
    {
        var expectedEntriesByPath = AllowedSurface
            .GroupBy(entry => $"{CommonProjectPath}/{entry.RelativePath}", StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(entry => entry.MetadataName, StringComparer.Ordinal),
            StringComparer.Ordinal);
        var observedTypesByPath = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var violations = new List<CommonSurfaceViolation>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var relativePath = Normalize(tree.FilePath);
            if (!relativePath.StartsWith($"{CommonProjectPath}/", StringComparison.Ordinal))
            {
                continue;
            }

            expectedEntriesByPath.TryGetValue(relativePath, out var expectedEntries);
            if (expectedEntries == null)
            {
                violations.Add(new CommonSurfaceViolation(relativePath, "Unexpected Common source file."));
            }

            if (TryGetForbiddenFamily(relativePath, out var family))
            {
                violations.Add(new CommonSurfaceViolation(relativePath, $"Forbidden Common path family '{family}'."));
            }

            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var observedTypes = new HashSet<string>(StringComparer.Ordinal);
            observedTypesByPath[relativePath] = observedTypes;

            foreach (var declaration in GetTypeDeclarations(tree.GetRoot()))
            {
                if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
                {
                    continue;
                }

                var metadataName = GetMetadataName(symbol);
                observedTypes.Add(metadataName);

                if (ForbiddenRootSymbols.Contains(symbol.Name))
                {
                    violations.Add(new CommonSurfaceViolation(relativePath, $"Forbidden Common symbol '{symbol.Name}'."));
                }

                if (declaration is DelegateDeclarationSyntax)
                {
                    violations.Add(new CommonSurfaceViolation(relativePath, $"Unsupported Common delegate '{metadataName}'."));
                }

                if (expectedEntries == null)
                {
                    violations.Add(new CommonSurfaceViolation(
                        relativePath,
                        $"Unexpected Common symbol '{metadataName}' in an unrecognized Common source file."));
                    continue;
                }

                if (!expectedEntries.TryGetValue(metadataName, out var expectedEntry))
                {
                    violations.Add(new CommonSurfaceViolation(
                        relativePath,
                        $"Unexpected Common symbol '{metadataName}'."));
                    continue;
                }

                AddShapeViolations(relativePath, symbol, expectedEntry, violations);
            }
        }

        foreach (var entry in expectedEntriesByPath)
        {
            if (!observedTypesByPath.TryGetValue(entry.Key, out var observedTypes))
            {
                violations.Add(new CommonSurfaceViolation(entry.Key, "Missing required Common source file."));
                continue;
            }

            foreach (var expectedEntry in entry.Value.Values)
            {
                if (!observedTypes.Contains(expectedEntry.MetadataName))
                {
                    violations.Add(new CommonSurfaceViolation(entry.Key, $"Missing required Common symbol '{expectedEntry.MetadataName}'."));
                }
            }
        }

        return violations
            .OrderBy(violation => violation.RelativePath, StringComparer.Ordinal)
            .ThenBy(violation => violation.Message, StringComparer.Ordinal)
            .ToList();
    }

    private static CSharpCompilation CreateFixtureCompilation(IEnumerable<(string Path, string Source)> sources)
    {
        var syntaxTrees = sources
            .Select(source => CSharpSyntaxTree.ParseText(source.Source, path: source.Path))
            .ToList();
        var compilation = CSharpCompilation.Create(
            "BackgroundWorkerCommonSurfaceFixture",
            syntaxTrees,
            ArchitectureTestHelpers.ResolveMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.That(
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "The Common surface fixture must compile before its semantic surface is evaluated.");

        return compilation;
    }

    private static IEnumerable<(string Path, string Source)> CreateAllowedFixtureSources(
        string? replacementRelativePath = null,
        string? replacementDeclaration = null)
    {
        return AllowedSurface
            .GroupBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .Select(group =>
            {
                var namespaceName = group.First().MetadataName[..group.First().MetadataName.LastIndexOf('.')];
                var declarations = group.Select(entry => string.Equals(entry.RelativePath, replacementRelativePath, StringComparison.Ordinal)
                    ? replacementDeclaration!
                    : entry.Declaration);
                return ($"{CommonProjectPath}/{group.Key}", $"namespace {namespaceName}; {string.Join(Environment.NewLine, declarations)}");
            });
    }

    private static IEnumerable<MemberDeclarationSyntax> GetTypeDeclarations(SyntaxNode root)
    {
        return root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(declaration => declaration is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);
    }

    private static void AddShapeViolations(
        string relativePath,
        INamedTypeSymbol symbol,
        CommonSurfaceEntry expected,
        List<CommonSurfaceViolation> violations)
    {
        var metadataName = GetMetadataName(symbol);
        if (symbol.TypeKind != expected.TypeKind)
        {
            violations.Add(new CommonSurfaceViolation(
                relativePath,
                $"Common symbol '{metadataName}' expected type kind '{expected.TypeKind}' but was '{symbol.TypeKind}'."));
        }

        if (symbol.DeclaredAccessibility != expected.DeclaredAccessibility)
        {
            violations.Add(new CommonSurfaceViolation(
                relativePath,
                $"Common symbol '{metadataName}' expected accessibility '{expected.DeclaredAccessibility}' but was '{symbol.DeclaredAccessibility}'."));
        }

        if (symbol.IsStatic != expected.IsStatic)
        {
            violations.Add(new CommonSurfaceViolation(
                relativePath,
                $"Common symbol '{metadataName}' expected staticness '{expected.IsStatic}' but was '{symbol.IsStatic}'."));
        }
    }

    private static bool TryGetForbiddenFamily(string relativePath, out string family)
    {
        var projectRelativePath = relativePath[$"{CommonProjectPath}/".Length..];
        foreach (var candidate in new[] { "Commands", "Serialization", "Push" })
        {
            if (projectRelativePath.StartsWith($"{candidate}/", StringComparison.Ordinal))
            {
                family = candidate;
                return true;
            }
        }

        family = string.Empty;
        return false;
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

    private static string Normalize(string path) => path.Replace('\\', '/');

    private sealed record CommonSurfaceEntry(
        string RelativePath,
        string MetadataName,
        TypeKind TypeKind,
        Accessibility DeclaredAccessibility,
        bool IsStatic,
        string Declaration);

    private sealed record CommonSurfaceViolation(string RelativePath, string Message)
    {
        public override string ToString() => $"{RelativePath}: {Message}";
    }
}
