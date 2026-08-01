using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Coaching.Invitations.Models;
using LgymApi.Application.Coaching.Relationships.GetCurrentTrainer;
using LgymApi.Application.Coaching.Relationships.TrainerDashboard;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.Pagination;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Application.WorkoutProgress.Dashboard.Models;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Coaching.ApiAdapters;

public interface ITrainerInvitationApiPort
{
    Task<Result<InvitationReadModel, AppError>> CreateAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
    Task<Result<InvitationReadModel, AppError>> CreateByEmailAsync(AuthenticatedAccountContext trainer, string email, string preferredLanguage, string preferredTimeZone, CancellationToken cancellationToken = default);
    Task<Result<Pagination<InvitationReadModel>, AppError>> GetPaginatedAsync(AuthenticatedAccountContext trainer, FilterInput filter, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> RevokeAsync(AuthenticatedAccountContext trainer, Id<TrainerInvitation> invitationId, CancellationToken cancellationToken = default);
}

public interface ITrainerDashboardProgressApiPort
{
    Task<Result<Pagination<TrainerDashboardTraineeReadModel>, AppError>> GetDashboardAsync(AuthenticatedAccountContext trainer, string? search, string? status, string? sortBy, string? sortDirection, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<List<DateTime>, AppError>> GetTrainingDatesAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
    Task<Result<List<WorkoutProgressDashboardTrainingReadModel>, AppError>> GetTrainingByDateAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, DateTime createdAt, CancellationToken cancellationToken = default);
    Task<Result<List<ExerciseScoreChartPoint>, AppError>> GetExerciseScoresChartAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default);
    Task<Result<List<EloChartPoint>, AppError>> GetEloChartAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
    Task<Result<List<MainRecordReadModel>, AppError>> GetMainRecordsHistoryAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UnlinkAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
}

public interface ITrainerTraineeNotesApiPort
{
    Task<Result<IReadOnlyList<TraineeNoteReadModel>, AppError>> GetNotesAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
    Task<Result<TraineeNoteReadModel, AppError>> CreateAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, TraineeNoteUpsertData data, CancellationToken cancellationToken = default);
    Task<Result<TraineeNoteReadModel, AppError>> UpdateAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<TraineeNote> noteId, TraineeNoteUpsertData data, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<TraineeNote> noteId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<TraineeNoteHistoryReadModel>, AppError>> GetHistoryAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<TraineeNote> noteId, CancellationToken cancellationToken = default);
}

public interface ITraineeNotesApiPort
{
    Task<Result<IReadOnlyList<TraineeNoteReadModel>, AppError>> GetVisibleNotesAsync(AuthenticatedAccountContext trainee, CancellationToken cancellationToken = default);
    Task<Result<TraineeNoteReadModel, AppError>> GetVisibleNoteAsync(AuthenticatedAccountContext trainee, Id<TraineeNote> noteId, CancellationToken cancellationToken = default);
}

public interface ITraineeRelationshipApiPort
{
    Task<Result<Unit, AppError>> AcceptInvitationAsync(AuthenticatedAccountContext trainee, Id<TrainerInvitation> invitationId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> RejectInvitationAsync(AuthenticatedAccountContext trainee, Id<TrainerInvitation> invitationId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DetachAsync(AuthenticatedAccountContext trainee, CancellationToken cancellationToken = default);
    Task<Result<CurrentTrainerReadModel, AppError>> GetCurrentTrainerAsync(AuthenticatedAccountContext trainee, CancellationToken cancellationToken = default);
    Task<Result<ManagedPlanReadModel, AppError>> GetActivePlanAsync(AuthenticatedAccountContext trainee, CancellationToken cancellationToken = default);
}
