using LgymApi.Application.Coaching.Invitations.Create;
using LgymApi.Application.Coaching.Invitations.CreateByEmail;
using LgymApi.Application.Coaching.Invitations.ListPaginated;
using LgymApi.Application.Coaching.Invitations.Accept;
using LgymApi.Application.Coaching.Invitations.Reject;
using LgymApi.Application.Coaching.Invitations.Revoke;
using LgymApi.Application.Coaching.ManagedPlans.GetActive;
using LgymApi.Application.Coaching.Progress.EloChart;
using LgymApi.Application.Coaching.Progress.ExerciseScoresChart;
using LgymApi.Application.Coaching.Progress.MainRecordsHistory;
using LgymApi.Application.Coaching.Progress.TrainingByDate;
using LgymApi.Application.Coaching.Progress.TrainingDates;
using LgymApi.Application.Coaching.Relationships.DetachFromTrainer;
using LgymApi.Application.Coaching.Relationships.GetCurrentTrainer;
using LgymApi.Application.Coaching.Relationships.TrainerDashboard;
using LgymApi.Application.Coaching.Relationships.UnlinkTrainee;
using LgymApi.Application.Coaching.TraineeNotes.Create;
using LgymApi.Application.Coaching.TraineeNotes.Delete;
using LgymApi.Application.Coaching.TraineeNotes.History;
using LgymApi.Application.Coaching.TraineeNotes.TrainerList;
using LgymApi.Application.Coaching.TraineeNotes.Update;
using LgymApi.Application.Coaching.TraineeNotes.VisibleList;
using LgymApi.Application.Coaching.TraineeNotes.VisibleSingle;
using LgymApi.Application.Coaching.TraineeNotes.VisibleHistory;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Coaching.ApiAdapters;

public sealed class CoachingApiAdapterMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<TrainerTraineeAccountInput, CreateInvitationCommand>((source, _) => new(
            source.TrainerId.Rebind<User>(),
            source.TraineeId.Rebind<User>()));
        configuration.CreateMap<ActorEmailAccountInput, CreateInvitationByEmailCommand>((source, _) => new(
            source.ActorId.Rebind<User>(), source.Email, source.PreferredLanguage, source.PreferredTimeZone));
        configuration.CreateMap<ActorFilterAccountInput, ListPaginatedInvitationsQuery>((source, _) => new(
            source.ActorId.Rebind<User>(), source.Filter));
        configuration.CreateMap<ActorInvitationAccountInput, RevokeInvitationCommand>((source, _) => new(
            source.ActorId.Rebind<User>(), source.InvitationId));
        configuration.CreateMap<DashboardAccountInput, GetTrainerDashboardQuery>((source, _) => new(
            source.TrainerId.Rebind<User>(), source.Search, source.Status, source.SortBy, source.SortDirection, source.Page, source.PageSize));

        configuration.CreateMap<TrainerTraineeAccountInput, GetTrainingDatesQuery>((source, _) => new(
            source.TrainerId, source.TraineeId));
        configuration.CreateMap<TrainingByDateAccountInput, GetTrainingByDateQuery>((source, _) => new(
            source.TrainerId, source.TraineeId, source.CreatedAt));
        configuration.CreateMap<ExerciseScoresChartAccountInput, GetExerciseScoresChartQuery>((source, _) => new(
            source.TrainerId, source.TraineeId, source.ExerciseId));
        configuration.CreateMap<TrainerTraineeAccountInput, GetEloChartQuery>((source, _) => new(
            source.TrainerId, source.TraineeId));
        configuration.CreateMap<TrainerTraineeAccountInput, GetMainRecordsHistoryQuery>((source, _) => new(
            source.TrainerId, source.TraineeId));
        configuration.CreateMap<TrainerTraineeAccountInput, UnlinkTraineeCommand>((source, _) => new(
            source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>()));

        configuration.CreateMap<TrainerTraineeAccountInput, ListTrainerNotesQuery>((source, _) => new(
            source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>()));
        configuration.CreateMap<CreateNoteAccountInput, CreateTraineeNoteCommand>((source, _) => new(
            source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>(), source.Data));
        configuration.CreateMap<UpdateNoteAccountInput, UpdateTraineeNoteCommand>((source, _) => new(
            source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>(), source.NoteId, source.Data));
        configuration.CreateMap<ActorTraineeNoteAccountInput, DeleteTraineeNoteCommand>((source, _) => new(
            source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>(), source.NoteId));
        configuration.CreateMap<ActorTraineeNoteAccountInput, GetTraineeNoteHistoryQuery>((source, _) => new(
            source.TrainerId.Rebind<User>(), source.TraineeId.Rebind<User>(), source.NoteId));
        configuration.CreateMap<ActorAccountInput, ListVisibleTraineeNotesQuery>((source, _) => new(source.ActorId.Rebind<User>()));
        configuration.CreateMap<ActorNoteAccountInput, GetVisibleTraineeNoteQuery>((source, _) => new(source.ActorId.Rebind<User>(), source.NoteId));
        configuration.CreateMap<ActorNoteAccountInput, GetVisibleTraineeNoteHistoryQuery>((source, _) => new(source.ActorId.Rebind<User>(), source.NoteId));

        configuration.CreateMap<ActorInvitationAccountInput, AcceptInvitationCommand>((source, _) => new(source.ActorId.Rebind<User>(), source.InvitationId));
        configuration.CreateMap<ActorInvitationAccountInput, RejectInvitationCommand>((source, _) => new(source.ActorId.Rebind<User>(), source.InvitationId));
        configuration.CreateMap<ActorAccountInput, DetachFromTrainerCommand>((source, _) => new(source.ActorId.Rebind<User>()));
        configuration.CreateMap<ActorAccountInput, GetCurrentTrainerQuery>((source, _) => new(source.ActorId.Rebind<User>()));
        configuration.CreateMap<ActorAccountInput, GetActiveManagedPlanQuery>((source, _) => new(source.ActorId));
    }
}

internal sealed record ActorAccountInput(Id<AccountReference> ActorId);
internal sealed record TrainerTraineeAccountInput(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId);
internal sealed record ActorInvitationAccountInput(Id<AccountReference> ActorId, Id<TrainerInvitation> InvitationId);
internal sealed record ActorFilterAccountInput(Id<AccountReference> ActorId, Application.Pagination.FilterInput Filter);
internal sealed record ActorEmailAccountInput(Id<AccountReference> ActorId, string Email, string PreferredLanguage, string PreferredTimeZone);
internal sealed record DashboardAccountInput(Id<AccountReference> TrainerId, string? Search, string? Status, string? SortBy, string? SortDirection, int Page, int PageSize);
internal sealed record TrainingByDateAccountInput(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId, DateTime CreatedAt);
internal sealed record ExerciseScoresChartAccountInput(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId, Id<Exercise> ExerciseId);
internal sealed record CreateNoteAccountInput(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId, TraineeNotes.Models.TraineeNoteUpsertData Data);
internal sealed record UpdateNoteAccountInput(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId, Id<TraineeNote> NoteId, TraineeNotes.Models.TraineeNoteUpsertData Data);
internal sealed record ActorTraineeNoteAccountInput(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId, Id<TraineeNote> NoteId);
internal sealed record ActorNoteAccountInput(Id<AccountReference> ActorId, Id<TraineeNote> NoteId);
