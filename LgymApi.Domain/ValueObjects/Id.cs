namespace LgymApi.Domain.ValueObjects;

public readonly record struct Id<TEntity> : IComparable<Id<TEntity>>, IComparable
{
    public Guid Value { get; }

    public Id(Guid value)
    {
        Value = value;
    }

    public static Id<TEntity> New() => new(Guid.NewGuid());

    public static Id<TEntity> FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length != 16)
        {
            throw new ArgumentException("Id byte array must contain exactly 16 bytes.", nameof(bytes));
        }

        return new(new Guid(bytes));
    }

    public static Id<TEntity> Empty => new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString();

    public Guid GetValue() => Value;

    public int CompareByValue(Id<TEntity> other) => Value.CompareTo(other.Value);

    public int CompareTo(Id<TEntity> other) => CompareByValue(other);

    public int CompareTo(object? obj)
    {
        if (obj is null)
        {
            return 1;
        }

        if (obj is Id<TEntity> other)
        {
            return CompareTo(other);
        }

        throw new ArgumentException($"Object must be of type {typeof(Id<TEntity>)}.", nameof(obj));
    }

    public Id<TTarget> Rebind<TTarget>() => new(Value);

    public static bool TryParse(string id, out Id<TEntity> parsedId)
    {
        if (Guid.TryParse(id, out var guid))
        {
            parsedId = new Id<TEntity>(guid);
            return true;
        }

        parsedId = Empty;
        return false;
    }

    public static explicit operator Guid(Id<TEntity> id) => id.Value;

    public static explicit operator Id<TEntity>(Guid value) => new(value);
}
