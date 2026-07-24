using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Nutrition.Supplementation.GetSchedule.Models;
using LgymApi.Application.Nutrition.Supplementation.Models;

namespace LgymApi.Application.Nutrition.Supplementation.GetSchedule.Contracts;

public interface IGetSupplementScheduleUseCase
{
    Task<Result<IReadOnlyList<SupplementScheduleEntryReadModel>, AppError>> ExecuteAsync(
        GetSupplementScheduleQuery query,
        CancellationToken cancellationToken = default);
}
