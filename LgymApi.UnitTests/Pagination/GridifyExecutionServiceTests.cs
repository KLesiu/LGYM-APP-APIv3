using FluentAssertions;
using LgymApi.Application.Pagination;
using LgymApi.Infrastructure;
using LgymApi.Infrastructure.Pagination;
using LgymApi.Platform;
using LgymApi.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.UnitTests.Pagination;

[TestFixture]
public sealed class GridifyExecutionServiceTests
{
    [Test]
    public async Task GridifyExecutionService_ReturnsCorrectPageAndTotalCount()
    {
        await using var dbContext = CreateDbContext();
        var service = new GridifyExecutionService();
        var registry = CreateRegistry();

        await SeedRowsAsync(dbContext,
        [
            new TestRow { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Alpha", Rank = 10, IsActive = true },
            new TestRow { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "Bravo", Rank = 20, IsActive = true },
            new TestRow { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = "Charlie", Rank = 30, IsActive = true },
            new TestRow { Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), Name = "Delta", Rank = 40, IsActive = true },
            new TestRow { Id = Guid.Parse("55555555-5555-5555-5555-555555555555"), Name = "Echo", Rank = 50, IsActive = false }
        ]);

        var result = await service.ExecuteAsync(
            dbContext.Rows,
            new FilterInput
            {
                Page = 2,
                PageSize = 2,
                FilterGroups =
                [
                    new FilterGroup
                    {
                        Operator = GroupOperator.And,
                        Conditions =
                        [
                            new FilterCondition
                            {
                                FieldName = "isActive",
                                Operator = FilterOperator.Equals,
                                Value = true
                            }
                        ]
                    }
                ],
                SortDescriptors = [new SortDescriptor { FieldName = "rank", Descending = true }]
            },
            registry,
            CreatePolicy());

        result.TotalCount.Should().Be(4);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(2);
        result.Items.Select(x => x.Rank).Should().Equal(20, 10);
        dbContext.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Test]
    public async Task GridifyExecutionService_ReturnsEmptyItemsWhenPageBeyondTotal()
    {
        await using var dbContext = CreateDbContext();
        var service = new GridifyExecutionService();
        var registry = CreateRegistry();

        await SeedRowsAsync(dbContext,
        [
            new TestRow { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Name = "Alpha", Rank = 10, IsActive = true },
            new TestRow { Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Name = "Bravo", Rank = 20, IsActive = true },
            new TestRow { Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), Name = "Charlie", Rank = 30, IsActive = true }
        ]);

        var result = await service.ExecuteAsync(
            dbContext.Rows,
            new FilterInput
            {
                Page = 4,
                PageSize = 2,
                SortDescriptors = [new SortDescriptor { FieldName = "name" }]
            },
            registry,
            CreatePolicy());

        result.TotalCount.Should().Be(3);
        result.Items.Should().BeEmpty();
        result.Page.Should().Be(4);
        result.PageSize.Should().Be(2);
    }

    [Test]
    public void GridifyExecutionService_IsResolvableFromDependencyInjection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlatformModule();
        services.AddInfrastructure(
            TestConfigurationBuilder.BuildEnabledEmailConfiguration(),
            enableSensitiveLogging: false,
            isTesting: true);

        using var provider = services.BuildServiceProvider();

        var service = provider.GetService<GridifyExecutionService>();

        service.Should().NotBeNull();
    }

    private static async Task SeedRowsAsync(TestPaginationDbContext dbContext, IEnumerable<TestRow> rows)
    {
        dbContext.Rows.AddRange(rows);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private static TestPaginationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TestPaginationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TestPaginationDbContext(options);
    }

    private static MapperRegistry CreateRegistry()
    {
        var registry = new MapperRegistry();
        registry.Register<TestRow>(
        [
            new FieldMapping { FieldName = "id", MemberName = nameof(TestRow.Id), AllowFilter = false },
            new FieldMapping { FieldName = "name", MemberName = nameof(TestRow.Name) },
            new FieldMapping { FieldName = "rank", MemberName = nameof(TestRow.Rank) },
            new FieldMapping { FieldName = "isActive", MemberName = nameof(TestRow.IsActive) }
        ]);

        return registry;
    }

    private static PaginationPolicy CreatePolicy() => new()
    {
        MaxPageSize = 100,
        DefaultPageSize = 20,
        DefaultSortField = "name",
        TieBreakerField = "id"
    };

    private sealed class TestPaginationDbContext(DbContextOptions<TestPaginationDbContext> options) : DbContext(options)
    {
        public DbSet<TestRow> Rows => Set<TestRow>();
    }

    private sealed class TestRow
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public int Rank { get; init; }

        public bool IsActive { get; init; }
    }
}
