using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class IdentityNotificationsDependencyGuardTests
{
    private const string NotificationsApplicationNamespace = "LgymApi.Application.Notifications";
    private const string NotificationsModuleNamespace = "LgymApi.Notifications";

    [Test]
    public void Identity_Source_Should_NotDeclareOrImport_NotificationsNamespaces()
    {
        var (_, _, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Identity");

        Assert.That(CollectViolations(syntaxTrees), Is.Empty,
            "Identity must consume only its own public ports and must not reference Notifications.");
    }

    [TestCase("IdentityImportsNotifications.cs", "using LgymApi.Application.Notifications;")]
    [TestCase("IdentityDeclaresNotifications.cs", "namespace LgymApi.Notifications;")]
    public void Identity_NotificationsDependencyFixture_IsRejected(string fileName, string dependencySource)
    {
        var (repositoryRoot, _, _) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Identity");
        var fixture = CSharpSyntaxTree.ParseText(
            $"{dependencySource}{Environment.NewLine}public sealed class Fixture {{ }}",
            path: Path.Combine(repositoryRoot, "LgymApi.Identity", fileName));

        Assert.That(CollectViolations([fixture]), Is.Not.Empty);
    }

    private static IReadOnlyList<string> CollectViolations(IEnumerable<SyntaxTree> syntaxTrees)
    {
        return syntaxTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>()
                .Select(usingDirective => usingDirective.Name?.ToString())
                .Concat(tree.GetRoot().DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>()
                    .Select(namespaceDeclaration => namespaceDeclaration.Name.ToString()))
                .Where(IsNotificationsNamespace)
                .Select(namespaceName => $"{ArchitectureTestHelpers.NormalizePath(tree.FilePath)}:{namespaceName}"))
            .OrderBy(violation => violation, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsNotificationsNamespace(string? namespaceName)
    {
        return namespaceName is not null
            && (namespaceName.Equals(NotificationsApplicationNamespace, StringComparison.Ordinal)
                || namespaceName.StartsWith($"{NotificationsApplicationNamespace}.", StringComparison.Ordinal)
                || namespaceName.Equals(NotificationsModuleNamespace, StringComparison.Ordinal)
                || namespaceName.StartsWith($"{NotificationsModuleNamespace}.", StringComparison.Ordinal));
    }
}
