using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.TrainingPlanning.Plan.GenerateShareCode;

public interface IGenerateShareCodeUseCase
{
    Task<Result<string, AppError>> ExecuteAsync(GenerateShareCodeCommand input, CancellationToken cancellationToken = default);
}
