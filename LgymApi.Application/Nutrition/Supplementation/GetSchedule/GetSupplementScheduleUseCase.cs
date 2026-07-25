using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Nutrition.Supplementation.Models;

namespace LgymApi.Application.Nutrition.Supplementation.GetSchedule;

internal sealed class GetSupplementScheduleUseCase : Contracts.IGetSupplementScheduleUseCase
{
    private readonly ISupplementationPersistence _plans;

    public GetSupplementScheduleUseCase(ISupplementationPersistence plans)
    {
        _plans = plans;
    }

    public async Task<Result<IReadOnlyList<SupplementScheduleEntryReadModel>, AppError>> ExecuteAsync(
        Models.GetSupplementScheduleQuery query,
        CancellationToken cancellationToken = default)
    {
        var activePlan = await _plans.GetActivePlanForTraineeAsync(query.TraineeId, cancellationToken);
        if (activePlan is null)
        {
            return Result<IReadOnlyList<SupplementScheduleEntryReadModel>, AppError>.Success([]);
        }

        var logs = await _plans.ListIntakeLogsForPlanAsync(
            query.TraineeId,
            activePlan.Id,
            query.IntakeDate,
            query.IntakeDate,
            cancellationToken);

        return Result<IReadOnlyList<SupplementScheduleEntryReadModel>, AppError>.Success(
            SupplementScheduleProjector.Project(activePlan, query.IntakeDate, logs));
    }
}
