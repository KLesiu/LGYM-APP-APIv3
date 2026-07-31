using System.Xml;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace LgymApi.ArchitectureTests;

internal static partial class ProjectReferenceDocumentationEvidence
{
    private const string EvidenceHeading = "## Direct-use evidence";
    private const string LockedTopologyHeading = "## Locked topology";

    [GeneratedRegex("^\\| `(?<source>[^`|]+) -> (?<target>[^`|]+)` \\| `(?<path>[^`:]+):(?<line>[1-9][0-9]*)`(?<analyzer> analyzer reference)? \\|$")]
    private static partial Regex EvidenceRowPattern();

    public static IReadOnlyList<ProjectReferenceDocumentationEvidenceRow> Parse(string markdown)
    {
        var section = ExtractEvidenceSection(markdown);
        var rows = new List<ProjectReferenceDocumentationEvidenceRow>();

        foreach (var rawLine in section.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line is "| Edge | Roslyn-resolved source or analyzer evidence |" or "| --- | --- |" ||
                string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!line.StartsWith('|'))
            {
                continue;
            }

            var match = EvidenceRowPattern().Match(line);
            if (!match.Success)
            {
                throw new InvalidOperationException($"Malformed project-reference evidence row: '{line}'.");
            }

            var source = match.Groups["source"].Value;
            var target = match.Groups["target"].Value;
            var path = match.Groups["path"].Value;
            if (path.Contains('\\', StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Project-reference evidence path must be repository-relative: '{path}'.");
            }

            rows.Add(new ProjectReferenceDocumentationEvidenceRow(
                source,
                target,
                path,
                int.Parse(match.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture),
                match.Groups["analyzer"].Success));
        }

        var duplicates = rows
            .GroupBy(row => row.EdgeIdentity, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(edge => edge, StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length != 0)
        {
            throw new InvalidOperationException("Duplicate project-reference evidence rows: " + string.Join(", ", duplicates));
        }

        return rows;
    }

    public static void AssertMatchesScannedImports(
        IReadOnlyList<ProjectReferenceDocumentationEvidenceRow> rows,
        ProjectImportFixture scannedImports)
    {
        var expectedEdges = scannedImports.EdgeIdentities.ToHashSet(StringComparer.Ordinal);
        var documentedEdges = rows.Select(row => row.EdgeIdentity).ToHashSet(StringComparer.Ordinal);
        var unexpectedEdges = documentedEdges.Except(expectedEdges, StringComparer.Ordinal).OrderBy(edge => edge, StringComparer.Ordinal).ToArray();
        var missingEdges = expectedEdges.Except(documentedEdges, StringComparer.Ordinal).OrderBy(edge => edge, StringComparer.Ordinal).ToArray();
        var analyzerEdges = scannedImports.AnalyzerEdgeIdentities.ToHashSet(StringComparer.Ordinal);
        var semanticRows = rows.Where(row => !row.IsAnalyzerEvidence).ToArray();
        var analyzerRows = rows.Where(row => row.IsAnalyzerEvidence).ToArray();
        var violations = new List<string>();

        if (rows.Count != expectedEdges.Count)
        {
            violations.Add($"Expected {expectedEdges.Count} project-reference evidence rows but found {rows.Count}.");
        }

        violations.AddRange(unexpectedEdges.Select(edge => $"Unexpected project-reference evidence edge: {edge}"));
        violations.AddRange(missingEdges.Select(edge => $"Missing project-reference evidence edge: {edge}"));

        foreach (var row in rows)
        {
            if (!row.SourcePath.StartsWith(row.SourceProject + "/", StringComparison.Ordinal))
            {
                violations.Add($"Project-reference evidence source is outside '{row.SourceProject}': {row.Locator}.");
            }

            if (analyzerEdges.Contains(row.EdgeIdentity) != row.IsAnalyzerEvidence)
            {
                violations.Add($"Project-reference evidence kind is incorrect for '{row.EdgeIdentity}'.");
            }
        }

        if (semanticRows.Length != expectedEdges.Count - analyzerEdges.Count)
        {
            violations.Add($"Expected {expectedEdges.Count - analyzerEdges.Count} Roslyn source evidence rows but found {semanticRows.Length}.");
        }

        if (!analyzerRows.Select(row => row.EdgeIdentity).ToHashSet(StringComparer.Ordinal)
            .SetEquals(analyzerEdges))
        {
            violations.Add("Analyzer evidence rows do not match analyzer-configured project references.");
        }

        foreach (var row in semanticRows)
        {
            if (scannedImports.SymbolUses.Any(use =>
                    string.Equals(use.EdgeIdentity, row.EdgeIdentity, StringComparison.Ordinal) &&
                    string.Equals(use.FilePath, row.SourcePath, StringComparison.Ordinal) &&
                    use.Line == row.Line))
            {
                continue;
            }

            var resolvedAtLocator = scannedImports.SymbolUses
                .Where(use => string.Equals(use.FilePath, row.SourcePath, StringComparison.Ordinal) && use.Line == row.Line)
                .Select(use => $"{use.EdgeIdentity} ({use.Symbol})")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(use => use, StringComparer.Ordinal)
                .ToArray();
            var replacementLocator = scannedImports.SymbolUses
                .Where(use => string.Equals(use.EdgeIdentity, row.EdgeIdentity, StringComparison.Ordinal))
                .OrderBy(use => use.FilePath, StringComparer.Ordinal)
                .ThenBy(use => use.Line)
                .Select(use => use.FilePath + ":" + use.Line)
                .FirstOrDefault();
            violations.Add(
                $"Project-reference evidence locator does not resolve to '{row.TargetProject}': {row.Locator}. " +
                $"Actual: {string.Join(", ", resolvedAtLocator)}. Replacement: {replacementLocator}");
        }

        if (violations.Count != 0)
        {
            throw new InvalidOperationException(
                "Project-reference documentation evidence drift detected:" + Environment.NewLine +
                string.Join(Environment.NewLine, violations));
        }
    }

    public static void AssertAnalyzerLocators(
        string repositoryRoot,
        IReadOnlyList<ProjectReferenceDocumentationEvidenceRow> rows)
    {
        var violations = rows
            .Where(row => row.IsAnalyzerEvidence)
            .Where(row => !IsAnalyzerReferenceAtLocator(repositoryRoot, row))
            .Select(row => $"Analyzer project-reference evidence locator does not resolve to '{row.TargetProject}': {row.Locator}.")
            .ToArray();

        if (violations.Length != 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, violations));
        }
    }

    private static string ExtractEvidenceSection(string markdown)
    {
        var start = markdown.IndexOf(EvidenceHeading, StringComparison.Ordinal);
        var end = markdown.IndexOf(LockedTopologyHeading, StringComparison.Ordinal);
        if (start < 0 || end <= start)
        {
            throw new InvalidOperationException("Project-reference documentation must contain the direct-use evidence section before locked topology.");
        }

        return markdown[start..end];
    }

    private static bool IsAnalyzerReferenceAtLocator(
        string repositoryRoot,
        ProjectReferenceDocumentationEvidenceRow row)
    {
        if (!row.SourcePath.EndsWith(".csproj", StringComparison.Ordinal))
        {
            return false;
        }

        var projectPath = Path.Combine(repositoryRoot, row.SourcePath);
        if (!File.Exists(projectPath))
        {
            return false;
        }

        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var document = XDocument.Load(projectPath, LoadOptions.SetLineInfo);
        return document.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Where(element => string.Equals(element.Attribute("OutputItemType")?.Value, "Analyzer", StringComparison.Ordinal))
            .Any(element =>
            {
                var include = element.Attribute("Include")?.Value;
                var lineInfo = (IXmlLineInfo)element;
                return include is not null &&
                       lineInfo.HasLineInfo() &&
                       lineInfo.LineNumber == row.Line &&
                       string.Equals(
                           Path.GetFileNameWithoutExtension(Path.GetFullPath(include, projectDirectory)),
                           row.TargetProject,
                           StringComparison.Ordinal);
            });
    }
}

internal sealed record ProjectReferenceDocumentationEvidenceRow(
    string SourceProject,
    string TargetProject,
    string SourcePath,
    int Line,
    bool IsAnalyzerEvidence)
{
    public string EdgeIdentity => $"{SourceProject} -> {TargetProject}";

    public string Locator => $"{SourcePath}:{Line}";
}
