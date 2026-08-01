using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

internal static class TypedEntityIdBoundaryGuard
{
    private const string EntityBaseMetadataName = "LgymApi.Domain.Entities.EntityBase`1";
    private const string TypedIdMetadataName = "LgymApi.Domain.ValueObjects.Id`1";

    private static readonly HashSet<string> ExactPolymorphicEntityIdExceptions = new(StringComparer.Ordinal)
    {
        "LgymApi.Domain.Entities.PushNotificationMessage.EntityId",
        "LgymApi.Application.Notifications.Contracts.Push.PushEventPayload.EntityId"
    };

    internal static IReadOnlyList<TypedEntityIdViolation> Collect(
        CSharpCompilation compilation,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        IReadOnlySet<string>? exactPolymorphicEntityIdExceptions = null)
    {
        var typedIdDefinition = compilation.GetTypeByMetadataName(TypedIdMetadataName);
        var entityBaseDefinition = compilation.GetTypeByMetadataName(EntityBaseMetadataName);

        Assert.That(typedIdDefinition, Is.Not.Null, $"Unable to resolve '{TypedIdMetadataName}'.");
        Assert.That(entityBaseDefinition, Is.Not.Null, $"Unable to resolve '{EntityBaseMetadataName}'.");

        var entities = GetEntityTypes(compilation, entityBaseDefinition!);
        var polymorphicEntityIdExceptions = exactPolymorphicEntityIdExceptions ?? ExactPolymorphicEntityIdExceptions;
        var violations = new List<TypedEntityIdViolation>();

        foreach (var tree in syntaxTrees.Where(tree => IsScopedInternalPath(tree.FilePath)))
        {
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var root = tree.GetCompilationUnitRoot();

            foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                AddViolationIfNeeded(semanticModel.GetDeclaredSymbol(property), property.Type, semanticModel, typedIdDefinition!, entities, polymorphicEntityIdExceptions, violations);
            }

            foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            {
                foreach (var variable in field.Declaration.Variables)
                {
                    AddViolationIfNeeded(semanticModel.GetDeclaredSymbol(variable), field.Declaration.Type, semanticModel, typedIdDefinition!, entities, polymorphicEntityIdExceptions, violations);
                }
            }

            foreach (var parameter in root.DescendantNodes().OfType<ParameterSyntax>())
            {
                AddViolationIfNeeded(GetParameterMemberSymbol(semanticModel.GetDeclaredSymbol(parameter)), parameter.Type, semanticModel, typedIdDefinition!, entities, polymorphicEntityIdExceptions, violations);
            }
        }

