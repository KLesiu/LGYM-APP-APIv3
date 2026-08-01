using LgymApi.Domain.ValueObjects;
using TraineeNoteEntity = LgymApi.Domain.Entities.TraineeNote;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.TraineeNotes.VisibleSingle;

public sealed record GetVisibleTraineeNoteQuery(
    Id<UserEntity> TraineeId,
    Id<TraineeNoteEntity> NoteId);
