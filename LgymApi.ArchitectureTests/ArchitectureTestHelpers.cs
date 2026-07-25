using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

/// <summary>
/// Shared utility methods for architecture guard tests (Roslyn-based pattern validation).
/// Extracted from common patterns in existing guard test files to reduce duplication.
/// </summary>
public static class ArchitectureTestHelpers
{
    public const string PlatformModuleName = "Platform / Reference Data";
    public const string IdentityModuleName = "Identity & Accounts";
    public const string NotificationsModuleName = "Notifications";
    public const string ReportingModuleName = "Reporting";
    public const string TrainingPlanningModuleName = "Training Planning";
    public const string WorkoutProgressModuleName = "Workout & Progress";
    public const string CoachingModuleName = "Coaching";
    public const string NutritionModuleName = "Nutrition";

    private static readonly string[] ApiFeatureLeafFolders =
    {
        "Contracts",
        "Controllers",
        "Validation"
    };

    private static readonly IReadOnlyList<string> CanonicalModuleCatalog =
    [
        PlatformModuleName,
        IdentityModuleName,
        NotificationsModuleName,
        ReportingModuleName,
        TrainingPlanningModuleName,
        WorkoutProgressModuleName,
        CoachingModuleName,
        NutritionModuleName
    ];

    private static readonly string[] BuildingBlocksPathMarkers =
    {
        "/LgymApi.Application/BuildingBlocks/",
        "/LgymApi.Application/Common/Results/"
    };

    private static readonly string[] BuildingBlocksExactFiles =
    {
        "/LgymApi.Application/Common/Errors/AppError.cs"
    };

    private static readonly string[] TechnicalPlatformPathMarkers =
    {
        "/LgymApi.Application/Platform/Contracts/",
        "/LgymApi.Application/Pagination/",
        "/LgymApi.Application/Mapping/"
    };

    private static readonly string[] TechnicalPlatformExactFiles =
    {
        "/LgymApi.Application/Platform/ServiceCollectionExtensions.cs",
        "/LgymApi.Application/Repositories/IUnitOfWork.cs",
        "/LgymApi.Application/Repositories/ICommandEnvelopeRepository.cs",
        "/LgymApi.Application/Repositories/IApiIdempotencyRecordRepository.cs"
    };

    private static readonly string[] ReferenceDataPathMarkers =
    {
        "/LgymApi.Application/Platform/ReferenceData/"
    };

    private static readonly string[] ReferenceDataExactFiles =
    {
        "/LgymApi.Application/Repositories/IAppConfigRepository.cs",
        "/LgymApi.Application/Platform/ReferenceData/Errors/AppConfigErrors.cs",
        "/LgymApi.Application/Platform/ReferenceData/Errors/EnumErrors.cs"
    };

    private static readonly string[] TestProjectPathMarkers =
    {
        "/LgymApi.UnitTests/",
        "/LgymApi.IntegrationTests/",
        "/LgymApi.ArchitectureTests/",
        "/LgymApi.DataSeeder.Tests/",
        "/LgymApi.TestUtils/"
    };

    private static readonly string[] HelperPathMarkers =
    {
        "/Helpers/",
        "/Fakes/",
        "/Fixtures/",
        "/Builders/",
        "/Mocks/",
        "/Stubs/"
    };

    private static readonly string[] SharedProjectSegments =
    {
        "Repositories",
        "Services",
        "Pagination",
        "UnitOfWork",
        "Mapping",
        "Extensions",
        "Exceptions",
        "Contracts",
        "Validation",
        "Controllers",
        "Interfaces",
        "Common",
        "Properties",
        "Data",
        "Migrations",
        "Configuration",
        "Configurations",
        "Abstractions",
        "Models",
        "Filters",
        "Middleware"
    };

    private static readonly string[] IdentityApplicationPathMarkers =
    {
        "/LgymApi.Application/Identity/",
        "/LgymApi.Application/User/",
        "/LgymApi.Application/Role/",
        "/LgymApi.Application/ExternalAuth/",
        "/LgymApi.Application/Features/AdminManagement/",
        "/LgymApi.Application/Features/Tutorial/",
        "/LgymApi.Application/Features/PasswordReset/"
    };

    private static readonly string[] TrainingPlanningApplicationPathMarkers =
    {
        "/LgymApi.Application/TrainingPlanning/",
        "/LgymApi.Application/PlanDay/"
    };

    private static readonly string[] WorkoutProgressApplicationPathMarkers =
    {
        "/LgymApi.Application/WorkoutProgress/",
        "/LgymApi.Application/Training/",
        "/LgymApi.Application/Exercise/",
        "/LgymApi.Application/ExerciseScores/",
        "/LgymApi.Application/Gym/",
        "/LgymApi.Application/Measurements/",
        "/LgymApi.Application/EloRegistry/",
        "/LgymApi.Application/MainRecords/"
    };

