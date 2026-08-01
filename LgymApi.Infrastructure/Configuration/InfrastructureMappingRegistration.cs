using LgymApi.Application.Pagination;
using LgymApi.Infrastructure.Pagination;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Domain.Entities;

namespace LgymApi.Infrastructure.Configuration;

internal static class InfrastructureMappingRegistration
{
    internal static void RegisterAll(MapperRegistry registry)
    {
        registry.Register<UserRepository.AdminUserProjection>(
        [
            new FieldMapping { FieldName = "id", MemberName = "Id", AllowSort = true, AllowFilter = false },
            new FieldMapping { FieldName = "name", MemberName = "Name", AllowSort = true, AllowFilter = true },
            new FieldMapping { FieldName = "email", MemberName = "Email", AllowSort = true, AllowFilter = true },
            new FieldMapping { FieldName = "createdAt", MemberName = "CreatedAt", AllowSort = true, AllowFilter = false },
            new FieldMapping { FieldName = "profileRank", MemberName = "ProfileRank", AllowSort = true, AllowFilter = false },
            new FieldMapping { FieldName = "isBlocked", MemberName = "IsBlocked", AllowSort = true, AllowFilter = true },
            new FieldMapping { FieldName = "isDeleted", MemberName = "IsDeleted", AllowSort = false, AllowFilter = true }
        ]);

        registry.Register<Role>(
        [
            new FieldMapping { FieldName = "id", MemberName = "Id", AllowSort = true, AllowFilter = false },
            new FieldMapping { FieldName = "name", MemberName = "Name", AllowSort = true, AllowFilter = true },
            new FieldMapping { FieldName = "description", MemberName = "Description", AllowSort = false, AllowFilter = true },
            new FieldMapping { FieldName = "createdAt", MemberName = "CreatedAt", AllowSort = true, AllowFilter = false }
        ]);

        registry.Register<AppConfig>(
        [
            new FieldMapping { FieldName = "id", MemberName = "Id", AllowSort = true, AllowFilter = false },
            new FieldMapping { FieldName = "platform", MemberName = "Platform", AllowSort = true, AllowFilter = true },
            new FieldMapping { FieldName = "minRequiredVersion", MemberName = "MinRequiredVersion", AllowSort = true, AllowFilter = true },
            new FieldMapping { FieldName = "latestVersion", MemberName = "LatestVersion", AllowSort = true, AllowFilter = true },
            new FieldMapping { FieldName = "forceUpdate", MemberName = "ForceUpdate", AllowSort = true, AllowFilter = true },
            new FieldMapping { FieldName = "createdAt", MemberName = "CreatedAt", AllowSort = true, AllowFilter = false }
        ]);

        registry.Register<LgymApi.Application.Coaching.Invitations.Models.InvitationReadModel>(
        [
            new FieldMapping { FieldName = "id", MemberName = "Id", AllowSort = true, AllowFilter = false },
            new FieldMapping { FieldName = "status", MemberName = "Status", AllowSort = true, AllowFilter = true },
            new FieldMapping { FieldName = "expiresAt", MemberName = "ExpiresAt", AllowSort = true, AllowFilter = true },
            new FieldMapping { FieldName = "createdAt", MemberName = "CreatedAt", AllowSort = true, AllowFilter = true },
            new FieldMapping { FieldName = "inviteeEmail", MemberName = "InviteeEmail", AllowSort = true, AllowFilter = true },
            new FieldMapping { FieldName = "traineeName", MemberName = "TraineeName", AllowSort = true, AllowFilter = true },
            new FieldMapping { FieldName = "traineeEmail", MemberName = "TraineeEmail", AllowSort = true, AllowFilter = true }
        ]);
        registry.Register<LgymApi.Application.Coaching.Relationships.TrainerDashboard.TrainerDashboardTraineeReadModel>(
        [
            new FieldMapping { FieldName = "id", MemberName = "Id", AllowSort = true, AllowFilter = false },
            new FieldMapping { FieldName = "name", MemberName = "Name", AllowSort = true, AllowFilter = true },
            new FieldMapping { FieldName = "email", MemberName = "Email", AllowSort = true, AllowFilter = true },
            new FieldMapping { FieldName = "createdAt", MemberName = "CreatedAt", AllowSort = true, AllowFilter = false },
            new FieldMapping { FieldName = "statusOrder", MemberName = "StatusOrder", AllowSort = true, AllowFilter = false }
        ]);
    }
}
