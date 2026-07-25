using System.Globalization;
using FluentAssertions;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.ReferenceData.Enums;
using LgymApi.Application.Platform.ReferenceData.Enums.Models;
using LgymApi.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.UnitTests;

[TestFixture]
[NonParallelizable]
public sealed class EnumLookupMappingProfileTests
{
    [Test]
    public void Mapper_Should_Register_Exactly_The_Six_Supported_Enum_Lookup_Maps()
    {
        var mapper = CreateApplicationMapper();
        var concreteMapper = mapper.Should().BeOfType<Mapper>().Subject;

        var enumLookupMaps = concreteMapper.RegisteredMappings
            .Where(map => map.Target == typeof(EnumLookupEntry))
            .Select(map => map.Source)
            .ToList();

        enumLookupMaps.Should().BeEquivalentTo(
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
    public void Mapper_Should_Reject_A_Lookup_When_Its_Concrete_Enum_Map_Is_Omitted()
    {
        var mapper = new Mapper([new MissingEnumLookupMappingProfile()]);

        var action = () => mapper.Map<EnumLookupEntry>(BodyParts.Chest);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Mapping from BodyParts to EnumLookupEntry is not registered.");
    }

    [Test]
    public void Mapper_Should_Prefer_The_Explicit_Culture_Context_Over_Current_Ui_Culture()
    {
        var mapper = CreateApplicationMapper();
        using var cultureScope = new CurrentUiCultureScope("pl");
        var context = mapper.CreateContext();
        context.Set(EnumLookupContextKeys.Culture, CultureInfo.GetCultureInfo("en"));

        var lookup = mapper.Map<ExerciseEloFormula, EnumLookupEntry>(ExerciseEloFormula.PullupWeighted, context);

        lookup.Name.Should().Be("Pull-up weighted");
        lookup.DisplayName.Should().Be("Pull-up weighted");
    }

    [Test]
    public void Mapper_Should_Use_Current_Ui_Culture_When_The_Culture_Context_Is_Omitted()
    {
        var mapper = CreateApplicationMapper();
        using var cultureScope = new CurrentUiCultureScope("pl");

        var lookup = mapper.Map<ExerciseEloFormula, EnumLookupEntry>(ExerciseEloFormula.PullupWeighted);

        lookup.Name.Should().Be("Podciąganie: im mniejszy ciężar, tym lepiej");
        lookup.DisplayName.Should().Be("Podciąganie: im mniejszy ciężar, tym lepiej");
    }

    [Test]
    public void EnumService_Should_Keep_Hidden_Values_Out_Of_Lookups()
    {
        var service = new EnumService(CreateApplicationMapper());

        var values = service.GetLookup<WeightUnits>(CultureInfo.GetCultureInfo("en"));

        values.Select(value => value.Id).Should().NotContain(nameof(WeightUnits.Unknown));
    }

    [TestCase("en", "Pull-up weighted")]
    [TestCase("pl", "Podciąganie: im mniejszy ciężar, tym lepiej")]
    public void EnumService_Should_Map_All_Supported_Enums_And_Exclude_Hidden_Values(string cultureName, string expectedPullupLabel)
    {
        var service = new EnumService(CreateApplicationMapper());
        var culture = CultureInfo.GetCultureInfo(cultureName);

        var lookups = new List<List<EnumLookupEntry>>
        {
            service.GetLookup<BodyParts>(culture),
            service.GetLookup<ExerciseEloFormula>(culture),
            service.GetLookup<MeasurementUnits>(culture),
            service.GetLookup<WeightUnits>(culture),
            service.GetLookup<HeightUnits>(culture),
            service.GetLookup<Platforms>(culture)
        };
        var pullupWeighted = lookups[1].Single(value => value.Id == nameof(ExerciseEloFormula.PullupWeighted));

        lookups.Should().AllSatisfy(values =>
        {
            values.Should().NotBeEmpty();
            values.Select(value => value.Id).Should().NotContain("Unknown");
        });
        pullupWeighted.Name.Should().Be(expectedPullupLabel);
        pullupWeighted.DisplayName.Should().Be(expectedPullupLabel);
    }

    private static IMapper CreateApplicationMapper()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(typeof(EnumLookupMappingProfile).Assembly);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMapper>();
    }

    private sealed class MissingEnumLookupMappingProfile : IMappingProfile
    {
        public void Configure(MappingConfiguration configuration)
        {
        }
    }

    private sealed class CurrentUiCultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture = CultureInfo.CurrentUICulture;

        public CurrentUiCultureScope(string cultureName)
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public void Dispose()
        {
            CultureInfo.CurrentUICulture = _previousCulture;
        }
    }
}
