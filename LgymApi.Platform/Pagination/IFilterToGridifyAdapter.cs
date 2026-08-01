using LgymApi.Application.Pagination;

namespace LgymApi.Infrastructure.Pagination;

internal interface IFilterToGridifyAdapter
{
    string Adapt(FilterInput input);
}
