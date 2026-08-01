namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class FileLengthGuardTests
{
    private const int MaxLineCount = 300;

    [Test]
    public void Production_Files_Should_Not_Exceed_Maximum_Line_Count()
    {
        var repoRoot = ResolveRepositoryRoot();
        var productionDirectories = new[]
        {
            Path.Combine(repoRoot, "LgymApi.Api"),
            Path.Combine(repoRoot, "LgymApi.Application"),
            Path.Combine(repoRoot, "LgymApi.Domain"),
            Path.Combine(repoRoot, "LgymApi.Infrastructure"),
            Path.Combine(repoRoot, "LgymApi.BackgroundWorker"),
            Path.Combine(repoRoot, "LgymApi.Resources")
        };

        var violations = new List<Violation>();
        foreach (var directory in productionDirectories)
        {
            var csFiles = Directory
                .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsInBuildArtifacts(path) && !IsExcludedFile(path))
                .ToList();

            foreach (var file in csFiles)
            {
                var lineCount = File.ReadAllLines(file).Length;
                if (lineCount > MaxLineCount)
                {
                    var relPath = Path.GetRelativePath(repoRoot, file);
                    violations.Add(new Violation(relPath, lineCount));
                }
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            $"Production files must not exceed {MaxLineCount} lines." + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    private static bool IsInBuildArtifacts(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcludedFile(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.EndsWith("/LgymApi.Api/Program.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var fileName = Path.GetFileName(path);
        if (fileName.EndsWith(".Designer.cs", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static string ResolveRepositoryRoot()
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

    private sealed record Violation(string File, int LineCount)
    {
        public override string ToString() => $"{File}: {LineCount} lines (max {MaxLineCount})";
    }
}
