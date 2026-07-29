using LgymApi.Application.Mapping.Core;

namespace LgymApi.Application.Mapping.Extensions;

public static class MappingExtensions
{
    public static TTarget MapTo<TTarget>(this object source, IMapper mapper, MappingContext? context = null)
    {
        return mapper.Map<TTarget>(source, context);
    }

    public static TTarget MapTo<TSource, TTarget>(this TSource source, IMapper mapper, MappingContext? context = null)
    {
        return mapper.Map<TSource, TTarget>(source, context);
    }

    public static List<TTarget> MapToList<TTarget>(this System.Collections.IEnumerable source, IMapper mapper, MappingContext? context = null)
    {
        return mapper.MapList<TTarget>(source, context);
    }

    public static List<TTarget> MapToList<TSource, TTarget>(this IEnumerable<TSource> source, IMapper mapper, MappingContext? context = null)
    {
        return mapper.MapList<TSource, TTarget>(source, context);
    }
}
