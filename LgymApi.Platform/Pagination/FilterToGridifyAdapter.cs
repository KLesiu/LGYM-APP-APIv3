using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LgymApi.Application.Pagination;

namespace LgymApi.Infrastructure.Pagination;

internal sealed record GridifyFieldDefinition
{
    public required string FieldName { get; init; }

    public required Type FieldType { get; init; }

    public bool AllowFilter { get; init; } = true;

    public bool AllowSort { get; init; } = true;
}

internal sealed partial class FilterToGridifyAdapter : IFilterToGridifyAdapter
{
    private const int DefaultMaxPage = 21_474_837;
    private const int DefaultMaxPageSize = 100;
    private const int DefaultMaxNestingDepth = 3;

    private static readonly HashSet<FilterOperator> StringOperators =
    [
        FilterOperator.Equals,
        FilterOperator.NotEquals,
        FilterOperator.Contains,
        FilterOperator.StartsWith,
        FilterOperator.EndsWith,
        FilterOperator.In
    ];

    private static readonly HashSet<FilterOperator> ComparableOperators =
    [
        FilterOperator.Equals,
        FilterOperator.NotEquals,
        FilterOperator.In,
        FilterOperator.GreaterThan,
        FilterOperator.GreaterThanOrEqual,
        FilterOperator.LessThan,
        FilterOperator.LessThanOrEqual
    ];

    private static readonly HashSet<FilterOperator> EqualityOperators =
    [
        FilterOperator.Equals,
        FilterOperator.NotEquals,
        FilterOperator.In
    ];

    private readonly Dictionary<string, GridifyFieldDefinition> _fields;
    private readonly int _maxPage;
    private readonly int _maxPageSize;
    private readonly int _maxNestingDepth;

    public FilterToGridifyAdapter(
        IEnumerable<GridifyFieldDefinition> fields,
        int maxPage = DefaultMaxPage,
        int maxPageSize = DefaultMaxPageSize,
        int maxNestingDepth = DefaultMaxNestingDepth)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (maxPage < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPage), "Maximum page must be positive.");
        }

        if (maxPageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPageSize), "Maximum page size must be positive.");
        }

        if (maxNestingDepth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxNestingDepth), "Maximum nesting depth must be positive.");
        }

        _fields = BuildFieldMap(fields);
        _maxPage = maxPage;
        _maxPageSize = maxPageSize;
        _maxNestingDepth = maxNestingDepth;
    }

    public string Adapt(FilterInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        ValidatePage(input.Page);
        ValidatePageSize(input.PageSize);
        ValidateSortDescriptors(input.SortDescriptors);

        if (input.FilterGroups.Count == 0)
        {
            return string.Empty;
        }

        var groups = input.FilterGroups
            .Select(group => BuildGroup(group, 1))
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .ToArray();

        return groups.Length switch
        {
            0 => string.Empty,
            1 => groups[0],
            _ => $"({string.Join("&", groups)})"
        };
    }

    private static Dictionary<string, GridifyFieldDefinition> BuildFieldMap(IEnumerable<GridifyFieldDefinition> fields)
    {
        var map = new Dictionary<string, GridifyFieldDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields)
        {
            ArgumentNullException.ThrowIfNull(field);

            if (string.IsNullOrWhiteSpace(field.FieldName) || !FieldNamePattern().IsMatch(field.FieldName))
            {
                throw new ArgumentException($"Field '{field.FieldName}' is not a valid Gridify field name.", nameof(fields));
            }

            if (!map.TryAdd(field.FieldName, field))
            {
                throw new ArgumentException($"Duplicate field definition '{field.FieldName}'.", nameof(fields));
            }
        }

        if (map.Count == 0)
        {
            throw new ArgumentException("At least one field definition is required.", nameof(fields));
        }

        return map;
    }

    private static string NormalizeGuidValue(object? value)
    {
        return value switch
        {
            Guid guid => guid.ToString("D"),
            string guidText when Guid.TryParse(guidText, out var guid) => guid.ToString("D"),
            _ => throw new InvalidCastException()
        };
    }

    private static bool IsGuidType(Type targetType)
    {
        var normalizedType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return normalizedType == typeof(Guid);
    }
}