        return violations
            .DistinctBy(violation => GetFullyQualifiedSymbolName(violation.Symbol), StringComparer.Ordinal)
            .OrderBy(violation => GetFullyQualifiedSymbolName(violation.Symbol), StringComparer.Ordinal)
            .ToList();
    }

    private static void AddViolationIfNeeded(
        ISymbol? symbol,
        TypeSyntax? typeSyntax,
        SemanticModel semanticModel,
        INamedTypeSymbol typedIdDefinition,
        IReadOnlyDictionary<string, INamedTypeSymbol> entities,
        IReadOnlySet<string> exactPolymorphicEntityIdExceptions,
        List<TypedEntityIdViolation> violations)
    {
        if (symbol == null || typeSyntax == null || IsExactPolymorphicEntityIdException(symbol, exactPolymorphicEntityIdExceptions))
        {
            return;
        }

        var expectedEntity = ResolveExpectedEntity(symbol, entities);
        if (expectedEntity == null && !string.Equals(symbol.Name, "EntityId", StringComparison.Ordinal))
        {
            return;
        }

        var actualType = semanticModel.GetTypeInfo(typeSyntax).Type;
        if (actualType == null || IsExpectedTypedId(actualType, expectedEntity, typedIdDefinition))
        {
            return;
        }

        if (IsRawStringOrGuid(actualType) || IsTypedId(actualType, typedIdDefinition))
        {
            violations.Add(new TypedEntityIdViolation(symbol, FormatExpectedType(expectedEntity)));
        }
    }

    private static IReadOnlyDictionary<string, INamedTypeSymbol> GetEntityTypes(CSharpCompilation compilation, INamedTypeSymbol entityBaseDefinition)
    {
        return compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            .Select(declaration => compilation.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration) as INamedTypeSymbol)
            .Where(symbol => symbol != null && InheritsFromEntityBase(symbol, entityBaseDefinition))
            .Cast<INamedTypeSymbol>()
            .ToDictionary(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static bool InheritsFromEntityBase(INamedTypeSymbol symbol, INamedTypeSymbol entityBaseDefinition)
    {
        for (var current = symbol.BaseType; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, entityBaseDefinition))
            {
                return true;
            }
        }

        return false;
    }

    private static INamedTypeSymbol? ResolveExpectedEntity(ISymbol symbol, IReadOnlyDictionary<string, INamedTypeSymbol> entities)
    {
        if (symbol.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && symbol.Name.Length > 2)
        {
            var entityName = symbol.Name[..^2];
            if (entities.TryGetValue(entityName, out var namedEntity))
            {
                return namedEntity;
            }
        }

        if (!string.Equals(symbol.Name, "Id", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var owner = ArchitectureTestHelpers.GetOwnedNamedTypeSymbol(symbol);
        if (owner == null || owner.Name.EndsWith("ChartData", StringComparison.Ordinal))
        {
            return null;
        }

        if (entities.Values.Any(entity => SymbolEqualityComparer.Default.Equals(entity, owner)))
        {
            return owner;
        }

        return entities.Values
            .Where(entity => owner.Name.StartsWith(entity.Name, StringComparison.Ordinal))
            .OrderByDescending(entity => entity.Name.Length)
            .FirstOrDefault();
    }

    private static ISymbol? GetParameterMemberSymbol(IParameterSymbol? parameter)
    {
        if (parameter?.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
            || !constructor.ContainingType.IsRecord)
        {
            return parameter;
        }

        var recordProperty = constructor.ContainingType.GetMembers(parameter.Name).OfType<IPropertySymbol>().SingleOrDefault();
        return recordProperty == null ? parameter : recordProperty;
    }

    private static bool IsExpectedTypedId(ITypeSymbol actualType, INamedTypeSymbol? expectedEntity, INamedTypeSymbol typedIdDefinition)
    {
        var namedType = UnwrapNullable(actualType);
        if (namedType == null || !SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, typedIdDefinition))
        {
            return false;
        }

        return expectedEntity == null || SymbolEqualityComparer.Default.Equals(namedType.TypeArguments[0], expectedEntity);
    }

    private static bool IsTypedId(ITypeSymbol type, INamedTypeSymbol typedIdDefinition)
    {
        return UnwrapNullable(type) is { } namedType
            && SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, typedIdDefinition);
    }

    private static INamedTypeSymbol? UnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableType)
        {
            return nullableType.TypeArguments[0] as INamedTypeSymbol;
        }

        return type as INamedTypeSymbol;
    }

    private static bool IsRawStringOrGuid(ITypeSymbol type)
    {
        var namedType = UnwrapNullable(type);
        return namedType?.SpecialType == SpecialType.System_String
            || IsGuid(namedType);
    }

    private static bool IsGuid(INamedTypeSymbol? type)
    {
        return type != null
            && string.Equals(type.Name, "Guid", StringComparison.Ordinal)
            && string.Equals(type.ContainingNamespace.ToDisplayString(), "System", StringComparison.Ordinal);
    }

    private static bool IsExactPolymorphicEntityIdException(ISymbol symbol, IReadOnlySet<string> exactPolymorphicEntityIdExceptions)
    {
        return exactPolymorphicEntityIdExceptions.Contains(GetFullyQualifiedSymbolName(symbol));
    }

    private static string FormatExpectedType(INamedTypeSymbol? entity)
    {
        return entity == null ? "Id<TEntity>" : $"Id<{entity.Name}>";
    }

    private static string GetFullyQualifiedSymbolName(ISymbol symbol)
    {
        var parts = new Stack<string>();
        for (ISymbol? current = symbol; current != null; current = current.ContainingSymbol)
        {
            if (current is IAssemblySymbol or IModuleSymbol or INamespaceSymbol { IsGlobalNamespace: true })
            {
                continue;
            }

            parts.Push(current.MetadataName);
        }

        return string.Join(".", parts);
    }

    private static bool IsScopedInternalPath(string path)
    {
        var normalized = ArchitectureTestHelpers.NormalizePath(path);
        return !normalized.EndsWith("/LgymApi.Infrastructure/Data/TypedIdValueConverter.cs", StringComparison.OrdinalIgnoreCase)
            && (IsUnder(normalized, "LgymApi.Domain/Entities/")
                || IsUnder(normalized, "LgymApi.Application/")
                || IsUnder(normalized, "LgymApi.Infrastructure/Repositories/")
                || IsUnder(normalized, "LgymApi.BackgroundWorker/")
                || IsUnder(normalized, "LgymApi.BackgroundWorker.Common/"));
    }

    private static bool IsUnder(string path, string projectRelativePath)
    {
        return path.StartsWith(projectRelativePath, StringComparison.OrdinalIgnoreCase)
            || path.Contains($"/{projectRelativePath}", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record TypedEntityIdViolation(ISymbol Symbol, string ExpectedType)
{
    public override string ToString() => $"{Symbol.ToDisplayString()}: expected {ExpectedType}";
}
