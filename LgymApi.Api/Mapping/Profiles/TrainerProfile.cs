using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Features.Training.Contracts;
using LgymApi.Api.Features.ExerciseScores.Contracts;
using LgymApi.Application.Coaching.Invitations.Create;
using LgymApi.Application.Coaching.Invitations.CreateByEmail;
using LgymApi.Application.Coaching.Invitations.Models;
using LgymApi.Application.Coaching.Progress.ExerciseScoresChart;
using LgymApi.Application.Coaching.Progress.TrainingByDate;
using LgymApi.Application.Coaching.Relationships.TrainerDashboard;
using LgymApi.Application.Coaching.ManagedPlans.Create;
using LgymApi.Application.Coaching.ManagedPlans.Update;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Pagination;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Domain.ValueObjects;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class TrainerProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<CreateTrainerInvitationRequest, CreateInvitationCommand>((_, _) =>
            new CreateInvitationCommand(Id<UserEntity>.Empty, Id<UserEntity>.Empty));

        configuration.CreateMap<CreateTrainerInvitationByEmailRequest, CreateInvitationByEmailCommand>((source, _) =>
            new CreateInvitationByEmailCommand(Id<UserEntity>.Empty, source.Email, source.PreferredLanguage, source.PreferredTimeZone));

        configuration.CreateMap<PaginatedTrainerInvitationRequest, FilterInput>((source, _) => new FilterInput
        {
            Page = source.Page,
            PageSize = source.PageSize,
            FilterGroups = source.FilterGroups,
            SortDescriptors = source.SortDescriptors
        });

        configuration.CreateMap<TrainerDashboardTraineesRequest, GetTrainerDashboardQuery>((source, _) =>
            new GetTrainerDashboardQuery(Id<UserEntity>.Empty, source.Search, source.Status, source.SortBy, source.SortDirection, source.Page, source.PageSize));

        configuration.CreateMap<TrainingByDateRequestDto, GetTrainingByDateQuery>((source, _) =>
            new GetTrainingByDateQuery(Id<UserEntity>.Empty, Id<UserEntity>.Empty, source.CreatedAt));

        configuration.CreateMap<ExerciseScoresChartRequestDto, GetExerciseScoresChartQuery>((_, _) =>
            new GetExerciseScoresChartQuery(Id<UserEntity>.Empty, Id<UserEntity>.Empty, Id<ExerciseEntity>.Empty));

        configuration.CreateMap<TrainerPlanFormRequest, CreateTraineeManagedPlanCommand>((source, _) =>
            new CreateTraineeManagedPlanCommand(Id<UserEntity>.Empty, Id<UserEntity>.Empty, source.Name));

        configuration.CreateMap<TrainerPlanFormRequest, UpdateTraineeManagedPlanCommand>((source, _) =>
            new UpdateTraineeManagedPlanCommand(Id<UserEntity>.Empty, Id<UserEntity>.Empty, Id<LgymApi.Domain.Entities.Plan>.Empty, source.Name));

        configuration.CreateMap<InvitationReadModel, TrainerInvitationDto>((source, _) => new TrainerInvitationDto
        {
            Id = source.Id.ToString(),
            TrainerId = source.TrainerId.ToString(),
            TraineeId = source.TraineeId?.ToString() ?? string.Empty,
            InviteeEmail = source.InviteeEmail,
            Code = source.Code,
            Status = source.Status.ToString(),
            ExpiresAt = source.ExpiresAt,
            RespondedAt = source.RespondedAt,
            CreatedAt = source.CreatedAt,
            TraineeName = source.TraineeName,
            TraineeEmail = source.TraineeEmail
        });

        configuration.CreateMap<LgymApi.Application.Coaching.Relationships.TrainerDashboard.TrainerDashboardTraineeReadModel, TrainerDashboardTraineeDto>((source, _) => new TrainerDashboardTraineeDto
        {
            Id = source.Id.ToString(),
            Name = source.Name,
            Email = source.Email,
            Avatar = source.Avatar,
            Status = source.Status,
            IsLinked = source.IsLinked,
            HasPendingInvitation = source.HasPendingInvitation,
            HasExpiredInvitation = source.HasExpiredInvitation,
            LinkedAt = source.LinkedAt,
            LastInvitationExpiresAt = source.LastInvitationExpiresAt,
            LastInvitationRespondedAt = source.LastInvitationRespondedAt
        });

        configuration.CreateMap<ManagedPlanReadModel, TrainerManagedPlanDto>((source, _) => new TrainerManagedPlanDto
        {
            Id = source.Id.ToString(),
            Name = source.Name,
            IsActive = source.IsActive,
            CreatedAt = source.CreatedAt
        });

    }
}
