using LgymApi.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LgymApi.Infrastructure.Data;

/// <summary>
/// EF Core value comparer for Id{TEntity} that enables change tracking and comparisons.
/// Compares typed IDs by their underlying Guid values for consistency with database comparisons.
/// </summary>
/// <typeparam name="TEntity">The entity type the ID belongs to.</typeparam>
public sealed class IdValueComparer<TEntity> : ValueComparer<Id<TEntity>>
{
    public IdValueComparer()
        : base(
            (left, right) => left.Equals(right),                    // Equality check via Id<TEntity>.Equals
            id => id.GetHashCode(),                                 // Hash code for comparison collections
            id => id)                                               // Snapshot for change tracking
    {
    }
}

/// <summary>
/// EF Core value converter for Id{TEntity} that maps the typed ID to/from Guid in the database.
/// Preserves existing GUID column types while enabling compile-time type safety in domain code.
/// Includes a built-in ValueComparer for EF Core key validation.
/// </summary>
/// <typeparam name="TEntity">The entity type the ID belongs to.</typeparam>
public sealed class TypedIdValueConverter<TEntity> : ValueConverter<Id<TEntity>, Guid>
{
    /// <summary>
    /// Creates a converter that translates Id{TEntity} to Guid for storage
    /// and Guid to Id{TEntity} when materializing from database.
    /// Includes a value comparer for change tracking.
    /// </summary>
    public TypedIdValueConverter()
        : base(
            id => id.GetValue(),                 // Convert Id<TEntity> to Guid for database storage
            guid => new Id<TEntity>(guid),       // Convert Guid to Id<TEntity> when reading from database
            new ConverterMappingHints(valueGeneratorFactory: null))
    {
        // Set the comparer using reflection on the internal property
        SetComparerProperty();
    }

    private void SetComparerProperty()
    {
        try
        {
            var comparerProp = this.GetType().BaseType?.GetProperty("ConverterValueComparer",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Instance);

            if (comparerProp?.CanWrite == true)
            {
                comparerProp.SetValue(this, new IdValueComparer<TEntity>());
            }
        }
        catch
        {
            // Ignore if property doesn't exist
        }
    }
}

/// <summary>
/// EF Core value comparer for nullable Id{TEntity}? that enables change tracking.
/// </summary>
/// <typeparam name="TEntity">The entity type the ID belongs to.</typeparam>
public sealed class NullableIdValueComparer<TEntity> : ValueComparer<Id<TEntity>?>
{
    public NullableIdValueComparer()
        : base(
            (left, right) => CompareNullable(left, right),
            id => ComputeHashCode(id),
            id => id)
    {
    }

    private static bool CompareNullable(Id<TEntity>? left, Id<TEntity>? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;
        return left.Value.Equals(right.Value);
    }

    private static int ComputeHashCode(Id<TEntity>? id)
    {
        return id?.GetHashCode() ?? 0;
    }
}

/// <summary>
/// EF Core value converter for nullable Id{TEntity}? that maps to nullable Guid? in the database.
/// Handles foreign key scenarios where entity references may be optional.
/// </summary>
/// <typeparam name="TEntity">The entity type the ID belongs to.</typeparam>
public sealed class NullableTypedIdValueConverter<TEntity> : ValueConverter<Id<TEntity>?, Guid?>
{
    /// <summary>
    /// Creates a converter that translates nullable Id{TEntity}? to Guid? for storage
    /// and Guid? to Id{TEntity}? when materializing from database.
    /// </summary>
    public NullableTypedIdValueConverter()
        : base(
            id => id.HasValue ? id.Value.GetValue() : null,
            guid => guid.HasValue ? new Id<TEntity>(guid.Value) : null,
            new ConverterMappingHints(valueGeneratorFactory: null))
    {
    }
}
