using LgymApi.Api.Features.Public.Contracts;
using LgymApi.Application.Coaching.Invitations.PublicStatus;
using LgymApi.Application.Mapping.Core;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class PublicInvitationProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<PublicInvitationStatusReadModel, PublicInvitationStatusDto>((source, _) => new PublicInvitationStatusDto
        {
            Status = source.Status.ToString(),
            UserExists = source.UserExists
        });
    }
}
