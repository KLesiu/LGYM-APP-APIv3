using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Repositories;
using LgymApi.Resources;

namespace LgymApi.Application.TrainingPlanning.Plan.GenerateShareCode;

internal sealed class GenerateShareCodeUseCase : IGenerateShareCodeUseCase
{
    private readonly IPlanRepository _planRepository;
    private readonly IUnitOfWork _unitOfWork;

    public GenerateShareCodeUseCase(IPlanRepository planRepository, IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(planRepository);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _planRepository = planRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<string, AppError>> ExecuteAsync(GenerateShareCodeCommand input, CancellationToken cancellationToken = default)
    {
        if (input.CurrentUserId.IsEmpty || input.PlanId.IsEmpty)
        {
            return Result<string, AppError>.Failure(new InvalidPlanError(Messages.InvalidId));
        }

        try
        {
            var shareCode = await _planRepository.GenerateShareCodeAsync(input.PlanId, input.CurrentUserId, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<string, AppError>.Success(shareCode);
        }
        catch (KeyNotFoundException)
        {
            return Result<string, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind));
        }
        catch (Exception exception) when (exception is InvalidOperationException || exception.GetType().Name == "DbUpdateException")
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return Result<string, AppError>.Failure(new PlanForbiddenError(Messages.Forbidden));
        }
    }
}
