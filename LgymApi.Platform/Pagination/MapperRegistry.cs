using System.Collections.Concurrent;
using LgymApi.Application.Pagination;

namespace LgymApi.Infrastructure.Pagination;

internal sealed class MapperRegistry : IMapperRegistry
{
    private readonly ConcurrentDictionary<Type, IReadOnlyCollection<FieldMapping>> _mappings = new();

    public void Register<TProjection>(IEnumerable<FieldMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);

        var fieldMappings = mappings.ToList().AsReadOnly();

        if (fieldMappings.Count == 0)
        {
            throw new ArgumentException("At least one field mapping is required.", nameof(mappings));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in fieldMappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.FieldName))
            {
                throw new ArgumentException("Field name must not be empty.", nameof(mappings));
            }

            if (string.IsNullOrWhiteSpace(mapping.MemberName))
            {
                throw new ArgumentException($"Member name for field '{mapping.FieldName}' must not be empty.", nameof(mappings));
            }

            if (!seen.Add(mapping.FieldName))
            {
                throw new ArgumentException($"Duplicate field mapping '{mapping.FieldName}'.", nameof(mappings));
            }
        }

        if (!_mappings.TryAdd(typeof(TProjection), fieldMappings))
        {
            throw new InvalidOperationException(
                $"Mappings for projection type '{typeof(TProjection).Name}' are already registered.");
        }
    }

    public IReadOnlyCollection<FieldMapping> GetMappings<TProjection>()
    {
        if (!_mappings.TryGetValue(typeof(TProjection), out var mappings))
        {
            throw new InvalidOperationException(
                $"No mappings registered for projection type '{typeof(TProjection).Name}'.");
        }

        return mappings;
    }

    public bool TryGetMapping<TProjection>(string fieldName, out FieldMapping? mapping)
    {
        ArgumentNullException.ThrowIfNull(fieldName);

        if (!_mappings.TryGetValue(typeof(TProjection), out var mappings))
        {
            mapping = null;
            return false;
        }

        mapping = mappings.FirstOrDefault(
            m => string.Equals(m.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));

        return mapping is not null;
    }
}
