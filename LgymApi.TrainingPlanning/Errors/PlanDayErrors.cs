using LgymApi.Application.BuildingBlocks.Errors;

namespace LgymApi.Application.TrainingPlanning.Errors;

public sealed class PlanDayNotFoundError(string message) : NotFoundError(message);

public sealed class InvalidPlanDayError(string message) : BadRequestError(message);

public sealed class PlanDayForbiddenError(string message) : ForbiddenError(message);
