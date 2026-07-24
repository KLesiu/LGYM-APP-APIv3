using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Models;
using LgymApi.Application.Nutrition.Supplementation.Models;

namespace LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Contracts;

public interface ICheckOffSupplementIntakeUseCase
{
    Task<Result<SupplementScheduleEntryReadModel, AppError>> ExecuteAsync(
        CheckOffSupplementIntakeCommand command,
        CancellationToken cancellationToken = default);
}
