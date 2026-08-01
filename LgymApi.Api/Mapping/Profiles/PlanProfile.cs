using LgymApi.Api.Features.Plan.Contracts;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Plan.Models;
using LgymApi.Domain.Entities;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class PlanProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<Plan, PlanFormDto>((source, _) => new PlanFormDto
        {
            Id = source.Id.ToString(),
            Name = source.Name,
            IsActive = source.IsActive
        });

        configuration.CreateMap<Plan, PlanDto>((source, _) => new PlanDto
        {
            Id = source.Id.ToString(),
            Name = source.Name,
            IsActive = source.IsActive,
            UserId = source.UserId.ToString()
        });

        configuration.CreateMap<PlanReadModel, PlanFormDto>((source, _) => new PlanFormDto
        {
            Id = source.Id.ToString(),
            Name = source.Name,
            IsActive = source.IsActive
        });

        configuration.CreateMap<PlanReadModel, PlanDto>((source, _) => new PlanDto
        {
            Id = source.Id.ToString(),
            Name = source.Name,
            IsActive = source.IsActive,
            UserId = source.UserId.ToString()
        });

        configuration.CreateMap<string, ShareCodeResponseDto>((source, _) => new ShareCodeResponseDto(source));
    }
}
