using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class NotificationsPublicContractBoundaryGuardTests
{
    private const string ContractsPath = "LgymApi.Notifications/Contracts/";
    private const string EventInputPath = "LgymApi.Notifications/Models/EnqueueNotificationEventInput.cs";
    private const string EventBridgePath = "LgymApi.Notifications/INotificationEventBridge.cs";
    private const string AppErrorPath = "LgymApi.Platform/BuildingBlocks/Errors/AppError.cs";

    private static readonly HashSet<string> NotificationsPersistedEntityNames = PersistedEntityOwnershipCatalog.Entries
        .Where(entry => entry.Owner == PersistedEntityOwnershipCatalog.NotificationsModuleName)
        .Select(entry => entry.EntityType.FullName!)
        .ToHashSet(StringComparer.Ordinal);

    private static readonly object[] RejectedFixtureCases =
    {
        CreateRejectedCase(
            "HangfirePayload.cs",
            "public sealed record HangfirePayload(Hangfire.IBackgroundJobClient Client);",
            "Hangfire runtime",
            "Hangfire.IBackgroundJobClient"),
        CreateRejectedCase(
            "FcmDeliveryRequest.cs",
            "public sealed record FcmDeliveryRequest(string EventId);",
            "FCM provider",
            "FcmDeliveryRequest"),
        CreateRejectedCase(
            "FirebasePayload.cs",
            "public sealed record FirebasePayload(FirebaseAdmin.Messaging.Message Message);",
            "Firebase provider",
            "FirebaseAdmin.Messaging.Message"),
        CreateRejectedCase(
            "InfrastructurePayload.cs",
            "public sealed record InfrastructurePayload(LgymApi.Infrastructure.Notifications.ProviderEnvelope Envelope);",
            "Infrastructure implementation",
            "LgymApi.Infrastructure.Notifications.ProviderEnvelope"),
        CreateRejectedCase(
            "WorkerPayload.cs",
            "public sealed record WorkerPayload(LgymApi.BackgroundWorker.Runtime.JobEnvelope Envelope);",
            "BackgroundWorker runtime",
            "LgymApi.BackgroundWorker.Runtime.JobEnvelope"),
        CreateRejectedCase(
            "CommonPayload.cs",
            "public sealed record CommonPayload(LgymApi.BackgroundWorker.Common.Jobs.IPushNotificationJob Job);",
            "BackgroundWorker.Common runtime",
            "LgymApi.BackgroundWorker.Common.Jobs.IPushNotificationJob"),
        CreateRejectedCase(
            "EfPayload.cs",
            "public sealed record EfPayload(Microsoft.EntityFrameworkCore.DbContext Context);",
            "EF Core persistence",
            "Microsoft.EntityFrameworkCore.DbContext"),
        CreateRejectedCase(
            "RepositoryPayload.cs",
            "public sealed record RepositoryPayload(LgymApi.Application.Repositories.IPushNotificationMessageRepository Repository);",
            "notification repository",
            "LgymApi.Application.Repositories.IPushNotificationMessageRepository"),
        CreateRejectedCase(
            "PersistedEntityPayload.cs",
            "public sealed record PersistedEntityPayload(LgymApi.Domain.Entities.PushInstallation Installation);",
            "Notifications persisted entity",
            "LgymApi.Domain.Entities.PushInstallation"),
        CreateRejectedCase(
            "PersistedEntityProviderSender.cs",
            "public interface IPersistedEntityProviderSender { void Send(LgymApi.Domain.Entities.PushInstallation installation); }",
            "Notifications persisted entity",
            "LgymApi.Domain.Entities.PushInstallation"),
        CreateRejectedCase(
            "ProviderMemberNamePayload.cs",
            "public sealed record ProviderMemberNamePayload(string FcmToken);",
            "FCM provider",
            "FcmToken"),
        CreateRejectedCase(
            "TransitiveProviderTokenSender.cs",
            "public interface ITransitiveProviderTokenSender { void Send(LgymApi.Application.Notifications.Models.PushDeliveryTarget target); }",
            "raw token",
            "DeviceToken")
    };

    [Test]
    public void Application_Notifications_Public_Contracts_Should_Not_Leak_Provider_Runtime_Or_Persistence_Details()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var violations = CollectViolations(LoadRepositorySurface(repoRoot), requireSuccessfulCompilation: true);

        Assert.That(
            violations,
            Is.Empty,
            "Application-facing Notifications contracts must remain provider-neutral and persistence-free payloads." +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [TestCaseSource(nameof(RejectedFixtureCases))]
    public void Public_Contract_Fixture_With_Implementation_Leak_Is_Rejected(
        string fileName,
        string declaration,
        string expectedCategory,
        string expectedLeak)
    {
        var path = $"{ContractsPath}{fileName}";
        var violations = CollectViolations(
            CreateFixtureSources(path, declaration),
            requireSuccessfulCompilation: true);

        Assert.That(
            violations,
            Has.Some.Matches<NotificationsContractViolation>(violation =>
                violation.Path == path
                && violation.Category == expectedCategory
                && violation.LeakedDependency.Contains(expectedLeak, StringComparison.Ordinal)));
        Assert.That(
            violations.Select(violation => violation.ToString()),
            Has.Some.Contains($"path:{path}"));
    }

    [Test]
    public void Provider_Neutral_Push_Contracts_And_Typed_Ids_Are_Allowed()
    {
        var sources = new[]
        {
            new SourceFixture(
                $"{ContractsPath}Push/PushEventPayload.cs",
                """
                using LgymApi.Domain.Entities;
                using LgymApi.Domain.ValueObjects;

                namespace LgymApi.Application.Notifications.Contracts.Push;

                public sealed record PushEventPayload(
                    int SchemaVersion,
                    string Type,
                    string EventId,
                    string? EntityId,
                    Id<InAppNotification>? InAppNotificationId,
                    string? Deeplink);
                """),
            new SourceFixture(
                $"{ContractsPath}Push/IPushBackgroundScheduler.cs",
                """
                using LgymApi.Domain.Entities;
                using LgymApi.Domain.ValueObjects;

                namespace LgymApi.Application.Notifications.Contracts.Push;

                public interface IPushBackgroundScheduler
                {
                    string? Enqueue(Id<PushNotificationMessage> notificationId);
                }
                """),
            new SourceFixture(
                $"{ContractsPath}Push/IPushProviderSender.cs",
                """
                using LgymApi.Domain.Entities;
                using LgymApi.Domain.ValueObjects;

                namespace LgymApi.Application.Notifications.Contracts.Push;

                public interface IPushProviderSender
                {
                    Task<PushSendAttemptResult> SendAsync(
                        Id<PushInstallation> installationId,
                        PushEventPayload payload,
                        CancellationToken cancellationToken = default);
                }

                public sealed record PushSendAttemptResult(
                    PushSendOutcome Outcome,
                    string ProviderStatus,
                    string? ProviderMessageId,
                    string? ProviderErrorCode,
                    string? ProviderResponseSummary);

                public enum PushSendOutcome
                {
                    Sent = 0,
                    TransientFailure = 1,
                    InvalidToken = 2,
                    PermanentFailure = 3,
                    Skipped = 4
                }
                """),
            new SourceFixture(
                EventInputPath,
                """
                using LgymApi.Domain.Entities;
                using LgymApi.Domain.ValueObjects;

                namespace LgymApi.Application.Notifications.Models;

                public sealed record EnqueueNotificationEventInput(
                    Id<User> UserId,
                    string? EntityKey,
                    Id<InAppNotification>? InAppNotificationId);
                """)
        };

        Assert.That(
            CollectViolations(sources.Prepend(CreateImplicitUsings()), requireSuccessfulCompilation: true),
            Is.Empty,
            "Provider-neutral Push names, PushEventPayload.EntityId, and typed entity IDs are allowed contract shapes.");
    }

    private static object[] CreateRejectedCase(
        string fileName,
        string declaration,
        string expectedCategory,
        string expectedLeak)
    {
        return [fileName, declaration, expectedCategory, expectedLeak];
    }

    private static IReadOnlyList<SourceFixture> LoadRepositorySurface(string repoRoot)
    {
        var contractsDirectory = Path.Combine(
            repoRoot,
            ContractsPath.Replace('/', Path.DirectorySeparatorChar));
        var paths = Directory
            .EnumerateFiles(contractsDirectory, "*.cs", SearchOption.AllDirectories)
            .Append(Path.Combine(repoRoot, EventInputPath.Replace('/', Path.DirectorySeparatorChar)))
            .Append(Path.Combine(repoRoot, EventBridgePath.Replace('/', Path.DirectorySeparatorChar)))
            .Append(Path.Combine(repoRoot, AppErrorPath.Replace('/', Path.DirectorySeparatorChar)));

        return paths
            .Select(path => new SourceFixture(
                ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, path)),
                File.ReadAllText(path)))
            .Prepend(CreateImplicitUsings())
            .ToList();
    }

    private static IEnumerable<SourceFixture> CreateFixtureSources(string path, string declaration)
    {
        yield return CreateImplicitUsings();
        yield return new SourceFixture(
            "Fixture/ImplementationTypes.cs",
            """
            namespace Hangfire { public interface IBackgroundJobClient {} }
            namespace FirebaseAdmin.Messaging { public sealed class Message {} }
            namespace LgymApi.Infrastructure.Notifications { public sealed class ProviderEnvelope {} }
            namespace LgymApi.BackgroundWorker.Runtime { public sealed class JobEnvelope {} }
            namespace LgymApi.BackgroundWorker.Common.Jobs { public interface IPushNotificationJob {} }
            namespace Microsoft.EntityFrameworkCore { public class DbContext {} }
            namespace LgymApi.Application.Repositories { public interface IPushNotificationMessageRepository {} }
            namespace LgymApi.Application.Notifications.Models { public sealed record PushDeliveryTarget(string DeviceToken); }
            """);
        yield return new SourceFixture(
            path,
            $$"""
            namespace LgymApi.Application.Notifications.Contracts.Fixtures;

            {{declaration}}
            """);
    }

    private static SourceFixture CreateImplicitUsings()
    {
        return new SourceFixture(
            "Fixture/ImplicitUsings.g.cs",
            """
            global using System;
            global using System.Collections.Generic;
            global using System.Linq;
            global using System.Threading;
            global using System.Threading.Tasks;
            """);
    }

    private static IReadOnlyList<NotificationsContractViolation> CollectViolations(
        IEnumerable<SourceFixture> sourceFiles,
        bool requireSuccessfulCompilation)
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

        Assert.That(syntaxErrors, Is.Empty, "Notifications public contract sources must be valid C# before analysis.");

        var compilation = ArchitectureTestHelpers.CreateCompilation(trees);
        if (requireSuccessfulCompilation)
        {
            Assert.That(
                compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                Is.Empty,
                "Notifications public contract fixtures must compile before semantic analysis.");
        }

        var violations = new Dictionary<string, NotificationsContractViolation>(StringComparer.Ordinal);
        foreach (var tree in trees.Where(tree => IsContractSurfacePath(tree.FilePath)))
        {
            CollectTreeViolations(compilation, tree, violations);
        }

        return violations.Values
            .OrderBy(violation => violation.IdentityKey, StringComparer.Ordinal)
            .ToList();
    }

    private static void CollectTreeViolations(
        CSharpCompilation compilation,
        SyntaxTree tree,
        IDictionary<string, NotificationsContractViolation> violations)
    {
        var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
        var root = tree.GetCompilationUnitRoot();
        var path = ArchitectureTestHelpers.NormalizePath(tree.FilePath);

        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            if (usingDirective.Name == null)
            {
                continue;
            }

            var symbol = semanticModel.GetSymbolInfo(usingDirective.Name).Symbol;
            var dependency = symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                ?? usingDirective.Name.ToString();
            AddTextViolation(violations, path, "using directive", dependency);
        }

        foreach (var declaration in GetTypeDeclarations(root))
        {
            if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type || !IsPubliclyVisible(type))
            {
                continue;
            }

            var typeDisplayName = GetDisplayName(type);
            AddTextViolation(violations, path, typeDisplayName, typeDisplayName);

            foreach (var attribute in type.GetAttributes())
            {
                AddTypeViolation(violations, path, typeDisplayName, attribute.AttributeClass, rejectPersistedEntity: false);
            }

            foreach (var exposedType in GetDeclaredTypeDependencies(type))
            {
                AddTypeViolation(violations, path, typeDisplayName, exposedType, rejectPersistedEntity: false);
            }

            foreach (var member in type.GetMembers().Where(IsPublicSurfaceMember))
            {
                var memberDisplayName = GetDisplayName(member);
                AddTextViolation(violations, path, memberDisplayName, member.Name);

                foreach (var attribute in member.GetAttributes())
                {
                    AddTypeViolation(violations, path, memberDisplayName, attribute.AttributeClass, rejectPersistedEntity: false);
                }

                foreach (var exposedType in GetMemberTypes(member))
                {
                    AddTypeViolation(violations, path, memberDisplayName, exposedType, rejectPersistedEntity: true);
                }
            }
        }
    }

    private static IEnumerable<MemberDeclarationSyntax> GetTypeDeclarations(SyntaxNode root)
    {
        return root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(declaration => declaration is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);
    }

    private static IEnumerable<ITypeSymbol> GetDeclaredTypeDependencies(INamedTypeSymbol type)
    {
        if (type.BaseType != null)
        {
            yield return type.BaseType;
        }

        foreach (var @interface in type.Interfaces)
        {
            yield return @interface;
        }

        foreach (var typeParameter in type.TypeParameters)
        {
            foreach (var constraintType in typeParameter.ConstraintTypes)
            {
                yield return constraintType;
            }
        }
    }

    private static IEnumerable<ITypeSymbol> GetMemberTypes(ISymbol member)
    {
        switch (member)
        {
            case IFieldSymbol field:
                yield return field.Type;
                break;
            case IPropertySymbol property:
                yield return property.Type;
                break;
            case IEventSymbol @event:
                yield return @event.Type;
                break;
            case IMethodSymbol method:
                yield return method.ReturnType;
                foreach (var parameter in method.Parameters)
                {
                    yield return parameter.Type;
                }

                foreach (var typeParameter in method.TypeParameters)
                {
                    foreach (var constraintType in typeParameter.ConstraintTypes)
                    {
                        yield return constraintType;
                    }
                }

                break;
        }
    }

    private static void AddTypeViolation(
        IDictionary<string, NotificationsContractViolation> violations,
        string path,
        string sourceSymbol,
        ITypeSymbol? exposedType,
        bool rejectPersistedEntity)
    {
        if (exposedType == null)
        {
            return;
        }

        foreach (var namedType in EnumerateNamedTypes(exposedType))
        {
            AddNamedTypeViolation(violations, path, sourceSymbol, namedType, rejectPersistedEntity);
            AddTransitiveNotificationMemberViolations(
                violations,
                path,
                sourceSymbol,
                namedType,
                rejectPersistedEntity,
                new HashSet<string>(StringComparer.Ordinal));
        }
    }

    private static void AddNamedTypeViolation(
        IDictionary<string, NotificationsContractViolation> violations,
        string path,
        string sourceSymbol,
        INamedTypeSymbol namedType,
        bool rejectPersistedEntity)
    {
        var dependency = GetDisplayName(namedType);
        if (TryClassifyText(dependency, out var category)
            || TryClassifyNotificationRepository(namedType, out category)
            || (rejectPersistedEntity && TryClassifyPersistedEntity(namedType, out category)))
        {
            AddViolation(violations, new NotificationsContractViolation(path, sourceSymbol, category, dependency));
        }
    }

    private static void AddTransitiveNotificationMemberViolations(
        IDictionary<string, NotificationsContractViolation> violations,
        string path,
        string sourceSymbol,
        INamedTypeSymbol namedType,
        bool rejectPersistedEntity,
        ISet<string> visitedTypes)
    {
        if (!IsNotificationsType(namedType) || !visitedTypes.Add(GetMetadataName(namedType.OriginalDefinition)))
        {
            return;
        }

        foreach (var member in namedType.GetMembers().Where(IsPublicSurfaceMember))
        {
            AddTextViolation(violations, path, sourceSymbol, member.Name);

            foreach (var memberType in GetMemberTypes(member))
            {
                foreach (var nestedType in EnumerateNamedTypes(memberType))
                {
                    AddNamedTypeViolation(violations, path, sourceSymbol, nestedType, rejectPersistedEntity);
                    AddTransitiveNotificationMemberViolations(
                        violations,
                        path,
                        sourceSymbol,
                        nestedType,
                        rejectPersistedEntity,
                        visitedTypes);
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol namedType)
        {
            yield return namedType;

            if (IsTypedEntityId(namedType))
            {
                yield break;
            }

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
            foreach (var nestedType in EnumerateNamedTypes(arrayType.ElementType))
            {
                yield return nestedType;
            }
        }
    }

    private static bool TryClassifyNotificationRepository(INamedTypeSymbol type, out string category)
    {
        var namespaceName = type.ContainingNamespace.ToDisplayString();
        if (type.Name.EndsWith("Repository", StringComparison.Ordinal)
            && (type.Name.Contains("Notification", StringComparison.OrdinalIgnoreCase)
                || type.Name.Contains("PushInstallation", StringComparison.OrdinalIgnoreCase)
                || namespaceName.Contains(".Notifications", StringComparison.Ordinal)))
        {
            category = "notification repository";
            return true;
        }

        category = string.Empty;
        return false;
    }

    private static bool TryClassifyPersistedEntity(INamedTypeSymbol type, out string category)
    {
        if (NotificationsPersistedEntityNames.Contains(GetMetadataName(type.OriginalDefinition)))
        {
            category = "Notifications persisted entity";
            return true;
        }

        category = string.Empty;
        return false;
    }

    private static void AddTextViolation(
        IDictionary<string, NotificationsContractViolation> violations,
        string path,
        string sourceSymbol,
        string dependency)
    {
        if (TryClassifyText(dependency, out var category))
        {
            AddViolation(violations, new NotificationsContractViolation(path, sourceSymbol, category, dependency));
        }
    }

    private static bool TryClassifyText(string value, out string category)
    {
        if (value.Contains("LgymApi.BackgroundWorker.Common", StringComparison.Ordinal))
        {
            category = "BackgroundWorker.Common runtime";
            return true;
        }

        if (value.Contains("LgymApi.BackgroundWorker", StringComparison.Ordinal))
        {
            category = "BackgroundWorker runtime";
            return true;
        }

        if (value.Contains("LgymApi.Infrastructure", StringComparison.Ordinal))
        {
            category = "Infrastructure implementation";
            return true;
        }

        if (value.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
        {
            category = "EF Core persistence";
            return true;
        }

        if (value.Contains("Hangfire", StringComparison.OrdinalIgnoreCase))
        {
            category = "Hangfire runtime";
            return true;
        }

        if (value.Contains("Firebase", StringComparison.OrdinalIgnoreCase))
        {
            category = "Firebase provider";
            return true;
        }

        if (value.Contains("Fcm", StringComparison.OrdinalIgnoreCase))
        {
            category = "FCM provider";
            return true;
        }

        if (value.Contains("DeviceToken", StringComparison.OrdinalIgnoreCase)
            || value.Contains("RegistrationToken", StringComparison.OrdinalIgnoreCase))
        {
            category = "raw token";
            return true;
        }

        category = string.Empty;
        return false;
    }

    private static bool IsNotificationsType(INamedTypeSymbol type)
    {
        return type.ContainingNamespace.ToDisplayString()
            .StartsWith("LgymApi.Application.Notifications", StringComparison.Ordinal);
    }

    private static bool IsContractSurfacePath(string path)
    {
        var normalized = ArchitectureTestHelpers.NormalizePath(path);
        return normalized.StartsWith(ContractsPath, StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith($"/{EventInputPath}", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(EventInputPath, StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith($"/{EventBridgePath}", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(EventBridgePath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPubliclyVisible(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPublicSurfaceMember(ISymbol member)
    {
        if (member is INamedTypeSymbol || member.IsImplicitlyDeclared && member is not IMethodSymbol)
        {
            return false;
        }

        if (member is IMethodSymbol { AssociatedSymbol: not null })
        {
            return false;
        }

        return member.DeclaredAccessibility is Accessibility.Public
            or Accessibility.Protected
            or Accessibility.ProtectedOrInternal;
    }

    private static bool IsTypedEntityId(INamedTypeSymbol type)
    {
        return GetMetadataName(type.OriginalDefinition) == "LgymApi.Domain.ValueObjects.Id`1";
    }

    private static string GetMetadataName(INamedTypeSymbol type)
    {
        var typeNames = new Stack<string>();
        for (var current = type; current != null; current = current.ContainingType)
        {
            typeNames.Push(current.MetadataName);
        }

        var namespaceName = type.ContainingNamespace.ToDisplayString();
        return string.IsNullOrEmpty(namespaceName)
            ? string.Join(".", typeNames)
            : $"{namespaceName}.{string.Join(".", typeNames)}";
    }

    private static string GetDisplayName(ISymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    private static void AddViolation(
        IDictionary<string, NotificationsContractViolation> violations,
        NotificationsContractViolation violation)
    {
        violations.TryAdd(violation.IdentityKey, violation);
    }

    private sealed record SourceFixture(string Path, string Content);

    private sealed record NotificationsContractViolation(
        string Path,
        string SourceSymbol,
        string Category,
        string LeakedDependency)
    {
        public string IdentityKey => $"path:{Path}|symbol:{SourceSymbol}|category:{Category}|leak:{LeakedDependency}";

        public override string ToString()
        {
            return $"path:{Path} symbol:{SourceSymbol} category:{Category} leak:{LeakedDependency}";
        }
    }
}
