using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class RowSecurityProviderIsolationGuardTests
{
    private const string ActorScopePath = "LgymApi.Infrastructure/RowSecurity/EfActorRowSecurityScopeFactory.cs";
    private const string ApiStartupGuardPath = "LgymApi.Api/Configuration/StartupRuntimeGuards.cs";
    private const string RuntimeConnectionInspectorPath = "LgymApi.Infrastructure/Data/PostgreSqlRuntimeConnectionInspector.cs";
    private const string RuntimeConnectionGuardPath = "LgymApi.Infrastructure/Data/PostgreSqlRuntimeConnectionValidator.cs";

    private static readonly string[] RowSecurityIdentifiers =
    [
        "lgym.account_id",
        "set_config",
        "current_setting",
        "pg_policy",
        "relrowsecurity",
        "relforcerowsecurity",
        "CREATE POLICY",
        "ROW LEVEL SECURITY"
    ];

    [Test]
    public void RowSecurityIdentifiers_And_NpgsqlScopeTypes_MustRemainInApprovedSources()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var sourceFiles = Directory
            .EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories)
            .Where(IsScannedProductionSource)
            .ToArray();
        var violations = sourceFiles
            .SelectMany(path => Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase)
                ? FindViolations(repositoryRoot, Parse(path))
                : FindFileTextViolations(repositoryRoot, path))
            .ToArray();

        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
    }

    [Test]
    public void ActorScope_MustUseAParameterizedTransactionLocalCommand_AndDirectFailureDisposal()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, ActorScopePath));

        Assert.Multiple(() =>
        {
            Assert.That(source.IndexOf("BeginTransactionAsync", StringComparison.Ordinal), Is.LessThan(source.IndexOf("SetActorAsync", StringComparison.Ordinal)));
            Assert.That(source, Does.Contain("command.Transaction = transaction;"));
            Assert.That(source, Does.Contain("SELECT set_config('lgym.account_id', @actorId, true);"));
            Assert.That(source, Does.Contain("command.Parameters.AddWithValue(\"actorId\", actorId.ToString());"));
            Assert.That(source, Does.Contain("await unitOfWorkTransaction.DisposeAsync();"));
            Assert.That(source, Does.Not.Contain("RollbackAsync"));
            Assert.That(source, Does.Not.Contain("AsyncLocal"));
            Assert.That(source, Does.Not.Contain("SET lgym.account_id"));
        });
    }

    [TestCase("LgymApi.Application/Unsafe.cs", "using Npgsql; class Unsafe { NpgsqlConnection Connection; }")]
    [TestCase("LgymApi.Infrastructure/Unsafe.cs", "class Unsafe { string Sql = \"SELECT set_config('lgym.account_id', 'actor', false);\"; }")]
    public void Guard_RejectsNpgsqlScopeTypesAndRlsIdentifiersOutsideApprovedSources(string path, string source)
    {
        var violations = FindViolations(".", CSharpSyntaxTree.ParseText(source, path: path)).ToArray();

        Assert.That(violations, Is.Not.Empty);
    }

    [TestCase(ActorScopePath, "using Npgsql; class Scope { NpgsqlConnection Connection; NpgsqlCommand Command; NpgsqlTransaction Transaction; string Sql = \"SELECT set_config('lgym.account_id', @actorId, true);\"; }")]
    [TestCase("LgymApi.Infrastructure/Migrations/Fixture.cs", "class Migration { string Sql = \"CREATE POLICY example USING (current_setting('lgym.account_id', true) IS NOT NULL);\"; }")]
    [TestCase(ApiStartupGuardPath, "class StartupGuard { string Sql = \"SELECT relrowsecurity FROM pg_class;\"; }")]
    public void Guard_AllowsOnlyTheApprovedRowSecuritySources(string path, string source)
    {
        var violations = FindViolations(".", CSharpSyntaxTree.ParseText(source, path: path)).ToArray();

        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
    }

    [TestCase("scripts/row-security.ps1", "SELECT set_config('lgym.account_id', @actorId, true);")]
    [TestCase("deploy/postgres/row-security.sql", "CREATE POLICY example USING (current_setting('lgym.account_id', true) IS NOT NULL);")]
    public void Guard_AllowsRlsIdentifiersInOperatorScripts(string path, string source)
    {
        var violations = FindTextViolations(path, source).ToArray();

        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> FindViolations(string repositoryRoot, SyntaxTree tree)
    {
        var relativePath = Normalize(Path.GetRelativePath(repositoryRoot, tree.FilePath));
        var allowedForRowSecurity = IsAllowedRowSecuritySource(relativePath);

        foreach (var literal in tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!literal.IsKind(SyntaxKind.StringLiteralExpression)
                || !RowSecurityIdentifiers.Any(identifier => literal.Token.ValueText.Contains(identifier, StringComparison.OrdinalIgnoreCase))
                || allowedForRowSecurity)
            {
                continue;
            }

            yield return $"{relativePath}:{GetLine(tree, literal)} contains a row-security identifier outside the approved sources.";
        }

        foreach (var typeName in tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (typeName.Identifier.ValueText is not ("NpgsqlConnection" or "NpgsqlCommand" or "NpgsqlTransaction")
                || IsApprovedNpgsqlSource(relativePath))
            {
                continue;
            }

            yield return $"{relativePath}:{GetLine(tree, typeName)} references {typeName.Identifier.ValueText} outside {ActorScopePath}.";
        }
    }

    private static IEnumerable<string> FindFileTextViolations(string repositoryRoot, string path)
        => FindTextViolations(Normalize(Path.GetRelativePath(repositoryRoot, path)), File.ReadAllText(path));

    private static IEnumerable<string> FindTextViolations(string relativePath, string source)
    {
        if (IsAllowedRowSecuritySource(relativePath)
            || !RowSecurityIdentifiers.Any(identifier => source.Contains(identifier, StringComparison.OrdinalIgnoreCase)))
        {
            yield break;
        }

        yield return $"{relativePath} contains a row-security identifier outside the approved sources.";
    }

    private static bool IsScannedProductionSource(string path)
    {
        var normalized = Normalize(path);
        return Path.GetExtension(path) is ".cs" or ".sql" or ".ps1" or ".sh" or ".cmd"
            && !normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("/LgymApi.UnitTests/", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("/LgymApi.IntegrationTests/", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("/LgymApi.ArchitectureTests/", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("/LgymApi.DataSeeder.Tests/", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("/LgymApi.TestUtils/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedRowSecuritySource(string relativePath)
        => string.Equals(relativePath, ActorScopePath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, ApiStartupGuardPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, RuntimeConnectionInspectorPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, RuntimeConnectionGuardPath, StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("LgymApi.Infrastructure/Migrations/", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("deploy/postgres/", StringComparison.OrdinalIgnoreCase);

    private static bool IsApprovedNpgsqlSource(string relativePath)
        => string.Equals(relativePath, ActorScopePath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, RuntimeConnectionInspectorPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, RuntimeConnectionGuardPath, StringComparison.OrdinalIgnoreCase);

    private static int GetLine(SyntaxTree tree, SyntaxNode node) => tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;

    private static SyntaxTree Parse(string path) => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);

    private static string Normalize(string path) => path.Replace('\\', '/');
}
