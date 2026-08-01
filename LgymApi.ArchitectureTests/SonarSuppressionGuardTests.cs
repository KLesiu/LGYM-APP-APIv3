using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed partial class SonarSuppressionGuardTests
{
    private const string MarkerJustification = "Contract-only Id<T> marker; construction is intentionally prohibited and enforced by ReferenceMarkerPersistenceGuardTests.";
    private const string LegacyPasswordJustification = "Test-only passport-local-mongoose compatibility fixture; production password hashing is unchanged.";
    [GeneratedRegex(@"(?<![A-Za-z0-9_:])(?:S3453|S5344|csharpsquid:S3453|csharpsquid:S5344)(?![A-Za-z0-9_:])", RegexOptions.CultureInvariant)]
    private static partial Regex GuardedPragmaRuleIdentityPattern();

    private static readonly Suppression[] ApprovedSuppressions =
    [
        new("LgymApi.Platform/Contracts/ActorReference.cs", "ActorReference", "class", "Major Bug", "S3453", MarkerJustification),
        new("LgymApi.Identity/Contracts/IdentityReferenceMarkers.cs", "AccountReference", "class", "Major Bug", "S3453", MarkerJustification),
        new("LgymApi.Identity/Contracts/IdentityReferenceMarkers.cs", "AccountSessionReference", "class", "Major Bug", "S3453", MarkerJustification),
        new("LgymApi.Identity/Contracts/IdentityReferenceMarkers.cs", "RoleReference", "class", "Major Bug", "S3453", MarkerJustification),
        new("LgymApi.TrainingPlanning/Contracts/PlanningReferenceMarkers.cs", "PlanReference", "class", "Major Bug", "S3453", MarkerJustification),
        new("LgymApi.TrainingPlanning/Contracts/PlanningReferenceMarkers.cs", "PlanDayReference", "class", "Major Bug", "S3453", MarkerJustification),
        new("LgymApi.TrainingPlanning/Contracts/PlanningReferenceMarkers.cs", "PlanExerciseReference", "class", "Major Bug", "S3453", MarkerJustification),
        new("LgymApi.Notifications/Contracts/NotificationReferenceMarkers.cs", "NotificationReference", "class", "Major Bug", "S3453", MarkerJustification),
        new("LgymApi.Notifications/Contracts/NotificationReferenceMarkers.cs", "PushInstallationReference", "class", "Major Bug", "S3453", MarkerJustification),
        new("LgymApi.TestUtils/TestDataFactory.cs", "CreateLegacyPasswordData", "method", "Critical Vulnerability", "S5344", LegacyPasswordJustification)
    ];

    [Test]
    public void Suppressions_Should_Stay_Exactly_Within_The_Approved_Declaration_Roster()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var trees = ApprovedSuppressions
            .Select(suppression => Parse(Path.Combine(repositoryRoot, suppression.Path)))
            .DistinctBy(tree => tree.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var repositoryTrees = EnumerateRepositoryTrees(repositoryRoot)
            .DistinctBy(tree => tree.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var actual = repositoryTrees.SelectMany(tree => FindSuppressions(repositoryRoot, tree)).ToArray();
        var scopeViolations = FindScopeViolations(repositoryRoot, repositoryTrees).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(actual, Is.EquivalentTo(ApprovedSuppressions));
            Assert.That(scopeViolations, Is.Empty, string.Join(Environment.NewLine, scopeViolations));
            Assert.That(actual.Count(suppression => suppression.CheckId == "S3453"), Is.EqualTo(9));
            Assert.That(actual.Count(suppression => suppression.CheckId == "S5344"), Is.EqualTo(1));
        }

        AssertMarkerConstructorsRemainPrivate(trees);
        AssertLegacyPasswordFixtureRemainsCompatible(trees.Single(tree => tree.FilePath.EndsWith("TestDataFactory.cs", StringComparison.Ordinal)));
    }

    [TestCase("extra target", "using System.Diagnostics.CodeAnalysis; [SuppressMessage(\"Major Bug\", \"S3453\", Justification = \"Contract-only Id<T> marker; construction is intentionally prohibited and enforced by ReferenceMarkerPersistenceGuardTests.\")] class ExtraReference { private ExtraReference() { } }", "Unexpected suppression target 'ExtraReference'.")]
    [TestCase("wrong category", "using System.Diagnostics.CodeAnalysis; [SuppressMessage(\"Minor Code Smell\", \"S3453\", Justification = \"Contract-only Id<T> marker; construction is intentionally prohibited and enforced by ReferenceMarkerPersistenceGuardTests.\")] class ActorReference { private ActorReference() { } }", "Incorrect suppression for 'ActorReference'.")]
    [TestCase("wrong rule", "using System.Diagnostics.CodeAnalysis; [SuppressMessage(\"Major Bug\", \"csharpsquid:S3453\", Justification = \"Contract-only Id<T> marker; construction is intentionally prohibited and enforced by ReferenceMarkerPersistenceGuardTests.\")] class ActorReference { private ActorReference() { } }", "Incorrect suppression for 'ActorReference'.")]
    [TestCase("assembly scope", "using System.Diagnostics.CodeAnalysis; [assembly: SuppressMessage(\"Major Bug\", \"S3453\", Justification = \"Contract-only Id<T> marker; construction is intentionally prohibited and enforced by ReferenceMarkerPersistenceGuardTests.\")]", "Assembly-level suppression is forbidden.")]
    public void Scope_Guard_Should_Reject_Broader_Or_Malformed_Suppressions(string _, string source, string expectedViolation)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "Fixtures/InvalidSuppression.cs");
        var actual = FindCandidateSuppressions(".", tree).ToArray();
        var violations = FindRosterViolations(actual, ApprovedSuppressions)
            .Concat(FindScopeViolations(".", [tree]))
            .ToArray();

        Assert.That(violations, Does.Contain(expectedViolation));
    }

    [Test]
    public void Production_Roster_Should_Reject_A_Declaration_Local_Suppression_Outside_The_Approved_Paths()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var extraTree = CSharpSyntaxTree.ParseText(
            "using System.Diagnostics.CodeAnalysis; [SuppressMessage(\"Major Bug\", \"S3453\", Justification = \"Contract-only Id<T> marker; construction is intentionally prohibited and enforced by ReferenceMarkerPersistenceGuardTests.\")] class ExtraReference { private ExtraReference() { } }",
            path: Path.Combine(repositoryRoot, "LgymApi.Api", "Fixtures", "ExtraLocalSuppression.cs"));
        var repositoryTrees = EnumerateRepositoryTrees(repositoryRoot)
            .Append(extraTree)
            .DistinctBy(tree => tree.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var actual = repositoryTrees.SelectMany(tree => FindSuppressions(repositoryRoot, tree)).ToArray();
        var violations = FindRosterViolations(actual, ApprovedSuppressions).ToArray();

        Assert.That(violations, Does.Contain("Unexpected suppression target 'ExtraReference'."));
    }

    [Test]
    public void Production_Roster_Should_Reject_Qualified_Declaration_Local_Suppressions_Outside_The_Approved_Paths()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var extraTree = CSharpSyntaxTree.ParseText(
            "using System.Diagnostics.CodeAnalysis; [SuppressMessage(\"Major Bug\", \"csharpsquid:S3453\", Justification = \"probe\")] internal sealed class QualifiedS3453Probe { } [SuppressMessage(\"Critical Vulnerability\", \"csharpsquid:S5344\", Justification = \"probe\")] internal sealed class QualifiedS5344Probe { }",
            path: Path.Combine(repositoryRoot, "LgymApi.Api", "Fixtures", "QualifiedLocalSuppression.cs"));
        var repositoryTrees = EnumerateRepositoryTrees(repositoryRoot)
            .Append(extraTree)
            .DistinctBy(tree => tree.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var actual = repositoryTrees.SelectMany(tree => FindSuppressions(repositoryRoot, tree)).ToArray();
        var violations = FindRosterViolations(actual, ApprovedSuppressions).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(violations, Does.Contain("Unexpected suppression target 'QualifiedS3453Probe'."));
            Assert.That(violations, Does.Contain("Unexpected suppression target 'QualifiedS5344Probe'."));
        }
    }

    [Test]
    public void Production_Scope_Should_Reject_A_Qualified_Assembly_Suppression()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var extraTree = CSharpSyntaxTree.ParseText(
            "using System.Diagnostics.CodeAnalysis; [assembly: SuppressMessage(\"Major Bug\", \"csharpsquid:S3453\", Justification = \"probe\")]",
            path: Path.Combine(repositoryRoot, "LgymApi.Api", "Fixtures", "QualifiedAssemblySuppression.cs"));
        var repositoryTrees = EnumerateRepositoryTrees(repositoryRoot)
            .Append(extraTree)
            .DistinctBy(tree => tree.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var violations = FindScopeViolations(repositoryRoot, repositoryTrees).ToArray();

        Assert.That(violations, Does.Contain("Assembly-level suppression is forbidden."));
    }

    [TestCase("#pragma warning disable S3453", true)]
    [TestCase("#pragma warning disable S5344", true)]
    [TestCase("#pragma warning disable csharpsquid:S3453", true)]
    [TestCase("#pragma warning disable csharpsquid:S5344", true)]
    [TestCase("#pragma warning disable S34530", false)]
    [TestCase("#pragma warning disable other:S3453", false)]
    public void Pragma_Guard_Should_Recognize_Only_Exact_Guarded_Rule_Identities(string source, bool expected)
    {
        Assert.That(ContainsGuardedPragmaRuleIdentity(ParsePragma(source)), Is.EqualTo(expected));
    }

    [Test]
    public void Qualified_Pragma_Guard_Should_Use_Directive_Source_When_Roslyn_Splits_The_Identity()
    {
        const string source = "#pragma warning disable csharpsquid:S3453";
        var pragma = ParsePragma(source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(string.Join("|", pragma.ErrorCodes.Select(code => code.ToString())), Is.EqualTo("csharpsquid"));
            Assert.That(pragma.ToString(), Is.EqualTo(source));
            Assert.That(ContainsGuardedPragmaRuleIdentity(pragma), Is.True);
        }
    }

    [TestCase("csharpsquid:S3453")]
    [TestCase("csharpsquid:S5344")]
    public void Production_Scope_Should_Reject_A_Qualified_Pragma_Suppression(string checkId)
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var extraTree = CSharpSyntaxTree.ParseText(
            $"#pragma warning disable {checkId}",
            path: Path.Combine(repositoryRoot, "LgymApi.Api", "Fixtures", $"QualifiedPragma{checkId[^5..]}.cs"));
        var repositoryTrees = EnumerateRepositoryTrees(repositoryRoot)
            .Append(extraTree)
            .DistinctBy(tree => tree.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var violations = FindScopeViolations(repositoryRoot, repositoryTrees).ToArray();

        Assert.That(violations, Does.Contain($"Pragma suppression is forbidden in 'LgymApi.Api/Fixtures/QualifiedPragma{checkId[^5..]}.cs'."));
    }

    private static void AssertMarkerConstructorsRemainPrivate(IEnumerable<SyntaxTree> trees)
    {
        var markerClasses = trees
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(declaration => ApprovedSuppressions.Any(suppression => suppression.Kind == "class" && suppression.Target == declaration.Identifier.ValueText))
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(markerClasses, Has.Length.EqualTo(9));
            Assert.That(markerClasses, Has.All.Matches<ClassDeclarationSyntax>(declaration => declaration.Modifiers.Any(SyntaxKind.PublicKeyword) && declaration.Modifiers.Any(SyntaxKind.SealedKeyword)));
            Assert.That(markerClasses, Has.All.Matches<ClassDeclarationSyntax>(declaration => declaration.Members.OfType<ConstructorDeclarationSyntax>().Count() == 1));
            Assert.That(markerClasses, Has.All.Matches<ClassDeclarationSyntax>(declaration => declaration.Members.OfType<ConstructorDeclarationSyntax>().Single().Modifiers.Any(SyntaxKind.PrivateKeyword)));
        }
    }

    private static void AssertLegacyPasswordFixtureRemainsCompatible(SyntaxTree tree)
    {
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(declaration => declaration.Identifier.ValueText == "CreateLegacyPasswordData");
        var pbkdf2 = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression.ToString() == "Rfc2898DeriveBytes.Pbkdf2");
        var arguments = pbkdf2.ArgumentList.Arguments;
        var methodSource = method.NormalizeWhitespace().ToFullString();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(method.Modifiers.Any(SyntaxKind.PrivateKeyword), Is.True);
            Assert.That(method.Modifiers.Any(SyntaxKind.StaticKeyword), Is.True);
            Assert.That(arguments[2].Expression.ToString(), Is.EqualTo("25000"));
            Assert.That(arguments[3].Expression.ToString(), Is.EqualTo("HashAlgorithmName.SHA256"));
            Assert.That(arguments[4].Expression.ToString(), Is.EqualTo("512"));
            Assert.That(methodSource, Does.Contain("Convert.ToHexString(hash).ToLowerInvariant()"));
            Assert.That(methodSource, Does.Contain("saltHex, 25000, 512, \"sha256\""));
        }
    }

    private static IEnumerable<string> FindScopeViolations(string repositoryRoot, IEnumerable<SyntaxTree> trees)
    {
        foreach (var tree in trees)
        {
            var relativePath = RelativePath(repositoryRoot, tree.FilePath);
            if (Path.GetFileName(tree.FilePath).Equals("GlobalSuppressions.cs", StringComparison.OrdinalIgnoreCase)
                && tree.GetRoot().DescendantNodes().OfType<AttributeSyntax>().Any(IsGuardedSuppression))
            {
                yield return $"Global suppression file is forbidden: '{relativePath}'.";
            }

            foreach (var attribute in tree.GetRoot().DescendantNodes().OfType<AttributeSyntax>().Where(IsGuardedSuppression))
            {
                if (attribute.Parent?.Parent is CompilationUnitSyntax)
                {
                    var target = ((AttributeListSyntax)attribute.Parent).Target?.Identifier.ValueText;
                    yield return target is "assembly" or "module"
                        ? $"{char.ToUpperInvariant(target[0])}{target[1..]}-level suppression is forbidden."
                        : $"Global suppression is forbidden in '{relativePath}'.";
                }
            }

            foreach (var pragma in tree.GetRoot().DescendantTrivia().Select(trivia => trivia.GetStructure()).OfType<PragmaWarningDirectiveTriviaSyntax>())
            {
                if (pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword)
                    && ContainsGuardedPragmaRuleIdentity(pragma))
                {
                    yield return $"Pragma suppression is forbidden in '{relativePath}'.";
                }
            }

            if (ApprovedSuppressions.Any(suppression => suppression.Path == relativePath)
                && tree.GetRoot().DescendantTrivia().Any(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && trivia.ToString().Contains("NOSONAR", StringComparison.OrdinalIgnoreCase)))
            {
                yield return $"NOSONAR suppression is forbidden in '{relativePath}'.";
            }
        }
    }

    private static IEnumerable<string> FindRosterViolations(IEnumerable<Suppression> actual, IEnumerable<Suppression> approved)
    {
        var approvedByTarget = approved.ToDictionary(suppression => suppression.Target, StringComparer.Ordinal);
        foreach (var suppression in actual)
        {
            if (!approvedByTarget.TryGetValue(suppression.Target, out var expected))
            {
                yield return $"Unexpected suppression target '{suppression.Target}'.";
            }
            else if (suppression != expected)
            {
                yield return $"Incorrect suppression for '{suppression.Target}'.";
            }
        }
    }

    private static IEnumerable<Suppression> FindSuppressions(string repositoryRoot, SyntaxTree tree) => FindCandidateSuppressions(repositoryRoot, tree)
        .Where(suppression => IsGuardedRuleIdentity(suppression.CheckId));

    private static IEnumerable<Suppression> FindCandidateSuppressions(string repositoryRoot, SyntaxTree tree)
    {
        foreach (var attribute in tree.GetRoot().DescendantNodes().OfType<AttributeSyntax>().Where(IsSuppressMessage))
        {
            if (!TryGetTarget(attribute, out var target, out var kind))
            {
                continue;
            }

            var arguments = attribute.ArgumentList?.Arguments ?? default;
            yield return new Suppression(
                RelativePath(repositoryRoot, tree.FilePath),
                target,
                kind,
                arguments.Count > 0 ? StringValue(arguments[0].Expression) : string.Empty,
                arguments.Count > 1 ? StringValue(arguments[1].Expression) : string.Empty,
                arguments.FirstOrDefault(argument => argument.NameEquals?.Name.Identifier.ValueText == "Justification") is { } justification
                    ? StringValue(justification.Expression)
                    : string.Empty);
        }
    }

    private static bool TryGetTarget(AttributeSyntax attribute, out string target, out string kind)
    {
        switch (attribute.Parent?.Parent)
        {
            case ClassDeclarationSyntax declaration:
                target = declaration.Identifier.ValueText;
                kind = "class";
                return true;
            case MethodDeclarationSyntax declaration:
                target = declaration.Identifier.ValueText;
                kind = "method";
                return true;
            default:
                target = string.Empty;
                kind = string.Empty;
                return false;
        }
    }

    private static bool IsSuppressMessage(AttributeSyntax attribute) => attribute.Name.ToString() is "SuppressMessage" or "SuppressMessageAttribute" or "System.Diagnostics.CodeAnalysis.SuppressMessage" or "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute";

    private static bool IsGuardedSuppression(AttributeSyntax attribute) => IsSuppressMessage(attribute)
        && IsGuardedRuleIdentity(GetCheckId(attribute));

    private static bool IsGuardedRuleIdentity(string checkId) => checkId is "S3453" or "S5344" or "csharpsquid:S3453" or "csharpsquid:S5344";

    private static bool ContainsGuardedPragmaRuleIdentity(PragmaWarningDirectiveTriviaSyntax pragma) => pragma.ErrorCodes.Any(code => IsGuardedRuleIdentity(code.ToString()))
        || GuardedPragmaRuleIdentityPattern().IsMatch(pragma.ToString());

    private static string GetCheckId(AttributeSyntax attribute)
    {
        var arguments = attribute.ArgumentList?.Arguments ?? default;
        return arguments.Count > 1 ? StringValue(arguments[1].Expression) : string.Empty;
    }

    private static string StringValue(ExpressionSyntax expression) => expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
        ? literal.Token.ValueText
        : string.Empty;

    private static SyntaxTree Parse(string path) => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);

    private static PragmaWarningDirectiveTriviaSyntax ParsePragma(string source) => CSharpSyntaxTree.ParseText(source)
        .GetRoot()
        .DescendantTrivia()
        .Select(trivia => trivia.GetStructure())
        .OfType<PragmaWarningDirectiveTriviaSyntax>()
        .Single();

    private static IEnumerable<SyntaxTree> EnumerateRepositoryTrees(string repositoryRoot) => Directory
        .EnumerateFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment is ".git" or "bin" or "obj"))
        .Select(Parse);

    private static string RelativePath(string repositoryRoot, string path) => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');

    private sealed record Suppression(string Path, string Target, string Kind, string Category, string CheckId, string Justification);
}
