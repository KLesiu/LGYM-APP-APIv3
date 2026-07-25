using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Repositories;
using LgymApi.Resources;

namespace LgymApi.Application.TrainingPlanning.Plan.CheckIsUserHavePlan;

internal sealed class CheckIsUserHavePlanUseCase : ICheckIsUserHavePlanUseCase
{
    private readonly IPlanRepository _planRepository;
    private readonly IPlanDayRepository _planDayRepository;

    public CheckIsUserHavePlanUseCase(IPlanRepository planRepository, IPlanDayRepository planDayRepository)
    {
        ArgumentNullException.ThrowIfNull(planRepository);
        ArgumentNullException.ThrowIfNull(planDayRepository);
        _planRepository = planRepository;
        _planDayRepository = planDayRepository;
    }

    public async Task<Result<bool, AppError>> ExecuteAsync(CheckIsUserHavePlanQuery input, CancellationToken cancellationToken = default)
    {
        if (input.CurrentUserId.IsEmpty || input.RouteUserId.IsEmpty)
        {
            return Result<bool, AppError>.Failure(new PlanFlagBadRequestError(Messages.InvalidId, false));
        }

        if (input.CurrentUserId != input.RouteUserId)
        {
            return Result<bool, AppError>.Failure(new PlanFlagForbiddenError(Messages.Forbidden, false));
        }

        var plan = await _planRepository.FindActiveReadModelByUserIdAsync(input.CurrentUserId, cancellationToken);
        if (plan is null)
        {
            return Result<bool, AppError>.Success(false);
        }

        return Result<bool, AppError>.Success(await _planDayRepository.AnyByPlanIdAsync(plan.Id, cancellationToken));
    }

    private sealed class PlanFlagBadRequestError : BadRequestError
    {
        private readonly object? _payload;

        public PlanFlagBadRequestError(string message, object? payload)
            : base(message)
        {
            _payload = payload;
        }

        public override object? GetPayload() => _payload;
    }

    private sealed class PlanFlagForbiddenError : ForbiddenError
    {
        private readonly object? _payload;

        public PlanFlagForbiddenError(string message, object? payload)
            : base(message)
        {
            _payload = payload;
        }

        public override object? GetPayload() => _payload;
    }
}
