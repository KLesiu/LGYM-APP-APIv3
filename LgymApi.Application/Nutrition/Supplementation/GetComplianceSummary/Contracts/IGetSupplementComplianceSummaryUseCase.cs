using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.Supplementation.Models;

namespace LgymApi.Application.Nutrition.Supplementation.GetComplianceSummary;

public interface IGetSupplementComplianceSummaryUseCase
{
    Task<Result<SupplementComplianceSummaryReadModel, AppError>> ExecuteAsync(
        GetSupplementComplianceSummaryQuery query,
        CancellationToken cancellationToken = default);
}
