using System.Collections.ObjectModel;

namespace LgymApi.Application.Mapping.Core;

public sealed class Mapper : IMapper
{
    private readonly IReadOnlyDictionary<(Type Source, Type Target), Func<object, MappingContext?, object>> _mappings;
    private readonly IReadOnlySet<string> _allowedContextKeys;

    internal Mapper(IEnumerable<IMappingProfile> profiles)
    {
        var configuration = new MappingConfiguration();
        foreach (var profile in profiles)
        {
            profile.Configure(configuration);
        }

        _mappings = new ReadOnlyDictionary<(Type Source, Type Target), Func<object, MappingContext?, object>>(configuration.GetMappings().ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
        _allowedContextKeys = new HashSet<string>(configuration.AllowedContextKeys, StringComparer.Ordinal);
        ValidateMappings();
    }

    public TTarget Map<TTarget>(object source, MappingContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var effectiveContext = PrepareContext(context);

        return MapInternal<TTarget>(source.GetType(), source, effectiveContext);
    }

    public TTarget Map<TSource, TTarget>(TSource source, MappingContext? context = null)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var effectiveContext = PrepareContext(context);
        return MapInternal<TTarget>(typeof(TSource), source!, effectiveContext);
    }

    public List<TTarget> MapList<TSource, TTarget>(IEnumerable<TSource> source, MappingContext? context = null)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var effectiveContext = PrepareContext(context);

        return MapListInternal<TSource, TTarget>(source, effectiveContext);
    }

    public List<TTarget> MapList<TTarget>(System.Collections.IEnumerable source, MappingContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var effectiveContext = PrepareContext(context);
        var result = new List<TTarget>();

        foreach (var item in source)
        {
            if (item is null)
            {
                continue;
            }

            result.Add(MapInternal<TTarget>(item.GetType(), item, effectiveContext));
        }

        return result;
    }

    internal TTarget MapInternal<TSource, TTarget>(TSource source, MappingContext context)
    {
        return MapInternal<TTarget>(typeof(TSource), source!, context);
    }

    internal TTarget MapInternal<TTarget>(Type sourceType, object source, MappingContext context)
    {
        var key = (Source: sourceType, Target: typeof(TTarget));
        if (!_mappings.TryGetValue(key, out var mapper))
        {
            throw new InvalidOperationException($"Mapping from {key.Source.Name} to {key.Target.Name} is not registered.");
        }
        using var scope = context.EnterMappingScope(key.Source, key.Target, source);
        return (TTarget)mapper(source, context);
    }

    internal List<TTarget> MapListInternal<TSource, TTarget>(IEnumerable<TSource> source, MappingContext context)
    {
        var key = (Source: typeof(TSource), Target: typeof(TTarget));
        if (!_mappings.TryGetValue(key, out var mapper))
        {
            throw new InvalidOperationException($"Mapping from {key.Source.Name} to {key.Target.Name} is not registered.");
        }

        var result = new List<TTarget>();
        foreach (var item in source)
        {
            if (item is null)
            {
                continue;
            }

            using var scope = context.EnterMappingScope(key.Source, key.Target, item);
            result.Add((TTarget)mapper(item!, context));
        }

        return result;
    }

    internal IReadOnlyCollection<(Type Source, Type Target)> RegisteredMappings => _mappings.Keys.ToList();

    internal void ValidateMappings()
    {
        foreach (var (key, mapper) in _mappings)
        {
            object? instance = null;

            if (key.Source.IsValueType)
            {
                instance = Activator.CreateInstance(key.Source);
            }
            else
            {
                var ctor = key.Source.GetConstructor(Type.EmptyTypes);
                if (ctor != null)
                {
                    instance = Activator.CreateInstance(key.Source);
                }
            }

            if (instance is null)
            {
                continue;
            }

            var context = CreateContext();
            mapper(instance, context);
        }
    }

    public MappingContext CreateContext()
    {
        var context = new MappingContext(_allowedContextKeys);
        context.AttachMapper(this);
        return context;
    }

    private MappingContext PrepareContext(MappingContext? context)
    {
        var effectiveContext = context ?? CreateContext();
        effectiveContext.AttachMapper(this);
        return effectiveContext;
    }
}
