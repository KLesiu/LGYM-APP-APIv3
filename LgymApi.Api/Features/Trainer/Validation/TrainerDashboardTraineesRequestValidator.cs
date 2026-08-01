using FluentValidation;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Application.Coaching.Relationships.TrainerDashboard;
using LgymApi.Resources;

namespace LgymApi.Api.Features.Trainer.Validation;

public sealed class TrainerDashboardTraineesRequestValidator : AbstractValidator<TrainerDashboardTraineesRequest>
{
    private const int MaxPage = 21_474_837;
    private static readonly string[] AllowedSortBy = ["name", "createdAt", "status"];
    private static readonly string[] AllowedSortDirection = ["asc", "desc"];

    public TrainerDashboardTraineesRequestValidator()
    {
        RuleFor(x => x.Page)
            .InclusiveBetween(1, MaxPage)
            .WithMessage(Messages.DashboardPageRange);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage(Messages.DashboardPageSizeRange);

        RuleFor(x => x.SortBy)
            .Must(value => string.IsNullOrWhiteSpace(value) || AllowedSortBy.Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage(Messages.DashboardSortByInvalid);

        RuleFor(x => x.SortDirection)
            .Must(value => string.IsNullOrWhiteSpace(value) || AllowedSortDirection.Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage(Messages.DashboardSortDirectionInvalid);

        RuleFor(x => x.Status)
            .Must(value => string.IsNullOrWhiteSpace(value) || System.Enum.TryParse(value, true, out TrainerDashboardTraineeStatus _))
            .WithMessage(Messages.DashboardStatusInvalid);
    }
}
