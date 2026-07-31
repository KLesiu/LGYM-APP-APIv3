using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

internal static class PartialServiceContributionAnalyzer
{
    internal static void AssertExactPartialManifest(string repoRoot, Compilation compilation, IReadOnlyList<SyntaxTree> trees, IReadOnlyList<PartialFamily> families, IReadOnlyList<PartialContribution> contributions)
    {
        var typeNames = families.Select(family => family.TypeMetadataName).ToHashSet(StringComparer.Ordinal);
        var observed = trees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(declaration => declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
                .Where(declaration => typeNames.Contains(GetMetadataName((INamedTypeSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!), StringComparer.Ordinal))
                .Select(_ => RelativePath(repoRoot, tree.FilePath)))
            .ToHashSet(StringComparer.Ordinal);
        var expected = contributions.Select(contribution => contribution.RelativePath).ToHashSet(StringComparer.Ordinal);
        var missing = expected.Except(observed, StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var unexpected = observed.Except(expected, StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();

        if (missing.Length != 0 || unexpected.Length != 0)
        {
            throw new InvalidOperationException($"Approved partial source manifest mismatch. Missing: {Format(missing)}; unexpected: {Format(unexpected)}.");
        }
    }

    internal static void AssertFamilyRegistrations(string repoRoot, Compilation compilation, IReadOnlyList<SyntaxTree> trees, IReadOnlyList<PartialFamily> families)
    {
        foreach (var family in families)
        {
            var implementation = RequireType(compilation, family.TypeMetadataName);
            var service = family.InterfaceMetadataName is null ? implementation : RequireType(compilation, family.InterfaceMetadataName);
            var registrations = FindScopedRegistrations(compilation, trees, service, implementation).ToArray();
            if (registrations.Length != 1 || registrations[0] != family.RegistrationPath)
            {
                throw new InvalidOperationException($"{family.TypeMetadataName} must have exactly one scoped registration in '{family.RegistrationPath}', observed: {Format(registrations)}.");
            }
        }
    }

    internal static void AssertContribution(string repoRoot, Compilation compilation, IReadOnlyList<SyntaxTree> trees, IReadOnlyList<PartialFamily> families, PartialContribution contribution, IReadOnlyDictionary<IMethodSymbol, IReadOnlySet<IMethodSymbol>> callGraph)
    {
        var family = families.Single(candidate => candidate.TypeMetadataName == contribution.TypeMetadataName);
        var type = RequireType(compilation, contribution.TypeMetadataName);
        var member = FindMember(type, contribution.MemberName, contribution.RelativePath, repoRoot);
        var identity = $"{contribution.RelativePath}#{contribution.TypeMetadataName}.{contribution.MemberName}";
        if (member is null)
        {
            throw new InvalidOperationException($"Partial contributor '{identity}' does not declare a compiled member named '{contribution.MemberName}'.");
        }

        if (contribution.Route == ContributionRoute.DependencyInjection)
        {
            return;
        }

        var root = FindMember(type, contribution.RootMemberName, relativePath: null, repoRoot);
        if (root is null || !HasLiveRoot(compilation, trees, family, type, root, contribution.Route))
        {
            throw new InvalidOperationException($"Partial contributor '{identity}' has no compiled live root '{contribution.TypeMetadataName}.{contribution.RootMemberName}'.");
        }

        if (!HasCallPath(callGraph, root, member))
        {
            throw new InvalidOperationException($"Partial contributor '{identity}' has no compiled live path from '{contribution.TypeMetadataName}.{contribution.RootMemberName}'.");
        }
    }

    private static IMethodSymbol? FindMember(INamedTypeSymbol type, string memberName, string? relativePath, string repoRoot)
    {
        var candidates = memberName == ".ctor"
            ? type.InstanceConstructors.Where(constructor => !constructor.IsImplicitlyDeclared)
            : type.GetMembers(memberName).OfType<IMethodSymbol>();
        return candidates.SingleOrDefault(member => relativePath is null || member.Locations.Any(location => location.IsInSource && RelativePath(repoRoot, location.SourceTree!.FilePath) == relativePath));
    }

    private static bool HasLiveRoot(Compilation compilation, IReadOnlyList<SyntaxTree> trees, PartialFamily family, INamedTypeSymbol type, IMethodSymbol root, ContributionRoute route)
    {
        if (route == ContributionRoute.ConcreteCaller)
        {
            return trees.Any(tree => tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Any(invocation => SymbolEqualityComparer.Default.Equals((compilation.GetSemanticModel(tree).GetSymbolInfo(invocation).Symbol as IMethodSymbol)?.OriginalDefinition, root.OriginalDefinition)
                    && !SymbolEqualityComparer.Default.Equals(compilation.GetSemanticModel(tree).GetEnclosingSymbol(invocation.SpanStart)?.ContainingType, type)));
        }

        var contract = RequireType(compilation, family.InterfaceMetadataName!);
        return contract.GetMembers().OfType<IMethodSymbol>()
            .Any(member => SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), root));
    }

    internal static IReadOnlyDictionary<IMethodSymbol, IReadOnlySet<IMethodSymbol>> BuildCallGraph(Compilation compilation, IReadOnlyList<SyntaxTree> trees)
    {
        var graph = new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(SymbolEqualityComparer.Default);
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is not IMethodSymbol caller)
                {
                    continue;
                }

                foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    var callee = symbolInfo.Symbol as IMethodSymbol
                        ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                    if (callee is not null)
                    {
                        graph.TryAdd(caller.OriginalDefinition, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
                        graph[caller.OriginalDefinition].Add(callee.OriginalDefinition);
                    }
                }
            }
        }

        var readOnlyGraph = new Dictionary<IMethodSymbol, IReadOnlySet<IMethodSymbol>>(SymbolEqualityComparer.Default);
        foreach (var (caller, callees) in graph)
        {
            readOnlyGraph.Add(caller, callees);
        }

        return readOnlyGraph;
    }

    private static bool HasCallPath(IReadOnlyDictionary<IMethodSymbol, IReadOnlySet<IMethodSymbol>> graph, IMethodSymbol root, IMethodSymbol target)
    {
        var pending = new Queue<IMethodSymbol>([root.OriginalDefinition]);
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        while (pending.TryDequeue(out var current))
        {
            if (!visited.Add(current)) continue;
            if (SymbolEqualityComparer.Default.Equals(current, target.OriginalDefinition)) return true;
            if (graph.TryGetValue(current, out var callees)) foreach (var callee in callees) pending.Enqueue(callee);
        }

        return false;
    }

    private static IEnumerable<string> FindScopedRegistrations(Compilation compilation, IReadOnlyList<SyntaxTree> trees, INamedTypeSymbol service, INamedTypeSymbol implementation)
        => trees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(invocation => compilation.GetSemanticModel(tree).GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "AddScoped" } method
                && ((method.TypeArguments.Length == 2 && SymbolEqualityComparer.Default.Equals(method.TypeArguments[0], service) && SymbolEqualityComparer.Default.Equals(method.TypeArguments[1], implementation))
                    || (method.TypeArguments.Length == 1 && SymbolEqualityComparer.Default.Equals(service, implementation) && SymbolEqualityComparer.Default.Equals(method.TypeArguments[0], implementation))))
            .Select(_ => RelativePath(ArchitectureTestHelpers.ResolveRepositoryRoot(), tree.FilePath)));

    private static INamedTypeSymbol RequireType(Compilation compilation, string metadataName)
        => compilation.GetTypeByMetadataName(metadataName) ?? throw new InvalidOperationException($"Compiled type '{metadataName}' was not found.");

    private static string GetMetadataName(INamedTypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    private static string RelativePath(string root, string path) => string.IsNullOrEmpty(root) ? ArchitectureTestHelpers.NormalizePath(path) : ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(root, path));

    private static string Format(IEnumerable<string> values)
    {
        var materialized = values.ToArray();
        return materialized.Length == 0 ? "none" : string.Join(", ", materialized);
    }

    internal static (CSharpCompilation Compilation, IReadOnlyList<SyntaxTree> Trees) CreateFixture(string primarySource, string primaryPath, string partialSource)
    {
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(primarySource, path: primaryPath),
            CSharpSyntaxTree.ParseText(partialSource, path: "Fixtures/EmptyPartial.cs")
        };
        return (ArchitectureTestHelpers.CreateCompilation(trees.ToList()), trees);
    }

    internal sealed record PartialFamily(string TypeMetadataName, string? InterfaceMetadataName, string RegistrationPath);
    internal sealed record PartialContribution(string RelativePath, string TypeMetadataName, string MemberName, string RootMemberName, ContributionRoute Route);
    internal enum ContributionRoute { Interface, DependencyInjection, ConcreteCaller }
}
