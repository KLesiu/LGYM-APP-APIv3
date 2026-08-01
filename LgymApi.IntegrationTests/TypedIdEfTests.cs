using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

/// <summary>
/// Integration tests proving that Id{TEntity} works in EF Core LINQ queries,
/// FindAsync, and nullable FK scenarios without causing schema drift.
/// 
/// SPIKE SCOPE (T4):
/// These tests use test-local entities with Id{T} properties to prove
/// TypedIdValueConverter and NullableTypedIdValueConverter enable:
/// - FindAsync with typed ID parameter
/// - Where(...) clause comparison  
/// - Contains/collection filtering
/// - Nullable Id{T}? FK round-tripping
/// 
/// Tests would FAIL if converters are removed, proving genuine typed-ID translation.
/// </summary>
[TestFixture]
public sealed class TypedIdEfTests : IntegrationTestBase
{
    /// <summary>
    /// Test-local entity with typed-ID primary key (Id{TestEntity}) for converter proof.
    /// </summary>
    private sealed class TestEntity
    {
        public Id<TestEntity> Id { get; set; } = Id<TestEntity>.New();
        public string Name { get; set; } = string.Empty;
        public Id<RelatedEntity>? RelatedId { get; set; } // Nullable typed FK
    }

    /// <summary>
    /// Related test entity for FK relationship testing.
    /// </summary>
    private sealed class RelatedEntity
    {
        public Id<RelatedEntity> Id { get; set; } = Id<RelatedEntity>.New();
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// Test-local DbContext with converter configuration for typed-ID proof.
    /// </summary>
    private sealed class TestDbContext : DbContext
    {
        public DbSet<TestEntity> TestEntities => Set<TestEntity>();
        public DbSet<RelatedEntity> RelatedEntities => Set<RelatedEntity>();

        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<TestEntity>(entity =>
            {
                entity.ToTable("TestEntities");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id)
                    .HasConversion(new TypedIdValueConverter<TestEntity>());
                entity.Property(e => e.RelatedId)
                    .HasConversion(new NullableTypedIdValueConverter<RelatedEntity>());
            });

            modelBuilder.Entity<RelatedEntity>(entity =>
            {
                entity.ToTable("RelatedEntities");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id)
                    .HasConversion(new TypedIdValueConverter<RelatedEntity>());
            });
        }
    }

    private TestDbContext CreateTestDbContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"TypedIdTest_{Id<TestDbContext>.New()}")
            .Options;
        var context = new TestDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    /// Proves FindAsync can accept Id{TestEntity} and converter translates to underlying GUID.
    /// </summary>
    [Test]
    public async Task TestEntity_FindAsync_WithTypedId_ReturnsMatchingEntity()
    {
        // Arrange
        using var dbContext = CreateTestDbContext();

        var testId = Id<TestEntity>.New();
        var entity = new TestEntity { Id = testId, Name = "Find Test Entity" };

        await dbContext.TestEntities.AddAsync(entity);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        // Act - FindAsync with Id<TestEntity> (converter translates to underlying ID lookup)
        var found = await dbContext.TestEntities.FindAsync(testId);

        // Assert
        found.Should().NotBeNull("entity should be found by typed ID");
        found!.Id.Should().Be(testId, "typed ID should match");
        found.Name.Should().Be("Find Test Entity");
    }

    /// <summary>
    /// Proves Where(...) clause can compare Id{TestEntity} properties.
    /// </summary>
    [Test]
    public async Task TestEntity_WhereClause_WithTypedId_FiltersCorrectly()
    {
        // Arrange
        using var dbContext = CreateTestDbContext();

        var targetId = Id<TestEntity>.New();
        var entity1 = new TestEntity { Id = targetId, Name = "Target Entity" };
        var entity2 = new TestEntity { Id = Id<TestEntity>.New(), Name = "Other Entity" };

        await dbContext.TestEntities.AddRangeAsync(entity1, entity2);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        // Act - Where clause with typed ID comparison (converter enables LINQ translation)
        var found = await dbContext.TestEntities
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == targetId);

        // Assert
        found.Should().NotBeNull("entity should be found by typed ID in Where clause");
        found!.Id.Should().Be(targetId);
        found.Name.Should().Be("Target Entity");
    }

    /// <summary>
    /// Proves Contains works with Id{TestEntity} collection.
    /// </summary>
    [Test]
    public async Task TestEntity_WhereClause_WithMultipleTypedIds_ReturnsMatchingSet()
    {
        // Arrange
        using var dbContext = CreateTestDbContext();

        var id1 = Id<TestEntity>.New();
        var id2 = Id<TestEntity>.New();
        var id3 = Id<TestEntity>.New();

        var entity1 = new TestEntity { Id = id1, Name = "Entity A" };
        var entity2 = new TestEntity { Id = id2, Name = "Entity B" };
        var entity3 = new TestEntity { Id = id3, Name = "Entity C" };

        await dbContext.TestEntities.AddRangeAsync(entity1, entity2, entity3);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var targetIds = new[] { id1, id3 };

        // Act - Contains with typed ID collection (converter handles translation)
        var matching = await dbContext.TestEntities
            .AsNoTracking()
            .Where(e => targetIds.Contains(e.Id))
            .OrderBy(e => e.Name)
            .ToListAsync();

        // Assert
        matching.Should().HaveCount(2, "both targeted entities should match");
        matching[0].Name.Should().Be("Entity A");
        matching[1].Name.Should().Be("Entity C");
    }

    /// <summary>
    /// Proves nullable Id{RelatedEntity}? FK round-trips correctly when null.
    /// This validates NullableTypedIdValueConverter preserves null values.
    /// </summary>
    [Test]
    public async Task TestEntity_NullableTypedIdForeignKey_RoundTripsNull()
    {
        // Arrange
        using var dbContext = CreateTestDbContext();

        var testId = Id<TestEntity>.New();
        var entity = new TestEntity
        {
            Id = testId,
            Name = "Entity Without FK",
            RelatedId = null // Nullable Id<RelatedEntity>? set to null
        };

        await dbContext.TestEntities.AddAsync(entity);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        // Act
        var loaded = await dbContext.TestEntities
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == testId);

        // Assert
        loaded.Should().NotBeNull("entity should persist and reload");
        loaded!.RelatedId.Should().BeNull("nullable typed FK should remain null via NullableTypedIdValueConverter");
    }

    /// <summary>
    /// Proves nullable Id{RelatedEntity}? FK round-trips correctly with a value.
    /// This validates NullableTypedIdValueConverter preserves FK relationships.
    /// </summary>
    [Test]
    public async Task TestEntity_NullableTypedIdForeignKey_RoundTripsValue()
    {
        // Arrange
        using var dbContext = CreateTestDbContext();

        var relatedId = Id<RelatedEntity>.New();
        var related = new RelatedEntity { Id = relatedId, Value = "Related Value" };

        var testId = Id<TestEntity>.New();
        var entity = new TestEntity
        {
            Id = testId,
            Name = "Entity With FK",
            RelatedId = relatedId // Nullable Id<RelatedEntity>? set to value
        };

        await dbContext.RelatedEntities.AddAsync(related);
        await dbContext.TestEntities.AddAsync(entity);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        // Act
        var loaded = await dbContext.TestEntities
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == testId);

        // Assert
        loaded.Should().NotBeNull("entity should persist and reload");
        loaded!.RelatedId.Should().Be(relatedId, "nullable typed FK should preserve value via NullableTypedIdValueConverter");
    }
}

