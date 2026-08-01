using System.Linq.Expressions;
using Gridify;
using LgymApi.Application.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace LgymApi.Infrastructure.Pagination;

internal sealed class GridifyExecutionService : IGridifyExecutionService
{
    public async Task<Pagination<TProjection>> ExecuteAsync<TProjection>(
        IQueryable<TProjection> baseQuery,
        FilterInput filterInput,
        IMapperRegistry mapperRegistry,
        PaginationPolicy paginationPolicy,
        CancellationToken cancellationToken = default)
        where TProjection : class
    {
        ArgumentNullException.ThrowIfNull(baseQuery);
        ArgumentNullException.ThrowIfNull(filterInput);
        ArgumentNullException.ThrowIfNull(mapperRegistry);
        ArgumentNullException.ThrowIfNull(paginationPolicy);

        ValidatePolicy(paginationPolicy);

        var whitelistPolicy = WhitelistPolicy.Create<TProjection>(mapperRegistry, paginationPolicy);
        var pageSize = whitelistPolicy.CapPageSize(filterInput.PageSize);
        var sortDescriptors = BuildSortDescriptors(filterInput.SortDescriptors, paginationPolicy);
        var normalizedInput = new FilterInput
        {
            Page = filterInput.Page,
            PageSize = pageSize,
            FilterGroups = filterInput.FilterGroups,
            SortDescriptors = sortDescriptors
        };

        whitelistPolicy.ValidateSort(normalizedInput.SortDescriptors);

        var mappings = mapperRegistry.GetMappings<TProjection>();
        var gridifyAdapter = new FilterToGridifyAdapter(
            mappings.Select(CreateFieldDefinition<TProjection>),
            maxPageSize: paginationPolicy.MaxPageSize);
        var gridifyMapper = CreateGridifyMapper<TProjection>(mappings);
        var filter = gridifyAdapter.Adapt(normalizedInput);

        var query = baseQuery.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.ApplyFiltering(filter, gridifyMapper);
        }

        var supportsAsyncQuery = query.Provider is IAsyncQueryProvider;
        var totalCount = supportsAsyncQuery
            ? await query.CountAsync(cancellationToken)
            : await CountPlainQuery(query);

        var (queryWithEnumOrdering, remainingOrderBy) = ApplyEnumOrdering(
            query, normalizedInput.SortDescriptors, mappings);
        query = queryWithEnumOrdering;

        if (!string.IsNullOrWhiteSpace(remainingOrderBy))
        {
            query = query.ApplyOrdering(remainingOrderBy, gridifyMapper);
        }

        query = query.ApplyPaging(normalizedInput.Page, normalizedInput.PageSize);

        var items = supportsAsyncQuery
            ? await query.ToListAsync(cancellationToken)
            : await MaterializePlainQuery(query);

