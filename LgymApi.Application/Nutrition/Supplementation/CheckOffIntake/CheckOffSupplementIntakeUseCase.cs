using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Contracts;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Models;
using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;

namespace LgymApi.Application.Nutrition.Supplementation.CheckOffIntake;

internal sealed class CheckOffSupplementIntakeUseCase : ICheckOffSupplementIntakeUseCase
{
    private const string DbUpdateExceptionTypeName = "Microsoft.EntityFrameworkCore.DbUpdateException";
    private const string PostgresExceptionTypeName = "Npgsql.PostgresException";
    private const string UniqueViolationSqlState = "23505";
    private const string IntakeLogUniqueIndexName = "IX_SupplementIntakeLogs_TraineeId_PlanItemId_IntakeDate";

    private readonly ISupplementationPersistence _plans;
    private readonly IUnitOfWork _unitOfWork;

    public CheckOffSupplementIntakeUseCase(
        ISupplementationPersistence plans,
        IUnitOfWork unitOfWork)
    {
        _plans = plans;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SupplementScheduleEntryReadModel, AppError>> ExecuteAsync(
        CheckOffSupplementIntakeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.PlanItemId.IsEmpty)
        {
            return Result<SupplementScheduleEntryReadModel, AppError>.Failure(
                new InvalidSupplementationError(Messages.FieldRequired));
        }

        if (command.IntakeDate == default)
        {
            return Result<SupplementScheduleEntryReadModel, AppError>.Failure(
                new InvalidSupplementationError(Messages.DateRequired));
        }

        var activePlan = await _plans.GetActivePlanForTraineeAsync(command.TraineeId, cancellationToken);
        if (activePlan is null)
        {
            return Result<SupplementScheduleEntryReadModel, AppError>.Failure(
                new SupplementationNotFoundError(Messages.DidntFind));
        }

        var planItem = activePlan.Items.FirstOrDefault(item => item.Id == command.PlanItemId);
        if (planItem is null || !SupplementationRules.IsScheduledOnDate(planItem.DaysOfWeekMask, command.IntakeDate))
        {
            return Result<SupplementScheduleEntryReadModel, AppError>.Failure(
                new SupplementationNotFoundError(Messages.DidntFind));
        }

        var intakeLog = await _plans.FindTrackedIntakeLogAsync(
            command.TraineeId,
            command.PlanItemId,
            command.IntakeDate,
            cancellationToken);
        if (intakeLog is null)
        {
            intakeLog = new SupplementIntakeLog
            {
                Id = Id<SupplementIntakeLog>.New(),
                TraineeId = command.TraineeId,
                PlanItemId = command.PlanItemId,
                IntakeDate = command.IntakeDate,
                TakenAt = command.TakenAt ?? DateTimeOffset.UtcNow
            };
            await _plans.AddIntakeLogAsync(intakeLog, cancellationToken);

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (IsIntakeLogUniqueViolation(exception))
            {
                var winner = await _plans.FindIntakeLogAsync(
                    command.TraineeId,
                    command.PlanItemId,
                    command.IntakeDate,
                    cancellationToken);
                if (winner is null)
                {
                    throw;
                }

                intakeLog = winner;
            }
        }
        else
        {
            intakeLog.TakenAt = command.TakenAt ?? intakeLog.TakenAt;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var result = SupplementScheduleProjector.Project(activePlan, command.IntakeDate, [intakeLog])
            .Single(entry => entry.PlanItemId == command.PlanItemId);
        return Result<SupplementScheduleEntryReadModel, AppError>.Success(result);
    }

    private static bool IsIntakeLogUniqueViolation(Exception exception)
    {
        var postgresException = exception.InnerException;
        return string.Equals(exception.GetType().FullName, DbUpdateExceptionTypeName, StringComparison.Ordinal)
            && postgresException is not null
            && string.Equals(postgresException.GetType().FullName, PostgresExceptionTypeName, StringComparison.Ordinal)
            && string.Equals(
                postgresException.GetType().GetProperty("SqlState")?.GetValue(postgresException) as string,
                UniqueViolationSqlState,
                StringComparison.Ordinal)
            && string.Equals(
                postgresException.GetType().GetProperty("ConstraintName")?.GetValue(postgresException) as string,
                IntakeLogUniqueIndexName,
                StringComparison.Ordinal);
    }
}
