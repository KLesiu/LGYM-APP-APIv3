using FluentAssertions;
using LgymApi.Domain.ValueObjects;
using LgymApi.Domain.Entities;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ExerciseRepositoryTests
{
    [Test]
    public async Task GetTranslationsAsync_WithOnlyEmptyCultures_ReturnsEmptyDictionary()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"exercise-repo-{LgymApi.Domain.ValueObjects.Id<ExerciseRepositoryTests>.New():N}")
            .Options;

        await using var dbContext = new AppDbContext(options);

        var exerciseId = LgymApi.Domain.ValueObjects.Id<Exercise>.New();
        dbContext.Exercises.Add(new Exercise
        {
            Id = exerciseId,
            Name = "Bench press"
        });
        dbContext.ExerciseTranslations.Add(new ExerciseTranslation
        {
            Id = LgymApi.Domain.ValueObjects.Id<ExerciseTranslation>.New(),
            ExerciseId = exerciseId,
            Culture = "en",
            Name = "Bench press"
        });
        await dbContext.SaveChangesAsync();

        var repository = new ExerciseRepository(dbContext);
        var result = await repository.GetTranslationsAsync([exerciseId], ["   ", ""], CancellationToken.None);

        result.Should().BeEmpty();
    }
}
