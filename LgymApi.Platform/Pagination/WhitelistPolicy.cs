using LgymApi.Application.Pagination;

namespace LgymApi.Infrastructure.Pagination;

internal sealed class WhitelistPolicy : IWhitelistPolicy
{
    private readonly Dictionary<string, FieldMapping> _allowedFields;
    private readonly PaginationPolicy _policy;

    private WhitelistPolicy(IReadOnlyCollection<FieldMapping> mappings, PaginationPolicy policy)
    {
        _allowedFields = mappings.ToDictionary(
            m => m.FieldName,
            m => m,
            StringComparer.OrdinalIgnoreCase);
        _policy = policy;
    }

    public static WhitelistPolicy Create<TProjection>(IMapperRegistry registry, PaginationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(policy);

        var mappings = registry.GetMappings<TProjection>();

        return new WhitelistPolicy(mappings, policy);
    }

    public void ValidateField(string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);

        if (!_allowedFields.ContainsKey(fieldName))
        {
            throw new ArgumentException($"Field '{fieldName}' is not a recognized field.");
        }
    }

    public void ValidateSort(IEnumerable<SortDescriptor> sortDescriptors)
    {
        ArgumentNullException.ThrowIfNull(sortDescriptors);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in sortDescriptors)
        {
            if (!_allowedFields.TryGetValue(descriptor.FieldName, out var mapping))
            {
                throw new ArgumentException($"Sort field '{descriptor.FieldName}' is not a recognized field.");
            }

            if (!mapping.AllowSort)
            {
                throw new ArgumentException($"Sort field '{descriptor.FieldName}' does not allow sorting.");
            }

            if (!seen.Add(descriptor.FieldName))
            {
                throw new ArgumentException($"Duplicate sort field '{descriptor.FieldName}' is not allowed.");
            }
        }
    }

    public int CapPageSize(int requestedSize)
    {
        if (requestedSize < 1)
        {
            return _policy.DefaultPageSize;
        }

        return Math.Min(requestedSize, _policy.MaxPageSize);
    }
}
