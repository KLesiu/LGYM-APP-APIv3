using LgymApi.Application.Coaching.Relationships.GetCurrentTrainer;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping.Core;

namespace LgymApi.Application.Coaching.Relationships;

public sealed class RelationshipMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<CurrentTrainerSource, CurrentTrainerReadModel>((source, _) => new CurrentTrainerReadModel(
            source.Trainer.Id,
            source.Trainer.Name,
            source.Trainer.Email,
            source.Trainer.Avatar,
            source.LinkedAt));
    }
}

internal sealed record CurrentTrainerSource(
    AccountReadModel Trainer,
    DateTimeOffset LinkedAt);
