using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Nutrition.Supplementation.Models;

namespace LgymApi.Application.Nutrition.Supplementation.GetComplianceSummary;

public interface IGetSupplementComplianceSummaryUseCase
{
    Task<Result<SupplementComplianceSummaryReadModel, AppError>> ExecuteAsync(
        GetSupplementComplianceSummaryQuery query,
        CancellationToken cancellationToken = default);
}
