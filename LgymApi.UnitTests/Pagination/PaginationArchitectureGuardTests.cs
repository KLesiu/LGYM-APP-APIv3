using System.Reflection;
using FluentAssertions;
using LgymApi.Infrastructure.Pagination;
using NUnit.Framework;
using System.Xml.Linq;

namespace LgymApi.UnitTests.Pagination;

[TestFixture]
public sealed class PaginationArchitectureGuardTests
{
    private static readonly Assembly ApplicationAssembly =
        typeof(LgymApi.Application.ServiceCollectionExtensions).Assembly;

    private static readonly Assembly PaginationContractAssembly =
        typeof(LgymApi.Application.Pagination.Pagination<>).Assembly;

    private static readonly Assembly InfrastructureAssembly =
        typeof(GridifyExecutionService).Assembly;

    [Test]
    public void PaginationContracts_DoNotReferenceGridifyOrEfTypes()
    {
        var paginationTypes = PaginationContractAssembly.GetTypes()
            .Where(t => t.Namespace is "LgymApi.Application.Pagination")
            .ToList();

        paginationTypes.Should().NotBeEmpty("the Application.Pagination namespace should contain types");

        var violations = new List<string>();

        foreach (var type in paginationTypes)
        {
            CheckTypeForForbiddenReferences(type, violations);
        }

        violations.Should().BeEmpty(
            "Application.Pagination contracts must not depend on Gridify or EF Core types. " +
            $"Violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Test]
    public void ApplicationAssembly_DoesNotReferenceGridifyPackage()
    {
        var referencedAssemblies = ApplicationAssembly.GetReferencedAssemblies();

        var gridifyReferences = referencedAssemblies
            .Where(a => a.Name != null &&
                        a.Name.Contains("Gridify", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.FullName)
            .ToList();

        gridifyReferences.Should().BeEmpty(
            "LgymApi.Application must not reference Gridify assemblies — " +
            "Gridify is an infrastructure concern");
    }

    [Test]
    public void ApplicationProject_DoesNotDirectlyReferenceEfCorePackage()
    {
        var applicationProject = Path.Combine(ResolveRepositoryRoot(), "LgymApi.Application", "LgymApi.Application.csproj");
        var efCoreReferences = FindDirectEfCorePackageReferences(File.ReadAllText(applicationProject));

        efCoreReferences.Should().BeEmpty(
            "LgymApi.Application must not directly reference EF Core packages — " +
            "EF Core is an infrastructure concern");
    }

    [Test]
    public void DirectEfCorePackageFixture_ShouldBeDetected()
    {
        const string project = "<Project><ItemGroup><PackageReference Include=\"Microsoft.EntityFrameworkCore\" /></ItemGroup></Project>";

        FindDirectEfCorePackageReferences(project).Should().ContainSingle().Which.Should().Be("Microsoft.EntityFrameworkCore");
    }

    [Test]
    public void Repositories_MustDependOnIGridifyExecutionServiceInterface_NotConcreteClass()
    {
        var repositoryTypes = InfrastructureAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Repository", StringComparison.Ordinal)
                        && !t.IsAbstract
                        && !t.IsInterface)
            .ToList();

        repositoryTypes.Should().NotBeEmpty("the Infrastructure assembly should contain repository types");

        var violations = new List<string>();
        var concreteGridifyType = typeof(GridifyExecutionService);

        foreach (var repoType in repositoryTypes)
        {
            foreach (var ctor in repoType.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var param in ctor.GetParameters())
                {
                    if (param.ParameterType == concreteGridifyType)
                    {
                        violations.Add(
                            $"  {repoType.FullName}: constructor parameter '{param.Name}' uses concrete type '{concreteGridifyType.Name}' instead of '{nameof(IGridifyExecutionService)}'");
                    }
                }
            }
        }

        violations.Should().BeEmpty(
            "repositories must depend on IGridifyExecutionService (interface), not GridifyExecutionService (concrete class). " +
            "This enforces Dependency Inversion Principle and allows test substitution. " +
            $"Violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static void CheckTypeForForbiddenReferences(Type type, List<string> violations)
    {
        var forbiddenNamespacePrefixes = new[]
        {
            "Gridify",
            "Microsoft.EntityFrameworkCore"
        };

        // Check base type
        if (type.BaseType is { } baseType)
        {
            CheckSingleType(baseType, type, "base type", forbiddenNamespacePrefixes, violations);
        }

        // Check implemented interfaces
        foreach (var iface in type.GetInterfaces())
        {
            CheckSingleType(iface, type, "interface", forbiddenNamespacePrefixes, violations);
        }

        // Check method return types and parameter types
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        foreach (var method in methods)
        {
            CheckSingleType(method.ReturnType, type, $"method '{method.Name}' return type", forbiddenNamespacePrefixes, violations);

            foreach (var param in method.GetParameters())
            {
                CheckSingleType(param.ParameterType, type, $"method '{method.Name}' parameter '{param.Name}'", forbiddenNamespacePrefixes, violations);
            }
        }

        // Check property types
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        foreach (var prop in properties)
        {
            CheckSingleType(prop.PropertyType, type, $"property '{prop.Name}'", forbiddenNamespacePrefixes, violations);
        }

        // Check constructor parameters
        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var param in ctor.GetParameters())
            {
                CheckSingleType(param.ParameterType, type, $"constructor parameter '{param.Name}'", forbiddenNamespacePrefixes, violations);
            }
        }
    }

    private static void CheckSingleType(Type typeToCheck, Type ownerType, string context,
        string[] forbiddenPrefixes, List<string> violations)
    {
        var typesToInspect = new List<Type> { typeToCheck };

        if (typeToCheck.IsGenericType)
        {
            typesToInspect.AddRange(typeToCheck.GetGenericArguments());
        }

        foreach (var t in typesToInspect)
        {
            var ns = t.Namespace;
            if (ns is null)
            {
                continue;
            }

            foreach (var prefix in forbiddenPrefixes)
            {
                if (ns.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(
                        $"  {ownerType.FullName} -> {context}: references {t.FullName} (namespace: {ns})");
                }
            }
        }
    }

    private static IReadOnlyList<string> FindDirectEfCorePackageReferences(string projectXml)
    {
        return XDocument.Parse(projectXml).Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(package => package?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) == true)
            .Cast<string>()
            .ToList();
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LgymApi.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
