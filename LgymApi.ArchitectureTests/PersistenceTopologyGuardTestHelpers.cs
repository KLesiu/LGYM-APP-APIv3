using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

internal static class PersistenceTopologyGuardTestHelpers
{
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet<T>";
    private const string ConfigurationMetadataName = "Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<T>";
    private const string MigrationMetadataName = "Microsoft.EntityFrameworkCore.Migrations.Migration";
    private const string ModelSnapshotMetadataName = "Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot";
    private const string DbContextAttributeMetadataName = "Microsoft.EntityFrameworkCore.Infrastructure.DbContextAttribute";
    private const string DesignTimeFactoryMetadataName = "Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<TContext>";

    public static IReadOnlyList<TopologySource> LoadProductionSources(string repoRoot)
    {
        return Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !ArchitectureTestHelpers.IsInBuildArtifacts(path))
            .Where(path => !IsTestProject(path))
            .SelectMany(project => Directory.EnumerateFiles(Path.GetDirectoryName(project)!, "*.cs", SearchOption.AllDirectories))
            .Where(path => !ArchitectureTestHelpers.IsInBuildArtifacts(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new TopologySource(
                ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, path)),
                File.ReadAllText(path)))
            .ToList();
    }

    public static PersistenceTopologyAnalysis Analyze(IEnumerable<TopologySource> sourceFiles)
    {
        var semanticReferenceAssemblies = new[]
        {
            typeof(DbContext).Assembly,
            typeof(DbSet<>).Assembly,
            typeof(LgymApi.Domain.Entities.User).Assembly
        };
        var sources = sourceFiles.ToList();
        var trees = sources
            .Select(source => CSharpSyntaxTree.ParseText(source.Content, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest), source.Path))
            .ToList();
        var compilation = ArchitectureTestHelpers.CreateCompilation(trees);
        GC.KeepAlive(semanticReferenceAssemblies);
        var dbContexts = new List<DbContextTopologyDeclaration>();
        var designTimeFactories = new List<DesignTimeFactoryTopologyDeclaration>();
        var dbSets = new List<DbSetTopologyDeclaration>();
        var configurations = new List<EntityTypeConfigurationTopologyDeclaration>();
        var registrations = new List<RegistrarTopologyDeclaration>();
        var migrationTypes = new List<MigrationTypeTopologyDeclaration>();
        var migrationContexts = new List<MigrationContextTopologyDeclaration>();
        var ensureCreated = new List<EnsureCreatedTopologyViolation>();
        var schemaSplits = new List<SchemaSplitTopologyViolation>();

        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetCompilationUnitRoot();
            var sourcePath = ArchitectureTestHelpers.NormalizePath(tree.FilePath);

            foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol || symbol.IsAbstract)
                {
                    continue;
                }

                if (IsDbContextDeclaration(symbol, declaration))
                {
                    dbContexts.Add(new DbContextTopologyDeclaration(symbol.Name, sourcePath));
                }

                var designTimeContext = GetDesignTimeFactoryContext(symbol) ?? GetDesignTimeFactoryContext(declaration);
                if (designTimeContext is not null)
                {
                    designTimeFactories.Add(new DesignTimeFactoryTopologyDeclaration(symbol.Name, designTimeContext, sourcePath));
                }

                var configuredEntity = GetConfiguredEntity(symbol) ?? GetConfiguredEntity(declaration);
                if (configuredEntity != null)
                {
                    configurations.Add(new EntityTypeConfigurationTopologyDeclaration(
                        configuredEntity,
                        symbol.Name,
                        sourcePath));
                }

                var isSnapshot = Inherits(symbol, ModelSnapshotMetadataName) || HasBaseType(declaration, "ModelSnapshot");
                if (Inherits(symbol, MigrationMetadataName) || isSnapshot || HasBaseType(declaration, "Migration"))
                {
                    migrationTypes.Add(new MigrationTypeTopologyDeclaration(
                        GetMigrationRoot(sourcePath),
                        symbol.Name,
                        sourcePath,
                        isSnapshot));
                }
            }

            foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                if (property.ExplicitInterfaceSpecifier is not null)
                {
                    continue;
                }

                var entityType = GetDbSetEntity(model, property);
                if (entityType == null || property.Parent is not ClassDeclarationSyntax containingDeclaration ||
                    model.GetDeclaredSymbol(containingDeclaration) is not INamedTypeSymbol containingType ||
                    !IsDbContextDeclaration(containingType, containingDeclaration))
                {
                    continue;
                }

                dbSets.Add(new DbSetTopologyDeclaration(
                    containingType.Name,
                    property.Identifier.ValueText,
                    entityType,
                    model.GetDeclaredSymbol(property)?.DeclaredAccessibility == Accessibility.Public,
                    sourcePath));
            }

            if (sourcePath.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
                {
                    if (attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is not TypeOfExpressionSyntax typeOf ||
                        !IsDbContextAttribute(model, attribute))
                    {
                        continue;
                    }

                    migrationContexts.Add(new MigrationContextTopologyDeclaration(
                        GetMigrationRoot(sourcePath),
                        GetSimpleName(typeOf.Type),
                        sourcePath));
                }
            }

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (HasExplicitSchema(model, invocation))
                {
                    schemaSplits.Add(new SchemaSplitTopologyViolation(
                        sourcePath,
                        invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        invocation.ToString()));
                }

                if (!IsEnsureCreated(model, invocation) || IsNonRelationalOnly(model, invocation))
                {
                    continue;
                }

                ensureCreated.Add(new EnsureCreatedTopologyViolation(
                    sourcePath,
                    invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    invocation.ToString()));
            }
        }

        var configurationEntities = configurations
            .GroupBy(configuration => configuration.ConfigurationType, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single().EntityType, StringComparer.Ordinal);
        var identityRegistrar = trees.SingleOrDefault(tree =>
            Path.GetFileName(tree.FilePath).Equals("IdentityModelConfigurationRegistrar.cs", StringComparison.Ordinal));
        var trainingPlanningRegistrar = trees.SingleOrDefault(tree =>
            Path.GetFileName(tree.FilePath).Equals("TrainingPlanningModelConfigurationRegistrar.cs", StringComparison.Ordinal));
        var notificationsRegistrar = trees.SingleOrDefault(tree =>
            Path.GetFileName(tree.FilePath).Equals("NotificationsModelConfigurationRegistrar.cs", StringComparison.Ordinal));
        foreach (var tree in trees.Where(tree => Path.GetFileName(tree.FilePath).Equals("AppDbContextEntityTypeConfigurationRegistrar.cs", StringComparison.Ordinal)))
        {
            var root = tree.GetCompilationUnitRoot();
            if (identityRegistrar != null && root.ToFullString().Contains("IdentityModelConfigurationRegistrar.Apply", StringComparison.Ordinal))
            {
                foreach (var creation in identityRegistrar.GetCompilationUnitRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                {
                    var configurationType = GetSimpleName(creation.Type);
                    if (configurationEntities.TryGetValue(configurationType, out var entityType))
                    {
                        registrations.Add(new RegistrarTopologyDeclaration(entityType, configurationType, ArchitectureTestHelpers.NormalizePath(tree.FilePath)));
                    }
                }
            }
            if (trainingPlanningRegistrar != null && root.ToFullString().Contains("TrainingPlanningModelConfigurationRegistrar.Apply", StringComparison.Ordinal))
            {
                foreach (var creation in trainingPlanningRegistrar.GetCompilationUnitRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                {
                    var configurationType = GetSimpleName(creation.Type);
                    if (configurationEntities.TryGetValue(configurationType, out var entityType))
                    {
                        registrations.Add(new RegistrarTopologyDeclaration(entityType, configurationType, ArchitectureTestHelpers.NormalizePath(tree.FilePath)));
                    }
                }
            }
            foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                var configurationType = GetSimpleName(creation.Type);
                if (configurationType == "ApiIdempotencyRecordEntityTypeConfiguration" &&
                    notificationsRegistrar != null &&
                    root.ToFullString().Contains("NotificationsModelConfigurationRegistrar.Apply", StringComparison.Ordinal))
                {
                    foreach (var notificationCreation in notificationsRegistrar.GetCompilationUnitRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                    {
                        var notificationConfigurationType = GetSimpleName(notificationCreation.Type);
                        if (configurationEntities.TryGetValue(notificationConfigurationType, out var notificationEntityType))
                        {
                            registrations.Add(new RegistrarTopologyDeclaration(notificationEntityType, notificationConfigurationType, ArchitectureTestHelpers.NormalizePath(tree.FilePath)));
                        }
                    }
                }
                if (configurationEntities.TryGetValue(configurationType, out var entityType))
                {
                    registrations.Add(new RegistrarTopologyDeclaration(entityType, configurationType, ArchitectureTestHelpers.NormalizePath(tree.FilePath)));
                }
            }
        }

        var migrationStreams = migrationTypes
            .GroupBy(type => type.Root, StringComparer.Ordinal)
            .Select(group =>
            {
                var snapshotSourcePaths = group
                    .Where(type => type.IsSnapshot)
                    .Select(type => type.SourcePath)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToList();
                return new MigrationStreamTopologyDeclaration(
                    group.Key,
                    group.Select(type => type.TypeName).OrderBy(name => name, StringComparer.Ordinal).ToList(),
                    group.Where(type => type.IsSnapshot).Select(type => type.TypeName).OrderBy(name => name, StringComparer.Ordinal).ToList(),
                    snapshotSourcePaths,
                    migrationContexts.Where(context => context.Root == group.Key).Select(context => context.ContextType).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList(),
                    migrationContexts.Where(context => snapshotSourcePaths.Contains(context.SourcePath, StringComparer.Ordinal)).Select(context => context.ContextType).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList());
            })
            .OrderBy(stream => stream.Root, StringComparer.Ordinal)
            .ToList();

        return new PersistenceTopologyAnalysis(dbContexts, designTimeFactories, dbSets, configurations, registrations, migrationStreams, ensureCreated, schemaSplits);
    }

    public static void EnsureNoPendingModelChanges(bool hasPendingModelChanges)
    {
        if (hasPendingModelChanges)
        {
            throw new InvalidOperationException("Npgsql runtime model differs from AppDbContextModelSnapshot.");
        }
    }

    public static void EnsureExactDbSetIdentities(
        PersistenceTopologyAnalysis topology,
        IReadOnlyList<PersistedDbSetIdentity> expectedDbSets)
    {
        var expectedIdentities = expectedDbSets
            .OrderBy(dbSet => dbSet.PropertyName, StringComparer.Ordinal)
            .ToList();
        var actualIdentities = topology.DbSets
            .Select(dbSet => new PersistedDbSetIdentity(dbSet.PropertyName, dbSet.EntityType))
            .OrderBy(dbSet => dbSet.PropertyName, StringComparer.Ordinal)
            .ToList();
        var nonPublicDbSets = topology.DbSets
            .Where(dbSet => !dbSet.IsPublic)
            .Select(dbSet => dbSet.PropertyName)
            .OrderBy(propertyName => propertyName, StringComparer.Ordinal)
            .ToList();

        if (!actualIdentities.SequenceEqual(expectedIdentities) || nonPublicDbSets.Count != 0)
        {
            throw new InvalidOperationException(
                $"Public DbSet identities do not match the persistence contract.{Environment.NewLine}" +
                $"Expected: {string.Join(", ", expectedIdentities)}{Environment.NewLine}" +
                $"Actual: {string.Join(", ", actualIdentities)}{Environment.NewLine}" +
                $"Non-public: {string.Join(", ", nonPublicDbSets)}");
        }
    }

    public static void EnsureSingleDbContext(
        PersistenceTopologyAnalysis topology,
        string expectedTypeName,
        string expectedSourcePath)
    {
        if (topology.DbContexts.Count != 1 ||
            topology.DbContexts[0].TypeName != expectedTypeName ||
            topology.DbContexts[0].SourcePath != expectedSourcePath)
        {
            throw new InvalidOperationException(
                $"Expected one production DbContext '{expectedTypeName}' at '{expectedSourcePath}'. Actual: " +
                string.Join(", ", topology.DbContexts));
        }
    }

    public static void EnsureSingleDesignTimeFactory(
        PersistenceTopologyAnalysis topology,
        string expectedTypeName,
        string expectedSourcePath,
        string expectedContextTypeName)
    {
        if (topology.DesignTimeFactories.Count != 1 ||
            topology.DesignTimeFactories[0].TypeName != expectedTypeName ||
            topology.DesignTimeFactories[0].SourcePath != expectedSourcePath ||
            topology.DesignTimeFactories[0].ContextTypeName != expectedContextTypeName)
        {
            throw new InvalidOperationException(
                $"Expected one production design-time DbContext factory '{expectedTypeName}' at '{expectedSourcePath}' " +
                $"for '{expectedContextTypeName}'. Actual: {string.Join(", ", topology.DesignTimeFactories)}");
        }
    }

    public static void EnsureSingleMigrationRoot(PersistenceTopologyAnalysis topology, string expectedRoot)
    {
        if (topology.MigrationStreams.Count != 1 || topology.MigrationStreams[0].Root != expectedRoot)
        {
            throw new InvalidOperationException(
                $"Expected one migration root '{expectedRoot}'. Actual: " +
                string.Join(", ", topology.MigrationStreams.Select(stream => stream.Root)));
        }
    }

    public static void EnsureSingleSnapshot(
        PersistenceTopologyAnalysis topology,
        string expectedTypeName,
        string expectedSourcePath,
        string expectedContextTypeName)
    {
        var stream = topology.MigrationStreams.SingleOrDefault();
        if (stream == null ||
            !stream.SnapshotTypeNames.SequenceEqual([expectedTypeName], StringComparer.Ordinal) ||
            !stream.SnapshotSourcePaths.SequenceEqual([expectedSourcePath], StringComparer.Ordinal) ||
            !stream.SnapshotContextTypeNames.SequenceEqual([expectedContextTypeName], StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected one snapshot '{expectedTypeName}' at '{expectedSourcePath}' with DbContext metadata '{expectedContextTypeName}'. Actual: {stream}");
        }
    }

    public static void EnsureRegistrarOrder(
        PersistenceTopologyAnalysis topology,
        IReadOnlyList<string> expectedConfigurationTypes,
        string expectedSourcePath)
    {
        var actualConfigurationTypes = topology.RegistrarEntries
            .Select(entry => entry.ConfigurationType)
            .ToList();
        var sourcePaths = topology.RegistrarEntries
            .Select(entry => entry.SourcePath)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!actualConfigurationTypes.SequenceEqual(expectedConfigurationTypes, StringComparer.Ordinal) ||
            !sourcePaths.SequenceEqual([expectedSourcePath], StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Registrar order does not match '{expectedSourcePath}'.{Environment.NewLine}" +
                $"Expected: {string.Join(", ", expectedConfigurationTypes)}{Environment.NewLine}" +
                $"Actual: {string.Join(", ", actualConfigurationTypes)}{Environment.NewLine}" +
                $"Sources: {string.Join(", ", sourcePaths)}");
        }
    }

    private static bool IsTestProject(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var declaresTestSdk = document.Descendants().Any(element =>
            element.Name.LocalName == "PackageReference" &&
            element.Attribute("Include")?.Value == "Microsoft.NET.Test.Sdk");

        return projectName.EndsWith("Tests", StringComparison.Ordinal) ||
               projectName.EndsWith("TestUtils", StringComparison.Ordinal) ||
               declaresTestSdk;
    }

    private static bool Inherits(INamedTypeSymbol symbol, string metadataName)
    {
        for (var current = symbol.BaseType; current != null; current = current.BaseType)
        {
            if (IsNamed(current, metadataName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDbContextDeclaration(INamedTypeSymbol symbol, ClassDeclarationSyntax declaration)
    {
        return Inherits(symbol, DbContextMetadataName) ||
               declaration.BaseList?.Types.OfType<SimpleBaseTypeSyntax>().Any(baseType =>
                   baseType.Type.ToString() is "DbContext" or "Microsoft.EntityFrameworkCore.DbContext") == true;
    }

    private static string? GetConfiguredEntity(INamedTypeSymbol type)
    {
        var configuration = type.AllInterfaces.SingleOrDefault(@interface => IsNamed(@interface, ConfigurationMetadataName));
        return configuration == null ? null : GetTypeName(configuration.TypeArguments[0]);
    }

    private static string? GetDesignTimeFactoryContext(INamedTypeSymbol type)
    {
        var factory = type.AllInterfaces.SingleOrDefault(@interface => IsNamed(@interface, DesignTimeFactoryMetadataName));
        return factory == null ? null : factory.TypeArguments[0].Name;
    }

    private static string? GetDesignTimeFactoryContext(ClassDeclarationSyntax declaration)
    {
        return declaration.BaseList?.Types.Select(baseType => baseType.Type).OfType<GenericNameSyntax>()
            .Where(type => type.Identifier.ValueText == "IDesignTimeDbContextFactory")
            .Select(type => GetSimpleName(type.TypeArgumentList.Arguments[0]))
            .SingleOrDefault();
    }

    private static string? GetConfiguredEntity(ClassDeclarationSyntax declaration)
    {
        return declaration.BaseList?.Types.Select(baseType => baseType.Type).OfType<GenericNameSyntax>()
            .Where(type => type.Identifier.ValueText == "IEntityTypeConfiguration")
            .Select(type => GetSimpleName(type.TypeArgumentList.Arguments[0]))
            .SingleOrDefault();
    }

    private static string? GetDbSetEntity(SemanticModel model, PropertyDeclarationSyntax property)
    {
        if (model.GetDeclaredSymbol(property) is IPropertySymbol { Type: INamedTypeSymbol propertyType } && IsNamed(propertyType, DbSetMetadataName))
        {
            return GetTypeName(propertyType.TypeArguments[0]);
        }

        return property.Type is GenericNameSyntax { Identifier.ValueText: "DbSet" } dbSet
            ? GetSimpleName(dbSet.TypeArgumentList.Arguments[0])
            : null;
    }

    private static bool HasBaseType(ClassDeclarationSyntax declaration, string typeName)
    {
        return declaration.BaseList?.Types.Any(baseType => GetSimpleName(baseType.Type) == typeName) == true;
    }

    private static bool IsDbContextAttribute(SemanticModel model, AttributeSyntax attribute)
    {
        return model.GetSymbolInfo(attribute).Symbol is IMethodSymbol { ContainingType: { } type } &&
               IsNamed(type, DbContextAttributeMetadataName) ||
               attribute.Name.ToString() is "DbContext" or "DbContextAttribute";
    }

    private static string GetSimpleName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => GetSimpleName(qualified.Right),
            AliasQualifiedNameSyntax alias => GetSimpleName(alias.Name),
            GenericNameSyntax generic => generic.Identifier.ValueText,
            _ => type.ToString()
        };
    }

    private static string GetTypeName(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    private static bool IsNamed(INamedTypeSymbol type, string metadataName)
    {
        var separator = metadataName.LastIndexOf('.');
        var expectedNamespace = metadataName[..separator];
        var expectedName = metadataName[(separator + 1)..].Split('<')[0];
        return type.OriginalDefinition.ContainingNamespace.ToDisplayString() == expectedNamespace &&
               type.OriginalDefinition.Name == expectedName;
    }

    private static string GetMigrationRoot(string sourcePath)
    {
        const string marker = "/Migrations/";
        var migrationIndex = sourcePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return migrationIndex < 0 ? sourcePath : sourcePath[..migrationIndex] + "/Migrations";
    }

    private static bool IsEnsureCreated(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
               model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
               method.Name is "EnsureCreated" or "EnsureCreatedAsync" &&
               model.GetTypeInfo(memberAccess.Expression).Type?.ToDisplayString() ==
                   "Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade";
    }

    private static bool HasExplicitSchema(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method ||
            method.Name is not ("HasDefaultSchema" or "MigrationsHistoryTable" or "ToTable"))
        {
            return false;
        }

        var schemaArgument = invocation.ArgumentList.Arguments.FirstOrDefault(argument =>
            argument.NameColon?.Name.Identifier.ValueText == "schema");
        if (schemaArgument == null)
        {
            var schemaIndex = method.Name == "HasDefaultSchema" ? 0 : 1;
            schemaArgument = invocation.ArgumentList.Arguments.ElementAtOrDefault(schemaIndex);
        }

        return schemaArgument != null && !IsNullExpression(schemaArgument.Expression);
    }

    private static bool IsNullExpression(ExpressionSyntax expression)
    {
        return expression.IsKind(SyntaxKind.NullLiteralExpression) ||
               expression is CastExpressionSyntax { Expression.RawKind: (int)SyntaxKind.NullLiteralExpression };
    }

    private static bool IsNonRelationalOnly(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        return invocation.Ancestors().OfType<IfStatementSyntax>().Any(@if =>
            @if.Condition is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } negation &&
            negation.Operand is InvocationExpressionSyntax relationalCheck &&
            model.GetSymbolInfo(relationalCheck).Symbol is IMethodSymbol method &&
            method.Name == "IsRelational");
    }
}

internal sealed record TopologySource(string Path, string Content);
internal sealed record PersistedDbSetIdentity(string PropertyName, string EntityType);

internal sealed record PersistenceTopologyAnalysis(
    IReadOnlyList<DbContextTopologyDeclaration> DbContexts,
    IReadOnlyList<DesignTimeFactoryTopologyDeclaration> DesignTimeFactories,
    IReadOnlyList<DbSetTopologyDeclaration> DbSets,
    IReadOnlyList<EntityTypeConfigurationTopologyDeclaration> Configurations,
    IReadOnlyList<RegistrarTopologyDeclaration> RegistrarEntries,
    IReadOnlyList<MigrationStreamTopologyDeclaration> MigrationStreams,
    IReadOnlyList<EnsureCreatedTopologyViolation> EnsureCreatedViolations,
    IReadOnlyList<SchemaSplitTopologyViolation> SchemaSplitViolations);

internal sealed record DbContextTopologyDeclaration(string TypeName, string SourcePath);
internal sealed record DesignTimeFactoryTopologyDeclaration(string TypeName, string ContextTypeName, string SourcePath);
internal sealed record DbSetTopologyDeclaration(string ContextType, string PropertyName, string EntityType, bool IsPublic, string SourcePath);
internal sealed record EntityTypeConfigurationTopologyDeclaration(string EntityType, string ConfigurationType, string SourcePath);
internal sealed record RegistrarTopologyDeclaration(string EntityType, string ConfigurationType, string SourcePath);
internal sealed record MigrationStreamTopologyDeclaration(
    string Root,
    IReadOnlyList<string> TypeNames,
    IReadOnlyList<string> SnapshotTypeNames,
    IReadOnlyList<string> SnapshotSourcePaths,
    IReadOnlyList<string> ContextTypeNames,
    IReadOnlyList<string> SnapshotContextTypeNames);
internal sealed record EnsureCreatedTopologyViolation(string SourcePath, int Line, string Invocation);
internal sealed record MigrationTypeTopologyDeclaration(string Root, string TypeName, string SourcePath, bool IsSnapshot);
internal sealed record MigrationContextTopologyDeclaration(string Root, string ContextType, string SourcePath);
internal sealed record SchemaSplitTopologyViolation(string SourcePath, int Line, string Invocation);
