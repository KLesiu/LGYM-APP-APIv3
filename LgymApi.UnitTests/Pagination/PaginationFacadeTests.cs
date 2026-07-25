using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Pagination;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PaginationFacadeTests
{
    [Test]
    public void PaginationFacade_ExposesApplicationSafeContract()
    {
        var interfaceType = typeof(IQueryPaginationService);
        interfaceType.Namespace.Should().Be("LgymApi.Application.Pagination");

        var method = interfaceType.GetMethod(nameof(IQueryPaginationService.ExecuteAsync));
        method.Should().NotBeNull();

        method!.ReturnType.IsGenericType.Should().BeTrue();
        method.ReturnType.GetGenericTypeDefinition().Should().Be(typeof(Task<>));
        var resultType = method.ReturnType.GetGenericArguments()[0];
        resultType.GetGenericTypeDefinition().Should().Be(typeof(Result<,>));
        resultType.GetGenericArguments()[0].GetGenericTypeDefinition().Should().Be(typeof(Pagination<>));
        resultType.GetGenericArguments()[1].Should().Be(typeof(AppError));

        var genericArguments = method.GetGenericArguments();
        genericArguments.Should().HaveCount(1);

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(3);
        parameters[0].ParameterType.GetGenericTypeDefinition().Should().Be(typeof(Func<>));
        parameters[0].ParameterType.GetGenericArguments()[0].GetGenericTypeDefinition().Should().Be(typeof(IQueryable<>));
        parameters[0].ParameterType.GetGenericArguments()[0].GetGenericArguments()[0].Should().Be(genericArguments[0]);
        parameters[1].ParameterType.Should().Be(typeof(FilterInput));
        parameters[2].ParameterType.Should().Be(typeof(CancellationToken));

        typeof(FilterInput).Assembly.GetReferencedAssemblies()
            .Select(x => x.Name)
            .Should().NotContain(name => name != null && name.Contains("Gridify", StringComparison.OrdinalIgnoreCase));
    }
}
