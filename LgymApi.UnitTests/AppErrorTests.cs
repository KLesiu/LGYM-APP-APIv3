using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class AppErrorTests
{
    [Test]
    public void NotFoundError_ExposesMessageAnd404()
    {
        var error = new NotFoundError("missing");

        error.Message.Should().Be("missing");
        error.HttpStatusCode.Should().Be(404);
        error.GetPayload().Should().BeNull();
    }

    [Test]
    public void BadRequestError_ExposesMessageStatusAndPayload()
    {
        var payload = new { Field = "name" };
        var error = new BadRequestError("invalid", payload);

        error.Message.Should().Be("invalid");
        error.HttpStatusCode.Should().Be(400);
        error.GetPayload().Should().Be(payload);
    }

    [Test]
    public void RemainingBuiltInErrors_ExposeExpectedStatusCodes()
    {
        new UnauthorizedError("unauthorized").HttpStatusCode.Should().Be(401);
        new ForbiddenError("forbidden").HttpStatusCode.Should().Be(403);
        new ConflictError("conflict").HttpStatusCode.Should().Be(409);
        new UnprocessableEntityError("unprocessable", new { Code = 1 }).HttpStatusCode.Should().Be(422);
        new InternalServerError("boom").HttpStatusCode.Should().Be(500);
    }
}
