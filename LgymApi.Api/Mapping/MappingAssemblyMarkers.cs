using System.Reflection;
using LgymApi.Identity.Contracts;
using LgymApi.Notifications.Contracts;
using LgymApi.Platform.Contracts;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Api.Mapping;

public static class MappingAssemblyMarkers
{
    public static Assembly[] All =>
    [
        typeof(Program).Assembly,
        typeof(LgymApi.Application.ServiceCollectionExtensions).Assembly,
        typeof(ActorReference).Assembly,
        typeof(AccountReference).Assembly,
        typeof(PlanReference).Assembly,
        typeof(NotificationReference).Assembly
    ];
}
