using LgymApi.Api.Features.Enum.Contracts;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Application.Platform.ReferenceData.Enums.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Enums;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class EnumProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<BodyParts, EnumLookupDto>((source, context) => MapLookup(source, context));
        configuration.CreateMap<ExerciseEloFormula, EnumLookupDto>((source, context) => MapLookup(source, context));
        configuration.CreateMap<MeasurementUnits, EnumLookupDto>((source, context) => MapLookup(source, context));
        configuration.CreateMap<WeightUnits, EnumLookupDto>((source, context) => MapLookup(source, context));
        configuration.CreateMap<HeightUnits, EnumLookupDto>((source, context) => MapLookup(source, context));
        configuration.CreateMap<Platforms, EnumLookupDto>((source, context) => MapLookup(source, context));

        configuration.CreateMap<EnumLookupEntry, EnumLookupDto>((source, _) => new EnumLookupDto
        {
            Id = source.Id,
            Name = source.Name,
            DisplayName = source.DisplayName
        });

        configuration.CreateMap<EnumLookupDto, LookupItemVm>((source, _) => new LookupItemVm
        {
            Id = source.Id,
            DisplayName = source.DisplayName
        });

        configuration.CreateMap<EnumLookupResponse, EnumLookupResponseDto>((source, context) => new EnumLookupResponseDto
        {
            EnumType = source.EnumType,
            Values = context!.MapList<EnumLookupEntry, EnumLookupDto>(source.Values)
        });
    }

    private static EnumLookupDto MapLookup<TEnum>(TEnum source, MappingContext? context)
        where TEnum : struct, Enum
    {
        var entry = context!.Map<EnumLookupEntry>(source);
        return context.Map<EnumLookupEntry, EnumLookupDto>(entry);
    }
}
