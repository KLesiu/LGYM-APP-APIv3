using System.Text.Json;
using FluentAssertions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Exercise.Contracts;
using LgymApi.Api.Features.ExerciseScores.Contracts;
using LgymApi.Api.Features.EloRegistry.Contracts;
using LgymApi.Api;
using LgymApi.Api.Mapping.Profiles;
using LgymApi.Application.Features.EloRegistry.Models;
using LgymApi.Application.Features.ExerciseScores.Models;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Features.Exercise.Models;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class MapperConfigurationTests
{
    [Test]
    public void Mapper_Should_Load_All_Profiles_And_Validate()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        mapper.Should().NotBeNull();

        var concrete = mapper as Mapper;
        concrete.Should().NotBeNull("Mapper should be concrete implementation for validation");

        concrete!.RegisteredMappings.Count.Should().BeGreaterThan(0, "No mappings registered");
        var action = () => concrete.ValidateMappings();
        action.Should().NotThrow("Mapping validation failed");
        mapper.Map<BodyParts, LgymApi.Application.Platform.ReferenceData.Enums.Models.EnumLookupEntry>(BodyParts.Chest)
            .Id.Should().Be(nameof(BodyParts.Chest));
    }

    [Test]
    public void Mapper_Should_Register_Each_Discovered_Profile_Exactly_Once()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        var registeredProfiles = provider.GetServices<IMappingProfile>()
            .Select(profile => profile.GetType())
            .ToArray();
        var discoveredProfiles = LgymApi.Api.Mapping.MappingAssemblyMarkers.All
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IMappingProfile).IsAssignableFrom(type))
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .ToArray();

        registeredProfiles.Should().BeEquivalentTo(discoveredProfiles);
        registeredProfiles.Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void MappingContext_Should_Reject_Unknown_Key()
    {
        var services = new ServiceCollection();
services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        var context = mapper.CreateContext();

        var action = () => context.Set(new ContextKey<string>("Unknown.Key"), "value");
        action.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void Duplicate_ContextKeys_Should_Throw()
    {
        var configuration = new MappingConfiguration();
        var key = new ContextKey<string>("Duplicate.Key");

        configuration.AllowContextKey(key);

        var action = () => configuration.AllowContextKey(key);
        action.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void ExerciseExtendedFormDto_Should_Map_Formula_String_To_Application_Enum()
    {
        var services = new ServiceCollection();
services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        var dto = new ExerciseExtendedFormDto
        {
            Name = "Weighted pull-up",
            BodyPart = BodyParts.Back,
            EloFormula = new LookupItemVm { Id = ExerciseEloFormula.PullupWeighted.ToString(), DisplayName = "Pull-up weighted" },
            Description = "test",
            Image = "image"
        };

        var input = mapper.Map<ExerciseExtendedFormDto, AddExerciseWithFormulaInput>(dto);

        input.EloFormula.Should().Be(ExerciseEloFormula.PullupWeighted);
    }

    [Test]
    public void ExerciseResponseDto_UsesLocalizedDisplayNameFromMappingContext()
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        var exerciseId = Id<Exercise>.New();
        var context = mapper.CreateContext();
        context.Set(
            new ContextKey<IReadOnlyDictionary<Id<Exercise>, string>>("Exercise.Translations"),
            new Dictionary<Id<Exercise>, string> { [exerciseId] = "Polska nazwa" });
        var exercise = new ProgressExerciseReadModel(
            exerciseId,
            "English name",
            null,
            BodyParts.Back,
            null,
            null,
            null);

        var dto = context.Map<ProgressExerciseReadModel, ExerciseResponseDto>(exercise);

        dto.DisplayName.Should().Be("Polska nazwa");
    }

    [Test]
    public void ExerciseExtendedFormDto_Should_Map_User_Id_Through_Context()
    {
        var services = new ServiceCollection();
services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        var context = mapper.CreateContext();
        var userId = Id<LgymApi.Identity.Contracts.AccountReference>.New();
        context.Set(new ContextKey<Id<LgymApi.Identity.Contracts.AccountReference>>("Exercise.UserId"), userId);

        var dto = new ExerciseExtendedFormDto
        {
            Name = "Weighted pull-up",
            BodyPart = BodyParts.Back,
            EloFormula = new LookupItemVm { Id = ExerciseEloFormula.PullupWeighted.ToString(), DisplayName = "Pull-up weighted" },
            Description = "test",
            Image = "image"
        };

        var input = context.Map<ExerciseExtendedFormDto, AddUserExerciseWithFormulaInput>(dto);

        input.UserId.Should().Be(userId);
        input.EloFormula.Should().Be(ExerciseEloFormula.PullupWeighted);
    }

    [Test]
    public void ExerciseExtendedFormDto_Should_Require_BodyPart_When_Deserialized()
    {
        const string json = """
            {
              "name": "Weighted pull-up",
              "eloFormula": { "id": "PullupWeighted", "displayName": "Pull-up weighted" }
            }
            """;

        var action = () => JsonSerializer.Deserialize<ExerciseExtendedFormDto>(json);

        action.Should().Throw<JsonException>();
    }

    [Test]
    public void ExerciseExtendedFormDto_Should_Deserialize_Legacy_String_Formula()
    {
        const string json = """
            {
              "name": "Weighted pull-up",
              "bodyPart": 4,
              "eloFormula": "PullupWeighted"
            }
            """;

        var dto = JsonSerializer.Deserialize<ExerciseExtendedFormDto>(json);

        dto.Should().NotBeNull();
        dto!.EloFormula.Should().NotBeNull();
        dto.EloFormula!.Id.Should().Be(ExerciseEloFormula.PullupWeighted.ToString());

        var services = new ServiceCollection();
services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        var input = mapper.Map<ExerciseExtendedFormDto, AddExerciseWithFormulaInput>(dto);

        input.EloFormula.Should().Be(ExerciseEloFormula.PullupWeighted);
    }

    [Test]
    public void ExerciseExtendedFormDto_Should_Deserialize_Lookup_Object_Name_As_DisplayName_Fallback()
    {
        const string json = """
            {
              "name": "Weighted pull-up",
              "bodyPart": 4,
              "eloFormula": { "id": "PullupWeighted", "name": "Pull-up weighted" }
            }
            """;

        var dto = JsonSerializer.Deserialize<ExerciseExtendedFormDto>(json);

        dto.Should().NotBeNull();
        dto!.EloFormula.Should().NotBeNull();
        dto.EloFormula!.Id.Should().Be(ExerciseEloFormula.PullupWeighted.ToString());
        dto.EloFormula.DisplayName.Should().Be("Pull-up weighted");

        var services = new ServiceCollection();
services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        var input = mapper.Map<ExerciseExtendedFormDto, AddExerciseWithFormulaInput>(dto);

        input.EloFormula.Should().Be(ExerciseEloFormula.PullupWeighted);
    }

    [Test]
    public void Chart_Profiles_Should_Map_Typed_Entity_Ids_To_Uuid_Strings()
    {
        var exerciseId = Id<Exercise>.New();
        var eloRegistryId = Id<EloRegistry>.New();
        var exerciseScores = new ExerciseScoresChartData
        {
            Id = "presentation-key",
            Value = 120,
            Date = "01/02",
            ExerciseName = "Squat"
        };
        var eloRegistry = new EloRegistryChartEntry
        {
            Value = 1020,
            Date = "01/03"
        };
        var exerciseIdProperty = typeof(ExerciseScoresChartData).GetProperty(nameof(ExerciseScoresChartData.ExerciseId));
        var eloRegistryIdProperty = typeof(EloRegistryChartEntry).GetProperty(nameof(EloRegistryChartEntry.Id));

        exerciseIdProperty!.PropertyType.Should().Be(typeof(Id<Exercise>));
        eloRegistryIdProperty!.PropertyType.Should().Be(typeof(Id<EloRegistry>));
        exerciseIdProperty.SetValue(exerciseScores, exerciseId);
        eloRegistryIdProperty.SetValue(eloRegistry, eloRegistryId);

        var services = new ServiceCollection();
services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        var exerciseScoresDto = mapper.Map<ExerciseScoresChartData, ExerciseScoresChartDataDto>(exerciseScores);
        var eloRegistryDto = mapper.Map<EloRegistryChartEntry, EloRegistryBaseChartDto>(eloRegistry);

        exerciseScoresDto.Id.Should().Be("presentation-key");
        exerciseScoresDto.ExerciseId.Should().Be(exerciseId.ToString());
        eloRegistryDto.Id.Should().Be(eloRegistryId.ToString());
    }
}
