using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

internal static class PlatformReferenceDataBoundaryDocumentationTestHelpers
{
    internal static IReadOnlyList<BoundaryDocumentationRow> ParseRows(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var rows = new List<BoundaryDocumentationRow>();

        for (var index = 0; index < lines.Length - 1; index++)
        {
            var headers = ParseCells(lines[index]);
            if (headers.Count == 0 || !headers[0].EndsWith(" ID", StringComparison.Ordinal) ||
                !IsSeparator(lines[index + 1], headers.Count))
            {
                continue;
            }

            for (index += 2; index < lines.Length; index++)
            {
                var cells = ParseCells(lines[index]);
                if (cells.Count == 0)
                {
                    index--;
                    break;
                }

                if (cells.Count != headers.Count)
                {
                    throw new InvalidOperationException(
                        $"Stable documentation table '{headers[0]}' has {cells.Count} cells; expected {headers.Count}.");
                }

                rows.Add(new BoundaryDocumentationRow(
                    UnwrapCode(cells[0]),
                    headers.Zip(cells.Select(UnwrapCode))
                        .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal)));
            }
        }

        return rows;
    }

    internal static IReadOnlyDictionary<string, BoundaryDocumentationRow> RequireExactIds(
        IEnumerable<BoundaryDocumentationRow> rows,
        string prefix,
        IEnumerable<string> expectedIds)
    {
        var scopedRows = rows.Where(row => row.Id.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        var duplicateIds = scopedRows.GroupBy(row => row.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (duplicateIds.Count > 0)
        {
            throw new InvalidOperationException($"Duplicate stable row IDs: {string.Join(", ", duplicateIds)}.");
        }

        var expected = expectedIds.ToHashSet(StringComparer.Ordinal);
        var actual = scopedRows.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        var missingIds = expected.Except(actual).OrderBy(id => id, StringComparer.Ordinal).ToList();
        var unknownIds = actual.Except(expected).OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (missingIds.Count > 0 || unknownIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Stable row IDs drifted. Missing: {Format(missingIds)}. Unknown: {Format(unknownIds)}.");
        }

        return scopedRows.ToDictionary(row => row.Id, StringComparer.Ordinal);
    }

    internal static void AssertLocatorResolves(string repositoryRoot, string locator)
    {
        var separatorIndex = locator.LastIndexOf('#');
        if (separatorIndex <= 0 || separatorIndex == locator.Length - 1)
        {
            throw new InvalidOperationException($"Implementation locator '{locator}' must use 'path#symbol'.");
        }

        var relativePath = locator[..separatorIndex];
        var symbol = locator[(separatorIndex + 1)..];
        var sourcePath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(sourcePath))
        {
            throw new InvalidOperationException($"Implementation locator path does not exist: {relativePath}.");
        }

        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(sourcePath), path: sourcePath).GetCompilationUnitRoot();
        var hasDeclaration = root.DescendantNodes().OfType<MemberDeclarationSyntax>()
            .Any(declaration => declaration switch
            {
                BaseTypeDeclarationSyntax type => type.Identifier.ValueText == symbol,
                DelegateDeclarationSyntax delegateType => delegateType.Identifier.ValueText == symbol,
                MethodDeclarationSyntax method => method.Identifier.ValueText == symbol,
                PropertyDeclarationSyntax property => property.Identifier.ValueText == symbol,
                _ => false
            });
        var hasInvocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Any(invocation => GetInvocationName(invocation) == symbol);
        if (!hasDeclaration && !hasInvocation)
        {
            throw new InvalidOperationException($"Implementation locator symbol does not exist: {locator}.");
        }
    }

    internal static bool MethodInvokes(string sourcePath, string methodName, string invocationName)
    {
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(sourcePath), path: sourcePath).GetCompilationUnitRoot();
        return root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(candidate => candidate.Identifier.ValueText == methodName)
            .Any(method => method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Any(invocation => GetInvocationName(invocation) == invocationName));
    }

    private static List<string> ParseCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '|' || trimmed[^1] != '|')
        {
            return [];
        }

        return trimmed[1..^1].Split('|').Select(cell => cell.Trim()).ToList();
    }

    private static bool IsSeparator(string line, int expectedCellCount)
    {
        var cells = ParseCells(line);
        return cells.Count == expectedCellCount && cells.All(cell => cell.Length >= 3 && cell.All(character => character == '-'));
    }

    private static string UnwrapCode(string value) =>
        value.Length >= 2 && value[0] == '`' && value[^1] == '`' ? value[1..^1] : value;

    private static string GetInvocationName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
        _ => string.Empty
    };

    private static string Format(IReadOnlyCollection<string> values) => values.Count == 0 ? "none" : string.Join(", ", values);
}

internal sealed record BoundaryDocumentationRow(string Id, IReadOnlyDictionary<string, string> Fields)
{
    internal string GetField(string name) => Fields.TryGetValue(name, out var value)
        ? value
        : throw new InvalidOperationException($"Stable row '{Id}' is missing '{name}'.");
}
