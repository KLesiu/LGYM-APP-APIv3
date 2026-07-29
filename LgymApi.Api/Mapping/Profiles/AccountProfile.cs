using LgymApi.Api.Features.Account.Contracts;
using LgymApi.Application.Identity.ApiCompatibility;
using LgymApi.Application.Mapping.Core;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class AccountProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<ExternalLoginProjection, ExternalLoginDto>((source, _) => new ExternalLoginDto
        {
            Provider = source.Provider,
            ProviderEmail = source.ProviderEmail
        });
    }
}