    private static readonly string[] CoachingApplicationPathMarkers =
    {
        "/LgymApi.Application/Coaching/",
        "/LgymApi.Application/TrainerRelationships/",
        "/LgymApi.Application/Features/TraineeNotes/"
    };

    private static readonly string[] NutritionApplicationPathMarkers =
    {
        "/LgymApi.Application/Nutrition/",
        "/LgymApi.Application/Features/DietPlans/",
        "/LgymApi.Application/Features/Supplementation/"
    };

    private static readonly string[] ReportingApplicationPathMarkers =
    {
        "/LgymApi.Application/Reporting/",
        "/LgymApi.Application/Features/Reporting/"
    };

    private static readonly string[] NotificationsApplicationPathMarkers =
    {
        "/LgymApi.Application/Notifications/"
    };

    private static readonly string[] PlatformApplicationPathMarkers =
    {
        "/LgymApi.Application/BuildingBlocks/",
        "/LgymApi.Application/Platform/",
        "/LgymApi.Application/Common/",
        "/LgymApi.Application/Repositories/",
        "/LgymApi.Application/Services/",
        "/LgymApi.Application/Abstractions/",
        "/LgymApi.Application/Mapping/",
        "/LgymApi.Application/Properties/"
    };

    private static readonly string[] PlatformApplicationExactFiles =
    {
        "/LgymApi.Application/Services/ITokenService.cs",
        "/LgymApi.Application/Services/ILegacyPasswordService.cs",
        "/LgymApi.Application/Services/IUserSessionStore.cs",
        "/LgymApi.Application/Services/IGoogleTokenValidator.cs",
        "/LgymApi.Application/Services/GoogleTokenPayload.cs",
        "/LgymApi.Application/Services/RankService.cs",
        "/LgymApi.Application/ServiceCollectionExtensions.cs"
    };

