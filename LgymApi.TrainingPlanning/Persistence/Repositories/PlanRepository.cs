using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Plan.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Contracts;
using LgymApi.TrainingPlanning.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace LgymApi.Infrastructure.Repositories;

internal sealed partial class PlanRepository : IPlanRepository
{
    private const int ShareCodeLength = 10;
    private const int ShareCodeGenerationMaxAttempts = 10;
    private const string ShareCodeAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private static readonly HashSet<char> ShareCodeAllowedCharacters = [.. ShareCodeAlphabet];

    private readonly ITrainingPlanningPersistenceContext _context;
    private readonly Func<int, string> _shareCodeGenerator;

    public PlanRepository(ITrainingPlanningPersistenceContext context, Func<int, string>? shareCodeGenerator = null)
    {
        _context = context;
        _shareCodeGenerator = shareCodeGenerator ?? GenerateSecureAlphanumericCode;
    }

    public Task<Plan?> FindByIdAsync(Id<Plan> id, CancellationToken cancellationToken = default)
        => _context.Plans.FirstOrDefaultAsync(plan => plan.Id == id && !plan.IsDeleted, cancellationToken);

    public Task<Plan?> FindActiveByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
        => _context.Plans.AsNoTracking().FirstOrDefaultAsync(plan => plan.UserId == userId && plan.IsActive && !plan.IsDeleted, cancellationToken);

    public Task<PlanReadModel?> FindActiveReadModelByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
        => _context.Plans
            .AsNoTracking()
            .Where(plan => plan.UserId == userId && plan.IsActive && !plan.IsDeleted)
            .Select(plan => new PlanReadModel(plan.Id, plan.UserId, plan.Name, plan.IsActive, plan.ShareCode))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Plan?> FindLastActiveByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
        => _context.Plans
            .AsNoTracking()
            .Where(plan => plan.UserId == userId && !plan.IsActive && !plan.IsDeleted)
            .OrderByDescending(plan => plan.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<List<Plan>> GetByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
        => _context.Plans.AsNoTracking().Where(plan => plan.UserId == userId && !plan.IsDeleted).ToListAsync(cancellationToken);

    public Task<List<PlanReadModel>> GetReadModelsByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
        => _context.Plans
            .AsNoTracking()
            .Where(plan => plan.UserId == userId && !plan.IsDeleted)
            .Select(plan => new PlanReadModel(plan.Id, plan.UserId, plan.Name, plan.IsActive, plan.ShareCode))
            .ToListAsync(cancellationToken);

    public Task AddAsync(Plan plan, CancellationToken cancellationToken = default)
        => _context.Plans.AddAsync(plan, cancellationToken).AsTask();

    public Task UpdateAsync(Plan plan, CancellationToken cancellationToken = default)
    {
        _context.Plans.Update(plan);
        return Task.CompletedTask;
    }

    public async Task SetActivePlanAsync(Id<User> userId, Id<Plan> planId, CancellationToken cancellationToken = default)
    {
        var plans = await _context.Plans
            .Where(plan => plan.UserId == userId && !plan.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var plan in plans)
        {
            plan.IsActive = plan.Id == planId;
        }
    }

    public async Task ClearActivePlansAsync(Id<User> userId, CancellationToken cancellationToken = default)
    {
        var plans = await _context.Plans
            .Where(plan => plan.UserId == userId && !plan.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var plan in plans)
        {
            plan.IsActive = false;
        }
    }

    public Task<Plan?> FindByShareCodeAsync(string shareCode, CancellationToken cancellationToken = default)
        => _context.Plans.FirstOrDefaultAsync(plan => plan.ShareCode == shareCode && !plan.IsDeleted, cancellationToken);

    public async Task<IReadOnlyCollection<Id<PlanExerciseReference>>> GetPlanExerciseIdsAsync(
        Id<Plan> planId,
        CancellationToken cancellationToken = default)
        => (await _context.PlanDayExercises
                .Where(exercise => exercise.PlanDay.PlanId == planId && !exercise.PlanDay.IsDeleted)
                .Select(exercise => exercise.ExerciseId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(id => id.Rebind<PlanExerciseReference>())
            .ToArray();

    public async Task<Plan> ClonePlanAsync(
        Id<Plan> sourcePlanId,
        Id<User> userId,
        IReadOnlyDictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>> exerciseIdMap,
        bool isActive = true,
        CancellationToken cancellationToken = default)
    {
        var planToCopy = await _context.Plans
            .FirstOrDefaultAsync(plan => plan.Id == sourcePlanId && !plan.IsDeleted, cancellationToken);

        if (planToCopy is null)
        {
            throw new InvalidOperationException("Plan not found");
        }

        return await ClonePlanGraphAsync(planToCopy, userId, exerciseIdMap, isActive, cancellationToken);
    }

    public async Task<string> GenerateShareCodeAsync(Id<Plan> planId, Id<User> userId, CancellationToken cancellationToken = default)
    {
        var plan = await _context.Plans.FirstOrDefaultAsync(item => item.Id == planId && !item.IsDeleted, cancellationToken);

        if (plan is null)
        {
            throw new KeyNotFoundException("Plan not found");
        }

        if (plan.UserId != userId)
        {
            throw new UnauthorizedAccessException("Only the plan owner can generate a share code");
        }

        if (!string.IsNullOrEmpty(plan.ShareCode))
        {
            var isCurrentCodeTaken = await IsShareCodeTakenAsync(plan.ShareCode, plan.Id, cancellationToken);
            if (!isCurrentCodeTaken)
            {
                return plan.ShareCode;
            }

            plan.ShareCode = null;
        }

        for (var attempt = 0; attempt < ShareCodeGenerationMaxAttempts; attempt++)
        {
            var candidateCode = _shareCodeGenerator(ShareCodeLength);
            if (!IsValidShareCode(candidateCode))
            {
                continue;
            }

            if (await IsShareCodeTakenAsync(candidateCode, plan.Id, cancellationToken))
            {
                continue;
            }

            plan.ShareCode = candidateCode;
            return plan.ShareCode;
        }

        throw new InvalidOperationException("Unable to generate unique share code");
    }

    private Task<bool> IsShareCodeTakenAsync(string shareCode, Id<Plan> currentPlanId, CancellationToken cancellationToken)
        => _context.Plans.AnyAsync(
            plan => plan.Id != currentPlanId && plan.ShareCode == shareCode && !plan.IsDeleted,
            cancellationToken);

    private static bool IsValidShareCode(string? shareCode)
        => !string.IsNullOrWhiteSpace(shareCode) &&
           shareCode.Length == ShareCodeLength &&
           shareCode.All(ShareCodeAllowedCharacters.Contains);

    private static string GenerateSecureAlphanumericCode(int length)
    {
        var result = new char[length];

        for (var index = 0; index < length; index++)
        {
            result[index] = ShareCodeAlphabet[RandomNumberGenerator.GetInt32(ShareCodeAlphabet.Length)];
        }

        return new string(result);
    }
}
