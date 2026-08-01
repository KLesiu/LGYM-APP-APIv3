using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class TypedEntityIdBoundaryGuardTests
{
    private static readonly IReadOnlySet<string> FutureExactPolymorphicEntityIdExceptions = new HashSet<string>(StringComparer.Ordinal)
    {
        "LgymApi.Domain.Entities.PushNotificationMessage.EntityId",
        "LgymApi.Application.Notifications.Contracts.Push.PushEventPayload.EntityId"
    };

    [Test]
    public void Raw_Known_Entity_Id_Fixtures_Are_Rejected_When_Types_Are_String_Guid_Or_Nullable_Guid()
    {
        var compilation = CreateFixtureCompilation(("LgymApi.Application/Fixtures/RawEntityIds.cs", """
            namespace LgymApi.Application.Fixtures;

            public sealed class RawEntityIds
            {
                public string PhotoId { get; init; } = string.Empty;
                public System.Guid ExerciseId { get; init; }
                public System.Guid? UserId { get; init; }
            }
            """));

        var violations = Collect(compilation);

        AssertViolation(compilation, violations, "RawEntityIds", "PhotoId", "Id<Photo>");
        AssertViolation(compilation, violations, "RawEntityIds", "ExerciseId", "Id<Exercise>");
        AssertViolation(compilation, violations, "RawEntityIds", "UserId", "Id<User>");

        foreach (var violation in violations)
        {
            TestContext.Progress.WriteLine(violation);
        }
    }

    [Test]
    public void Typed_And_Nullable_Typed_Entity_Ids_Are_Allowed()
    {
        var compilation = CreateFixtureCompilation(("LgymApi.Application/Fixtures/TypedEntityIds.cs", """
            using LgymApi.Domain.Entities;
            using LgymApi.Domain.ValueObjects;

            namespace LgymApi.Application.Fixtures;

            public sealed class TypedEntityIds
            {
                public Id<Photo> PhotoId { get; init; }
                public Id<Exercise>? ExerciseId { get; init; }

                public void SetUser(Id<User>? userId)
                {
                }
            }
            """));

        Assert.That(Collect(compilation), Is.Empty);
    }

    [Test]
    public void Only_The_Two_Exact_Polymorphic_EntityId_Symbols_Are_Allowed()
    {
        var compilation = CreateFixtureCompilation(
            ("LgymApi.Domain/Entities/PushNotificationMessage.cs", """
                namespace LgymApi.Domain.Entities;

                public sealed class PushNotificationMessage : EntityBase<PushNotificationMessage>
                {
                    public string? EntityId { get; init; }
                }
                """),
            ("LgymApi.Application/Notifications/Contracts/Push/PushEventPayload.cs", """
                namespace LgymApi.Application.Notifications.Contracts.Push;

                public sealed record PushEventPayload(string? EntityId);
                """),
            ("LgymApi.BackgroundWorker.Common/Push/Models/OtherPayload.cs", """
                namespace LgymApi.BackgroundWorker.Common.Push.Models;

                public sealed class OtherPayload
                {
                    public string? EntityId { get; init; }
                }
                """));

        var violations = Collect(compilation);

        Assert.That(violations, Has.Count.EqualTo(1));
        AssertViolation(compilation, violations, "OtherPayload", "EntityId", "Id<TEntity>");
    }

    [Test]
    public void Future_Polymorphic_EntityId_Exception_Fixture_Rejects_Stale_Common_Metadata()
    {
        var compilation = CreateFixtureCompilation(
            ("LgymApi.Domain/Entities/PushNotificationMessage.cs", """
                namespace LgymApi.Domain.Entities;

                public sealed class PushNotificationMessage : EntityBase<PushNotificationMessage>
                {
                    public string? EntityId { get; init; }
                }
                """),
            ("LgymApi.Application/Notifications/Contracts/Push/PushEventPayload.cs", """
                namespace LgymApi.Application.Notifications.Contracts.Push;

                public sealed record PushEventPayload(string? EntityId);
                """),
            ("LgymApi.BackgroundWorker.Common/Push/Models/PushEventPayload.cs", """
                namespace LgymApi.BackgroundWorker.Common.Push.Models;

                public sealed record PushEventPayload(string? EntityId);
                """));

        var violations = TypedEntityIdBoundaryGuard.Collect(
            compilation,
            compilation.SyntaxTrees.ToList(),
            FutureExactPolymorphicEntityIdExceptions);
        var stalePushEntityId = GetCompiledPropertySymbol(
            compilation,
            "LgymApi.BackgroundWorker.Common.Push.Models.PushEventPayload",
            "EntityId");

        Assert.Multiple(() =>
        {
            Assert.That(violations, Has.Count.EqualTo(1));
            AssertViolation(violations, stalePushEntityId, "Id<TEntity>");
        });
    }

    [Test]
    public void Transport_Provider_And_External_Key_Fixtures_Are_Outside_The_Internal_Entity_Id_Rule()
    {
        var compilation = CreateFixtureCompilation(
            ("LgymApi.Api/Features/Photos/Contracts/PhotoDto.cs", """
                namespace LgymApi.Api.Features.Photos.Contracts;

                public sealed class PhotoDto
                {
                    public string PhotoId { get; init; } = string.Empty;
                }
                """),
            ("LgymApi.Infrastructure/Data/TypedIdValueConverter.cs", """
                namespace LgymApi.Infrastructure.Data;

                public sealed class TypedIdValueConverter
                {
                    public System.Guid PhotoId { get; init; }
                }
                """),
            ("LgymApi.Application/ExerciseScores/Models/ExerciseScoresChartData.cs", """
                namespace LgymApi.Application.ExerciseScores.Models;

                public sealed class ExerciseScoresChartData
                {
                    public string Id { get; init; } = string.Empty;
                    public string StorageKey { get; init; } = string.Empty;
                    public string ProviderMessageId { get; init; } = string.Empty;
                    public string Checksum { get; init; } = string.Empty;
                    public string ShareCode { get; init; } = string.Empty;
                    public string SchedulerJobId { get; init; } = string.Empty;
                }
                """));

        Assert.That(Collect(compilation), Is.Empty);
    }

    [Test]
    public void Current_Internal_Raw_Entity_Id_Seams_Are_Observed_For_Downstream_Remediation()
    {
        var (_, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation(
            "LgymApi.Domain",
            "LgymApi.Application",
            "LgymApi.Infrastructure",
            "LgymApi.BackgroundWorker",
            "LgymApi.BackgroundWorker.Common");
        var violations = TypedEntityIdBoundaryGuard.Collect(compilation, syntaxTrees);
        var reportingPhotoIdParameter = GetParameterSymbol(compilation, "IReportingService.cs", "GetSignedReadUrlAsync", "photoId");
        var pushNotificationId = GetCompiledPropertySymbol(
            compilation,
            "LgymApi.Application.Notifications.Contracts.Push.PushEventPayload",
            "InAppNotificationId");

        Assert.That(
            violations.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.Symbol, reportingPhotoIdParameter)),
            Is.False,
            "The reporting signed-read photo ID must stay typed beyond the API boundary.");

        Assert.That(
            violations.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.Symbol, pushNotificationId)),
            Is.False,
            "PushEventPayload.InAppNotificationId must use Id<InAppNotification> because only PushEventPayload.EntityId is polymorphic.");
    }

    [Test]
    public void Chart_Entity_Identifiers_Are_Typed()
    {
        var (_, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation(
            "LgymApi.Domain",
            "LgymApi.Application",
            "LgymApi.Infrastructure",
            "LgymApi.BackgroundWorker",
            "LgymApi.BackgroundWorker.Common");
        var violations = TypedEntityIdBoundaryGuard.Collect(compilation, syntaxTrees);

        Assert.That(
            violations.Any(violation => SymbolEqualityComparer.Default.Equals(
                violation.Symbol,
                GetPropertySymbol(compilation, "ExerciseScoresChartData", "ExerciseId"))),
            Is.False);
        Assert.That(
            violations.Any(violation => SymbolEqualityComparer.Default.Equals(
                violation.Symbol,
                GetPropertySymbol(compilation, "EloRegistryChartEntry", "Id"))),
            Is.False);
    }

    private static IReadOnlyList<TypedEntityIdViolation> Collect(CSharpCompilation compilation)
    {
        return TypedEntityIdBoundaryGuard.Collect(compilation, compilation.SyntaxTrees.ToList());
    }

    private static void AssertViolation(
        CSharpCompilation compilation,
        IReadOnlyList<TypedEntityIdViolation> violations,
        string typeName,
        string memberName,
        string expectedType)
    {
        var member = GetPropertySymbol(compilation, typeName, memberName);
        AssertViolation(violations, member, expectedType);
    }

    private static void AssertViolation(IReadOnlyList<TypedEntityIdViolation> violations, ISymbol member, string expectedType)
    {
        var violation = violations.SingleOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.Symbol, member));

        Assert.That(violation, Is.Not.Null, $"Expected a violation for source symbol '{member.ToDisplayString()}'.");
        Assert.That(violation!.ExpectedType, Is.EqualTo(expectedType));
    }

    private static IPropertySymbol GetPropertySymbol(CSharpCompilation compilation, string typeName, string memberName)
    {
        var declaration = compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>())
            .Single(property => property.Identifier.ValueText == memberName
                && property.Ancestors().OfType<TypeDeclarationSyntax>().First().Identifier.ValueText == typeName);
        var symbol = compilation.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration);

        Assert.That(symbol, Is.Not.Null, $"Source property '{typeName}.{memberName}' did not resolve to a symbol.");
        return symbol!;
    }

    private static IPropertySymbol GetCompiledPropertySymbol(CSharpCompilation compilation, string typeName, string memberName)
    {
        var type = compilation.GetTypeByMetadataName(typeName);
        Assert.That(type, Is.Not.Null, $"Source type '{typeName}' did not resolve to a symbol.");

        var property = type!.GetMembers(memberName).OfType<IPropertySymbol>().SingleOrDefault();
        Assert.That(property, Is.Not.Null, $"Source property '{typeName}.{memberName}' did not resolve to a symbol.");
        return property!;
    }

    private static IParameterSymbol GetParameterSymbol(CSharpCompilation compilation, string fileName, string methodName, string parameterName)
    {
        var parameter = compilation.SyntaxTrees
            .Where(tree => tree.FilePath.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<ParameterSyntax>())
            .Single(candidate => candidate.Identifier.ValueText == parameterName
                && candidate.Ancestors().OfType<MethodDeclarationSyntax>().First().Identifier.ValueText == methodName);
        var symbol = compilation.GetSemanticModel(parameter.SyntaxTree).GetDeclaredSymbol(parameter);

        Assert.That(symbol, Is.Not.Null, $"Source parameter '{methodName}.{parameterName}' did not resolve to a symbol.");
        return symbol!;
    }

    private static CSharpCompilation CreateFixtureCompilation(params (string Path, string Source)[] fixtureSources)
    {
        var domainSource = CSharpSyntaxTree.ParseText("""
            namespace LgymApi.Domain.Entities
            {
                public class EntityBase<TEntity>
                {
                }

                public sealed class Photo : EntityBase<Photo>
                {
                }

                public sealed class Exercise : EntityBase<Exercise>
                {
                }

                public sealed class User : EntityBase<User>
                {
                }
            }

            namespace LgymApi.Domain.ValueObjects
            {
                public readonly struct Id<TEntity>
                {
                }
            }
            """, path: "LgymApi.Domain/Entities/FixtureEntities.cs");
        var trees = fixtureSources
            .Select(fixture => CSharpSyntaxTree.ParseText(fixture.Source, path: fixture.Path))
            .Append(domainSource)
            .ToList();

        return CSharpCompilation.Create(
            "TypedEntityIdBoundaryFixture",
            trees,
            ArchitectureTestHelpers.ResolveMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
