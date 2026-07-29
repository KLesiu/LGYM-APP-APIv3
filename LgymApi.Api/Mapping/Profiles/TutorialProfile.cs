using LgymApi.Api.Features.Tutorial.Contracts;
using LgymApi.Application.Features.Tutorial.Models;
using LgymApi.Application.Identity.ApiCompatibility;
using LgymApi.Application.Mapping.Core;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class TutorialProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<TutorialProgressResult, TutorialProgressDto>((source, _) => new TutorialProgressDto
        {
            Id = source.Id.ToString(),
            TutorialType = source.TutorialType,
            TutorialName = source.TutorialName,
            TutorialDescription = source.TutorialDescription,
            IsCompleted = source.IsCompleted,
            CompletedAt = source.CompletedAt,
            CompletedSteps = source.CompletedSteps,
            RemainingSteps = source.RemainingSteps,
            TotalSteps = source.TotalSteps,
            CompletedStepsCount = source.CompletedStepsCount
        });

        configuration.CreateMap<TutorialProgressProjection, TutorialProgressDto>((source, _) => new TutorialProgressDto
        {
            Id = source.Id,
            TutorialType = source.TutorialType,
            TutorialName = source.TutorialName,
            TutorialDescription = source.TutorialDescription,
            IsCompleted = source.IsCompleted,
            CompletedAt = source.CompletedAt,
            CompletedSteps = source.CompletedSteps.ToList(),
            RemainingSteps = source.RemainingSteps.ToList(),
            TotalSteps = source.TotalSteps,
            CompletedStepsCount = source.CompletedStepsCount
        });
    }
}
