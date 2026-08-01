using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Pagination;
using LgymApi.Infrastructure.Pagination;
using NUnit.Framework;
using GridifyExecutionServiceContract = LgymApi.Infrastructure.Pagination.IGridifyExecutionService;
using PaginationFacade = LgymApi.Infrastructure.Pagination.QueryPaginationService;

namespace LgymApi.UnitTests.Pagination;

[TestFixture]
public sealed class QueryPaginationServiceTests
{
    private static readonly PaginationPolicy Policy = new()
    {
        MaxPageSize = 100,
        DefaultPageSize = 20,
        DefaultSortField = "name",
        TieBreakerField = "id"
    };

    [Test]
    public async Task PaginationFacadeService_ReturnsSuccessWrappedResult()
    {
        var service = CreateService(new FakeGridifyExecutionService());
        var query = new[]
        {
            new TestProjection { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Alpha" }
        }.AsQueryable();

        var result = await service.ExecuteAsync(() => query, new FilterInput { Page = 1, PageSize = 10 }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].Name.Should().Be("Alpha");
    }

    [Test]
    public async Task PaginationFacadeService_ReturnsValidationErrorWrappedResult()
    {
        var service = CreateService(new FakeGridifyExecutionService(
            throwArgument: new ArgumentException("Sort field 'unknown' is not allowed.")));
        var query = Array.Empty<TestProjection>().AsQueryable();

        var result = await service.ExecuteAsync(() => query, new FilterInput { Page = 1, PageSize = 10 }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<BadRequestError>();
        result.Error.Message.Should().Contain("Sort field 'unknown' is not allowed");
    }

    [Test]
    public async Task PaginationFacadeService_ReturnsInternalServerErrorForUnexpectedFailures()
    {
        var service = CreateService(new FakeGridifyExecutionService(new Exception("boom")));
        var query = Array.Empty<TestProjection>().AsQueryable();

        var result = await service.ExecuteAsync(() => query, new FilterInput { Page = 1, PageSize = 10 }, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InternalServerError>();
        result.Error.Message.Should().Be("Pagination failed.");
    }

    [Test]
    public async Task PaginationFacadeService_UsesQueryFactoryDirectlyWithoutReflection()
    {
        var service = CreateService(new FakeGridifyExecutionService());
        var invocationCount = 0;

        var result = await service.ExecuteAsync(
            () =>
            {
                invocationCount++;
                return new[]
                {
                    new TestProjection { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "Beta" }
                }.AsQueryable();
            },
            new FilterInput { Page = 1, PageSize = 10 },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        invocationCount.Should().Be(1);
    }

    private static PaginationFacade CreateService(GridifyExecutionServiceContract gridifyExecutionService)
        => new(gridifyExecutionService, new FakeMapperRegistry(), Policy);

    private sealed class FakeGridifyExecutionService : GridifyExecutionServiceContract
    {
        private readonly Exception? _throwArgument;

        public FakeGridifyExecutionService(Exception? throwArgument = null)
        {
            _throwArgument = throwArgument;
        }

        public Task<Pagination<TProjection>> ExecuteAsync<TProjection>(
            IQueryable<TProjection> baseQuery,
            FilterInput filterInput,
            IMapperRegistry mapperRegistry,
            PaginationPolicy paginationPolicy,
            CancellationToken cancellationToken = default)
            where TProjection : class
        {
            if (_throwArgument is not null)
            {
                throw _throwArgument;
            }

            return Task.FromResult(new Pagination<TProjection>
            {
                Items = baseQuery.Take(1).ToList(),
                Page = filterInput.Page,
                PageSize = filterInput.PageSize,
                TotalCount = 1
            });
        }
    }

    private sealed class FakeMapperRegistry : IMapperRegistry
    {
        public void Register<TProjection>(IEnumerable<FieldMapping> mappings) { }

        public IReadOnlyCollection<FieldMapping> GetMappings<TProjection>() => [];

        public bool TryGetMapping<TProjection>(string fieldName, out FieldMapping? mapping)
        {
            mapping = null;
            return false;
        }
    }

    private sealed record TestProjection
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }
}
