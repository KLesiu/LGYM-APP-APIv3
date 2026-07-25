using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Reflection;

namespace LgymApi.Infrastructure.Data.Conventions;

/// <summary>
/// Helper class for configuring typed ID conventions in EF Core.
/// Encapsulates reflection-based entity type discovery and converter registration logic.
/// </summary>
internal static class TypedIdConventionApplier
{
    /// <summary>
    /// Applies typed ID value converters to the model configuration for all entity types
    /// that inherit from EntityBase&lt;T&gt;.
    /// </summary>
    /// <param name="configurationBuilder">The model configuration builder.</param>
    public static void ApplyConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // T5: Register typed-ID value converters for all entity types.
        // These converters enable Id<TEntity> and Id<TEntity>? to map to/from GUID columns
        // in the database without changing schema (GUID columns remain GUID-backed).

        // Discover all entity types that inherit from EntityBase<T>
        // Load types from Domain assembly where entities are defined
        var domainAssembly = typeof(EntityBase<>).Assembly;
        var entityTypes = domainAssembly
            .GetTypes()
            .Where(t => t.Namespace == "LgymApi.Domain.Entities" && !t.IsAbstract && t.IsClass)
            .Where(t =>
            {
                var baseType = t.BaseType;
                while (baseType != null)
                {
                    // Check if this is EntityBase<T> by looking at generic type definition
                    if (baseType.IsGenericType &&
                        baseType.GetGenericTypeDefinition().Name == "EntityBase`1")
                    {
                        return true;
                    }
                    baseType = baseType.BaseType;
                }
                return false;
            })
            .ToList();

        // Register converters for each entity type
        foreach (var entityType in entityTypes)
        {
            var idType = typeof(Id<>).MakeGenericType(entityType);
            var nullableIdType = typeof(Nullable<>).MakeGenericType(idType);

            var converterType = typeof(TypedIdValueConverter<>).MakeGenericType(entityType);
            var nullableConverterType = typeof(NullableTypedIdValueConverter<>).MakeGenericType(entityType);

            // Register conversion for Id<TEntity> properties
            configurationBuilder.Properties(idType).HaveConversion(converterType);

            // Register conversion for nullable Id<TEntity>? properties
            configurationBuilder.Properties(nullableIdType).HaveConversion(nullableConverterType);
        }

        configurationBuilder.Properties<Id<CorrelationScope>>().HaveConversion<TypedIdValueConverter<CorrelationScope>>();
        configurationBuilder.Properties<Id<CorrelationScope>?>().HaveConversion<NullableTypedIdValueConverter<CorrelationScope>>();
    }

    /// <summary>
    /// Explicitly configures typed-ID converters with comparers for key validation.
    /// Registers converters for each entity type that has a typed ID property.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    public static void ApplyModelBuilderConverters(ModelBuilder modelBuilder)
    {
        // T5: Explicitly configure typed-ID converters with comparers to enable key validation
        // Register each entity type that has a typed ID property
        var entityTypesWithTypedIds = typeof(EntityBase<>).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "LgymApi.Domain.Entities" && !t.IsAbstract && t.IsClass)
            .Where(t =>
            {
                var baseType = t.BaseType;
                while (baseType != null)
                {
                    if (baseType.IsGenericType &&
                        baseType.GetGenericTypeDefinition().Name == "EntityBase`1")
                    {
                        return true;
                    }
                    baseType = baseType.BaseType;
                }
                return false;
            })
            .ToList();

        foreach (var entityType in entityTypesWithTypedIds)
        {
            var converterType = typeof(TypedIdValueConverter<>).MakeGenericType(entityType);
            var nullableConverterType = typeof(NullableTypedIdValueConverter<>).MakeGenericType(entityType);

            var converterInstance = (dynamic)Activator.CreateInstance(converterType)!;
            var nullableConverterInstance = (dynamic)Activator.CreateInstance(nullableConverterType)!;

            // Use reflection to call Entity<T>().Property(...).HasConversion(converter)
            var entityMethod = typeof(ModelBuilder)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Entity" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
                ?.MakeGenericMethod(entityType);

            if (entityMethod == null)
                continue;

            dynamic entityBuilder = entityMethod.Invoke(modelBuilder, null)!;

            // Configure Id property
            Type entityBuilderType = entityBuilder.GetType();
            MethodInfo[] allMethods = entityBuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            MethodInfo? propertyMethodInfo = allMethods.FirstOrDefault(m =>
                m.Name == "Property" &&
                m.IsGenericMethodDefinition &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType.Name.StartsWith("Expression`1"));

            var propertyMethod = propertyMethodInfo
                ?.MakeGenericMethod(typeof(Id<>).MakeGenericType(entityType));

            if (propertyMethod == null)
                continue;

            var idParam = System.Linq.Expressions.Expression.Parameter(entityType, "e");
            var idProp = System.Linq.Expressions.Expression.Property(idParam, "Id");
            var idLambda = System.Linq.Expressions.Expression.Lambda(idProp, idParam);

            dynamic propBuilder = propertyMethod.Invoke(entityBuilder, new object[] { idLambda })!;

            Type propBuilderType = propBuilder.GetType();
            MethodInfo? hasConversionMethod = propBuilderType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "HasConversion" && !m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Type));

            if (hasConversionMethod != null)
            {
                hasConversionMethod.Invoke(propBuilder, new object[] { converterInstance.GetType() });
            }
        }
    }
}
