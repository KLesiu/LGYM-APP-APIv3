using System.Text.Json.Serialization;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Enum.Contracts;
using LgymApi.Api.Interfaces;
using LgymApi.Domain.Enums;

namespace LgymApi.Api.Features.Exercise.Contracts;

public sealed class ExerciseFormDto : IDto
{
    [JsonPropertyName("_id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public string? UserId { get; set; }

    [JsonPropertyName("bodyPart")]
    public BodyParts BodyPart { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }
}

public sealed class ExerciseExtendedFormDto : IDto
{
    [JsonPropertyName("_id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public string? UserId { get; set; }

    [JsonPropertyName("bodyPart")]
    [JsonRequired]
    public BodyParts BodyPart { get; set; }

    [JsonPropertyName("eloFormula")]
    public LookupItemVm? EloFormula { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }
}

public sealed class ExerciseResponseDto : IResultDto
{
    [JsonPropertyName("_id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("user")]
    public string? UserId { get; set; }

    [JsonPropertyName("bodyPart")]
    public EnumLookupDto BodyPart { get; set; } = new();

    [JsonPropertyName("eloFormula")]
    public LookupItemVm? EloFormula { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }
}

public sealed class ExerciseTranslationDto : IDto
{
    [JsonPropertyName("exerciseId")]
    public string ExerciseId { get; set; } = string.Empty;

    [JsonPropertyName("culture")]
    public string Culture { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class SeriesScoreDto : IResultDto
{
    [JsonPropertyName("series")]
    public int Series { get; set; }

    [JsonPropertyName("score")]
    public ScoreDto? Score { get; set; }
}

public class ScoreDto : IResultDto
{
    [JsonPropertyName("reps")]
    public double Reps { get; set; }

    [JsonPropertyName("weight")]
    public double Weight { get; set; }

    [JsonPropertyName("unit")]
    public EnumLookupDto Unit { get; set; } = new();

    [JsonPropertyName("_id")]
    public string Id { get; set; } = string.Empty;
}

public sealed class ScoreWithGymDto : ScoreDto, IResultDto
{
    [JsonPropertyName("gymName")]
    public string? GymName { get; set; }
}

public sealed class SeriesScoreWithGymDto : IResultDto
{
    [JsonPropertyName("series")]
    public int Series { get; set; }

    [JsonPropertyName("score")]
    public ScoreWithGymDto? Score { get; set; }
}

public sealed class LastExerciseScoresRequestDto : IDto
{
    [JsonPropertyName("series")]
    [JsonRequired]
    public int Series { get; set; }

    [JsonPropertyName("exerciseId")]
    public string ExerciseId { get; set; } = string.Empty;

    [JsonPropertyName("gym")]
    public string? GymId { get; set; }

    [JsonPropertyName("exerciseName")]
    public string ExerciseName { get; set; } = string.Empty;
}

public sealed class LastExerciseScoresResponseDto : IResultDto
{
    [JsonPropertyName("exerciseId")]
    public string ExerciseId { get; set; } = string.Empty;

    [JsonPropertyName("exerciseName")]
    public string ExerciseName { get; set; } = string.Empty;

    [JsonPropertyName("seriesScores")]
    public List<SeriesScoreWithGymDto> SeriesScores { get; set; } = new();
}

public sealed class ExerciseByBodyPartRequestDto : IDto
{
    [JsonPropertyName("bodyPart")]
    [JsonRequired]
    public BodyParts BodyPart { get; set; }
}

public sealed class ExerciseTrainingHistoryItemDto : IResultDto
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("gymName")]
    public string GymName { get; set; } = string.Empty;

    [JsonPropertyName("trainingName")]
    public string TrainingName { get; set; } = string.Empty;

    [JsonPropertyName("seriesScores")]
    public List<SeriesScoreDto> SeriesScores { get; set; } = new();
}
