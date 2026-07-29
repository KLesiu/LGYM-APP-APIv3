using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Reporting.Compatibility;

public sealed class ReportingApiCompatibilityMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<PhotoHistoryAccountInput, GetPhotoHistoryCommand>((source, _) => new GetPhotoHistoryCommand
        {
            TraineeId = source.TraineeId,
            RequestId = source.RequestId
        });
    }
}

internal sealed record PhotoHistoryAccountInput(Id<AccountReference>? TraineeId, Id<ReportRequest>? RequestId);
