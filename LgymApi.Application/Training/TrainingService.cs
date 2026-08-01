using LgymApi.Application.WorkoutProgress.TrainingExecution;

namespace LgymApi.Application.Features.Training;

public sealed partial class TrainingService : ITrainingService
{
    private readonly ICompleteTrainingUseCase _completeTrainingUseCase;
    private readonly ITrainingHistoryReadService _trainingHistoryReadService;

    public TrainingService(
        ICompleteTrainingUseCase completeTrainingUseCase,
        ITrainingHistoryReadService trainingHistoryReadService)
    {
        _completeTrainingUseCase = completeTrainingUseCase;
        _trainingHistoryReadService = trainingHistoryReadService;
    }
}
