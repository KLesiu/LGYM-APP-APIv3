using System.Collections.Concurrent;

namespace LgymApi.Application.Mapping.Core;

public sealed class MappingContext : IMappingContext
{
    private readonly ConcurrentDictionary<ContextKey<object>, object?> _items = new();
    private readonly IReadOnlySet<string>? _allowedKeys;
    private readonly AsyncLocal<List<(Type Source, Type Target, object? SourceReference)>?> _mappingPath = new();
    private Mapper? _mapper;

    public MappingContext(IReadOnlySet<string>? allowedKeys = null)
    {
        _allowedKeys = allowedKeys;
    }

    public T? Get<T>(ContextKey<T> key)
    {
        if (_items.TryGetValue(new ContextKey<object>(key.Name), out var value) && value is T typed)
        {
            return typed;
        }

        return default;
    }

    public void Set<T>(ContextKey<T> key, T value)
    {
        if (_allowedKeys != null && !_allowedKeys.Contains(key.Name))
        {
            throw new InvalidOperationException($"Context key '{key.Name}' is not allowed. Register it via MappingConfiguration.AllowContextKey.");
        }

        _items[new ContextKey<object>(key.Name)] = value;
    }

    public TTarget Map<TTarget>(object source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return GetMapper().MapInternal<TTarget>(source.GetType(), source, this);
    }

    public TTarget Map<TSource, TTarget>(TSource source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return GetMapper().MapInternal<TSource, TTarget>(source, this);
    }

    public List<TTarget> MapList<TTarget>(System.Collections.IEnumerable source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var mapper = GetMapper();
        var result = new List<TTarget>();
        foreach (var item in source)
        {
            if (item is null)
            {
                continue;
            }

            result.Add(mapper.MapInternal<TTarget>(item.GetType(), item, this));
        }

        return result;
    }

    public List<TTarget> MapList<TSource, TTarget>(IEnumerable<TSource> source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return GetMapper().MapListInternal<TSource, TTarget>(source, this);
    }

    internal void AttachMapper(Mapper mapper)
    {
        var existing = Interlocked.CompareExchange(ref _mapper, mapper, comparand: null);
        if (existing is null)
        {
            return;
        }

        if (!ReferenceEquals(existing, mapper))
        {
            throw new InvalidOperationException("This mapping context is already bound to a different mapper instance.");
        }
    }

    internal IDisposable EnterMappingScope(Type sourceType, Type targetType, object source)
    {
        var path = _mappingPath.Value ??= new List<(Type Source, Type Target, object? SourceReference)>();

        var hasCycle = path.Any(item =>
            item.Source == sourceType &&
            item.Target == targetType &&
            (sourceType.IsValueType || ReferenceEquals(item.SourceReference, source)));

        if (hasCycle)
        {
            var chain = path
                .Select(item => $"{item.Source.Name}->{item.Target.Name}")
                .Append($"{sourceType.Name}->{targetType.Name}");

            throw new InvalidOperationException($"Cyclic nested mapping detected. Chain: {string.Join(" -> ", chain)}.");
        }

        var trackedReference = sourceType.IsValueType ? null : source;
        path.Add((sourceType, targetType, trackedReference));

        return new MappingScope(this);
    }

    private Mapper GetMapper()
    {
        return _mapper ?? throw new InvalidOperationException("Nested mapping is unavailable because this context is not bound to a mapper. Use IMapper.CreateContext().");
    }

    private void ExitMappingScope()
    {
        var path = _mappingPath.Value;
        if (path == null)
        {
            return;
        }

        if (path.Count > 0)
        {
            path.RemoveAt(path.Count - 1);
        }

        if (path.Count == 0)
        {
            _mappingPath.Value = null;
        }
    }

    private sealed class MappingScope : IDisposable
    {
        private readonly MappingContext _context;
        private bool _disposed;

        public MappingScope(MappingContext context)
        {
            _context = context;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _context.ExitMappingScope();
        }
    }
}
