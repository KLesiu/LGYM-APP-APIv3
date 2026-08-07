using System.IO.Compression;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ModulePublicSurfaceGuardTests
{
    private const int ManifestEntryCount = 775;

    private static readonly Lazy<IReadOnlySet<SurfaceEntry>> AllowedSurfaceStore = new(ReadManifest);

    private static IReadOnlySet<SurfaceEntry> AllowedSurface => AllowedSurfaceStore.Value;

    private static readonly IReadOnlySet<string> ForbiddenMetadataNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "LgymApi.Application.Identity.Profile.UserProfileService",
        "LgymApi.Application.Identity.Profile.UserProfileServiceDependencies",
        "LgymApi.Application.Repositories.IUserRepository",
        "LgymApi.Application.Repositories.IPushNotificationMessageRepository",
        "LgymApi.Infrastructure.Options.EmailOptions",
        "LgymApi.Notifications.EmailServiceCollectionExtensions",
        "LgymApi.Infrastructure.Services.SmtpEmailSender",
        "LgymApi.Application.Notifications.Providers.Fcm.PushNotificationOptions.FcmOptions"
    };

    private static readonly string[] ManifestPayload = [
        "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"
    ];

    private static readonly string[] ReviewedManifestRows =
    [
        "LgymApi.Application/Features/Reporting/RecurringReportAssignmentService.Processing.cs\tLgymApi.Application.Features.Reporting.RecurringReportAssignmentService",
        "LgymApi.Application/Features/Reporting/RecurringReportAssignmentService.RequestNow.cs\tLgymApi.Application.Features.Reporting.RecurringReportAssignmentService",
        "LgymApi.Application/Reporting/Errors/ReportingErrors.cs\tLgymApi.Application.Reporting.Errors.ReportingConflictError",
        "LgymApi.Platform/Repositories/IActorRowSecurityScopeFactory.cs\tLgymApi.Application.Repositories.IActorRowSecurityScopeFactory"
    ];


    [Test]
    public void Observed_Public_Module_Types_Should_Exactly_Match_The_Closed_Manifest()
    {
        var scan = ModuleBoundaryProductionScan.Prepare();
        var observed = CollectPublicSurfaceEntries(scan.RepoRoot, scan.Compilation, scan.SyntaxTrees).ToHashSet();
        var missingFromManifest = observed.Except(AllowedSurface).OrderBy(FormatEntry, StringComparer.Ordinal).ToArray();
        var staleManifestEntries = AllowedSurface.Except(observed).OrderBy(FormatEntry, StringComparer.Ordinal).ToArray();

        TestContext.Progress.WriteLine(
            $"Module public-surface scan: {scan.DescribeSourceTreeCounts()}; observed={observed.Count}; manifest={AllowedSurface.Count}; missing={missingFromManifest.Length}; stale={staleManifestEntries.Length}.");
        Assert.Multiple(() =>
        {
            Assert.That(
                missingFromManifest,
                Is.Empty,
                $"Observed public module declarations missing from the closed manifest:{Environment.NewLine}{FormatEntries(missingFromManifest)}");
            Assert.That(
                staleManifestEntries,
                Is.Empty,
                $"Closed manifest entries absent from the observed public module declarations:{Environment.NewLine}{FormatEntries(staleManifestEntries)}");
        });
    }

    [Test]
    public void Observed_Public_Module_Types_Should_Not_Contain_Exact_Forbidden_Implementation_Metadata_Names()
    {
        var scan = ModuleBoundaryProductionScan.Prepare();
        var violations = CollectPublicSurfaceEntries(scan.RepoRoot, scan.Compilation, scan.SyntaxTrees)
            .Where(entry => ForbiddenMetadataNames.Contains(entry.MetadataName))
            .OrderBy(FormatEntry, StringComparer.Ordinal)
            .ToArray();

        Assert.That(violations, Is.Empty, FormatEntries(violations));
    }

    [TestCase("LgymApi.Platform/Contracts/ActorReference.cs", "LgymApi.Platform.Contracts.ActorReference")]
    [TestCase("LgymApi.Platform/Contracts/BackgroundCommands/ICommandEnvelopeRuntime.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands.ICommandEnvelopeRuntime")]
    [TestCase("LgymApi.Platform/Contracts/BackgroundCommands/ICommandEnvelopeRuntime.cs", "LgymApi.Application.Platform.Contracts.BackgroundCommands.CommandEnvelopeRequest")]
    [TestCase("LgymApi.Platform/PlatformModule.cs", "LgymApi.Platform.PlatformModule")]
    [TestCase("LgymApi.Identity/IdentityModule.cs", "LgymApi.Identity.IdentityModule")]
    [TestCase("LgymApi.TrainingPlanning/TrainingPlanningModule.cs", "LgymApi.TrainingPlanning.TrainingPlanningModule")]
    [TestCase("LgymApi.Notifications/ServiceCollectionExtensions.cs", "LgymApi.Application.Notifications.NotificationsModule")]
    public void Known_Runtime_Scalar_Contracts_And_Registration_Facades_Are_Explicitly_Manifested(
        string relativePath,
        string metadataName)
    {
        Assert.That(AllowedSurface, Does.Contain(new SurfaceEntry(relativePath, metadataName)));
    }

    [TestCase(
        "LgymApi.Identity/Contracts/UnlistedRecord.cs",
        "namespace LgymApi.Identity.Contracts; public sealed record UnlistedRecord(string Value);")]
    [TestCase(
        "LgymApi.Identity/Contracts/IUnlistedContract.cs",
        "namespace LgymApi.Identity.Contracts; public interface IUnlistedContract { }")]
    [TestCase(
        "LgymApi.Identity/Models/UnlistedModel.cs",
        "namespace LgymApi.Identity.Models; public sealed class UnlistedModel { }")]
    [TestCase(
        "LgymApi.Identity/Services/UnlistedService.cs",
        "namespace LgymApi.Identity.Services; public sealed class UnlistedService { }")]
    [TestCase(
        "LgymApi.Identity/Factories/UnlistedFactory.cs",
        "namespace LgymApi.Identity.Factories; public sealed class UnlistedFactory { }")]
    [TestCase(
        "LgymApi.Identity/Contracts/UnlistedFacade.cs",
        "namespace LgymApi.Identity.Contracts; public static class UnlistedFacade { }")]
    public void Unlisted_Public_Record_Interface_Model_Suffix_And_Facade_Fixtures_Are_Rejected(
        string relativePath,
        string source)
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var tree = CSharpSyntaxTree.ParseText(source, path: Path.Combine(repoRoot, relativePath));
        var compilation = ArchitectureTestHelpers.CreateCompilation([tree]);

        var unlistedEntries = CollectPublicSurfaceEntries(repoRoot, compilation, [tree])
            .Except(AllowedSurface)
            .ToArray();

        Assert.That(unlistedEntries, Has.Exactly(1).Items, FormatEntries(unlistedEntries));
    }

    [TestCase(
        "LgymApi.Identity/Profile/UserProfileService.cs",
        "namespace LgymApi.Application.Identity.Profile; public sealed class UserProfileService { }")]
    [TestCase(
        "LgymApi.Identity/Profile/UserProfileServiceDependencies.cs",
        "namespace LgymApi.Application.Identity.Profile; public sealed class UserProfileServiceDependencies { }")]
    [TestCase(
        "LgymApi.Identity/Repositories/IUserRepository.cs",
        "namespace LgymApi.Application.Repositories; public interface IUserRepository { }")]
    [TestCase(
        "LgymApi.Notifications/EmailTemplates/Options/EmailOptions.cs",
        "namespace LgymApi.Infrastructure.Options; public sealed class EmailOptions { }")]
    [TestCase(
        "LgymApi.Notifications/EmailTemplates/EmailServiceCollectionExtensions.cs",
        "namespace LgymApi.Notifications; public static class EmailServiceCollectionExtensions { }")]
    [TestCase(
        "LgymApi.Notifications/Providers/Fcm/PushNotificationOptions.cs",
        "namespace LgymApi.Application.Notifications.Providers.Fcm; public sealed class PushNotificationOptions { public sealed class FcmOptions { } }")]
    public void Forbidden_Public_Category_Fixtures_Are_Rejected(string relativePath, string source)
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var tree = CSharpSyntaxTree.ParseText(source, path: Path.Combine(repoRoot, relativePath));
        var compilation = ArchitectureTestHelpers.CreateCompilation([tree]);
        var violations = CollectPublicSurfaceEntries(repoRoot, compilation, [tree])
            .Where(entry => ForbiddenMetadataNames.Contains(entry.MetadataName))
            .ToArray();

        Assert.That(violations, Has.Exactly(1).Items, FormatEntries(violations));
    }

    [Test]
    public void Public_Nested_Type_In_Nonpublic_Containing_Type_Is_Not_Exported()
    {
        const string source = "namespace Fixture; internal sealed class InternalProvider { public sealed class Settings { } }";
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var tree = CSharpSyntaxTree.ParseText(source, path: Path.Combine(repoRoot, "LgymApi.Notifications/Providers/Fixture.cs"));
        var compilation = ArchitectureTestHelpers.CreateCompilation([tree]);

        Assert.That(CollectPublicSurfaceEntries(repoRoot, compilation, [tree]), Is.Empty);
    }

    [Test]
    public void Closed_Manifest_Rejects_Duplicate_Entries_Before_Set_Construction()
    {
        var duplicateEntry = "LgymApi.Application/Fixture.cs\tLgymApi.Application.Fixture";

        var exception = Assert.Throws<InvalidOperationException>(() => ParseManifest([duplicateEntry, duplicateEntry], 2));

        Assert.That(exception!.Message, Does.Contain("Duplicate closed module public-surface manifest entries"));
    }

    private static IReadOnlySet<SurfaceEntry> ReadManifest()
    {
        using var compressed = new MemoryStream(Convert.FromBase64String(string.Concat(ManifestPayload)));
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var rows = reader.ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Concat(ReviewedManifestRows);
        return ParseManifest(rows, ManifestEntryCount);
    }

    private static IReadOnlySet<SurfaceEntry> ParseManifest(IEnumerable<string> rows, int expectedEntryCount)
    {
        var entries = rows.Select(ParseEntry).ToArray();
        var duplicates = entries
            .GroupBy(entry => entry)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(FormatEntry, StringComparer.Ordinal)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate closed module public-surface manifest entries:{Environment.NewLine}{FormatEntries(duplicates)}");
        }

        if (entries.Length != expectedEntryCount)
        {
            throw new InvalidOperationException(
                $"The closed module public-surface manifest must contain exactly {expectedEntryCount} entries, but contained {entries.Length}.");
        }

        return entries.ToHashSet();
    }

    private static SurfaceEntry ParseEntry(string value)
    {
        var delimiterIndex = value.IndexOf('\t', StringComparison.Ordinal);
        if (delimiterIndex <= 0 || delimiterIndex == value.Length - 1)
        {
            throw new InvalidOperationException($"Malformed closed module public-surface manifest entry '{value}'.");
        }

        return new SurfaceEntry(value[..delimiterIndex], value[(delimiterIndex + 1)..]);
    }

    private static IEnumerable<SurfaceEntry> CollectPublicSurfaceEntries(
        string repoRoot,
        Compilation compilation,
        IEnumerable<SyntaxTree> syntaxTrees)
    {
        foreach (var tree in syntaxTrees)
        {
            if (ModuleBoundaryProductionScan.ResolveCanonicalModule(tree, repoRoot) == null)
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var relativePath = ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, tree.FilePath));
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                if (declaration is not (BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
                    || semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol
                    || !IsEffectivelyPublic(symbol))
                {
                    continue;
                }

                yield return new SurfaceEntry(relativePath, GetMetadataName(symbol));
            }
        }
    }

    private static bool IsEffectivelyPublic(INamedTypeSymbol symbol)
    {
        for (var current = symbol; current != null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
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

    private static string FormatEntry(SurfaceEntry entry) => $"{entry.RelativePath}|{entry.MetadataName}";

    private static string FormatEntries(IEnumerable<SurfaceEntry> entries)
    {
        var values = entries.Select(FormatEntry).ToArray();
        return values.Length == 0 ? "none" : string.Join(Environment.NewLine, values);
    }

    private sealed record SurfaceEntry(string RelativePath, string MetadataName);
}
