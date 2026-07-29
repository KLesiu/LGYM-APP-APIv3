using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.User.Models;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Identity.Contracts.Registration;

public interface IUserRegistrationService
{
    Task<Result<Id<UserEntity>, AppError>> RegisterAsync(RegisterUserInput input, CancellationToken cancellationToken = default);
    Task<Result<Id<UserEntity>, AppError>> RegisterTrainerAsync(RegisterUserInput input, CancellationToken cancellationToken = default);
}
