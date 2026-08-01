using System.Globalization;
using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.ReferenceData.Errors;
using LgymApi.Application.Platform.ReferenceData.Enums;
using LgymApi.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class EnumServiceTests
{
    [Test]
    public async Task GetLookupByNameAsync_WithInvalidEnumTypeName_ReturnsInvalidEnumError()
    {
        var service = CreateService();

        var result = await service.GetLookupByNameAsync("NonExistentEnum");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidEnumError>();
    }

    [TestCase("en", "Pull-up weighted")]
    [TestCase("pl", "Podciąganie: im mniejszy ciężar, tym lepiej")]
    public void GetLookup_Should_Return_Raw_Id_And_Translated_Labels(string cultureName, string expectedLabel)
    {
        var service = CreateService();

        var values = service.GetLookup<ExerciseEloFormula>(CultureInfo.GetCultureInfo(cultureName));

        var pullupWeighted = values.Single(value => value.Id == "PullupWeighted");
        pullupWeighted.Id.Should().Be("PullupWeighted");
        pullupWeighted.Name.Should().Be(expectedLabel);
        pullupWeighted.DisplayName.Should().Be(expectedLabel);
    }

    [Test]
    public void GetLookupByName_Should_Match_Enum_Type_Case_Insensitively()
    {
        var service = CreateService();

        var lookup = service.GetLookupByName("eXeRcIsEeLoFoRmUlA", CultureInfo.GetCultureInfo("en"));

        lookup.Should().NotBeNull();
        lookup!.EnumType.Should().Be("ExerciseEloFormula");
        lookup.Values.Select(value => value.Id).Should().Contain("PullupWeighted");
    }

    private static EnumService CreateService()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        return new EnumService(provider.GetRequiredService<IMapper>());
    }
}
