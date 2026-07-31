using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class Issue395FinalDocumentationTests
{
    private const string AdrPath = "docs/adr/007-final-modular-monolith-compatibility-commitments.md";
    private const string VerificationPath = "docs/modular-monolith/issue-395-final-verification.md";
    private const string Placeholder = "TODO-22-UNFILLED";

    [Test]
    public void Final_Documents_Should_Publish_The_Completed_Cutover_Commitments()
    {
        var adr = ReadArtifact(AdrPath);
        var verification = ReadArtifact(VerificationPath);
        var rows = ParseRows(verification);

        Assert.That(adr, Does.Contain("## Status\n\nAccepted"));
        Assert.That(adr, Does.Contain("25 scoped Application adapter contracts"));
        Assert.That(adr, Does.Contain("3 scoped Notifications adapter contracts"));
        Assert.That(adr, Does.Contain("Task7`, `ApiCompatibility`, and `Compatibility.Task7` CLR adapter identities are removed"));
        Assert.That(adr, Does.Contain("one production `AppDbContext`, one PostgreSQL database"));
        Assert.That(adr, Does.Contain("High constructor arity is accepted"));
        Assert.That(adr, Does.Contain("PushInstallationSessionDisassociationAdapter"));

        ValidateFinalDisposition(rows);
        ValidateAdapterDisposition(rows);
        ValidatePartialDisposition(rows);
        ValidateSourceLocators(rows);
        Assert.That(Regex.Matches(verification, "```mermaid", RegexOptions.CultureInvariant).Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(verification, Does.Contain("clean isolated worktree"));
        Assert.That(verification, Does.Contain("no claim about a clean-SHA matrix, GitHub status, or a live runtime result"));
    }

    [Test]
    public void Final_Evidence_Placeholders_Should_Remain_Unfilled_Until_Todo22()
    {
        var rows = ParseRows(ReadArtifact(VerificationPath));
        var evidenceRows = RequirePrefix(rows, "issue395.evidence.", 5);

        foreach (var row in evidenceRows.Values)
        {
            Assert.That(row["Recorded result"], Is.EqualTo(Placeholder), $"{row.Id} must remain unfilled until Todo 22.");
        }
    }

    [Test]
    public void Parser_Should_Reject_Stale_Locators_Claims_And_Filled_Placeholders()
    {
        var valid = ReadArtifact(VerificationPath);

        Assert.Throws<InvalidOperationException>(() => ValidateSourceLocators(ParseRows(valid.Replace(
            "LgymApi.Application/ApiAdapters/ServiceCollectionExtensions.cs#ApplicationApiAdapterServiceCollectionExtensions.AddApplicationApiAdapters",
            "LgymApi.Application/ApiAdapters/ServiceCollectionExtensions.cs#ApplicationApiAdapterServiceCollectionExtensions.MissingMember",
            StringComparison.Ordinal))));
        Assert.Throws<InvalidOperationException>(() => ValidateSourceLocators(ParseRows(valid.Replace(
            "LgymApi.ArchitectureTests/Issue395MigrationLedgerTests.cs#Issue395MigrationLedgerTests",
            "LgymApi.ArchitectureTests/Missing.cs#Missing",
            StringComparison.Ordinal))));
        Assert.Throws<AssertionException>(() => ValidateFinalDisposition(ParseRows(valid.Replace(
            "projects=18; direct-edges=90; forbidden-complement=216",
            "projects=19; direct-edges=90; forbidden-complement=216",
            StringComparison.Ordinal))));
        Assert.Throws<AssertionException>(() => ValidateAdapterDisposition(ParseRows(valid.Replace(
            "| `issue395.adapter.application-api` | Controller-facing API adapters | `25` |",
            "| `issue395.adapter.application-api` | Controller-facing API adapters | `24` |",
            StringComparison.Ordinal))));
        Assert.Throws<AssertionException>(() => ValidatePartialDisposition(ParseRows(valid.Replace(
            "| `issue395.namespace.application-compatible` | Established non-Task7 `LgymApi.Application.*` namespaces in extracted owners | Physical owner project | Retained for source or wire compatibility |",
            "| `issue395.namespace.application-compatible` | Established non-Task7 `LgymApi.Application.*` namespaces in extracted owners | Application | Retained for source or wire compatibility |",
            StringComparison.Ordinal))));
        Assert.Throws<InvalidOperationException>(() => ValidateEvidenceRows(ParseRows(valid.Replace(Placeholder, "passed", StringComparison.Ordinal))));
    }

    [Test]
    public void Historical_Issue375_Documents_Should_Remain_Unchanged()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var changedPaths = RunGit(root, "diff", "--name-only", "HEAD", "--",
            "docs/modular-monolith/issue-375-project-reference-graph.md",
            "docs/modular-monolith/issue-375-architecture-baseline.md");

        Assert.That(changedPaths, Is.Empty, "Historical issue-375 documents must remain unchanged.");
    }

    private static void ValidateFinalDisposition(IReadOnlyList<Row> rows)
    {
        var disposition = RequirePrefix(rows, "issue395.dependency.", 5);
        Assert.That(disposition["issue395.dependency.graph"]["Final fact"], Is.EqualTo("projects=18; direct-edges=90; forbidden-complement=216"));
        Assert.That(disposition["issue395.dependency.persistence"]["Final fact"], Is.EqualTo("AppDbContext=1; PostgreSQL-database=1; migration-stream=1; deployables=1"));
        Assert.That(disposition["issue395.dependency.ownership"]["Final fact"], Is.EqualTo("entities=48; owners=8"));
        Assert.That(disposition["issue395.dependency.mapping"]["Final fact"], Is.EqualTo("profiles=46"));
        Assert.That(disposition["issue395.dependency.exports"]["Final fact"], Is.EqualTo("entries=771"));
    }

    private static void ValidateAdapterDisposition(IReadOnlyList<Row> rows)
    {
        var adapters = RequirePrefix(rows, "issue395.adapter.", 4);
        Assert.That(adapters["issue395.adapter.application-api"]["Count"], Is.EqualTo("25"));
        Assert.That(adapters["issue395.adapter.application-api"]["Owner"], Is.EqualTo("Application owners"));
        Assert.That(adapters["issue395.adapter.notifications-api"]["Count"], Is.EqualTo("3"));
        Assert.That(adapters["issue395.adapter.notifications-integration"]["Count"], Is.EqualTo("3"));
        Assert.That(adapters["issue395.adapter.migration-clr-removal"]["Count"], Is.EqualTo("0"));
    }

    private static void ValidatePartialDisposition(IReadOnlyList<Row> rows)
    {
        var disposition = RequirePrefix(rows, "issue395.partial.", 2)
            .Concat(RequirePrefix(rows, "issue395.namespace.", 1))
            .Concat(RequirePrefix(rows, "issue395.constructor.", 1))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        Assert.That(disposition["issue395.namespace.application-compatible"]["Owner"], Is.EqualTo("Physical owner project"));
        Assert.That(disposition["issue395.constructor.direct-injection"]["Status"], Does.Contain("High arity accepted"));
    }

    private static void ValidateEvidenceRows(IReadOnlyList<Row> rows)
    {
        var evidence = RequirePrefix(rows, "issue395.evidence.", 5);
        foreach (var row in evidence.Values)
        {
            if (row["Recorded result"] != Placeholder)
            {
                throw new InvalidOperationException($"{row.Id} must remain {Placeholder} until Todo 22.");
            }
        }
    }

    private static void ValidateSourceLocators(IReadOnlyList<Row> rows)
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var locators = RequirePrefix(rows, "issue395.locator.", 4);
        foreach (var row in locators.Values)
        {
            var parts = row["Source locator"].Split('#', 2);
            if (parts.Length != 2 || parts[0].Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{row.Id} has an invalid source locator.");
            }

            var sourcePath = Path.Combine(root, parts[0].Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(sourcePath) || !HasSourceDeclaration(File.ReadAllText(sourcePath), Path.GetFileNameWithoutExtension(sourcePath), parts[1]))
            {
                throw new InvalidOperationException($"{row.Id} has a stale source locator '{row["Source locator"]}'.");
            }
        }
    }

    private static bool HasSourceDeclaration(string source, string sourceFileName, string locator)
    {
        var symbolParts = locator.Split('.', 2);
        if (symbolParts.Length == 0 || symbolParts[0].Length == 0)
        {
            return false;
        }

        var typeName = symbolParts[0];
        var memberName = symbolParts.Length == 2 ? symbolParts[1] : null;
        var root = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest)).GetRoot();
        var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Where(type => type.Identifier.ValueText == typeName);
        if (!types.Any() && memberName is not null && typeName == sourceFileName)
        {
            types = root.DescendantNodes().OfType<TypeDeclarationSyntax>();
        }

        return memberName is null
            ? types.Any()
            : types.Any(type => type.Members.Any(member => DeclaresMember(member, memberName)));
    }

    private static bool DeclaresMember(MemberDeclarationSyntax member, string memberName)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText == memberName,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText == memberName,
            PropertyDeclarationSyntax property => property.Identifier.ValueText == memberName,
            EventDeclarationSyntax @event => @event.Identifier.ValueText == memberName,
            BaseFieldDeclarationSyntax field => field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == memberName),
            _ => false
        };
    }

    private static IReadOnlyDictionary<string, Row> RequirePrefix(IReadOnlyList<Row> rows, string prefix, int count)
    {
        var scoped = rows.Where(row => row.Id.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        if (scoped.Length != count || scoped.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != count)
        {
            throw new InvalidOperationException($"Expected exactly {count} rows with prefix '{prefix}'.");
        }

        return scoped.ToDictionary(row => row.Id, StringComparer.Ordinal);
    }

    private static IReadOnlyList<Row> ParseRows(string markdown)
    {
        var rows = new List<Row>();
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length - 1; index++)
        {
            var headers = Cells(lines[index]);
            if (headers.Count == 0 || !IsSeparator(lines[index + 1], headers.Count))
            {
                continue;
            }

            for (index += 2; index < lines.Length; index++)
            {
                var cells = Cells(lines[index]);
                if (cells.Count == 0)
                {
                    index--;
                    break;
                }

                if (cells.Count != headers.Count)
                {
                    throw new InvalidOperationException($"Documentation table row has {cells.Count} cells; expected {headers.Count}.");
                }

                var id = Unwrap(cells[0]);
                if (id.StartsWith("issue395.", StringComparison.Ordinal))
                {
                    rows.Add(new Row(id, headers.Zip(cells.Select(Unwrap)).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal)));
                }
            }
        }

        return rows;
    }

    private static List<string> Cells(string line)
    {
        var value = line.Trim();
        return value.Length < 2 || value[0] != '|' || value[^1] != '|'
            ? []
            : value[1..^1].Split('|').Select(cell => cell.Trim()).ToList();
    }

    private static bool IsSeparator(string line, int count) => Cells(line).Count == count && Cells(line).All(cell => cell.Length >= 3 && cell.All(character => character == '-'));

    private static string Unwrap(string value) => value.Length >= 2 && value[0] == '`' && value[^1] == '`' ? value[1..^1] : value;

    private static string ReadArtifact(string relativePath) => File.ReadAllText(Path.Combine(ArchitectureTestHelpers.ResolveRepositoryRoot(), relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string[] RunGit(string root, params string[] arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", string.Join(' ', arguments.Select(argument => $"\"{argument}\"")))
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Unable to start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git diff failed: {error}");
        }

        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private sealed record Row(string Id, IReadOnlyDictionary<string, string> Fields)
    {
        public string this[string column] => Fields.TryGetValue(column, out var value)
            ? value
            : throw new InvalidOperationException($"{Id} is missing column '{column}'.");
    }
}
