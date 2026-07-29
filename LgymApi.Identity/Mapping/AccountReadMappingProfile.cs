using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping.Core;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Identity.Mapping;

internal sealed class AccountReadMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<UserEntity, AccountReadModel>((source, _) =>
            new AccountReadModel(
                source.Id,
                source.Name,
                source.Email.Value,
                source.Avatar,
                source.PreferredLanguage,
                source.PreferredTimeZone,
                source.CreatedAt));
    }
}
