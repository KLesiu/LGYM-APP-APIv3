using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class PlatformBuildingBlocksBoundaryGuardTests
{
    private const string ApplicationProjectPath = "LgymApi.Application";
    private const string PlatformProjectPath = "LgymApi.Platform";
    private const string InfrastructureProjectPath = "LgymApi.Infrastructure";

    private static readonly SurfaceEntry[] AllowedSurface =
    [
        new(Surface.BuildingBlocks, "LgymApi.Platform/BuildingBlocks/Results/Result.cs", "LgymApi.Application.BuildingBlocks.Results.Result`2", TypeKind.Struct, false, false, true),
        new(Surface.BuildingBlocks, "LgymApi.Platform/BuildingBlocks/Results/Result.cs", "LgymApi.Application.BuildingBlocks.Results.Result", TypeKind.Class, true, false, true),
        new(Surface.BuildingBlocks, "LgymApi.Platform/BuildingBlocks/Results/Unit.cs", "LgymApi.Application.BuildingBlocks.Results.Unit", TypeKind.Struct, false, false, true),
        new(Surface.BuildingBlocks, "LgymApi.Platform/BuildingBlocks/Errors/AppError.cs", "LgymApi.Application.BuildingBlocks.Errors.AppError", TypeKind.Class, false, true, true),
        new(Surface.BuildingBlocks, "LgymApi.Platform/BuildingBlocks/Errors/AppError.cs", "LgymApi.Application.BuildingBlocks.Errors.NotFoundError", TypeKind.Class, false, false, true),
        new(Surface.BuildingBlocks, "LgymApi.Platform/BuildingBlocks/Errors/AppError.cs", "LgymApi.Application.BuildingBlocks.Errors.BadRequestError", TypeKind.Class, false, false, true),
        new(Surface.BuildingBlocks, "LgymApi.Platform/BuildingBlocks/Errors/AppError.cs", "LgymApi.Application.BuildingBlocks.Errors.UnauthorizedError", TypeKind.Class, false, false, true),
        new(Surface.BuildingBlocks, "LgymApi.Platform/BuildingBlocks/Errors/AppError.cs", "LgymApi.Application.BuildingBlocks.Errors.ForbiddenError", TypeKind.Class, false, false, true),
        new(Surface.BuildingBlocks, "LgymApi.Platform/BuildingBlocks/Errors/AppError.cs", "LgymApi.Application.BuildingBlocks.Errors.ConflictError", TypeKind.Class, false, false, true),
        new(Surface.BuildingBlocks, "LgymApi.Platform/BuildingBlocks/Errors/AppError.cs", "LgymApi.Application.BuildingBlocks.Errors.UnprocessableEntityError", TypeKind.Class, false, false, true),
        new(Surface.BuildingBlocks, "LgymApi.Platform/BuildingBlocks/Errors/AppError.cs", "LgymApi.Application.BuildingBlocks.Errors.InternalServerError", TypeKind.Class, false, false, true),

new(Surface.TechnicalPlatform, "LgymApi.Platform/Contracts/BackgroundCommands/IActionCommand.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands.IActionCommand", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Contracts/ActorReference.cs", "LgymApi.Platform.Contracts.ActorReference", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Contracts/BackgroundCommands/ICommandDispatcher.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands.ICommandDispatcher", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Contracts/BackgroundCommands/ICommandOutboxWriter.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands.ICommandOutboxWriter", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Contracts/BackgroundCommands/ICommandOutboxWriter.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands.CommandEnvelopeStageResult", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Contracts/BackgroundCommands/ICommandEnvelopeRuntime.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands.ICommandEnvelopeRuntime", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Contracts/BackgroundCommands/ICommandEnvelopeRuntime.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands.CommandEnvelopeRequest", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Contracts/BackgroundCommands/ICommandEnvelopeRuntime.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands.CommandEnvelopeReceipt", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Contracts/BackgroundCommands/ICommandEnvelopeRuntime.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands.CommandEnvelopeStart", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Contracts/BackgroundCommands/ICommandEnvelopeRuntime.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands.CommandHandlerResult", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Contracts/BackgroundCommands/ICommandEnvelopeRuntime.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands.CommandEnvelopeFinalization", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Contracts/Serialization/SharedSerializationOptions.cs", "LgymApi.Application.Platform.Contracts.Serialization.SharedSerializationOptions", TypeKind.Class, true, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Contracts/Serialization/TypedIdJsonConverter.cs", "LgymApi.Application.Platform.Contracts.Serialization.TypedIdJsonConverter`1", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Contracts/Serialization/TypedIdJsonConverterFactory.cs", "LgymApi.Application.Platform.Contracts.Serialization.TypedIdJsonConverterFactory", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Mapping/MappingServiceCollectionExtensions.cs", "LgymApi.Application.Mapping.MappingServiceCollectionExtensions", TypeKind.Class, true, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Mapping/Extensions/MappingExtensions.cs", "LgymApi.Application.Mapping.Extensions.MappingExtensions", TypeKind.Class, true, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Mapping/Core/ContextKey.cs", "LgymApi.Application.Mapping.Core.ContextKey`1", TypeKind.Struct, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Mapping/Core/IMappingContext.cs", "LgymApi.Application.Mapping.Core.IMappingContext", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Mapping/Core/IMapper.cs", "LgymApi.Application.Mapping.Core.IMapper", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Mapping/Core/IMappingProfile.cs", "LgymApi.Application.Mapping.Core.IMappingProfile", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Mapping/Core/MappingConfiguration.cs", "LgymApi.Application.Mapping.Core.MappingConfiguration", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Mapping/Core/MappingContext.cs", "LgymApi.Application.Mapping.Core.MappingContext", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Mapping/Core/Mapper.cs", "LgymApi.Application.Mapping.Core.Mapper", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/PlatformModule.cs", "LgymApi.Platform.PlatformModule", TypeKind.Class, true, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Pagination/FieldMapping.cs", "LgymApi.Application.Pagination.FieldMapping", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Pagination/FieldMapping.cs", "LgymApi.Application.Pagination.SortDescriptor", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Pagination/FilterCondition.cs", "LgymApi.Application.Pagination.FilterCondition", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Pagination/FilterCondition.cs", "LgymApi.Application.Pagination.FilterOperator", TypeKind.Enum, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Pagination/FilterInput.cs", "LgymApi.Application.Pagination.FilterInput", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Pagination/FilterInput.cs", "LgymApi.Application.Pagination.FilterGroup", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Pagination/FilterInput.cs", "LgymApi.Application.Pagination.GroupOperator", TypeKind.Enum, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Pagination/IMapperRegistry.cs", "LgymApi.Application.Pagination.IMapperRegistry", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Pagination/IQueryPaginationService.cs", "LgymApi.Application.Pagination.IQueryPaginationService", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Pagination/IWhitelistPolicy.cs", "LgymApi.Application.Pagination.IWhitelistPolicy", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Pagination/PaginatedRequest.cs", "LgymApi.Application.Pagination.PaginatedRequest", TypeKind.Class, false, true, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Pagination/Pagination.cs", "LgymApi.Application.Pagination.Pagination`1", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Pagination/PaginationPolicy.cs", "LgymApi.Application.Pagination.PaginationPolicy", TypeKind.Class, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Pagination/IGridifyExecutionService.cs", "LgymApi.Infrastructure.Pagination.IGridifyExecutionService", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Repositories/IUnitOfWork.cs", "LgymApi.Application.Repositories.IUnitOfWork", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Repositories/IUnitOfWork.cs", "LgymApi.Application.Repositories.IUnitOfWorkTransaction", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Repositories/IActorRowSecurityScopeFactory.cs", "LgymApi.Application.Repositories.IActorRowSecurityScopeFactory", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Repositories/ICommittedIntentDispatcher.cs", "LgymApi.Application.Repositories.ICommittedIntentDispatcher", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Repositories/ICommandEnvelopeRepository.cs", "LgymApi.Application.Repositories.ICommandEnvelopeRepository", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Platform/Repositories/IApiIdempotencyRecordRepository.cs", "LgymApi.Application.Repositories.IApiIdempotencyRecordRepository", TypeKind.Interface, false, false, true),
        new(Surface.TechnicalPlatform, "LgymApi.Infrastructure/PlatformServiceCollectionExtensions.cs", "LgymApi.Infrastructure.ServiceCollectionExtensions", TypeKind.Class, true, false, true),

        new(Surface.ReferenceData, "LgymApi.Platform/Repositories/IAppConfigRepository.cs", "LgymApi.Application.Repositories.IAppConfigRepository", TypeKind.Interface, false, false, true),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/AppConfig/AppConfigService.cs", "LgymApi.Application.Platform.ReferenceData.AppConfig.AppConfigService", TypeKind.Class, false, false, false),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/AppConfig/IAppConfigService.cs", "LgymApi.Application.Platform.ReferenceData.AppConfig.IAppConfigService", TypeKind.Interface, false, false, true),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/AppConfig/CreateAppVersionInput.cs", "LgymApi.Application.Platform.ReferenceData.AppConfig.CreateAppVersionInput", TypeKind.Class, false, false, true),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/AppConfig/UpdateAppConfigInput.cs", "LgymApi.Application.Platform.ReferenceData.AppConfig.UpdateAppConfigInput", TypeKind.Class, false, false, true),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Enums/EnumService.cs", "LgymApi.Application.Platform.ReferenceData.Enums.EnumService", TypeKind.Class, false, false, false),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Enums/IEnumService.cs", "LgymApi.Application.Platform.ReferenceData.Enums.IEnumService", TypeKind.Interface, false, false, true),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Enums/EnumLookupEntryFormatter.cs", "LgymApi.Application.Platform.ReferenceData.Enums.EnumLookupEntryFormatter", TypeKind.Class, false, false, false),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Enums/Models/EnumLookupEntry.cs", "LgymApi.Application.Platform.ReferenceData.Enums.Models.EnumLookupEntry", TypeKind.Class, false, false, true),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Enums/Models/EnumLookupResponse.cs", "LgymApi.Application.Platform.ReferenceData.Enums.Models.EnumLookupResponse", TypeKind.Class, false, false, true),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Units/HeightLinearUnitStrategy.cs", "LgymApi.Application.Platform.ReferenceData.Units.HeightLinearUnitStrategy", TypeKind.Class, false, false, false),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Units/ILinearUnitStrategy.cs", "LgymApi.Application.Platform.ReferenceData.Units.ILinearUnitStrategy`1", TypeKind.Interface, false, false, true),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Units/IUnitConverter.cs", "LgymApi.Application.Platform.ReferenceData.Units.IUnitConverter`1", TypeKind.Interface, false, false, true),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Units/LinearUnitConverter.cs", "LgymApi.Application.Platform.ReferenceData.Units.LinearUnitConverter`1", TypeKind.Class, false, false, false),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Units/UnitValueComparer.cs", "LgymApi.Application.Platform.ReferenceData.Units.UnitValueComparer", TypeKind.Class, true, false, true),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Units/WeightLinearUnitStrategy.cs", "LgymApi.Application.Platform.ReferenceData.Units.WeightLinearUnitStrategy", TypeKind.Class, false, false, false),

        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/AppConfig/Contracts/IAppConfigAuthorizationPort.cs", "LgymApi.Application.Platform.ReferenceData.AppConfig.Contracts.IAppConfigAuthorizationPort", TypeKind.Interface, false, false, true),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Errors/AppConfigErrors.cs", "LgymApi.Application.Platform.ReferenceData.Errors.AppConfigNotFoundError", TypeKind.Class, false, false, true),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Errors/AppConfigErrors.cs", "LgymApi.Application.Platform.ReferenceData.Errors.AppConfigForbiddenError", TypeKind.Class, false, false, true),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Errors/AppConfigErrors.cs", "LgymApi.Application.Platform.ReferenceData.Errors.InvalidAppConfigError", TypeKind.Class, false, false, true),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Errors/EnumErrors.cs", "LgymApi.Application.Platform.ReferenceData.Errors.InvalidEnumError", TypeKind.Class, false, false, true),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/ServiceCollectionExtensions.cs", "LgymApi.Application.Platform.ReferenceData.ServiceCollectionExtensions", TypeKind.Class, true, false, false),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Enums/EnumLookupContextKeys.cs", "LgymApi.Application.Platform.ReferenceData.Enums.EnumLookupContextKeys", TypeKind.Class, true, false, false),
        new(Surface.ReferenceData, "LgymApi.Platform/ReferenceData/Enums/EnumLookupMappingProfile.cs", "LgymApi.Application.Platform.ReferenceData.Enums.EnumLookupMappingProfile", TypeKind.Class, false, false, false)
    ];

    private static readonly HashSet<string> LegacyPrimitiveMetadataNames = new(StringComparer.Ordinal)
    {
        "LgymApi.Application.Common.Results.Result`2",
        "LgymApi.Application.Common.Results.Result",
        "LgymApi.Application.Common.Results.Unit",
        "LgymApi.Application.Common.Errors.AppError",
        "LgymApi.Application.Common.Errors.NotFoundError",
        "LgymApi.Application.Common.Errors.BadRequestError",
        "LgymApi.Application.Common.Errors.UnauthorizedError",
        "LgymApi.Application.Common.Errors.ForbiddenError",
        "LgymApi.Application.Common.Errors.ConflictError",
        "LgymApi.Application.Common.Errors.UnprocessableEntityError",
        "LgymApi.Application.Common.Errors.InternalServerError"
    };

    [Test]
    public void Repository_Platform_BuildingBlocks_And_ReferenceData_Public_Surface_Matches_Exact_Manifest()
    {
        var (repoRoot, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation(PlatformProjectPath, ApplicationProjectPath, InfrastructureProjectPath);

        Assert.That(
            CollectViolations(compilation, syntaxTrees, repoRoot),
            Is.Empty,
            "BuildingBlocks, Technical Platform, and Reference Data must retain their exact public manifests.");
    }

    [Test]
    public void Exact_Manifest_Fixture_Allows_Only_Listed_Symbols()
    {
        var compilation = CreateFixtureCompilation(CreateAllowedFixtureSources());

        Assert.That(CollectViolations(compilation, compilation.SyntaxTrees, null), Is.Empty);
    }

    [TestCase("LgymApi.Platform/BuildingBlocks/Services/UnexpectedBuildingBlocksService.cs", "LgymApi.Application.BuildingBlocks.Services", "public interface IUnexpectedBuildingBlocksService {}")]
    [TestCase("LgymApi.Platform/BuildingBlocks/Repositories/ISharedRepository.cs", "LgymApi.Application.BuildingBlocks.Repositories", "public interface ISharedRepository {}")]
    [TestCase("LgymApi.Platform/BuildingBlocks/Models/SharedDto.cs", "LgymApi.Application.BuildingBlocks.Models", "public sealed class SharedDto {}")]
    [TestCase("LgymApi.Platform/BuildingBlocks/Errors/UserNotFoundError.cs", "LgymApi.Application.BuildingBlocks.Errors", "public sealed class UserNotFoundError {}")]
    [TestCase("LgymApi.Platform/BuildingBlocks/Errors/AppConfigErrors.cs", "LgymApi.Application.BuildingBlocks.Errors", "public sealed class InvalidAppConfigError {}")]
    [TestCase("LgymApi.Platform/BuildingBlocks/Errors/EnumErrors.cs", "LgymApi.Application.BuildingBlocks.Errors", "public sealed class InvalidEnumError {}")]
    [TestCase("LgymApi.Platform/BuildingBlocks/Providers/FcmProvider.cs", "LgymApi.Application.BuildingBlocks.Providers", "public sealed class FcmProvider {}")]
    [TestCase("LgymApi.Platform/Contracts/BackgroundCommands/FeatureCommand.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands", "public sealed class FeatureCommand {}")]
    [TestCase("LgymApi.Platform/Contracts/Fixtures/IUnexpectedPlatformContract.cs", "LgymApi.Application.Platform.Contracts.Fixtures", "public interface IUnexpectedPlatformContract {}")]
    [TestCase("LgymApi.Platform/ReferenceData/Models/UnexpectedReferenceDataDto.cs", "LgymApi.Application.Platform.ReferenceData.Models", "public sealed class UnexpectedReferenceDataDto {}")]
    public void Unlisted_Public_Surface_Semantic_Fixtures_Are_Rejected(string path, string namespaceName, string declaration)
    {
        var compilation = CreateFixtureCompilation(
            CreateAllowedFixtureSources().Append((path, $"namespace {namespaceName}; {declaration}")));

        Assert.That(
            CollectViolations(compilation, compilation.SyntaxTrees, null),
            Has.Some.Matches<SurfaceViolation>(violation =>
                violation.RelativePath == path
                && violation.Message.Contains("Unexpected", StringComparison.Ordinal)));
    }

    [Test]
    public void Legacy_Common_Primitive_Alias_Fixture_Is_Rejected()
    {
        const string path = "LgymApi.Application/Common/Results/Result.cs";
        var compilation = CreateFixtureCompilation(
            CreateAllowedFixtureSources().Append((
                path,
                "namespace LgymApi.Application.Common.Results; public readonly struct Result<TValue, TError> {} public static class Result {}")));

        Assert.That(
            CollectViolations(compilation, compilation.SyntaxTrees, null),
            Has.Some.Matches<SurfaceViolation>(violation =>
                violation.RelativePath == path
                && violation.Message.Contains("Legacy Common primitive alias", StringComparison.Ordinal)));
    }

    [Test]
    public void Unexpected_Public_Delegate_In_Result_Source_Fixture_Is_Rejected()
    {
        const string path = "LgymApi.Platform/BuildingBlocks/Results/Result.cs";
        var compilation = CreateFixtureCompilation(
            CreateAllowedFixtureSources().Select(source =>
                source.Path == path
                    ? (source.Path, $"{source.Source} public delegate void ResultCallback();")
                    : source));

        Assert.That(
            CollectViolations(compilation, compilation.SyntaxTrees, null),
            Has.Some.Matches<SurfaceViolation>(violation =>
                violation.RelativePath == path
                && violation.Message.Contains("Unexpected public symbol", StringComparison.Ordinal)));
    }

    [Test]
    public void Legacy_Application_Duplicate_Source_Fixture_Is_Rejected()
    {
        const string path = "LgymApi.Application/BuildingBlocks/Results/Result.cs";
        var sources = CreateAllowedFixtureSources()
            .Append((Path: path, Source: "namespace LgymApi.Application.BuildingBlocks.Results; public static class Result {}"));
        var trees = sources
            .Select(source => CSharpSyntaxTree.ParseText(source.Source, path: source.Path))
            .ToList();
        var compilation = CSharpCompilation.Create(
            "PlatformBuildingBlocksDuplicateFixture",
            trees,
            ArchitectureTestHelpers.ResolveMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.That(
            CollectViolations(compilation, trees, null),
            Has.Some.Matches<SurfaceViolation>(violation =>
                violation.RelativePath == path
                && violation.Message.Contains("Unexpected public-surface source file", StringComparison.Ordinal)));
    }

    private static IReadOnlyList<SurfaceViolation> CollectViolations(
        Compilation compilation,
        IEnumerable<SyntaxTree> syntaxTrees,
        string? repoRoot)
    {
        var expectedByPath = AllowedSurface
            .GroupBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(entry => entry.MetadataName, StringComparer.Ordinal),
                StringComparer.Ordinal);
        var observedByPath = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var violations = new List<SurfaceViolation>();

        foreach (var tree in syntaxTrees)
        {
            var relativePath = NormalizePath(tree.FilePath, repoRoot);
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var publicTypes = GetPublicTypes(tree, semanticModel).ToList();

            AddLegacyAliasViolations(relativePath, publicTypes, violations);
            if (!IsSurfacePath(relativePath))
            {
                continue;
            }

            if (!expectedByPath.TryGetValue(relativePath, out var expectedEntries))
            {
                if (!publicTypes.Any())
                {
                    continue;
                }

                violations.Add(new SurfaceViolation(relativePath, "Unexpected public-surface source file."));
                foreach (var type in publicTypes)
                {
                    violations.Add(new SurfaceViolation(relativePath, $"Unexpected public symbol '{GetMetadataName(type)}'."));
                }

                continue;
            }

            var observedTypes = new HashSet<string>(StringComparer.Ordinal);
            observedByPath[relativePath] = observedTypes;
            foreach (var type in publicTypes)
            {
                var metadataName = GetMetadataName(type);
                observedTypes.Add(metadataName);
                if (!expectedEntries.TryGetValue(metadataName, out var expected))
                {
                    violations.Add(new SurfaceViolation(relativePath, $"Unexpected public symbol '{metadataName}'."));
                    continue;
                }

                AddShapeViolations(relativePath, type, expected, violations);
            }
        }

        foreach (var expected in AllowedSurface.Where(entry => entry.RequiredNow))
        {
            if (!observedByPath.TryGetValue(expected.RelativePath, out var observedTypes)
                || !observedTypes.Contains(expected.MetadataName))
            {
                violations.Add(new SurfaceViolation(expected.RelativePath, $"Missing required public symbol '{expected.MetadataName}'."));
            }
        }

        return violations
            .OrderBy(violation => violation.RelativePath, StringComparer.Ordinal)
            .ThenBy(violation => violation.Message, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddLegacyAliasViolations(string relativePath, IEnumerable<INamedTypeSymbol> publicTypes, ICollection<SurfaceViolation> violations)
    {
        foreach (var type in publicTypes)
        {
            var metadataName = GetMetadataName(type);
            if (LegacyPrimitiveMetadataNames.Contains(metadataName))
            {
                violations.Add(new SurfaceViolation(relativePath, $"Legacy Common primitive alias '{metadataName}' is forbidden."));
            }
        }
    }

    private static bool IsSurfacePath(string relativePath)
    {
        return relativePath.StartsWith("LgymApi.Platform/BuildingBlocks/", StringComparison.Ordinal)
            || relativePath.StartsWith("LgymApi.Platform/Contracts/", StringComparison.Ordinal)
            || relativePath.StartsWith("LgymApi.Platform/Pagination/", StringComparison.Ordinal)
            || relativePath.StartsWith("LgymApi.Platform/Mapping/", StringComparison.Ordinal)
            || relativePath.StartsWith("LgymApi.Platform/Repositories/", StringComparison.Ordinal)
            || relativePath.StartsWith("LgymApi.Platform/ReferenceData/", StringComparison.Ordinal)
            || relativePath.StartsWith("LgymApi.Application/BuildingBlocks/", StringComparison.Ordinal)
            || relativePath.StartsWith("LgymApi.Application/Platform/Contracts/", StringComparison.Ordinal)
            || relativePath.StartsWith("LgymApi.Application/Pagination/", StringComparison.Ordinal)
            || relativePath.StartsWith("LgymApi.Application/Mapping/", StringComparison.Ordinal)
            || relativePath.StartsWith("LgymApi.Application/Repositories/IUnitOfWork.cs", StringComparison.Ordinal)
            || relativePath.StartsWith("LgymApi.Application/Repositories/ICommittedIntentDispatcher.cs", StringComparison.Ordinal)
            || relativePath.StartsWith("LgymApi.Application/Repositories/ICommandEnvelopeRepository.cs", StringComparison.Ordinal)
            || relativePath.StartsWith("LgymApi.Application/Repositories/IApiIdempotencyRecordRepository.cs", StringComparison.Ordinal)
            || AllowedSurface.Any(entry => entry.RelativePath == relativePath);
    }

    private static string NormalizePath(string path, string? repoRoot)
    {
        return repoRoot != null && Path.IsPathFullyQualified(path)
            ? ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, path))
            : ArchitectureTestHelpers.NormalizePath(path);
    }

    private static IEnumerable<INamedTypeSymbol> GetPublicTypes(SyntaxTree tree, SemanticModel semanticModel)
    {
        return tree.GetRoot()
            .DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(declaration => declaration is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            .Select(declaration => semanticModel.GetDeclaredSymbol(declaration))
            .OfType<INamedTypeSymbol>()
            .Where(symbol => symbol.DeclaredAccessibility == Accessibility.Public);
    }

    private static void AddShapeViolations(string relativePath, INamedTypeSymbol actual, SurfaceEntry expected, ICollection<SurfaceViolation> violations)
    {
        var metadataName = GetMetadataName(actual);
        if (actual.TypeKind != expected.TypeKind)
        {
            violations.Add(new SurfaceViolation(relativePath, $"Public symbol '{metadataName}' expected type kind '{expected.TypeKind}' but was '{actual.TypeKind}'."));
        }

        if (actual.IsStatic != expected.IsStatic)
        {
            violations.Add(new SurfaceViolation(relativePath, $"Public symbol '{metadataName}' expected staticness '{expected.IsStatic}' but was '{actual.IsStatic}'."));
        }

        if (actual.TypeKind == TypeKind.Class && actual.IsAbstract != expected.IsAbstract)
        {
            violations.Add(new SurfaceViolation(relativePath, $"Public symbol '{metadataName}' expected abstractness '{expected.IsAbstract}' but was '{actual.IsAbstract}'."));
        }
    }

    private static CSharpCompilation CreateFixtureCompilation(IEnumerable<(string Path, string Source)> sources)
    {
        var trees = sources
            .Select(source => CSharpSyntaxTree.ParseText(source.Source, path: source.Path))
            .ToList();
        var compilation = CSharpCompilation.Create(
            "PlatformBuildingBlocksSurfaceFixture",
            trees,
            ArchitectureTestHelpers.ResolveMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.That(
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "Platform and BuildingBlocks fixtures must compile before semantic analysis.");

        return compilation;
    }

    private static IEnumerable<(string Path, string Source)> CreateAllowedFixtureSources()
    {
        return AllowedSurface
            .GroupBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .Select(group =>
            {
                var namespaceName = group.First().MetadataName[..group.First().MetadataName.LastIndexOf('.')];
                var declarations = group.Select(CreateFixtureDeclaration);
                return (group.Key, $"namespace {namespaceName}; {string.Join(Environment.NewLine, declarations)}");
            });
    }

    private static string CreateFixtureDeclaration(SurfaceEntry entry)
    {
        var metadataTypeName = entry.MetadataName[(entry.MetadataName.LastIndexOf('.') + 1)..];
        var genericArityIndex = metadataTypeName.IndexOf('`');
        var typeName = genericArityIndex < 0 ? metadataTypeName : metadataTypeName[..genericArityIndex];
        var genericParameters = genericArityIndex < 0
            ? string.Empty
            : $"<{string.Join(", ", Enumerable.Range(1, int.Parse(metadataTypeName[(genericArityIndex + 1)..])).Select(index => $"T{index}"))}>";
        var modifiers = entry.IsStatic
            ? "public static"
            : entry.IsAbstract && entry.TypeKind == TypeKind.Class
                ? "public abstract"
                : "public";
        var declarationKind = entry.TypeKind switch
        {
            TypeKind.Interface => "interface",
            TypeKind.Struct => "struct",
            TypeKind.Enum => "enum",
            TypeKind.Class => "class",
            _ => throw new InvalidOperationException($"Unsupported fixture type kind '{entry.TypeKind}'.")
        };

        return entry.TypeKind == TypeKind.Enum
            ? $"{modifiers} {declarationKind} {typeName} {{ Value = 0 }}"
            : $"{modifiers} {declarationKind} {typeName}{genericParameters} {{}}";
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

    private enum Surface
    {
        BuildingBlocks,
        TechnicalPlatform,
        ReferenceData
    }

    private sealed record SurfaceEntry(
        Surface Surface,
        string RelativePath,
        string MetadataName,
        TypeKind TypeKind,
        bool IsStatic,
        bool IsAbstract,
        bool RequiredNow);

    private sealed record SurfaceViolation(string RelativePath, string Message)
    {
        public override string ToString() => $"{RelativePath}: {Message}";
    }
}
