namespace LgymApi.Application.Pagination;

public interface IWhitelistPolicy
{
    void ValidateField(string fieldName);

    void ValidateSort(IEnumerable<SortDescriptor> sortDescriptors);

    int CapPageSize(int requestedSize);
}
