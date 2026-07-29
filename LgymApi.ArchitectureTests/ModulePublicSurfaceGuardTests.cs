using System.IO.Compression;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ModulePublicSurfaceGuardTests
{
    private const int ManifestEntryCount = 721;

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

    private static readonly string[] ManifestPayload =
    [
        "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"
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

    private static IReadOnlySet<SurfaceEntry> ReadManifest()
    {
        using var compressed = new MemoryStream(Convert.FromBase64String(string.Concat(ManifestPayload)));
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var entries = reader.ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseEntry)
            .ToHashSet();

        if (entries.Count != ManifestEntryCount)
        {
            throw new InvalidOperationException(
                $"The closed module public-surface manifest must contain exactly {ManifestEntryCount} entries, but contained {entries.Count}.");
        }

        return entries;
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
