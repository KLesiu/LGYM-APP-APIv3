using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

internal static class BusinessServiceDependencyAnalyzer
{
    private const string ServiceProviderMetadataName = "System.IServiceProvider";
    private const string ScopeFactoryMetadataName = "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory";
    private const string ServiceProviderExtensionsMetadataName = "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions";
    private const string ActivatorUtilitiesMetadataName = "Microsoft.Extensions.DependencyInjection.ActivatorUtilities";

    private static readonly string[] AggregateSuffixes =
    [
        "Dependencies",
        "DependencyBag",
        "DependencyAggregate"
    ];

    private static readonly HashSet<string> ServiceResolutionMethodNames =
    [
        "GetService",
        "GetRequiredService"
    ];

    private static readonly HashSet<string> ScopeCreationMethodNames =
    [
        "CreateScope",
        "CreateAsyncScope"
    ];

    public static IReadOnlyList<BusinessServiceDependencyViolation> Analyze(
        CSharpCompilation compilation,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        string repositoryRoot)
    {
        var violations = new List<BusinessServiceDependencyViolation>();

        foreach (var tree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetCompilationUnitRoot();

            foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var serviceSymbol = semanticModel.GetDeclaredSymbol(declaration);
                if (!IsBusinessServiceOrUseCase(serviceSymbol))
                {
                    continue;
                }

                AnalyzeConstructorParameters(
                    declaration,
                    serviceSymbol!,
                    semanticModel,
                    tree,
                    repositoryRoot,
                    violations);
                AnalyzeServiceLocatorTypes(
                    declaration,
                    serviceSymbol!,
                    semanticModel,
                    tree,
                    repositoryRoot,
                    violations);
                AnalyzeServiceLocatorInvocations(
                    declaration,
                    serviceSymbol!,
                    semanticModel,
                    tree,
                    repositoryRoot,
                    violations);
            }
        }

