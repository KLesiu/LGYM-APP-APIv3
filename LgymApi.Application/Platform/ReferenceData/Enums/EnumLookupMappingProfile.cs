using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.ReferenceData.Enums.Models;
using LgymApi.Domain.Enums;

namespace LgymApi.Application.Platform.ReferenceData.Enums;

public sealed class EnumLookupMappingProfile : IMappingProfile
{
    private readonly EnumLookupEntryFormatter _formatter = new();

    public void Configure(MappingConfiguration configuration)
    {
        configuration.AllowContextKey(EnumLookupContextKeys.Culture);
        configuration.CreateMap<BodyParts, EnumLookupEntry>((source, context) => _formatter.Format(source, context));
        configuration.CreateMap<ExerciseEloFormula, EnumLookupEntry>((source, context) => _formatter.Format(source, context));
        configuration.CreateMap<MeasurementUnits, EnumLookupEntry>((source, context) => _formatter.Format(source, context));
        configuration.CreateMap<WeightUnits, EnumLookupEntry>((source, context) => _formatter.Format(source, context));
        configuration.CreateMap<HeightUnits, EnumLookupEntry>((source, context) => _formatter.Format(source, context));
        configuration.CreateMap<Platforms, EnumLookupEntry>((source, context) => _formatter.Format(source, context));
    }
}