    private static readonly Dictionary<string, string> ApplicationExactFileModuleMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/LgymApi.Application/Repositories/IUserRepository.cs"] = IdentityModuleName,
        ["/LgymApi.Application/Repositories/IUserExternalLoginRepository.cs"] = IdentityModuleName,
        ["/LgymApi.Application/Repositories/IPasswordResetTokenRepository.cs"] = IdentityModuleName,
        ["/LgymApi.Application/Repositories/IRoleRepository.cs"] = IdentityModuleName,
        ["/LgymApi.Application/Repositories/IEloRegistryRepository.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Application/Repositories/ITutorialProgressRepository.cs"] = IdentityModuleName,
        ["/LgymApi.Application/Repositories/IInAppNotificationRepository.cs"] = NotificationsModuleName,
        ["/LgymApi.Application/Notifications/Repositories/IPushInstallationRepository.cs"] = NotificationsModuleName,
        ["/LgymApi.Application/Repositories/IPushNotificationMessageRepository.cs"] = NotificationsModuleName,
        ["/LgymApi.Application/Repositories/IReportingRepository.cs"] = ReportingModuleName,
        ["/LgymApi.Application/Repositories/IRecurringReportAssignmentRepository.cs"] = ReportingModuleName,
        ["/LgymApi.Application/Repositories/IPlanRepository.cs"] = TrainingPlanningModuleName,
        ["/LgymApi.Application/Repositories/IPlanDayRepository.cs"] = TrainingPlanningModuleName,
        ["/LgymApi.Application/Repositories/IPlanDayExerciseRepository.cs"] = TrainingPlanningModuleName,
        ["/LgymApi.Application/Repositories/IGymRepository.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Application/Repositories/ITrainingRepository.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Application/Repositories/IExerciseRepository.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Application/Repositories/IExerciseScoreRepository.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Application/Repositories/ITrainingExerciseScoreRepository.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Application/Repositories/IMeasurementRepository.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Application/Repositories/IMainRecordRepository.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Application/Nutrition/Persistence/IDietPlanPersistence.cs"] = NutritionModuleName,
        ["/LgymApi.Application/Nutrition/Persistence/ISupplementationPersistence.cs"] = NutritionModuleName,
        ["/LgymApi.Application/Repositories/IAppConfigRepository.cs"] = PlatformModuleName,
        ["/LgymApi.Application/Repositories/ICommandEnvelopeRepository.cs"] = PlatformModuleName,
        ["/LgymApi.Application/Repositories/IApiIdempotencyRecordRepository.cs"] = PlatformModuleName,
        ["/LgymApi.Application/Repositories/IEmailNotificationLogRepository.cs"] = NotificationsModuleName,
        ["/LgymApi.Application/Repositories/IEmailNotificationSubscriptionRepository.cs"] = NotificationsModuleName,
        ["/LgymApi.Application/Repositories/IUnitOfWork.cs"] = PlatformModuleName,
        ["/LgymApi.Application/Abstractions/Storage/IPhotoStorageProvider.cs"] = ReportingModuleName
    };

    private static readonly Dictionary<string, string> InfrastructureExactFileModuleMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/LgymApi.Infrastructure/ServiceCollectionExtensions.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/PlatformServiceCollectionExtensions.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/IdentityServiceCollectionExtensions.cs"] = IdentityModuleName,
        ["/LgymApi.Infrastructure/TrainingPlanningServiceCollectionExtensions.cs"] = TrainingPlanningModuleName,
        ["/LgymApi.Infrastructure/WorkoutProgressServiceCollectionExtensions.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Infrastructure/CoachingServiceCollectionExtensions.cs"] = CoachingModuleName,
        ["/LgymApi.Infrastructure/NutritionServiceCollectionExtensions.cs"] = NutritionModuleName,
        ["/LgymApi.Infrastructure/ReportingServiceCollectionExtensions.cs"] = ReportingModuleName,
        ["/LgymApi.Infrastructure/NotificationsServiceCollectionExtensions.cs"] = NotificationsModuleName,
        ["/LgymApi.Infrastructure/Repositories/UserRepository.cs"] = IdentityModuleName,
        ["/LgymApi.Infrastructure/Repositories/UserExternalLoginRepository.cs"] = IdentityModuleName,
        ["/LgymApi.Infrastructure/Repositories/PasswordResetTokenRepository.cs"] = IdentityModuleName,
        ["/LgymApi.Infrastructure/Repositories/RoleRepository.cs"] = IdentityModuleName,
        ["/LgymApi.Infrastructure/Repositories/EloRegistryRepository.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Infrastructure/Repositories/TutorialProgressRepository.cs"] = IdentityModuleName,
        ["/LgymApi.Infrastructure/Repositories/PlanRepository.cs"] = TrainingPlanningModuleName,
        ["/LgymApi.Infrastructure/Repositories/PlanRepository.Clone.cs"] = TrainingPlanningModuleName,
        ["/LgymApi.Infrastructure/Repositories/PlanDayRepository.cs"] = TrainingPlanningModuleName,
        ["/LgymApi.Infrastructure/Repositories/PlanDayExerciseRepository.cs"] = TrainingPlanningModuleName,
        ["/LgymApi.Infrastructure/Repositories/ExerciseRepository.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Infrastructure/Repositories/TrainingRepository.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Infrastructure/Repositories/TrainingExerciseScoreRepository.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Infrastructure/Repositories/ExerciseScoreRepository.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Infrastructure/Repositories/MeasurementRepository.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Infrastructure/Repositories/GymRepository.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Infrastructure/Repositories/MainRecordRepository.cs"] = WorkoutProgressModuleName,
        ["/LgymApi.Infrastructure/Repositories/Nutrition/DietPlanPersistenceRepository.cs"] = NutritionModuleName,
        ["/LgymApi.Infrastructure/Repositories/Nutrition/SupplementationPersistenceRepository.cs"] = NutritionModuleName,
        ["/LgymApi.Infrastructure/Repositories/ReportingRepository.cs"] = ReportingModuleName,
        ["/LgymApi.Infrastructure/Repositories/RecurringReportAssignmentRepository.cs"] = ReportingModuleName,
        ["/LgymApi.Infrastructure/Repositories/InAppNotificationRepository.cs"] = NotificationsModuleName,
        ["/LgymApi.Infrastructure/Repositories/PushInstallationRepository.cs"] = NotificationsModuleName,
        ["/LgymApi.Infrastructure/Repositories/PushNotificationMessageRepository.cs"] = NotificationsModuleName,
        ["/LgymApi.Infrastructure/Repositories/ReferenceData/AppConfigRepository.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/Repositories/CommandEnvelopeRepository.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/Repositories/ApiIdempotencyRecordRepository.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/Repositories/EmailNotificationLogRepository.cs"] = NotificationsModuleName,
        ["/LgymApi.Infrastructure/Repositories/EmailNotificationSubscriptionRepository.cs"] = NotificationsModuleName,
        ["/LgymApi.Infrastructure/Services/TokenService.cs"] = IdentityModuleName,
        ["/LgymApi.Infrastructure/Services/GoogleTokenValidator.cs"] = IdentityModuleName,
        ["/LgymApi.Infrastructure/Services/LegacyPasswordService.cs"] = IdentityModuleName,
        ["/LgymApi.Infrastructure/Services/UserSessionStore.cs"] = IdentityModuleName,
        ["/LgymApi.Infrastructure/Services/LocalPhotoStorageProvider.cs"] = ReportingModuleName,
        ["/LgymApi.Infrastructure/Services/CloudflareR2PhotoStorageProvider.cs"] = ReportingModuleName,
        ["/LgymApi.Infrastructure/Services/DbPhotoUploadInitTracker.cs"] = ReportingModuleName,
        ["/LgymApi.Infrastructure/Services/InMemoryPhotoUploadInitTracker.cs"] = ReportingModuleName,
        ["/LgymApi.Infrastructure/Services/LocalPhotoDevelopmentStore.cs"] = ReportingModuleName,
        ["/LgymApi.Infrastructure/Services/FcmPushSender.cs"] = NotificationsModuleName,
        ["/LgymApi.Infrastructure/Services/HangfirePushBackgroundScheduler.cs"] = NotificationsModuleName,
        ["/LgymApi.Infrastructure/Services/NoOpPushBackgroundScheduler.cs"] = NotificationsModuleName,
        ["/LgymApi.Infrastructure/Services/PushInstallationCleanupSettings.cs"] = NotificationsModuleName,
        ["/LgymApi.Infrastructure/Services/CommittedIntentDispatcher.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/Services/TrainerInvitationEmailTemplateComposer.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/Services/TrainerInvitationAcceptedEmailTemplateComposer.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/Services/TrainerInvitationRevokedEmailTemplateComposer.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/Services/TrainingCompletedEmailTemplateComposer.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/Services/WelcomeEmailTemplateComposer.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/Services/PasswordRecoveryEmailTemplateComposer.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/Services/EmailTemplateComposerFactory.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/Services/EmailTemplateComposerBase.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/Services/EmailNotificationsFeature.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/Services/EmailMetrics.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/Services/SmtpEmailSender.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/Services/DummyEmailSender.cs"] = PlatformModuleName,
        ["/LgymApi.Infrastructure/Services/LegacyPasswordConstants.cs"] = PlatformModuleName
    };

    /// <summary>
    /// Resolves the solution root directory by walking up from the current test execution directory
    /// until it finds a file named "LgymApi.sln".
    /// </summary>
    /// <returns>The absolute path to the solution root directory.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the repository root cannot be located.</exception>
    public static string ResolveRepositoryRoot()
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

    /// <summary>
    /// Resolves all assembly metadata references currently loaded in the AppDomain.
    /// Used to populate Roslyn compilation metadata for semantic analysis.
    /// </summary>
    /// <returns>A list of MetadataReference objects for all non-dynamic assemblies.</returns>
    public static List<MetadataReference> ResolveMetadataReferences()
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(assembly => !assembly.IsDynamic)
            .Select(assembly => assembly.Location)
            .Where(location => !string.IsNullOrWhiteSpace(location) && File.Exists(location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToList();
    }

    /// <summary>
    /// Checks whether a file path is located in build artifacts (bin or obj directories).
    /// Used to exclude compiled output from source analysis.
    /// </summary>
    /// <param name="path">The file path to check.</param>
    /// <returns>True if the path is in bin or obj; false otherwise.</returns>
    public static bool IsInBuildArtifacts(string path)
    {
        var normalized = NormalizePath(path);
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    public static IReadOnlyList<ProjectReferenceEdge> ParseProjectReferences(string projectFilePath)
    {
        return ParseProjectReferences(projectFilePath, XDocument.Load(projectFilePath));
    }

    public static IReadOnlyList<ProjectReferenceEdge> ParseProjectReferences(string projectFilePath, string projectXml)
    {
        return ParseProjectReferences(projectFilePath, XDocument.Parse(projectXml));
    }

    private static IReadOnlyList<ProjectReferenceEdge> ParseProjectReferences(string projectFilePath, XDocument document)
    {
        var normalizedProjectPath = NormalizePath(Path.GetFullPath(projectFilePath));
        var sourceProject = Path.GetFileNameWithoutExtension(normalizedProjectPath);
        var projectDirectory = Path.GetDirectoryName(normalizedProjectPath)!;

        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => NormalizePath(Path.GetFullPath(include!, projectDirectory)))
            .Select(targetPath => new ProjectReferenceEdge(
                sourceProject,
                Path.GetFileNameWithoutExtension(targetPath),
                normalizedProjectPath,
                targetPath))
            .ToList();
    }

    public static IReadOnlyList<string> GetCanonicalModuleCatalog() => CanonicalModuleCatalog;

    public static bool TryGetPersistedEntityOwner(Type entityType, out string ownerModule)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        return TryGetPersistedEntityOwner(entry => entry.EntityType == entityType, out ownerModule);
    }

    public static bool TryGetPersistedEntityOwner(string entityTypeName, out string ownerModule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityTypeName);

        var normalizedEntityTypeName = entityTypeName.Replace("global::", string.Empty, StringComparison.Ordinal);
        return TryGetPersistedEntityOwner(
            entry => string.Equals(entry.EntityType.FullName, normalizedEntityTypeName, StringComparison.Ordinal) ||
                     string.Equals(entry.EntityType.Name, normalizedEntityTypeName, StringComparison.Ordinal),
            out ownerModule);
    }

    public static bool TryGetPersistedEntityOwner(INamedTypeSymbol entityType, out string ownerModule)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        var metadataName = entityType.OriginalDefinition
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty, StringComparison.Ordinal);
        return TryGetPersistedEntityOwner(entry =>
            string.Equals(entry.EntityType.FullName, metadataName, StringComparison.Ordinal), out ownerModule);
    }

    private static bool TryGetPersistedEntityOwner(
        Func<PersistedEntityOwnership, bool> matchesEntity,
        out string ownerModule)
    {
        var ownership = PersistedEntityOwnershipCatalog.Entries.SingleOrDefault(matchesEntity);
        if (ownership == null)
        {
            ownerModule = string.Empty;
            return false;
        }

        ownerModule = ownership.Owner;
        return true;
    }

    public static bool IsApiFeatureLeafFilePath(string path, string expectedLeafFolder)
    {
        if (!TryGetApiFeatureLeafFolder(path, out var leafFolder))
        {
            return false;
        }

        return string.Equals(leafFolder, expectedLeafFolder, StringComparison.Ordinal);
    }

    public static bool TryGetApiFeatureLeafFolder(string path, out string? leafFolder)
    {
        leafFolder = null;

        var normalized = NormalizePath(path);
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4)
        {
            return false;
        }

        var featuresIndex = Array.FindIndex(segments, segment => segment.Equals("Features", StringComparison.OrdinalIgnoreCase));
        if (featuresIndex < 0)
        {
            return false;
        }

        var leafFolderIndex = segments.Length - 2;
        if (leafFolderIndex <= featuresIndex + 1)
        {
            return false;
        }

        var candidate = segments[leafFolderIndex];
        if (!ApiFeatureLeafFolders.Contains(candidate, StringComparer.Ordinal))
        {
            return false;
        }

        leafFolder = candidate;
        return true;
    }

    public static bool IsTestProjectPath(string path)
    {
        var normalized = NormalizePath(path);

        return TestProjectPathMarkers.Any(marker => normalized.Contains(marker, StringComparison.OrdinalIgnoreCase))
            || normalized.Contains("/tests/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsHelperPath(string path)
    {
        var normalized = NormalizePath(path);
        return HelperPathMarkers.Any(marker => normalized.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsGeneratedCodePath(string path)
    {
        var normalized = NormalizePath(path);
        var fileName = Path.GetFileName(normalized);

        return fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/Generated/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsExcludedFromModuleBoundaryAnalysis(string path)
    {
        return ClassifyModuleBoundaryFile(path).IsExcluded;
    }

    public static ModuleBoundaryFileClassification ClassifyModuleBoundaryFile(string path)
    {
        var repoRoot = ResolveRepositoryRoot();
        return ClassifyModuleBoundaryFile(path, repoRoot);
    }

    public static ModuleBoundaryFileClassification ClassifyModuleBoundaryFile(string path, string repositoryRoot)
    {
        var normalizedPath = NormalizePath(path);
        var relativePath = NormalizePath(Path.GetRelativePath(repositoryRoot, path));
        var moduleName = GetModuleNameFromPath(path);

        ModuleBoundaryExclusionKind? exclusionKind = null;
        if (IsInBuildArtifacts(path))
        {
            exclusionKind = ModuleBoundaryExclusionKind.BuildArtifact;
        }
        else if (IsTestProjectPath(path))
        {
            exclusionKind = ModuleBoundaryExclusionKind.TestProject;
        }
        else if (IsHelperPath(path))
        {
            exclusionKind = ModuleBoundaryExclusionKind.Helper;
        }
        else if (IsGeneratedCodePath(path))
        {
            exclusionKind = ModuleBoundaryExclusionKind.GeneratedCode;
        }

        return new ModuleBoundaryFileClassification(
            path,
            relativePath,
            normalizedPath,
            moduleName,
            GetPlatformSubBoundaryFromPath(path),
            exclusionKind);
    }

    /// <summary>
    /// Enumerates all source files for a project tree, excluding build artifacts.
    /// </summary>
    public static IReadOnlyList<string> EnumerateProjectSourceFiles(string projectRelativePath, string searchPattern = "*.cs")
    {
        var repoRoot = ResolveRepositoryRoot();
        var projectRoot = Path.Combine(repoRoot, projectRelativePath);

        return Directory
            .EnumerateFiles(projectRoot, searchPattern, SearchOption.AllDirectories)
            .Where(path => !IsInBuildArtifacts(path))
            .ToList();
    }

    public static IReadOnlyList<string> EnumerateProductionSourceFiles(params string[] projectRelativePaths)
    {
        var repoRoot = ResolveRepositoryRoot();

        return projectRelativePaths
            .SelectMany(projectRelativePath => EnumerateProjectSourceFiles(projectRelativePath))
            .Where(path => !ClassifyModuleBoundaryFile(path, repoRoot).IsExcluded)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Resolves the module name for a ServiceCollectionExtensions file.
    /// Returns null for the project-root composition shims.
    /// </summary>
    public static string? GetServiceCollectionModuleName(string serviceCollectionExtensionsPath)
    {
        var fileName = Path.GetFileName(serviceCollectionExtensionsPath);
        const string suffix = "ServiceCollectionExtensions.cs";

        if (!fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }

        if (!fileName.Equals(suffix, StringComparison.Ordinal))
        {
            return NormalizeModuleName(fileName[..^suffix.Length]);
        }

        var parentDirectory = Path.GetFileName(Path.GetDirectoryName(serviceCollectionExtensionsPath));
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            return null;
        }

        return parentDirectory is "LgymApi.Application" or "LgymApi.Infrastructure" ? null : NormalizeModuleName(parentDirectory);
    }

    /// <summary>
    /// Resolves the module folder name for an EF Core configuration file under
    /// LgymApi.Infrastructure/Data/Configurations/&lt;Module&gt;/... .
    /// Returns null for files outside the explicit module-owned configuration tree.
    /// </summary>
    public static string? GetInfrastructureConfigurationModuleName(string configurationPath)
    {
        var normalized = NormalizePath(configurationPath);
        const string marker = "/Data/Configurations/";

        var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var remainder = normalized[(markerIndex + marker.Length)..];
        var segments = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        return NormalizeModuleName(segments[0]);
    }

    public static string? GetModuleNameFromPath(string path)
    {
        var normalized = NormalizePath(path);
        var serviceCollectionModule = GetServiceCollectionModuleName(normalized);
        if (!string.IsNullOrWhiteSpace(serviceCollectionModule))
        {
            return serviceCollectionModule;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        var featureIndex = Array.FindIndex(segments, segment => segment.Equals("Features", StringComparison.OrdinalIgnoreCase));
        if (featureIndex >= 0 && featureIndex + 1 < segments.Length)
        {
            return NormalizeModuleName(segments[featureIndex + 1]);
        }

        var projectIndex = Array.FindIndex(segments, segment => segment.StartsWith("LgymApi.", StringComparison.OrdinalIgnoreCase));
        if (projectIndex < 0 || projectIndex + 1 >= segments.Length)
        {
            return null;
        }

        var candidate = segments[projectIndex + 1];
        return SharedProjectSegments.Contains(candidate, StringComparer.OrdinalIgnoreCase)
            ? null
            : NormalizeModuleName(candidate);
    }

    public static string? GetCanonicalModuleNameFromPath(string path)
    {
        var normalized = NormalizePath(path);

        if (ApplicationExactFileModuleMap.TryGetValue(GetApplicationExactFileKey(normalized), out var applicationExactFileModule))
        {
            return applicationExactFileModule;
        }

        if (TryGetApplicationCanonicalModuleName(normalized, out var applicationModuleName))
        {
            return applicationModuleName;
        }

        if (TryGetInfrastructureCanonicalModuleName(normalized, out var infrastructureModuleName))
        {
            return infrastructureModuleName;
        }

        return null;
    }

    public static PlatformSubBoundary? GetPlatformSubBoundaryFromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = NormalizePath(path);
        var applicationPath = GetApplicationExactFileKey(normalizedPath);

        if (MatchesAny(normalizedPath, BuildingBlocksPathMarkers)
            || BuildingBlocksExactFiles.Contains(applicationPath, StringComparer.OrdinalIgnoreCase))
        {
            return PlatformSubBoundary.BuildingBlocks;
        }

        if (MatchesAny(normalizedPath, ReferenceDataPathMarkers)
            || ReferenceDataExactFiles.Contains(applicationPath, StringComparer.OrdinalIgnoreCase))
        {
            return PlatformSubBoundary.ReferenceData;
        }

        if (MatchesAny(normalizedPath, TechnicalPlatformPathMarkers)
            || TechnicalPlatformExactFiles.Contains(applicationPath, StringComparer.OrdinalIgnoreCase))
        {
            return PlatformSubBoundary.TechnicalPlatform;
        }

        return null;
    }

    public static void ValidateCanonicalModuleCatalog(IEnumerable<string> moduleNames)
    {
        ArgumentNullException.ThrowIfNull(moduleNames);

        var proposedCatalog = moduleNames.ToList();
        if (!proposedCatalog.SequenceEqual(CanonicalModuleCatalog, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The canonical module catalog must contain exactly the established eight modules in canonical order.");
        }
    }

    public static string? GetCanonicalModuleNameForSymbol(ISymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        var path = symbol.Locations
            .Where(location => location.IsInSource)
            .Select(location => location.SourceTree?.FilePath)
            .FirstOrDefault(filePath => !string.IsNullOrWhiteSpace(filePath));

        return string.IsNullOrWhiteSpace(path) ? null : GetCanonicalModuleNameFromPath(path);
    }

    public static INamedTypeSymbol? GetOwnedNamedTypeSymbol(ISymbol? symbol)
    {
        if (symbol == null)
        {
            return null;
        }

        return symbol switch
        {
            INamedTypeSymbol namedTypeSymbol => namedTypeSymbol,
            IMethodSymbol methodSymbol when methodSymbol.MethodKind == MethodKind.Constructor => methodSymbol.ContainingType,
            _ => symbol.ContainingType
        };
    }

    public static string NormalizeModuleName(string moduleName)
    {
        return moduleName.Trim();
    }

    /// <summary>
    /// Parses all C# source files in a given project directory and returns their syntax trees.
    /// Filters out build artifacts automatically.
    /// </summary>
    /// <param name="projectRelativePath">Relative path from repository root to the project directory (e.g., "LgymApi.Api").</param>
    /// <returns>A list of SyntaxTree objects for all source files in the project.</returns>
    public static List<SyntaxTree> ParseProjectSources(string projectRelativePath)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

        var sourceFiles = EnumerateProjectSourceFiles(projectRelativePath);

        var syntaxTrees = sourceFiles
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), options: parseOptions, path: path))
            .ToList();

        return syntaxTrees;
    }

    public static (string RepoRoot, CSharpCompilation Compilation, IReadOnlyList<SyntaxTree> SyntaxTrees) PrepareCompilation(params string[] projectRelativePaths)
    {
        var repoRoot = ResolveRepositoryRoot();
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var sourceFiles = EnumerateProductionSourceFiles(projectRelativePaths);

        var syntaxTrees = sourceFiles
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), options: parseOptions, path: path))
            .ToList();

        var compilation = CreateCompilation(syntaxTrees);
        return (repoRoot, compilation, syntaxTrees);
    }

    public static void AssertNoUnexpectedModuleBoundaryViolations(string guardId, IEnumerable<ModuleBoundaryObservedViolation> observedViolations)
    {
        ModuleBoundaryDebtAllowlistRegistry.AssertNoUnexpectedViolations(guardId, observedViolations);
    }

    /// <summary>
    /// Creates a CSharpCompilation from a list of syntax trees with full metadata references.
    /// Suitable for semantic analysis requiring type symbol resolution.
    /// </summary>
    /// <param name="trees">The syntax trees to include in the compilation.</param>
    /// <returns>A CSharpCompilation object with metadata references populated.</returns>
    public static CSharpCompilation CreateCompilation(List<SyntaxTree> trees)
    {
        return CSharpCompilation.Create(
            "ArchitectureGuardCompilation",
            trees,
            ResolveMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static bool TryGetApplicationCanonicalModuleName(string normalizedPath, out string? moduleName)
    {
        moduleName = null;

        if (MatchesAny(normalizedPath, IdentityApplicationPathMarkers))
        {
            moduleName = IdentityModuleName;
            return true;
        }

        if (MatchesAny(normalizedPath, TrainingPlanningApplicationPathMarkers))
        {
            moduleName = TrainingPlanningModuleName;
            return true;
        }

        if (MatchesAny(normalizedPath, WorkoutProgressApplicationPathMarkers))
        {
            moduleName = WorkoutProgressModuleName;
            return true;
        }

        if (MatchesAny(normalizedPath, CoachingApplicationPathMarkers))
        {
            moduleName = CoachingModuleName;
            return true;
        }

        if (MatchesAny(normalizedPath, NutritionApplicationPathMarkers))
        {
            moduleName = NutritionModuleName;
            return true;
        }

        if (MatchesAny(normalizedPath, ReportingApplicationPathMarkers))
        {
            moduleName = ReportingModuleName;
            return true;
        }

        if (MatchesAny(normalizedPath, NotificationsApplicationPathMarkers))
        {
            moduleName = NotificationsModuleName;
            return true;
        }

        if (MatchesAny(normalizedPath, PlatformApplicationPathMarkers) || MatchesAny(normalizedPath, PlatformApplicationExactFiles))
        {
            moduleName = PlatformModuleName;
            return true;
        }

        return false;
    }

    private static bool TryGetInfrastructureCanonicalModuleName(string normalizedPath, out string? moduleName)
    {
        moduleName = null;

        if (InfrastructureExactFileModuleMap.TryGetValue(GetInfrastructureExactFileKey(normalizedPath), out var exactFileModule))
        {
            moduleName = exactFileModule;
            return true;
        }

        if (normalizedPath.Contains("/LgymApi.Infrastructure/Data/Configurations/Identity/", StringComparison.OrdinalIgnoreCase))
        {
            moduleName = IdentityModuleName;
            return true;
        }

        if (normalizedPath.Contains("/LgymApi.Infrastructure/Data/Configurations/TrainingPlanning/", StringComparison.OrdinalIgnoreCase))
        {
            moduleName = TrainingPlanningModuleName;
            return true;
        }

        if (normalizedPath.Contains("/LgymApi.Infrastructure/Data/Configurations/WorkoutProgress/", StringComparison.OrdinalIgnoreCase))
        {
            moduleName = WorkoutProgressModuleName;
            return true;
        }

        if (normalizedPath.Contains("/LgymApi.Infrastructure/Data/Configurations/Coaching/", StringComparison.OrdinalIgnoreCase))
        {
            moduleName = CoachingModuleName;
            return true;
        }

        if (normalizedPath.Contains("/LgymApi.Infrastructure/Data/Configurations/Nutrition/", StringComparison.OrdinalIgnoreCase))
        {
            moduleName = NutritionModuleName;
            return true;
        }

        if (normalizedPath.Contains("/LgymApi.Infrastructure/Data/Configurations/Reporting/", StringComparison.OrdinalIgnoreCase))
        {
            moduleName = ReportingModuleName;
            return true;
        }

        if (normalizedPath.Contains("/LgymApi.Infrastructure/Data/Configurations/Notifications/", StringComparison.OrdinalIgnoreCase))
        {
            moduleName = NotificationsModuleName;
            return true;
        }

        if (normalizedPath.Contains("/LgymApi.Infrastructure/Data/Configurations/Platform/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/LgymApi.Infrastructure/Data/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/LgymApi.Infrastructure/Options/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/LgymApi.Infrastructure/Pagination/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/LgymApi.Infrastructure/UnitOfWork/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/LgymApi.Infrastructure/Extensions/", StringComparison.OrdinalIgnoreCase))
        {
            moduleName = PlatformModuleName;
            return true;
        }

        return false;
    }

    private static bool MatchesAny(string normalizedPath, IEnumerable<string> markers)
    {
        return markers.Any(marker => normalizedPath.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetInfrastructureExactFileKey(string normalizedPath)
    {
        var startIndex = normalizedPath.IndexOf("/LgymApi.Infrastructure/", StringComparison.OrdinalIgnoreCase);
        return startIndex >= 0 ? normalizedPath[startIndex..] : normalizedPath;
    }

    private static string GetApplicationExactFileKey(string normalizedPath)
    {
        var startIndex = normalizedPath.IndexOf("/LgymApi.Application/", StringComparison.OrdinalIgnoreCase);
        return startIndex >= 0 ? normalizedPath[startIndex..] : normalizedPath;
    }
}

public enum ModuleBoundaryExclusionKind
{
    BuildArtifact,
    TestProject,
    Helper,
    GeneratedCode
}

public enum PlatformSubBoundary
{
    BuildingBlocks,
    TechnicalPlatform,
    ReferenceData
}

public sealed record ModuleBoundaryFileClassification(
    string FilePath,
    string RelativePath,
    string NormalizedPath,
    string? ModuleName,
    PlatformSubBoundary? PlatformSubBoundary,
    ModuleBoundaryExclusionKind? ExclusionKind)
{
    public bool IsExcluded => ExclusionKind.HasValue;

    public bool IsProductionCode => !IsExcluded;
}

public sealed record ProjectReferenceEdge(
    string SourceProject,
    string TargetProject,
    string SourceProjectPath,
    string TargetProjectPath);
