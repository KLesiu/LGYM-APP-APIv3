namespace LgymApi.Application.Pagination;

public interface IMapperRegistry
{
    void Register<TProjection>(IEnumerable<FieldMapping> mappings);

    IReadOnlyCollection<FieldMapping> GetMappings<TProjection>();

    bool TryGetMapping<TProjection>(string fieldName, out FieldMapping? mapping);
}
