using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class PlatformSubBoundaryDependencyGuardTests
{
    private static readonly IReadOnlySet<string> BuildingBlocksContractManifest = CreateManifest(
        "LgymApi.Application.BuildingBlocks.Results.Result`2",
        "LgymApi.Application.BuildingBlocks.Results.Result",
        "LgymApi.Application.BuildingBlocks.Results.Unit",
        "LgymApi.Application.BuildingBlocks.Errors.AppError",
        "LgymApi.Application.BuildingBlocks.Errors.NotFoundError",
        "LgymApi.Application.BuildingBlocks.Errors.BadRequestError",
        "LgymApi.Application.BuildingBlocks.Errors.UnauthorizedError",
        "LgymApi.Application.BuildingBlocks.Errors.ForbiddenError",
        "LgymApi.Application.BuildingBlocks.Errors.ConflictError",
        "LgymApi.Application.BuildingBlocks.Errors.UnprocessableEntityError",
        "LgymApi.Application.BuildingBlocks.Errors.InternalServerError");

    private static readonly IReadOnlySet<string> TechnicalContractManifest = CreateManifest(
        "LgymApi.Application.Platform.Contracts.BackgroundCommands.IActionCommand",
        "LgymApi.Application.Platform.Contracts.BackgroundCommands.ICommandDispatcher",
        "LgymApi.Application.Platform.Contracts.BackgroundCommands.ICommandOutboxWriter",
        "LgymApi.Application.Platform.Contracts.BackgroundCommands.CommandEnvelopeStageResult",
        "LgymApi.Application.Platform.Contracts.Serialization.SharedSerializationOptions",
        "LgymApi.Application.Platform.Contracts.Serialization.TypedIdJsonConverter`1",
        "LgymApi.Application.Platform.Contracts.Serialization.TypedIdJsonConverterFactory",
        "LgymApi.Application.Pagination.PaginationPolicy",
        "LgymApi.Application.Pagination.Pagination`1",
        "LgymApi.Application.Pagination.PaginatedRequest",
        "LgymApi.Application.Pagination.IWhitelistPolicy",
        "LgymApi.Application.Pagination.FieldMapping",
        "LgymApi.Application.Pagination.SortDescriptor",
        "LgymApi.Application.Pagination.FilterInput",
        "LgymApi.Application.Pagination.FilterGroup",
        "LgymApi.Application.Pagination.GroupOperator",
        "LgymApi.Application.Pagination.FilterCondition",
        "LgymApi.Application.Pagination.FilterOperator",
        "LgymApi.Application.Pagination.IQueryPaginationService",
        "LgymApi.Application.Pagination.IMapperRegistry",
        "LgymApi.Application.Mapping.MappingServiceCollectionExtensions",
        "LgymApi.Application.Mapping.Extensions.MappingExtensions",
        "LgymApi.Application.Mapping.Core.IMapper",
        "LgymApi.Application.Mapping.Core.IMappingContext",
        "LgymApi.Application.Mapping.Core.IMappingProfile",
        "LgymApi.Application.Mapping.Core.MappingContext",
        "LgymApi.Application.Mapping.Core.MappingConfiguration",
        "LgymApi.Application.Mapping.Core.ContextKey`1",
        "LgymApi.Application.Mapping.Core.Mapper",
        "LgymApi.Application.Repositories.IUnitOfWork",
        "LgymApi.Application.Repositories.IUnitOfWorkTransaction",
        "LgymApi.Application.Repositories.ICommandEnvelopeRepository",
        "LgymApi.Application.Repositories.IApiIdempotencyRecordRepository");

    private static readonly IReadOnlySet<string> ReferenceDataContractManifest = CreateManifest(
        "LgymApi.Application.Platform.ReferenceData.AppConfig.Contracts.IAppConfigAuthorizationPort",
        "LgymApi.Application.Platform.ReferenceData.AppConfig.IAppConfigService",
        "LgymApi.Application.Platform.ReferenceData.AppConfig.CreateAppVersionInput",
        "LgymApi.Application.Platform.ReferenceData.AppConfig.UpdateAppConfigInput",
        "LgymApi.Application.Platform.ReferenceData.Enums.IEnumService",
        "LgymApi.Application.Platform.ReferenceData.Enums.Models.EnumLookupEntry",
        "LgymApi.Application.Platform.ReferenceData.Enums.Models.EnumLookupResponse",
        "LgymApi.Application.Platform.ReferenceData.Units.IUnitConverter`1",
        "LgymApi.Application.Platform.ReferenceData.Units.ILinearUnitStrategy`1",
        "LgymApi.Application.Platform.ReferenceData.Units.UnitValueComparer");

    private static readonly IReadOnlySet<string> ReferenceDataCompositionTypeManifest = CreateManifest(
        "LgymApi.Application.Platform.ReferenceData.AppConfig.IAppConfigService",
        "LgymApi.Application.Platform.ReferenceData.AppConfig.AppConfigService",
        "LgymApi.Application.Platform.ReferenceData.Enums.IEnumService",
        "LgymApi.Application.Platform.ReferenceData.Enums.EnumService",
        "LgymApi.Application.Platform.ReferenceData.Units.IUnitConverter`1",
        "LgymApi.Application.Platform.ReferenceData.Units.ILinearUnitStrategy`1",
        "LgymApi.Application.Platform.ReferenceData.Units.LinearUnitConverter`1",
        "LgymApi.Application.Platform.ReferenceData.Units.WeightLinearUnitStrategy",
        "LgymApi.Application.Platform.ReferenceData.Units.HeightLinearUnitStrategy",
        "LgymApi.Domain.Enums.WeightUnits",
        "LgymApi.Domain.Enums.HeightUnits");

    private static readonly IReadOnlySet<string> DomainNeutralPrimitiveManifest = CreateManifest(
        "LgymApi.Domain.ValueObjects.Id`1",
        "LgymApi.Domain.ValueObjects.CorrelationScope",
        "LgymApi.Domain.Entities.CommandEnvelope",
        "LgymApi.Domain.Entities.ApiIdempotencyRecord",
        "LgymApi.Domain.Enums.ActionExecutionStatus");

    private static IEnumerable<TestCaseData> ForbiddenDependencyCases()
    {
        yield return ForbiddenCase(
            "Technical_Platform_To_AppConfig",
            new[]
            {
                Source(
                    "LgymApi.Application/Platform/ReferenceData/AppConfig/AppConfigService.cs",
                    "namespace Fixture.ReferenceData; public sealed class AppConfigService { }"),
                Source(
                    "LgymApi.Application/Platform/Contracts/Runtime/TechnicalService.cs",
                    "using Fixture.ReferenceData; namespace Fixture.Technical; public sealed class TechnicalService { private AppConfigService? _service; }")
            },
            "Technical Platform -> Reference Data");
        yield return ForbiddenCase(
            "BuildingBlocks_To_Feature_Type",
            new[]
            {
                Source(
                    "LgymApi.Application/Identity/Profile/UserProfile.cs",
                    "namespace Fixture.Identity; public sealed class UserProfile { }"),
                Source(
                    "LgymApi.Application/BuildingBlocks/Results/OperationResult.cs",
                    "using Fixture.Identity; namespace Fixture.BuildingBlocks; public sealed class OperationResult { private UserProfile? _profile; }")
            },
            "BuildingBlocks -> Identity & Accounts");
        yield return ForbiddenCase(
            "Technical_Platform_To_Reference_Data_Contract",
            new[]
            {
                Source(
                    "LgymApi.Application/Platform/ReferenceData/AppConfig/Contracts/IAppConfigReader.cs",
                    "namespace Fixture.ReferenceData; public interface IAppConfigReader { }"),
                Source(
                    "LgymApi.Application/Platform/Contracts/Runtime/TechnicalService.cs",
                    "using Fixture.ReferenceData; namespace Fixture.Technical; public sealed class TechnicalService { private IAppConfigReader? _reader; }")
            },
            "Technical Platform -> Reference Data");
        yield return ForbiddenCase(
            "Reference_Data_To_Private_Technical_Implementation",
            new[]
            {
                Source(
                    "LgymApi.Application/Platform/Contracts/Runtime/TechnicalRuntime.cs",
                    "namespace Fixture.Technical; internal sealed class TechnicalRuntime { }"),
                Source(
                    "LgymApi.Application/Platform/ReferenceData/AppConfig/AppConfigService.cs",
                    "using Fixture.Technical; namespace Fixture.ReferenceData; public sealed class AppConfigService { private TechnicalRuntime? _runtime; }")
            },
            "Reference Data -> Technical Platform");
        yield return ForbiddenCase(
            "Feature_To_Private_Reference_Data_Contract",
            new[]
            {
                Source(
                    "LgymApi.Application/Platform/ReferenceData/AppConfig/Contracts/IInternalAppConfigReader.cs",
                    "namespace Fixture.ReferenceData; internal interface IInternalAppConfigReader { }"),
                Source(
                    "LgymApi.Application/Identity/Profile/ProfileService.cs",
                    "using Fixture.ReferenceData; namespace Fixture.Identity; internal sealed class ProfileService { private IInternalAppConfigReader? _reader; }")
            },
            "Identity & Accounts -> Reference Data");
    }

    [Test]
    public void Exact_SubBoundary_Dag_Should_Allow_Only_Approved_Forward_Contract_Edges()
    {
        var sources = new[]
        {
            Source(
                "LgymApi.Application/Common/Results/Unit.cs",
                "namespace LgymApi.Application.BuildingBlocks.Results; public readonly struct Unit { }"),
            Source(
                "LgymApi.Domain/ValueObjects/Id.cs",
                "namespace LgymApi.Domain.ValueObjects; public readonly record struct Id<T>(System.Guid Value);"),
            Source(
                "LgymApi.Domain/Entities/AppConfig.cs",
                "namespace LgymApi.Domain.Entities; public sealed class AppConfig { }"),
            Source(
                "LgymApi.Domain/Entities/User.cs",
                "namespace LgymApi.Domain.Entities; public sealed class User { }"),
            Source(
                "LgymApi.Application/Platform/Contracts/BackgroundCommands/ICommandDispatcher.cs",
                "using LgymApi.Application.BuildingBlocks.Results; using LgymApi.Domain.ValueObjects; namespace LgymApi.Application.Platform.Contracts.BackgroundCommands; public interface ICommandDispatcher { Unit Read(Id<object> id); }"),
            Source(
                "LgymApi.Application/Platform/ReferenceData/AppConfig/IAppConfigService.cs",
                "using AppConfigEntity = LgymApi.Domain.Entities.AppConfig; using LgymApi.Application.BuildingBlocks.Results; using LgymApi.Application.Platform.Contracts.BackgroundCommands; namespace LgymApi.Application.Platform.ReferenceData.AppConfig; public interface IAppConfigService { Unit Read(ICommandDispatcher dispatcher, AppConfigEntity config); }"),
            Source(
                "LgymApi.Application/Platform/ReferenceData/AppConfig/Contracts/IAppConfigAuthorizationPort.cs",
                "using System.Threading; using System.Threading.Tasks; using LgymApi.Domain.Entities; using LgymApi.Domain.ValueObjects; namespace LgymApi.Application.Platform.ReferenceData.AppConfig.Contracts; public interface IAppConfigAuthorizationPort { Task<bool> CanManageAppConfigAsync(Id<User> userId, CancellationToken cancellationToken = default); }"),
            Source(
                "LgymApi.Application/Identity/Profile/ProfileService.cs",
                "using LgymApi.Application.Platform.ReferenceData.AppConfig; using LgymApi.Application.Platform.ReferenceData.AppConfig.Contracts; using LgymApi.Application.BuildingBlocks.Results; using LgymApi.Application.Platform.Contracts.BackgroundCommands; namespace Fixture.Identity; public sealed class ProfileService { public Unit Read(ICommandDispatcher dispatcher, IAppConfigService service, IAppConfigAuthorizationPort authorizationPort) => default; }")
        };

        Assert.That(Analyze(sources), Is.Empty);
    }

    [TestCaseSource(nameof(ForbiddenDependencyCases))]
    public void Exact_SubBoundary_Dag_Should_Reject_Reverse_Feature_And_Private_Edges(
        string fixtureName,
        IReadOnlyList<FixtureSource> sources,
        string expectedViolation)
    {
        var violations = Analyze(sources);

        Assert.That(
            violations,
            Has.Some.Contains(expectedViolation),
            $"Fixture '{fixtureName}' must report the forbidden edge '{expectedViolation}'.");
    }

    [Test]
    public void Only_AddPlatformModule_Should_Compose_The_Internal_Reference_Data_Helper()
    {
        var allowedSources = new[]
        {
            Source(
                "LgymApi.Application/Platform/ReferenceData/ServiceCollectionExtensions.cs",
                "namespace Fixture.ReferenceData; internal static class ReferenceDataRegistration { internal static void AddReferenceDataServices() { } }"),
            Source(
                "LgymApi.Application/Platform/ServiceCollectionExtensions.cs",
                "using Fixture.ReferenceData; namespace Fixture.Technical; public static class ServiceCollectionExtensions { public static void AddPlatformModule() => ReferenceDataRegistration.AddReferenceDataServices(); }")
        };
        var forbiddenSources = allowedSources
            .Append(Source(
                "LgymApi.Application/Identity/ServiceCollectionExtensions.cs",
                "using Fixture.ReferenceData; namespace Fixture.Identity; public static class ServiceCollectionExtensions { public static void AddIdentityModule() => ReferenceDataRegistration.AddReferenceDataServices(); public static void AddReferenceDataModule() { } }"))
            .ToArray();
        var allowedViolations = Analyze(allowedSources);
        var forbiddenViolations = Analyze(forbiddenSources);

        Assert.Multiple(() =>
        {
            Assert.That(allowedViolations, Is.Empty);
            Assert.That(
                forbiddenViolations,
                Has.Some.Contains("Only AddPlatformModule may compose AddReferenceDataServices"));
            Assert.That(
                forbiddenViolations,
                Has.Some.Contains("AddReferenceDataModule is not a canonical module facade"));
        });
    }

    [Test]
    public void BuildingBlocks_Should_Reject_NonBcl_External_Dependencies()
    {
        var sources = new[]
        {
            Source(
                "LgymApi.Application/BuildingBlocks/Results/OperationResult.cs",
                "using Microsoft.CodeAnalysis; namespace Fixture.BuildingBlocks; public sealed class OperationResult { private SyntaxTree? _syntaxTree; }")
        };

        Assert.That(
            Analyze(sources),
            Has.Some.Contains("non-BCL external dependency Microsoft.CodeAnalysis.SyntaxTree"));
    }

    [Test]
    public void Feature_Should_Reject_An_Unlisted_Public_Type_In_A_Contracts_Path()
    {
        var sources = new[]
        {
            Source(
                "LgymApi.Application/Platform/Contracts/Fake/IUnexpectedTechnicalContract.cs",
                "namespace Fixture.Technical; public interface IUnexpectedTechnicalContract { }"),
            Source(
                "LgymApi.Application/Identity/Profile/ProfileService.cs",
                "using Fixture.Technical; namespace Fixture.Identity; public sealed class ProfileService { private IUnexpectedTechnicalContract? _contract; }")
        };

        Assert.That(
            Analyze(sources),
            Has.Some.Contains("Fixture.Technical.IUnexpectedTechnicalContract"));
    }

    [Test]
    public void Feature_Should_Reject_An_Unlisted_Public_Reference_Data_Type_In_A_Contracts_Path()
    {
        var sources = new[]
        {
            Source(
                "LgymApi.Application/Platform/ReferenceData/Fake/Contracts/IUnexpectedReferenceDataContract.cs",
                "namespace Fixture.ReferenceData; public interface IUnexpectedReferenceDataContract { }"),
            Source(
                "LgymApi.Application/Identity/Profile/ProfileService.cs",
                "using Fixture.ReferenceData; namespace Fixture.Identity; public sealed class ProfileService { private IUnexpectedReferenceDataContract? _contract; }")
        };

        Assert.That(
            Analyze(sources),
            Has.Some.Contains("Fixture.ReferenceData.IUnexpectedReferenceDataContract"));
    }

    [Test]
    public void Production_Scan_Should_Analyze_Real_Application_And_Domain_Trees()
    {
        var analysis = AnalyzeProductionSources();

        Assert.Multiple(() =>
        {
            Assert.That(analysis.SourceTreeCount, Is.GreaterThan(0));
            Assert.That(analysis.AnalyzedSourceTreeCount, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Production_Dag_Should_Have_No_Forbidden_Edges()
    {
        Assert.That(
            AnalyzeProductionSources().Violations,
            Is.Empty,
            "Production Platform sub-boundary dependencies must use only approved contracts.");
    }

    [Test]
    public void Production_Composition_Should_Inspect_The_Canonical_Platform_Facade()
    {
        var analysis = AnalyzeProductionSources();

        Assert.Multiple(() =>
        {
            Assert.That(analysis.AddPlatformModuleDeclarationCount, Is.EqualTo(1));
            Assert.That(analysis.ReferenceDataHelperDeclarationCount, Is.EqualTo(1));
            Assert.That(analysis.AddReferenceDataModuleDeclarationCount, Is.Zero);
            Assert.That(
                analysis.Violations.Where(violation =>
                    violation.Contains("Only AddPlatformModule may compose AddReferenceDataServices", StringComparison.Ordinal)
                    || violation.Contains("AddReferenceDataModule is not a canonical module facade", StringComparison.Ordinal)
                    || violation.Contains("AddReferenceDataServices must remain internal", StringComparison.Ordinal)),
                Is.Empty);
        });
    }

    private static ProductionAnalysis AnalyzeProductionSources()
    {
        var (_, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation(
            "LgymApi.Application",
            "LgymApi.Domain");

        return AnalyzeCompilation(compilation, syntaxTrees);
    }

    private static IReadOnlyList<string> Analyze(IEnumerable<FixtureSource> sources)
    {
        var sourceList = sources.ToList();
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var syntaxTrees = sourceList
            .Select(source => CSharpSyntaxTree.ParseText(source.Content, parseOptions, source.Path))
            .ToList();
        var compilation = ArchitectureTestHelpers.CreateCompilation(syntaxTrees);
        Assert.That(
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "The Roslyn fixture must compile before the Platform sub-boundary DAG is evaluated.");

        return AnalyzeCompilation(compilation, syntaxTrees).Violations;
    }

    private static ProductionAnalysis AnalyzeCompilation(
        Compilation compilation,
        IReadOnlyList<SyntaxTree> syntaxTrees)
    {
        var treeOwners = syntaxTrees
            .Select(tree => new TreeOwner(tree, ClassifyOwner(tree.FilePath)))
            .ToList();
        var ownedTypes = CollectOwnedTypes(compilation, treeOwners);
        var analyzedTrees = treeOwners
            .Where(entry => entry.Owner is { } owner && IsAnalyzedSource(owner))
            .ToList();
        var violations = new HashSet<string>(StringComparer.Ordinal);
        var addPlatformModuleDeclarationCount = 0;
        var referenceDataHelperDeclarationCount = 0;
        var addReferenceDataModuleDeclarationCount = 0;

        foreach (var treeOwner in treeOwners.Where(entry => entry.Owner is { IsDomain: false }))
        {
            var sourceOwner = treeOwner.Owner!;
            var semanticModel = compilation.GetSemanticModel(treeOwner.Tree, ignoreAccessibility: true);
            var root = treeOwner.Tree.GetCompilationUnitRoot();

            foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                switch (declaration.Identifier.ValueText)
                {
                    case "AddPlatformModule":
                        addPlatformModuleDeclarationCount++;
                        break;
                    case "AddReferenceDataServices":
                        referenceDataHelperDeclarationCount++;
                        break;
                    case "AddReferenceDataModule":
                        addReferenceDataModuleDeclarationCount++;
                        break;
                }
            }

            CollectCompositionViolations(sourceOwner, semanticModel, root, ownedTypes, violations);
            CollectForbiddenFacadeViolations(sourceOwner, semanticModel, root, violations);
        }

        foreach (var treeOwner in analyzedTrees)
        {
            var semanticModel = compilation.GetSemanticModel(treeOwner.Tree, ignoreAccessibility: true);
            CollectTypeDependencyViolations(
                treeOwner.Owner!,
                semanticModel,
                treeOwner.Tree.GetCompilationUnitRoot(),
                ownedTypes,
                violations);
        }

        return new ProductionAnalysis(
            syntaxTrees.Count,
            analyzedTrees.Count,
            addPlatformModuleDeclarationCount,
            referenceDataHelperDeclarationCount,
            addReferenceDataModuleDeclarationCount,
            violations.OrderBy(violation => violation, StringComparer.Ordinal).ToList());
    }

    private static IReadOnlyDictionary<INamedTypeSymbol, OwnedType> CollectOwnedTypes(
        Compilation compilation,
        IEnumerable<TreeOwner> treeOwners)
    {
        var ownedTypes = new Dictionary<INamedTypeSymbol, OwnedType>(SymbolEqualityComparer.Default);

        foreach (var treeOwner in treeOwners)
        {
            if (treeOwner.Owner == null)
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(treeOwner.Tree, ignoreAccessibility: true);
            foreach (var declaration in treeOwner.Tree.GetCompilationUnitRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol declaredType)
                {
                    continue;
                }

                ownedTypes[declaredType.OriginalDefinition] = new OwnedType(
                    treeOwner.Owner!,
                    declaredType);
            }
        }

        return ownedTypes;
    }

    private static void CollectTypeDependencyViolations(
        BoundaryOwner sourceOwner,
        SemanticModel semanticModel,
        CompilationUnitSyntax root,
        IReadOnlyDictionary<INamedTypeSymbol, OwnedType> ownedTypes,
        ISet<string> violations)
    {
        foreach (var typeSyntax in root.DescendantNodes().OfType<TypeSyntax>())
        {
            var referencedType = ArchitectureTestHelpers.GetOwnedNamedTypeSymbol(semanticModel.GetTypeInfo(typeSyntax).Type);
            if (referencedType == null || referencedType.TypeKind == TypeKind.Error)
            {
                continue;
            }

            if (ownedTypes.TryGetValue(referencedType.OriginalDefinition, out var targetType))
            {
                if (!IsAllowedDependency(sourceOwner, targetType, typeSyntax))
                {
                    violations.Add(FormatForbiddenEdge(sourceOwner, targetType));
                }

                continue;
            }

            if (sourceOwner.SubBoundary == PlatformSubBoundary.BuildingBlocks && !IsBclType(referencedType))
            {
                violations.Add(
                    $"BuildingBlocks -> non-BCL external dependency {GetTypeIdentity(referencedType)} is forbidden: {sourceOwner.RelativePath}.");
            }
        }
    }

    private static void CollectCompositionViolations(
        BoundaryOwner sourceOwner,
        SemanticModel semanticModel,
        CompilationUnitSyntax root,
        IReadOnlyDictionary<INamedTypeSymbol, OwnedType> ownedTypes,
        ISet<string> violations)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "AddReferenceDataServices" } method
                || !ownedTypes.TryGetValue(method.ContainingType.OriginalDefinition, out var targetType))
            {
                continue;
            }

            if (!IsCanonicalReferenceDataComposition(sourceOwner, targetType, invocation, method))
            {
                violations.Add(
                    $"Only AddPlatformModule may compose AddReferenceDataServices: {sourceOwner.RelativePath}.");
            }
        }
    }

    private static void CollectForbiddenFacadeViolations(
        BoundaryOwner sourceOwner,
        SemanticModel semanticModel,
        CompilationUnitSyntax root,
        ISet<string> violations)
    {
        foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(declaration) is not IMethodSymbol method)
            {
                continue;
            }

            if (method.Name.Equals("AddReferenceDataModule", StringComparison.Ordinal))
            {
                violations.Add($"AddReferenceDataModule is not a canonical module facade: {sourceOwner.RelativePath}.");
            }

            if (method.Name.Equals("AddReferenceDataServices", StringComparison.Ordinal)
                && method.DeclaredAccessibility != Accessibility.Internal)
            {
                violations.Add($"AddReferenceDataServices must remain internal: {sourceOwner.RelativePath}.");
            }
        }
    }

    private static bool IsAllowedDependency(BoundaryOwner source, OwnedType targetType, SyntaxNode reference)
    {
        var target = targetType.Owner;
        if (source.SubBoundary != null && source.SubBoundary == target.SubBoundary
            || source.IsDomain && target.IsDomain
            || source.IsFeature && target.IsFeature)
        {
            return true;
        }

        if (IsCanonicalPlatformComposition(source, targetType, reference))
        {
            return true;
        }

        if (source.SubBoundary == PlatformSubBoundary.BuildingBlocks)
        {
            return false;
        }

        if (source.SubBoundary == PlatformSubBoundary.TechnicalPlatform)
        {
            return target.SubBoundary == PlatformSubBoundary.BuildingBlocks
                   && IsApprovedBuildingBlocksContract(targetType)
                || target.IsDomain && IsDomainNeutralPrimitive(targetType);
        }

        if (source.SubBoundary == PlatformSubBoundary.ReferenceData)
        {
            return target.SubBoundary == PlatformSubBoundary.BuildingBlocks
                   && IsApprovedBuildingBlocksContract(targetType)
                || target.IsDomain
                || target.SubBoundary == PlatformSubBoundary.TechnicalPlatform
                   && IsApprovedTechnicalContract(targetType);
        }

        if (source.IsFeature && target.SubBoundary != null)
        {
            return target.SubBoundary switch
            {
                PlatformSubBoundary.BuildingBlocks => IsApprovedBuildingBlocksContract(targetType),
                PlatformSubBoundary.TechnicalPlatform => IsApprovedTechnicalContract(targetType),
                PlatformSubBoundary.ReferenceData => IsApprovedReferenceDataContract(targetType),
                _ => false
            };
        }

        return true;
    }

    private static bool IsCanonicalPlatformComposition(
        BoundaryOwner source,
        OwnedType targetType,
        SyntaxNode reference)
    {
        var containingMethod = reference.AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();
        if (source.SubBoundary != PlatformSubBoundary.TechnicalPlatform
            || !IsExactApplicationPath(source.RelativePath, "Platform/ServiceCollectionExtensions.cs")
            || containingMethod?.Identifier.ValueText != "AddPlatformModule")
        {
            return false;
        }

        if (ReferenceDataCompositionTypeManifest.Contains(GetTypeIdentity(targetType.Symbol)))
        {
            return true;
        }

        return targetType.Owner.SubBoundary == PlatformSubBoundary.ReferenceData
            && IsExactApplicationPath(targetType.Owner.RelativePath, "Platform/ReferenceData/ServiceCollectionExtensions.cs")
            && targetType.Symbol.DeclaredAccessibility == Accessibility.Internal;
    }

    private static bool IsCanonicalReferenceDataComposition(
        BoundaryOwner source,
        OwnedType targetType,
        SyntaxNode reference,
        IMethodSymbol? targetMethod)
    {
        var containingMethod = reference.AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        return source.SubBoundary == PlatformSubBoundary.TechnicalPlatform
            && IsExactApplicationPath(source.RelativePath, "Platform/ServiceCollectionExtensions.cs")
            && containingMethod?.Identifier.ValueText == "AddPlatformModule"
            && IsExactApplicationPath(targetType.Owner.RelativePath, "Platform/ReferenceData/ServiceCollectionExtensions.cs")
            && targetType.Symbol.DeclaredAccessibility == Accessibility.Internal
            && (targetMethod == null || targetMethod.DeclaredAccessibility == Accessibility.Internal);
    }

    private static bool IsApprovedTechnicalContract(OwnedType targetType)
    {
        return targetType.Symbol.DeclaredAccessibility == Accessibility.Public
            && TechnicalContractManifest.Contains(GetTypeIdentity(targetType.Symbol));
    }

    private static bool IsApprovedReferenceDataContract(OwnedType targetType)
    {
        return targetType.Symbol.DeclaredAccessibility == Accessibility.Public
            && ReferenceDataContractManifest.Contains(GetTypeIdentity(targetType.Symbol));
    }

    private static bool IsApprovedBuildingBlocksContract(OwnedType targetType)
    {
        return targetType.Symbol.DeclaredAccessibility == Accessibility.Public
            && BuildingBlocksContractManifest.Contains(GetTypeIdentity(targetType.Symbol));
    }

    private static bool IsDomainNeutralPrimitive(OwnedType targetType)
    {
        return DomainNeutralPrimitiveManifest.Contains(GetTypeIdentity(targetType.Symbol));
    }

    private static BoundaryOwner? ClassifyOwner(string path)
    {
        var relativePath = GetRelativeSourcePath(path);
        var subBoundary = ArchitectureTestHelpers.GetPlatformSubBoundaryFromPath(path);
        if (subBoundary != null)
        {
            return new BoundaryOwner(GetDisplayName(subBoundary.Value), relativePath, subBoundary, false, false);
        }

        if (relativePath.StartsWith("LgymApi.Domain/", StringComparison.Ordinal))
        {
            return new BoundaryOwner("Domain", relativePath, null, true, false);
        }

        var canonicalModule = ArchitectureTestHelpers.GetCanonicalModuleNameFromPath(path);
        if (canonicalModule != null && canonicalModule != ArchitectureTestHelpers.PlatformModuleName)
        {
            return new BoundaryOwner(canonicalModule, relativePath, null, false, true);
        }

        return relativePath.StartsWith("LgymApi.Application/", StringComparison.Ordinal)
            ? new BoundaryOwner("Unclassified Application", relativePath, null, false, false)
            : null;
    }

    private static bool IsAnalyzedSource(BoundaryOwner owner)
    {
        return owner.SubBoundary != null || owner.IsFeature;
    }

    private static string GetDisplayName(PlatformSubBoundary subBoundary)
    {
        return subBoundary switch
        {
            PlatformSubBoundary.BuildingBlocks => "BuildingBlocks",
            PlatformSubBoundary.TechnicalPlatform => "Technical Platform",
            PlatformSubBoundary.ReferenceData => "Reference Data",
            _ => throw new ArgumentOutOfRangeException(nameof(subBoundary), subBoundary, null)
        };
    }

    private static string FormatForbiddenEdge(BoundaryOwner source, OwnedType targetType)
    {
        return $"{source.DisplayName} -> {targetType.Owner.DisplayName} is forbidden: {source.RelativePath} -> {GetTypeIdentity(targetType.Symbol)}.";
    }

    private static bool IsExactApplicationPath(string relativePath, string expectedPath)
    {
        return relativePath.Equals($"LgymApi.Application/{expectedPath}", StringComparison.Ordinal);
    }

    private static string GetRelativeSourcePath(string path)
    {
        var normalizedPath = ArchitectureTestHelpers.NormalizePath(path);
        var applicationIndex = normalizedPath.IndexOf("/LgymApi.Application/", StringComparison.Ordinal);
        if (applicationIndex >= 0)
        {
            return normalizedPath[(applicationIndex + 1)..];
        }

        var domainIndex = normalizedPath.IndexOf("/LgymApi.Domain/", StringComparison.Ordinal);
        return domainIndex >= 0 ? normalizedPath[(domainIndex + 1)..] : normalizedPath;
    }

    private static string GetTypeIdentity(INamedTypeSymbol type)
    {
        var originalType = type.OriginalDefinition;
        var namespaceName = originalType.ContainingNamespace.ToDisplayString();
        return string.IsNullOrEmpty(namespaceName)
            ? originalType.MetadataName
            : $"{namespaceName}.{originalType.MetadataName}";
    }

    private static bool IsBclType(INamedTypeSymbol type)
    {
        var assemblyName = type.ContainingAssembly?.Name;
        return assemblyName is "mscorlib" or "netstandard" or "System.Private.CoreLib" or "System"
            || assemblyName?.StartsWith("System.", StringComparison.Ordinal) == true;
    }

    private static IReadOnlySet<string> CreateManifest(params string[] typeIdentities)
    {
        return new HashSet<string>(typeIdentities, StringComparer.Ordinal);
    }

    private static FixtureSource Source(string relativePath, string content)
    {
        return new FixtureSource($"C:/fixture/{relativePath}", content);
    }

    private static TestCaseData ForbiddenCase(
        string name,
        IReadOnlyList<FixtureSource> sources,
        string expectedViolation)
    {
        return new TestCaseData(name, sources, expectedViolation).SetName(name);
    }

    public sealed record FixtureSource(string Path, string Content);

    private sealed record TreeOwner(SyntaxTree Tree, BoundaryOwner? Owner);

    private sealed record BoundaryOwner(
        string DisplayName,
        string RelativePath,
        PlatformSubBoundary? SubBoundary,
        bool IsDomain,
        bool IsFeature);

    private sealed record OwnedType(BoundaryOwner Owner, INamedTypeSymbol Symbol);

    private sealed record ProductionAnalysis(
        int SourceTreeCount,
        int AnalyzedSourceTreeCount,
        int AddPlatformModuleDeclarationCount,
        int ReferenceDataHelperDeclarationCount,
        int AddReferenceDataModuleDeclarationCount,
        IReadOnlyList<string> Violations);
}
