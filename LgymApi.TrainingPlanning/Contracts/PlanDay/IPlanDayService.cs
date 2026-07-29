using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.TrainingPlanning.Contracts.PlanDay;

public interface IPlanDayService
{
    Task<Result<Unit, AppError>> CreateAsync(CreatePlanDayCommand command, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateAsync(UpdatePlanDayCommand command, CancellationToken cancellationToken = default);
    Task<Result<PlanDayReadModel, AppError>> GetAsync(GetPlanDayQuery query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<PlanDayReadModel>, AppError>> GetForPlanAsync(GetPlanDaysQuery query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<PlanDayChoiceReadModel>, AppError>> GetTypesAsync(GetPlanDayTypesQuery query, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteAsync(DeletePlanDayCommand command, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<PlanDayInfoReadModel>, AppError>> GetInfoAsync(GetPlanDaysInfoQuery query, CancellationToken cancellationToken = default);
}

public sealed record PlanDayExerciseWriteModel(Id<PlanExerciseReference> ExerciseId, int Series, string Reps);

public sealed record PlanDayWriteModel(string Name, IReadOnlyList<PlanDayExerciseWriteModel> Exercises);

public sealed record CreatePlanDayCommand(
    Id<AccountReference> CurrentAccountId,
    Id<PlanReference> PlanId,
    PlanDayWriteModel Input);

public sealed record UpdatePlanDayCommand(
    Id<AccountReference> CurrentAccountId,
    Id<PlanDayReference> PlanDayId,
    PlanDayWriteModel Input);

public sealed record GetPlanDayQuery(
    Id<AccountReference> CurrentAccountId,
    Id<PlanDayReference> PlanDayId,
    IReadOnlyList<string> Cultures);

public sealed record GetPlanDaysQuery(
    Id<AccountReference> CurrentAccountId,
    Id<PlanReference> PlanId,
    IReadOnlyList<string> Cultures);

public sealed record GetPlanDayTypesQuery(
    Id<AccountReference> CurrentAccountId,
    Id<AccountReference> RouteAccountId);

public sealed record DeletePlanDayCommand(
    Id<AccountReference> CurrentAccountId,
    Id<PlanDayReference> PlanDayId);

public sealed record GetPlanDaysInfoQuery(
    Id<AccountReference> CurrentAccountId,
    Id<PlanReference> PlanId);

public sealed record PlanExerciseReadModel(
    Id<PlanExerciseReference> Id,
    string Name,
    Id<AccountReference>? OwnerId,
    BodyParts BodyPart,
    ExerciseEloFormula EloFormula,
    string? Description,
    string? Image);

public sealed record PlanDayExerciseReadModel(
    Id<PlanExerciseReference> ExerciseId,
    int Order,
    int Series,
    string Reps,
    PlanExerciseReadModel? Exercise);

public sealed record PlanDayReadModel(
    Id<PlanDayReference> Id,
    string Name,
    IReadOnlyList<PlanDayExerciseReadModel> Exercises);

public sealed record PlanDayChoiceReadModel(Id<PlanDayReference> Id, string Name);

public sealed record PlanDayInfoReadModel(
    Id<PlanDayReference> Id,
    string Name,
    DateTime? LastTrainingDate,
    int TotalNumberOfSeries,
    int TotalNumberOfExercises);
