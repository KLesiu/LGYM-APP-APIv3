using System.Reflection;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

internal static class ProjectReferenceSourceScanner
{
    public static ProjectImportFixture Scan(string repositoryRoot)
    {
        var projectPaths = LoadSolutionProjectPaths(repositoryRoot);
        var projectNames = projectPaths
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToHashSet(StringComparer.Ordinal);
        var edgeIdentities = projectPaths
            .SelectMany(ArchitectureTestHelpers.ParseProjectReferences)
            .Select(edge => $"{edge.SourceProject} -> {edge.TargetProject}")
            .ToArray();
        var analyzerEdges = projectPaths
            .SelectMany(ParseAnalyzerEdges)
            .ToArray();
        var projectAssemblies = projectPaths.ToDictionary(
            projectPath => Path.GetFileNameWithoutExtension(projectPath)!,
            projectPath => FindProjectAssembly(projectPath),
            StringComparer.Ordinal);
        var metadataReferences = CollectMetadataReferences(projectPaths, projectAssemblies);
        var uses = new List<ProjectImportUse>();

        foreach (var projectPath in projectPaths)
        {
            CollectProjectUses(repositoryRoot, projectPath, projectNames, metadataReferences, uses);
        }

        return new ProjectImportFixture(
            projectNames.OrderBy(project => project, StringComparer.Ordinal).ToArray(),
            edgeIdentities,
            uses.ToArray(),
            analyzerEdges,
            ProjectReferenceGraphManifest.ForbiddenEdgeIdentities.ToArray(),
            ProjectReferenceGraphManifest.TopologicalOrder);
    }

    private static void CollectProjectUses(
        string repositoryRoot,
        string projectPath,
        IReadOnlySet<string> projectNames,
        IReadOnlyList<PortableExecutableReference> metadataReferences,
        ICollection<ProjectImportUse> uses)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var sourcePaths = Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var generatedGlobalUsingsRoot = Path.Combine(projectDirectory, "obj", "Release");
        var generatedGlobalUsings = Directory.Exists(generatedGlobalUsingsRoot)
            ? Directory.EnumerateFiles(
                generatedGlobalUsingsRoot,
                "*GlobalUsings.g.cs",
                SearchOption.AllDirectories)
            : [];
        var syntaxTrees = sourcePaths
            .Concat(generatedGlobalUsings)
            .Select(path => CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                path))
            .ToArray();
        var references = metadataReferences
            .Where(reference => !string.Equals(
                Path.GetFileNameWithoutExtension(reference.FilePath),
                projectName,
                StringComparison.Ordinal))
            .ToArray();
        var outputKind = string.Equals(projectName, "LgymApi.Api", StringComparison.Ordinal)
            ? OutputKind.ConsoleApplication
            : OutputKind.DynamicallyLinkedLibrary;
        var compilation = CSharpCompilation.Create(
            projectName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(outputKind, allowUnsafe: true));

        foreach (var tree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                var symbolInfo = semanticModel.GetSymbolInfo(name);
                var symbols = symbolInfo.Symbol is null ? symbolInfo.CandidateSymbols : [symbolInfo.Symbol];

                foreach (var symbol in symbols)
                {
                    var resolvedSymbol = symbol is IAliasSymbol alias ? alias.Target : symbol;
                    var targetProject = resolvedSymbol.ContainingAssembly?.Name;
                    if (targetProject is null
                        || !projectNames.Contains(targetProject)
                        || string.Equals(targetProject, projectName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var edgeIdentity = $"{projectName} -> {targetProject}";
                    uses.Add(new ProjectImportUse(
                        projectName,
                        targetProject,
                        Path.GetRelativePath(repositoryRoot, tree.FilePath).Replace('\\', '/'),
                        name.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        resolvedSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                }
            }
        }
    }

    private static string[] LoadSolutionProjectPaths(string repositoryRoot)
    {
        return File.ReadLines(Path.Combine(repositoryRoot, "LgymApi.sln"))
            .Where(line => line.StartsWith("Project(", StringComparison.Ordinal))
            .Select(line => line.Split('"'))
            .Where(parts => parts.Length > 5 && parts[5].EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(parts => Path.GetFullPath(Path.Combine(repositoryRoot, ArchitectureTestHelpers.ToHostPath(parts[5]))))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> ParseAnalyzerEdges(string projectPath)
    {
        var sourceProject = Path.GetFileNameWithoutExtension(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;

        return XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Where(element => string.Equals(
                element.Attribute("OutputItemType")?.Value,
                "Analyzer",
                StringComparison.Ordinal))
            .Select(element => Path.GetFileNameWithoutExtension(Path.GetFullPath(
                element.Attribute("Include")!.Value,
                projectDirectory)))
            .Select(targetProject => $"{sourceProject} -> {targetProject}");
    }

    private static PortableExecutableReference[] CollectMetadataReferences(
        IReadOnlyList<string> projectPaths,
        IReadOnlyDictionary<string, string> projectAssemblies)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        foreach (var path in trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            paths.TryAdd(Path.GetFileName(path), path);
        }

        AddManagedAssemblies(AppContext.BaseDirectory, paths);
        foreach (var projectPath in projectPaths)
        {
            AddManagedAssemblies(Path.Combine(Path.GetDirectoryName(projectPath)!, "bin", "Release"), paths);
        }

        foreach (var (projectName, path) in projectAssemblies)
        {
            paths[projectName + ".dll"] = path;
        }

        return paths.Values.Select(path => MetadataReference.CreateFromFile(path)).ToArray();
    }

    private static void AddManagedAssemblies(string directory, IDictionary<string, string> paths)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                _ = AssemblyName.GetAssemblyName(path);
                paths.TryAdd(Path.GetFileName(path), path);
            }
            catch (BadImageFormatException)
            {
            }
        }
    }

    private static string FindProjectAssembly(string projectPath)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var testOutputAssembly = Path.Combine(AppContext.BaseDirectory, projectName + ".dll");
        if (File.Exists(testOutputAssembly))
        {
            return testOutputAssembly;
        }

        var releaseDirectory = Path.Combine(Path.GetDirectoryName(projectPath)!, "bin", "Release");
        var assemblyPath = Directory
            .EnumerateFiles(releaseDirectory, projectName + ".dll", SearchOption.AllDirectories)
            .Where(path => !Normalize(path).Contains("/ref/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Length)
            .ThenBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();

        return assemblyPath ?? throw new InvalidOperationException(
            $"Release assembly for '{projectName}' was not found. Build LgymApi.sln before running import guards.");
    }

    private static bool IsBuildArtifact(string path)
    {
        var normalized = Normalize(path);
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
