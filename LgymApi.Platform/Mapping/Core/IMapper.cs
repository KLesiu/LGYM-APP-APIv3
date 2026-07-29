namespace LgymApi.Application.Mapping.Core;

public interface IMapper
{
    TTarget Map<TTarget>(object source, MappingContext? context = null);
    TTarget Map<TSource, TTarget>(TSource source, MappingContext? context = null);
    List<TTarget> MapList<TTarget>(System.Collections.IEnumerable source, MappingContext? context = null);
    List<TTarget> MapList<TSource, TTarget>(IEnumerable<TSource> source, MappingContext? context = null);

    MappingContext CreateContext();
}