        return violations
            .DistinctBy(violation => new
            {
                violation.File,
                violation.Line,
                violation.ServiceName,
                violation.Kind,
                violation.Dependency
            })
            .OrderBy(violation => violation.File, StringComparer.Ordinal)
            .ThenBy(violation => violation.Line)
            .ThenBy(violation => violation.ServiceName, StringComparer.Ordinal)
            .ThenBy(violation => violation.Kind)
            .ThenBy(violation => violation.Dependency, StringComparer.Ordinal)
            .ToList();
    }

    private static void AnalyzeConstructorParameters(
        ClassDeclarationSyntax declaration,
        INamedTypeSymbol serviceSymbol,
        SemanticModel semanticModel,
        SyntaxTree tree,
        string repositoryRoot,
        ICollection<BusinessServiceDependencyViolation> violations)
    {
        IEnumerable<ParameterSyntax> parameters = declaration.Members
            .OfType<ConstructorDeclarationSyntax>()
            .SelectMany(constructor => constructor.ParameterList.Parameters);

        if (declaration.ParameterList != null)
        {
            parameters = parameters.Concat(declaration.ParameterList.Parameters);
        }

        foreach (var parameter in parameters)
        {
            var parameterType = GetParameterType(parameter, semanticModel);
            var aggregateType = FindNestedNamedType(parameterType, IsDependencyAggregate);
            if (aggregateType == null)
            {
                continue;
            }

            violations.Add(CreateViolation(
                tree,
                parameter,
                repositoryRoot,
                serviceSymbol.Name,
                BusinessServiceDependencyViolationKind.DependencyAggregate,
                aggregateType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                "constructor parameters must expose each dependency directly"));
        }
    }

    private static void AnalyzeServiceLocatorTypes(
        ClassDeclarationSyntax declaration,
        INamedTypeSymbol serviceSymbol,
        SemanticModel semanticModel,
        SyntaxTree tree,
        string repositoryRoot,
        ICollection<BusinessServiceDependencyViolation> violations)
    {
        foreach (var typeSyntax in GetOwnedDescendants<TypeSyntax>(declaration))
        {
            var referencedType = semanticModel.GetTypeInfo(typeSyntax).Type;
            var locatorType = FindNestedNamedType(referencedType, IsServiceLocatorType);
            if (locatorType == null)
            {
                continue;
            }

            violations.Add(CreateViolation(
                tree,
                typeSyntax,
                repositoryRoot,
                serviceSymbol.Name,
                BusinessServiceDependencyViolationKind.ServiceLocatorType,
                locatorType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                "business services and use cases must receive focused dependencies instead of a service locator"));
        }
    }

    private static void AnalyzeServiceLocatorInvocations(
        ClassDeclarationSyntax declaration,
        INamedTypeSymbol serviceSymbol,
        SemanticModel semanticModel,
        SyntaxTree tree,
        string repositoryRoot,
        ICollection<BusinessServiceDependencyViolation> violations)
    {
        foreach (var invocation in GetOwnedDescendants<InvocationExpressionSyntax>(declaration))
        {
            var method = ResolveInvokedMethod(invocation, semanticModel);
            if (method == null || !IsServiceLocatorInvocation(method))
            {
                continue;
            }

            violations.Add(CreateViolation(
                tree,
                invocation,
                repositoryRoot,
                serviceSymbol.Name,
                BusinessServiceDependencyViolationKind.ServiceLocatorInvocation,
                method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                "dependency resolution and scope creation belong in composition code"));
        }
    }

    private static bool IsBusinessServiceOrUseCase(INamedTypeSymbol? typeSymbol)
    {
        return typeSymbol is { IsAbstract: false, IsStatic: false }
            && (typeSymbol.Name.EndsWith("Service", StringComparison.Ordinal)
                || typeSymbol.Name.EndsWith("UseCase", StringComparison.Ordinal));
    }

    private static ITypeSymbol? GetParameterType(ParameterSyntax parameter, SemanticModel semanticModel)
    {
        return semanticModel.GetDeclaredSymbol(parameter)?.Type
            ?? (parameter.Type == null ? null : semanticModel.GetTypeInfo(parameter.Type).Type);
    }

    private static INamedTypeSymbol? FindNestedNamedType(
        ITypeSymbol? typeSymbol,
        Func<INamedTypeSymbol, bool> predicate)
    {
        switch (typeSymbol)
        {
            case INamedTypeSymbol namedType when predicate(namedType):
                return namedType;
            case INamedTypeSymbol namedType:
                return namedType.TypeArguments
                    .Select(typeArgument => FindNestedNamedType(typeArgument, predicate))
                    .FirstOrDefault(match => match != null);
            case IArrayTypeSymbol arrayType:
                return FindNestedNamedType(arrayType.ElementType, predicate);
            case IPointerTypeSymbol pointerType:
                return FindNestedNamedType(pointerType.PointedAtType, predicate);
            default:
                return null;
        }
    }

    private static bool IsDependencyAggregate(INamedTypeSymbol typeSymbol)
    {
        return AggregateSuffixes.Any(suffix => typeSymbol.Name.EndsWith(suffix, StringComparison.Ordinal));
    }

    private static bool IsServiceLocatorType(INamedTypeSymbol typeSymbol)
    {
        return IsOrImplements(typeSymbol, ServiceProviderMetadataName)
            || IsOrImplements(typeSymbol, ScopeFactoryMetadataName);
    }

    private static IMethodSymbol? ResolveInvokedMethod(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        return symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }

    private static bool IsServiceLocatorInvocation(IMethodSymbol method)
    {
        var resolvedMethod = method.ReducedFrom ?? method;
        var containingType = resolvedMethod.ContainingType;

        if (HasMetadataName(containingType, ActivatorUtilitiesMetadataName))
        {
            return true;
        }

        if (ServiceResolutionMethodNames.Contains(resolvedMethod.Name))
        {
            return HasMetadataName(containingType, ServiceProviderExtensionsMetadataName)
                || IsOrImplements(containingType, ServiceProviderMetadataName);
        }

        if (ScopeCreationMethodNames.Contains(resolvedMethod.Name))
        {
            return HasMetadataName(containingType, ServiceProviderExtensionsMetadataName)
                || IsOrImplements(containingType, ServiceProviderMetadataName)
                || IsOrImplements(containingType, ScopeFactoryMetadataName);
        }

        return false;
    }

    private static bool IsOrImplements(INamedTypeSymbol typeSymbol, string metadataName)
    {
        return HasMetadataName(typeSymbol, metadataName)
            || typeSymbol.AllInterfaces.Any(@interface => HasMetadataName(@interface, metadataName));
    }

    private static bool HasMetadataName(INamedTypeSymbol typeSymbol, string metadataName)
    {
        return string.Equals(
            typeSymbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            metadataName,
            StringComparison.Ordinal);
    }

    private static IEnumerable<TNode> GetOwnedDescendants<TNode>(ClassDeclarationSyntax declaration)
        where TNode : SyntaxNode
    {
        return declaration
            .DescendantNodes()
            .OfType<TNode>()
            .Where(node => node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() == declaration);
    }

    private static BusinessServiceDependencyViolation CreateViolation(
        SyntaxTree tree,
        SyntaxNode node,
        string repositoryRoot,
        string serviceName,
        BusinessServiceDependencyViolationKind kind,
        string dependency,
        string reason)
    {
        var lineSpan = tree.GetLineSpan(node.Span);
        var file = string.IsNullOrWhiteSpace(repositoryRoot)
            ? tree.FilePath
            : Path.GetRelativePath(repositoryRoot, tree.FilePath);

        return new BusinessServiceDependencyViolation(
            file,
            lineSpan.StartLinePosition.Line + 1,
            serviceName,
            kind,
            dependency,
            reason);
    }
}

internal enum BusinessServiceDependencyViolationKind
{
    DependencyAggregate,
    ServiceLocatorType,
    ServiceLocatorInvocation
}

internal sealed record BusinessServiceDependencyViolation(
    string File,
    int Line,
    string ServiceName,
    BusinessServiceDependencyViolationKind Kind,
    string Dependency,
    string Reason)
{
    public override string ToString()
        => $"{File}:{Line} -> {ServiceName} [{Kind}] {Dependency}: {Reason}";
}

internal static class BusinessServiceDependencyFixture
{
    public static IReadOnlyList<BusinessServiceDependencyViolation> Analyze(params string[] sources)
    {
        _ = typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly;
        _ = typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory).Assembly;
        _ = typeof(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions).Assembly;
        _ = typeof(Microsoft.Extensions.DependencyInjection.ActivatorUtilities).Assembly;

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var syntaxTrees = sources
            .Select((source, index) => (SyntaxTree)CSharpSyntaxTree.ParseText(
                source,
                parseOptions,
                path: $"Fixture{index + 1}.cs"))
            .ToList();
        var compilation = ArchitectureTestHelpers.CreateCompilation(syntaxTrees);
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.That(
            errors,
            Is.Empty,
            "Semantic guard fixture must compile before it is analyzed." + Environment.NewLine +
            string.Join(Environment.NewLine, errors));

        return BusinessServiceDependencyAnalyzer.Analyze(compilation, syntaxTrees, repositoryRoot: string.Empty);
    }
}
