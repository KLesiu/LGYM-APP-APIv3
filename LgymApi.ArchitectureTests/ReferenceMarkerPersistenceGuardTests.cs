using System.Reflection;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using LgymApi.Notifications.Contracts;
using LgymApi.Platform.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ReferenceMarkerPersistenceGuardTests
{
    private static readonly Type[] MarkerTypes =
    [
        typeof(ActorReference), typeof(AccountReference), typeof(AccountSessionReference), typeof(RoleReference),
        typeof(PlanReference), typeof(PlanDayReference), typeof(PlanExerciseReference),
        typeof(NotificationReference), typeof(PushInstallationReference)
    ];

    [Test]
    public void Public_Module_Reference_Markers_Should_Match_The_Exact_Approved_Roster()
    {
        var actual = MarkerTypes
            .SelectMany(marker => marker.Assembly.GetExportedTypes())
            .Where(type => type.Namespace?.StartsWith("LgymApi.", StringComparison.Ordinal) == true
                && type.Namespace.EndsWith(".Contracts", StringComparison.Ordinal)
                && type.Name.EndsWith("Reference", StringComparison.Ordinal))
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EquivalentTo(MarkerTypes));
            Assert.That(actual, Has.All.Matches<Type>(type => type.IsClass && type.IsSealed));
            Assert.That(actual, Has.All.Matches<Type>(type => type.GetConstructors(BindingFlags.Instance | BindingFlags.Public).Length == 0));
            Assert.That(actual.Select(type => type.Name), Is.Unique);
        });
    }

    [Test]
    public void Public_Module_Contracts_Should_Reject_EntityTypedIds_At_Every_Nesting_Depth()
    {
        var productionViolations = MarkerTypes
            .Select(marker => marker.Assembly)
            .Distinct()
            .SelectMany(FindPublicContractTypeLeaks)
            .ToList();
        var fixtureViolations = FindPublicTypeLeaks(typeof(NestedEntityTypedIdFixture)).ToList();
        var approvedFixtureViolations = FindPublicTypeLeaks(typeof(NestedMarkerTypedIdFixture)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(productionViolations, Is.Empty, string.Join(Environment.NewLine, productionViolations));
            Assert.That(fixtureViolations, Is.EqualTo(new[]
            {
                "NestedEntityTypedIdFixture.UserIds exposes entity-typed ID Id<User>."
            }));
            Assert.That(approvedFixtureViolations, Is.Empty);
        });
    }

    [Test]
    public void Persisted_Primary_And_Foreign_Keys_Should_Remain_EntityTypedIds()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=marker_guard;Username=guard;Password=guard")
            .Options;
        using var context = new AppDbContext(options);

        var violations = context.Model.GetEntityTypes()
            .SelectMany(FindPersistedKeyViolations)
            .ToList();

        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
    }

    [Test]
    public void Persistence_Guard_Should_Reject_A_MarkerTyped_ForeignKey_Fixture()
    {
        var violation = FindPersistenceIdViolation(
            typeof(PersistenceFixture),
            nameof(PersistenceFixture.AccountId),
            typeof(Id<AccountReference>),
            typeof(User),
            "foreign key");

        Assert.That(
            violation,
            Is.EqualTo("PersistenceFixture.AccountId uses marker-typed foreign key Id<AccountReference>; expected Id<User>."));
    }

    private static IEnumerable<string> FindPublicContractTypeLeaks(Assembly assembly)
    {
        return assembly.GetExportedTypes()
            .Where(type => type.Namespace?.EndsWith(".Contracts", StringComparison.Ordinal) == true)
            .Where(type => type.Namespace != "LgymApi.Application.Features.PasswordReset.Contracts")
            .SelectMany(FindPublicTypeLeaks);
    }

    private static IEnumerable<string> FindPublicTypeLeaks(Type type)
    {
        foreach (var member in type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            foreach (var memberType in GetPublicMemberTypes(member))
            {
                foreach (var entityId in FindEntityTypedIds(memberType))
                {
                    yield return $"{type.Name}.{member.Name} exposes entity-typed ID {entityId}.";
                }
            }
        }
    }

    private static IEnumerable<Type> GetPublicMemberTypes(MemberInfo member)
    {
        return member switch
        {
            PropertyInfo property => [property.PropertyType],
            FieldInfo field => [field.FieldType],
            EventInfo @event => [@event.EventHandlerType!],
            MethodInfo { IsSpecialName: false } method => [method.ReturnType, .. method.GetParameters().Select(parameter => parameter.ParameterType)],
            ConstructorInfo constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType),
            _ => []
        };
    }

    private static IEnumerable<string> FindEntityTypedIds(Type type)
    {
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType is not null)
        {
            return FindEntityTypedIds(nullableType);
        }

        if (type.IsArray)
        {
            return FindEntityTypedIds(type.GetElementType()!);
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Id<>))
        {
            var scope = type.GetGenericArguments()[0];
            return IsPersistedEntity(scope) ? [$"Id<{scope.Name}>"] : [];
        }

        return type.IsGenericType
            ? type.GetGenericArguments().SelectMany(FindEntityTypedIds)
            : [];
    }

    private static IEnumerable<string> FindPersistedKeyViolations(Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType)
    {
        var foreignKeys = entityType.GetForeignKeys().ToList();
        var foreignKeyProperties = foreignKeys.SelectMany(foreignKey => foreignKey.Properties).ToHashSet();

        foreach (var property in entityType.FindPrimaryKey()?.Properties ?? [])
        {
            if (!foreignKeyProperties.Contains(property))
            {
                var violation = FindPersistenceIdViolation(entityType.ClrType, property.Name, property.ClrType, entityType.ClrType, "primary key");
                if (violation is not null)
                {
                    yield return violation;
                }
            }
        }

        foreach (var foreignKey in foreignKeys)
        {
            foreach (var property in foreignKey.Properties)
            {
                var violation = FindPersistenceIdViolation(
                    entityType.ClrType,
                    property.Name,
                    property.ClrType,
                    foreignKey.PrincipalEntityType.ClrType,
                    "foreign key");
                if (violation is not null)
                {
                    yield return violation;
                }
            }
        }
    }

    private static string? FindPersistenceIdViolation(Type entityType, string propertyName, Type propertyType, Type expectedScope, string keyKind)
    {
        var effectiveType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (effectiveType.IsGenericType && effectiveType.GetGenericTypeDefinition() == typeof(Id<>))
        {
            var actualScope = effectiveType.GetGenericArguments()[0];
            if (MarkerTypes.Contains(actualScope))
            {
                return $"{entityType.Name}.{propertyName} uses marker-typed {keyKind} Id<{actualScope.Name}>; expected Id<{expectedScope.Name}>.";
            }

            if (actualScope == expectedScope)
            {
                return null;
            }
        }

        return $"{entityType.Name}.{propertyName} uses {FormatType(propertyType)} for {keyKind}; expected Id<{expectedScope.Name}>.";
    }

    private static bool IsPersistedEntity(Type type) => PersistedEntityOwnershipCatalog.Entries.Any(entry => entry.EntityType == type);

    private static string FormatType(Type type) => type.IsGenericType
        ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(argument => argument.Name))}>"
        : type.Name;

    private sealed class NestedEntityTypedIdFixture
    {
        public Dictionary<string, IReadOnlyList<Id<User>?[]>>? UserIds { get; init; }
    }

    private sealed class NestedMarkerTypedIdFixture
    {
        public Dictionary<Id<AccountReference>, IReadOnlyList<Id<PlanReference>?[]>>? Ids { get; init; }
    }

    private sealed class PersistenceFixture
    {
        public Id<AccountReference> AccountId { get; init; }
    }
}
