using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LgymApi.E2ETests.Harness;

internal static class StandaloneBoundaryPolicy
{
    internal const string E2ETestsProjectPath = "LgymApi.E2ETests/LgymApi.E2ETests.csproj";

    internal static void AssertStandaloneSolution(string repositoryRoot, string solutionPath)
    {
        var projectPaths = ParseSolutionProjects(solutionPath);
        var relativeProjectPaths = projectPaths
            .Select(projectPath => NormalizePath(Path.GetRelativePath(repositoryRoot, projectPath)))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(projectPaths, Has.Count.EqualTo(1), "Standalone solution must contain exactly one project.");
            Assert.That(relativeProjectPaths, Is.EqualTo(new[] { E2ETestsProjectPath }));
        });
    }

    internal static IReadOnlyList<string> ParseSolutionProjects(string solutionPath)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)
            ?? throw new InvalidOperationException($"Solution path '{solutionPath}' has no parent directory.");
        var projectPaths = new List<string>();
        var lineNumber = 0;

        foreach (var line in File.ReadLines(solutionPath))
        {
            lineNumber++;
            var trimmedLine = line.TrimStart();

            if (!trimmedLine.StartsWith("Project(", StringComparison.Ordinal))
            {
                continue;
            }

            var fields = trimmedLine.Split('"');
            if (fields.Length <= 5 || !fields[5].EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Malformed solution project declaration at '{solutionPath}' line {lineNumber}.");
            }

            projectPaths.Add(Path.GetFullPath(Path.Combine(solutionDirectory, ToHostPath(fields[5]))));
        }

        return projectPaths;
    }

    internal static ParsedProject ParseProject(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var targetFrameworks = document
            .Descendants()
            .Where(element => element.Name.LocalName == "TargetFramework")
            .Select(element => element.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var projectReferences = document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? "<missing Include>")
            .ToArray();
        var inlinePackageVersions = document
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Where(HasInlineVersion)
            .Select(element => element.Attribute("Include")?.Value ?? "<unknown package>")
            .ToArray();

        return new ParsedProject(targetFrameworks, projectReferences, inlinePackageVersions);
    }

    internal static IReadOnlyList<string> ParseEvaluatedProjectReferences(string assetsPath)
    {
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        if (!assets.RootElement.TryGetProperty("libraries", out var libraries))
        {
            throw new InvalidOperationException($"NuGet assets file '{assetsPath}' has no libraries object.");
        }

        return libraries
            .EnumerateObject()
            .Where(library =>
                library.Value.TryGetProperty("type", out var type) &&
                type.GetString() == "project")
            .Select(library => library.Name)
            .ToArray();
    }

    internal static bool IsIgnored(string gitIgnore, string relativePath)
    {
        var ignored = false;

        foreach (var rule in ParseIgnoreRules(gitIgnore))
        {
            if (Matches(rule, relativePath))
            {
                ignored = !rule.IsNegated;
            }
        }

        return ignored;
    }

    internal static string CreateSolution(params string[] projectPaths)
    {
        var projectEntries = projectPaths
            .Select((projectPath, index) =>
                $"Project(\"type\") = \"project-{index}\", \"{projectPath.Replace('/', '\\')}\", \"id-{index}\"{Environment.NewLine}EndProject");

        return string.Join(Environment.NewLine, projectEntries);
    }

    internal static string NormalizePath(string path) => path.Replace('\\', '/');

    internal static string ToHostPath(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    private static bool HasInlineVersion(XElement packageReference) =>
        !string.IsNullOrWhiteSpace(packageReference.Attribute("Version")?.Value) ||
        packageReference.Elements().Any(element =>
            element.Name.LocalName == "Version" && !string.IsNullOrWhiteSpace(element.Value));

    private static IEnumerable<IgnoreRule> ParseIgnoreRules(string gitIgnore)
    {
        using var reader = new StringReader(gitIgnore);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            var value = line.Trim();
            if (value.Length == 0 || value.StartsWith('#'))
            {
                continue;
            }

            var isNegated = value.StartsWith('!');
            var pattern = (isNegated ? value[1..] : value).Replace('\\', '/');
            if (pattern.Length != 0)
            {
                yield return new IgnoreRule(pattern, isNegated);
            }
        }
    }

    private static bool Matches(IgnoreRule rule, string relativePath)
    {
        var path = NormalizePath(relativePath);
        var pattern = rule.Pattern.TrimStart('/');
        var isDirectoryPattern = pattern.EndsWith('/');
        pattern = pattern.TrimEnd('/');

        if (pattern.Length == 0)
        {
            return false;
        }

        if (isDirectoryPattern)
        {
            if (pattern.Contains('/'))
            {
                return path.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith(pattern + "/", StringComparison.OrdinalIgnoreCase);
            }

            return path.Split('/').Any(segment => GlobMatches(pattern, segment));
        }

        return pattern.Contains('/')
            ? GlobMatches(pattern, path)
            : GlobMatches(pattern, Path.GetFileName(path));
    }

    private static bool GlobMatches(string pattern, string value)
    {
        var expression = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(value, expression, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    internal sealed record ParsedProject(
        IReadOnlyList<string> TargetFrameworks,
        IReadOnlyList<string> ProjectReferences,
        IReadOnlyList<string> InlinePackageVersions);

    private sealed record IgnoreRule(string Pattern, bool IsNegated);

    internal sealed class TemporaryFixture : IDisposable
    {
        public TemporaryFixture()
        {
            Path = Directory.CreateTempSubdirectory("lgym-e2e-boundary-").FullName;
        }

        public string Path { get; }

        public string Write(string relativePath, string content)
        {
            var path = System.IO.Path.Combine(Path, ToHostPath(relativePath));
            var directory = System.IO.Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException($"Fixture path '{relativePath}' has no parent directory.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
