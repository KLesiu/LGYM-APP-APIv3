using System.Globalization;
using FluentAssertions;
using LgymApi.Api;
using LgymApi.Api.Features.Enum.Contracts;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.ReferenceData.Enums;
using LgymApi.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ApiEnumLookupMappingProfileTests
{
    [Test]
    public void Mapper_Should_Register_Exactly_The_Six_Enum_To_Api_Lookup_Maps()
    {
        var mapper = CreateMapper();
        var concreteMapper = mapper.Should().BeOfType<Mapper>().Subject;

        var apiEnumLookupMaps = concreteMapper.RegisteredMappings
            .Where(map => map.Source.IsEnum && map.Target == typeof(EnumLookupDto))
            .Select(map => map.Source)
            .ToList();

        apiEnumLookupMaps.Should().BeEquivalentTo(
        [
            typeof(BodyParts),
            typeof(ExerciseEloFormula),
            typeof(MeasurementUnits),
            typeof(WeightUnits),
            typeof(HeightUnits),
            typeof(Platforms)
        ]);
    }

    [Test]
    public void Mapper_Should_Forward_Explicit_Culture_To_Application_Enum_Lookup_Map()
    {
        var mapper = CreateMapper();
        var context = mapper.CreateContext();
        context.Set(EnumLookupContextKeys.Culture, CultureInfo.GetCultureInfo("pl"));

        var lookup = mapper.Map<ExerciseEloFormula, EnumLookupDto>(ExerciseEloFormula.PullupWeighted, context);

        lookup.Id.Should().Be("PullupWeighted");
        lookup.Name.Should().Be("Podciąganie: im mniejszy ciężar, tym lepiej");
        lookup.DisplayName.Should().Be("Podciąganie: im mniejszy ciężar, tym lepiej");
    }

    private static IMapper CreateMapper()
    {
        var services = new ServiceCollection();
services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMapper>();
    }
}