        return new Pagination<TProjection>
        {
            Items = items,
            Page = normalizedInput.Page,
            PageSize = normalizedInput.PageSize,
            TotalCount = totalCount
        };
    }

    private static Task<int> CountPlainQuery<TProjection>(IQueryable<TProjection> query)
        => Task.FromResult(query.Count());

    private static Task<List<TProjection>> MaterializePlainQuery<TProjection>(IQueryable<TProjection> query)
        => Task.FromResult(query.ToList());

    private static void ValidatePolicy(PaginationPolicy paginationPolicy)
    {
        if (paginationPolicy.MaxPageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(paginationPolicy), "MaxPageSize must be greater than zero.");
        }

        if (paginationPolicy.DefaultPageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(paginationPolicy), "DefaultPageSize must be greater than zero.");
        }
    }

    private static List<SortDescriptor> BuildSortDescriptors(
        IReadOnlyCollection<SortDescriptor> requestedSortDescriptors,
        PaginationPolicy paginationPolicy)
    {
        var sortDescriptors = requestedSortDescriptors.ToList();

        if (sortDescriptors.Count == 0 && !string.IsNullOrWhiteSpace(paginationPolicy.DefaultSortField))
        {
            sortDescriptors.Add(new SortDescriptor { FieldName = paginationPolicy.DefaultSortField });
        }

        if (!string.IsNullOrWhiteSpace(paginationPolicy.TieBreakerField)
            && sortDescriptors.All(x => !string.Equals(x.FieldName, paginationPolicy.TieBreakerField, StringComparison.OrdinalIgnoreCase)))
        {
            sortDescriptors.Add(new SortDescriptor { FieldName = paginationPolicy.TieBreakerField });
        }

        return sortDescriptors;
    }

    private static (IQueryable<TProjection> query, string remainingOrderBy) ApplyEnumOrdering<TProjection>(
        IQueryable<TProjection> query,
        IReadOnlyList<SortDescriptor> sortDescriptors,
        IEnumerable<FieldMapping> mappings)
        where TProjection : class
    {
        var enumSortFields = new List<SortDescriptor>();
        var remainingSortFields = new List<SortDescriptor>();

        foreach (var sort in sortDescriptors)
        {
            var mapping = mappings.FirstOrDefault(m =>
                m.FieldName.Equals(sort.FieldName, StringComparison.OrdinalIgnoreCase));
            if (mapping != null)
            {
                var memberType = ResolveMemberType(typeof(TProjection), mapping.MemberName);
                var underlyingType = Nullable.GetUnderlyingType(memberType) ?? memberType;
                if (underlyingType.IsEnum)
                {
                    enumSortFields.Add(sort);
                }
                else
                {
                    remainingSortFields.Add(sort);
                }
            }
            else
            {
                remainingSortFields.Add(sort);
            }
        }

        IOrderedQueryable<TProjection>? orderedQuery = null;

        for (int i = 0; i < enumSortFields.Count; i++)
        {
            var sort = enumSortFields[i];
            var mapping = mappings.First(m =>
                m.FieldName.Equals(sort.FieldName, StringComparison.OrdinalIgnoreCase));
            var expression = CreateEnumIntCastExpression<TProjection>(mapping.MemberName);

            if (i == 0 && orderedQuery == null)
            {
                orderedQuery = sort.Descending
                    ? query.OrderByDescending(expression)
                    : query.OrderBy(expression);
            }
            else
            {
                orderedQuery = sort.Descending
                    ? orderedQuery!.ThenByDescending(expression)
                    : orderedQuery.ThenBy(expression);
            }
        }

        query = orderedQuery ?? query;
        var remainingOrderBy = BuildOrderBy(remainingSortFields);

        return (query, remainingOrderBy);
    }

    private static Expression<Func<TProjection, int>> CreateEnumIntCastExpression<TProjection>(string memberName)
        where TProjection : class
    {
        var parameter = Expression.Parameter(typeof(TProjection), "x");
        Expression body = parameter;

        foreach (var segment in memberName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            body = Expression.PropertyOrField(body, segment);
        }

        body = Expression.Convert(body, typeof(int));

        return Expression.Lambda<Func<TProjection, int>>(body, parameter);
    }

    private static string BuildOrderBy(IEnumerable<SortDescriptor> sortDescriptors)
        => string.Join(", ", sortDescriptors.Select(sort => $"{sort.FieldName}{(sort.Descending ? " desc" : string.Empty)}"));

    private static GridifyFieldDefinition CreateFieldDefinition<TProjection>(FieldMapping mapping)
        where TProjection : class
        => new()
        {
            FieldName = mapping.FieldName,
            FieldType = ResolveMemberType(typeof(TProjection), mapping.MemberName),
            AllowFilter = mapping.AllowFilter,
            AllowSort = mapping.AllowSort
        };

    private static GridifyMapper<TProjection> CreateGridifyMapper<TProjection>(IEnumerable<FieldMapping> mappings)
        where TProjection : class
    {
        var mapper = new GridifyMapper<TProjection>(configuration =>
        {
            configuration.EntityFrameworkCompatibilityLayer = true;
        });

        foreach (var mapping in mappings)
        {
            mapper.AddMap(mapping.FieldName, CreateSelector<TProjection>(mapping.MemberName));
        }

        return mapper;
    }

    private static Expression<Func<TProjection, object?>> CreateSelector<TProjection>(string memberName)
        where TProjection : class
    {
        var parameter = Expression.Parameter(typeof(TProjection), "projection");
        Expression body = parameter;

        foreach (var memberSegment in memberName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            body = Expression.PropertyOrField(body, memberSegment);
        }

        if (body.Type.IsValueType)
        {
            body = Expression.Convert(body, typeof(object));
        }

        return Expression.Lambda<Func<TProjection, object?>>(body, parameter);
    }

    private static Type ResolveMemberType(Type rootType, string memberName)
    {
        var currentType = rootType;

        foreach (var memberSegment in memberName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var property = currentType.GetProperty(memberSegment);
            if (property is not null)
            {
                currentType = property.PropertyType;
                continue;
            }

            var field = currentType.GetField(memberSegment);
            if (field is not null)
            {
                currentType = field.FieldType;
                continue;
            }

            throw new ArgumentException(
                $"Member '{memberSegment}' was not found on type '{currentType.Name}'.",
                nameof(memberName));
        }

        return currentType;
    }
}
