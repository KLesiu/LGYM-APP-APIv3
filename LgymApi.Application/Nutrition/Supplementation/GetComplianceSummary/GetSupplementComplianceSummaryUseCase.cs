using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Nutrition.Supplementation.Models;

namespace LgymApi.Application.Nutrition.Supplementation.GetComplianceSummary;

internal sealed class GetSupplementComplianceSummaryUseCase : IGetSupplementComplianceSummaryUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly ISupplementationPersistence _plans;

    public GetSupplementComplianceSummaryUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        ISupplementationPersistence plans)
    {
        _relationshipAccess = relationshipAccess;
        _plans = plans;
    }

    public async Task<Result<SupplementComplianceSummaryReadModel, AppError>> ExecuteAsync(
        GetSupplementComplianceSummaryQuery query,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            query.TrainerId,
            query.TraineeId,
            cancellationToken);
        var accessError = SupplementationAccess.GetTrainerAccessError(
            access.IsTrainer,
            access.HasActiveRelationship,
            query.TraineeId);
        if (accessError is not null)
        {
            return Result<SupplementComplianceSummaryReadModel, AppError>.Failure(accessError);
        }

        var rangeError = SupplementationRules.GetComplianceRangeError(query.FromDate, query.ToDate);
        if (rangeError is not null)
        {
            return Result<SupplementComplianceSummaryReadModel, AppError>.Failure(rangeError);
        }

        var activePlan = await _plans.GetActivePlanForTraineeAsync(query.TraineeId, cancellationToken);
        if (activePlan is null || activePlan.TrainerId != query.TrainerId)
        {
            return Result<SupplementComplianceSummaryReadModel, AppError>.Success(
                new SupplementComplianceSummaryReadModel(
                    query.TraineeId,
                    query.FromDate,
                    query.ToDate,
                    0,
                    0,
                    0));
        }

        var intakeLogs = await _plans.ListIntakeLogsForPlanAsync(
            query.TraineeId,
            activePlan.Id,
            query.FromDate,
            query.ToDate,
            cancellationToken);
        var summary = SupplementComplianceProjector.Project(
            query.TraineeId,
            activePlan.Items,
            query.FromDate,
            query.ToDate,
            intakeLogs);
        return Result<SupplementComplianceSummaryReadModel, AppError>.Success(summary);
    }
}
