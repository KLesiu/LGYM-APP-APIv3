using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.TraineeNotes;

internal static class TraineeNoteRules
{
    public static AppError? GetAccessError(
        CoachingRelationshipAccessDecision access,
        Id<UserEntity> traineeId)
    {
        if (!access.IsTrainer)
        {
            return new TrainerRelationshipForbiddenError(Messages.TrainerRoleRequired);
        }

        if (traineeId.IsEmpty)
        {
            return new BadRequestError(Messages.UserIdRequired);
        }

        return access.HasActiveRelationship
            ? null
            : new NotFoundError(Messages.DidntFind);
    }

    public static AppError? GetUpsertError(TraineeNoteUpsertData data)
        => string.IsNullOrWhiteSpace(data.Content)
            ? new BadRequestError(Messages.FieldRequired)
            : null;

    public static bool IsOwnedBy(
        CoachingTraineeNoteFact note,
        Id<UserEntity> trainerId,
        Id<UserEntity> traineeId)
        => note.TrainerId == trainerId && note.TraineeId == traineeId;
}
