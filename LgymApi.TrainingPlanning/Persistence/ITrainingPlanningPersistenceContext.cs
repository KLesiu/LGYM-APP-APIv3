using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.TrainingPlanning.Persistence;

internal interface ITrainingPlanningPersistenceContext
{
    DbSet<Plan> Plans { get; }
    DbSet<PlanDay> PlanDays { get; }
    DbSet<PlanDayExercise> PlanDayExercises { get; }
}
