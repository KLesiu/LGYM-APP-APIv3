using System.Linq.Expressions;
using System.Reflection;
using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LgymApi.Notifications.Persistence;

internal static class NotificationStageUpdateExtensions
{
    internal static async Task<int> StageUpdateAsync<TSource, TProperty>(
        this IQueryable<TSource> source,
        DbSet<TSource> dbSet,
        Expression<Func<TSource, TProperty>> propertySelector,
        Expression<Func<TSource, TProperty>> valueExpression,
        CancellationToken cancellationToken = default)
        where TSource : class
    {
        var dbContext = GetDbContext(dbSet);
        if (!CanUseSetBasedUpdate(dbContext))
        {
            var entities = await source.ToListAsync(cancellationToken);
            var propertyInfo = GetPropertyInfo(propertySelector);
            var value = valueExpression.Compile();

            foreach (var entity in entities)
            {
                propertyInfo.SetValue(entity, value(entity));
            }

            return entities.Count;
        }

        return await ExecuteSetBasedUpdateAsync(source, propertySelector, valueExpression, cancellationToken);
    }

    internal static async Task<int> StageUpdateAsync<TSource, TProperty1, TProperty2>(
        this IQueryable<TSource> source,
        DbSet<TSource> dbSet,
        Expression<Func<TSource, TProperty1>> propertySelector1,
        Expression<Func<TSource, TProperty1>> valueExpression1,
        Expression<Func<TSource, TProperty2>> propertySelector2,
        Expression<Func<TSource, TProperty2>> valueExpression2,
        CancellationToken cancellationToken = default)
        where TSource : class
    {
        var dbContext = GetDbContext(dbSet);
        if (!CanUseSetBasedUpdate(dbContext))
        {
            var entities = await source.ToListAsync(cancellationToken);
            var propertyInfo1 = GetPropertyInfo(propertySelector1);
            var propertyInfo2 = GetPropertyInfo(propertySelector2);
            var value1 = valueExpression1.Compile();
            var value2 = valueExpression2.Compile();

            foreach (var entity in entities)
            {
                propertyInfo1.SetValue(entity, value1(entity));
                propertyInfo2.SetValue(entity, value2(entity));
            }

            return entities.Count;
        }

        return await ExecuteSetBasedUpdateAsync(
            source,
            propertySelector1,
            valueExpression1,
            propertySelector2,
            valueExpression2,
            cancellationToken);
    }

    private static DbContext GetDbContext<TSource>(DbSet<TSource> dbSet)
        where TSource : class
    {
        if (dbSet is not IInfrastructure<IServiceProvider> infrastructure ||
            infrastructure.Instance.GetService(typeof(ICurrentDbContext)) is not ICurrentDbContext currentDbContext)
        {
            throw new InvalidOperationException("DbContext is required for StageUpdateAsync fallback.");
        }

        return currentDbContext.Context;
    }

    private static bool CanUseSetBasedUpdate(DbContext dbContext)
    {
        return dbContext.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) != true;
    }

    private static Task<int> ExecuteSetBasedUpdateAsync<TSource, TProperty>(
        IQueryable<TSource> source,
        Expression<Func<TSource, TProperty>> propertySelector,
        Expression<Func<TSource, TProperty>> valueExpression,
        CancellationToken cancellationToken)
        where TSource : class
    {
        if (IsEntityBase(typeof(TSource)) && !IsUpdatedAtProperty(propertySelector))
        {
            var utcNow = DateTimeOffset.UtcNow;
            var updatedAtProperty = BuildUpdatedAtSelector<TSource>();
            return source.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(propertySelector, valueExpression)
                    .SetProperty(updatedAtProperty, _ => utcNow),
                cancellationToken);
        }

        return source.ExecuteUpdateAsync(
            setters => setters.SetProperty(propertySelector, valueExpression),
            cancellationToken);
    }

    private static Task<int> ExecuteSetBasedUpdateAsync<TSource, TProperty1, TProperty2>(
        IQueryable<TSource> source,
        Expression<Func<TSource, TProperty1>> propertySelector1,
        Expression<Func<TSource, TProperty1>> valueExpression1,
        Expression<Func<TSource, TProperty2>> propertySelector2,
        Expression<Func<TSource, TProperty2>> valueExpression2,
        CancellationToken cancellationToken)
        where TSource : class
    {
        if (IsEntityBase(typeof(TSource))
            && !IsUpdatedAtProperty(propertySelector1)
            && !IsUpdatedAtProperty(propertySelector2))
        {
            var utcNow = DateTimeOffset.UtcNow;
            var updatedAtProperty = BuildUpdatedAtSelector<TSource>();
            return source.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(propertySelector1, valueExpression1)
                    .SetProperty(propertySelector2, valueExpression2)
                    .SetProperty(updatedAtProperty, _ => utcNow),
                cancellationToken);
        }

        return source.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(propertySelector1, valueExpression1)
                .SetProperty(propertySelector2, valueExpression2),
            cancellationToken);
    }

    private static PropertyInfo GetPropertyInfo<TSource, TProperty>(Expression<Func<TSource, TProperty>> propertySelector)
    {
        var memberExpression = propertySelector.Body switch
        {
            MemberExpression member => member,
            UnaryExpression unary when unary.Operand is MemberExpression member => member,
            _ => null
        };

        return memberExpression?.Member as PropertyInfo
            ?? throw new InvalidOperationException("Property selector must target a property.");
    }

    private static bool IsEntityBase(Type type)
    {
        var baseType = type.BaseType;
        while (baseType != null)
        {
            if (baseType.IsGenericType && baseType.GetGenericTypeDefinition().Name.StartsWith("EntityBase", StringComparison.Ordinal))
            {
                return true;
            }

            baseType = baseType.BaseType;
        }

        return false;
    }

    private static bool IsUpdatedAtProperty<TSource, TProperty>(Expression<Func<TSource, TProperty>> propertySelector)
    {
        return GetPropertyInfo(propertySelector).Name == "UpdatedAt";
    }

    private static Expression<Func<TSource, DateTimeOffset>> BuildUpdatedAtSelector<TSource>()
    {
        var entity = Expression.Parameter(typeof(TSource), "entity");
        var updatedAt = Expression.Property(entity, "UpdatedAt");
        return Expression.Lambda<Func<TSource, DateTimeOffset>>(updatedAt, entity);
    }
}
