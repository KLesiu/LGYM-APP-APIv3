using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.Coaching;

public sealed class CoachingTraineeNotePersistenceRepository : ICoachingTraineeNotePersistence
{
    private readonly AppDbContext _dbContext;
    private readonly IMapper _mapper;

    public CoachingTraineeNotePersistenceRepository(AppDbContext dbContext, IMapper mapper)
    {
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public Task AddNoteAsync(CoachingTraineeNoteWriteModel note, CancellationToken cancellationToken = default)
    {
        var entity = _mapper.Map<CoachingTraineeNoteWriteModel, TraineeNote>(note, _mapper.CreateContext());
        return _dbContext.TraineeNotes.AddAsync(entity, cancellationToken).AsTask();
    }

    public Task UpdateNoteAsync(CoachingTraineeNoteWriteModel note, CancellationToken cancellationToken = default)
    {
        var entity = _mapper.Map<CoachingTraineeNoteWriteModel, TraineeNote>(note, _mapper.CreateContext());
        _dbContext.TraineeNotes.Attach(entity);
        var entry = _dbContext.Entry(entity);
        entry.Property(candidate => candidate.Title).IsModified = true;
        entry.Property(candidate => candidate.Content).IsModified = true;
        entry.Property(candidate => candidate.VisibleToTrainee).IsModified = true;
        entry.Property(candidate => candidate.IsPinned).IsModified = true;
        entry.Property(candidate => candidate.LastUpdatedByUserId).IsModified = true;
        entry.Property(candidate => candidate.LastUpdatedAt).IsModified = true;
        entry.Property(candidate => candidate.IsDeleted).IsModified = true;
        return Task.CompletedTask;
    }

    public Task AddHistoryEntryAsync(CoachingTraineeNoteHistoryWriteModel historyEntry, CancellationToken cancellationToken = default)
    {
        var entity = _mapper.Map<CoachingTraineeNoteHistoryWriteModel, TraineeNoteHistory>(historyEntry, _mapper.CreateContext());
        return _dbContext.TraineeNoteHistories.AddAsync(entity, cancellationToken).AsTask();
    }

    public async Task<CoachingTraineeNoteFact?> FindNoteByIdAsync(Id<TraineeNote> noteId, CancellationToken cancellationToken = default)
    {
        var note = await _dbContext.TraineeNotes
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == noteId, cancellationToken);

        return note is null
            ? null
            : _mapper.Map<TraineeNote, CoachingTraineeNoteFact>(note, _mapper.CreateContext());
    }

    public async Task<IReadOnlyList<CoachingTraineeNoteFact>> GetNotesByTrainerAndTraineeAsync(Id<User> trainerId, Id<User> traineeId, CancellationToken cancellationToken = default)
    {
        var notes = await _dbContext.TraineeNotes
            .AsNoTracking()
            .Where(note => note.TrainerId == trainerId && note.TraineeId == traineeId)
            .OrderByDescending(note => note.IsPinned)
            .ThenByDescending(note => note.LastUpdatedAt)
            .ToListAsync(cancellationToken);

        return _mapper.MapList<TraineeNote, CoachingTraineeNoteFact>(notes, _mapper.CreateContext());
    }

    public async Task<IReadOnlyList<CoachingTraineeNoteFact>> GetVisibleNotesByTraineeAsync(Id<User> traineeId, CancellationToken cancellationToken = default)
    {
        var notes = await _dbContext.TraineeNotes
            .AsNoTracking()
            .Where(note => note.TraineeId == traineeId && note.VisibleToTrainee)
            .OrderByDescending(note => note.IsPinned)
            .ThenByDescending(note => note.LastUpdatedAt)
            .ToListAsync(cancellationToken);

        return _mapper.MapList<TraineeNote, CoachingTraineeNoteFact>(notes, _mapper.CreateContext());
    }

    public async Task<IReadOnlyList<CoachingTraineeNoteHistoryFact>> GetNoteHistoryAsync(Id<TraineeNote> noteId, CancellationToken cancellationToken = default)
    {
        var historyEntries = await _dbContext.TraineeNoteHistories
            .AsNoTracking()
            .Where(historyEntry => historyEntry.TraineeNoteId == noteId)
            .OrderByDescending(historyEntry => historyEntry.ChangedAt)
            .ToListAsync(cancellationToken);

        return _mapper.MapList<TraineeNoteHistory, CoachingTraineeNoteHistoryFact>(historyEntries, _mapper.CreateContext());
    }
}
