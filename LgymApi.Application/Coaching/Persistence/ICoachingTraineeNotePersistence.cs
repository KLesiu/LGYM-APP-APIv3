using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Coaching.Persistence;

public interface ICoachingTraineeNotePersistence
{
    Task AddNoteAsync(CoachingTraineeNoteWriteModel note, CancellationToken cancellationToken = default);
    Task UpdateNoteAsync(CoachingTraineeNoteWriteModel note, CancellationToken cancellationToken = default);
    Task AddHistoryEntryAsync(CoachingTraineeNoteHistoryWriteModel historyEntry, CancellationToken cancellationToken = default);
    Task<CoachingTraineeNoteFact?> FindNoteByIdAsync(Id<TraineeNote> noteId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CoachingTraineeNoteFact>> GetNotesByTrainerAndTraineeAsync(Id<User> trainerId, Id<User> traineeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CoachingTraineeNoteFact>> GetVisibleNotesByTraineeAsync(Id<User> traineeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CoachingTraineeNoteHistoryFact>> GetNoteHistoryAsync(Id<TraineeNote> noteId, CancellationToken cancellationToken = default);
}
