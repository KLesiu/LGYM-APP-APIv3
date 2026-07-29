using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.EloRegistry;
using LgymApi.Application.Features.Role;
using LgymApi.Application.Features.User;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Features.Tutorial.Models;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Identity.Authentication;
using LgymApi.Application.Identity.Profile;
using LgymApi.Application.Identity.Registration;
using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Application.Coaching.Contracts;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Notifications;
using LgymApi.Application.Options;
using LgymApi.Application.Pagination;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Application.TrainingPlanning.Plan.ActivePlanPointer;
using LgymApi.Application.TrainingPlanning.Plan.CreatePlan;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Pagination;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.Repositories.WorkoutProgress;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Infrastructure.Services;
using LgymApi.Infrastructure.UnitOfWork;
using LgymApi.TestUtils.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ServiceCommitBehaviorTests
{
    [Test]
    public async Task CreatePlanAsync_PersistsPlanAndActivePlanProjection()
    {
        var dbName = $"service-commit-plan-{Id<Plan>.New():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using var dbContext = new AppDbContext(options);
        var user = new User
        {
            Id = Id<User>.New(),
            Name = "plan-user",
            Email = "plan-user@example.com",
            ProfileRank = "Junior 1",
            LegacyHash = "hash",
            LegacySalt = "salt",
            LegacyDigest = "sha256",
            LegacyIterations = 25000,
            LegacyKeyLength = 512
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        IPlanRepository planRepository = new PlanRepository(dbContext);
        IUnitOfWork unitOfWork = new EfUnitOfWork(dbContext);
        IActivePlanPointerStore activePlanPointerStore = new ActivePlanPointerStore(dbContext);
        var useCase = new CreatePlanUseCase(planRepository, activePlanPointerStore, unitOfWork);

        var result = await useCase.ExecuteAsync(new CreatePlanCommand(user.Id, user.Id, "UoW Plan"));

        result.IsSuccess.Should().BeTrue();

        var savedPlan = await dbContext.Plans.FirstOrDefaultAsync(p => p.UserId == user.Id && p.Name == "UoW Plan");
        savedPlan.Should().NotBeNull();

        (await activePlanPointerStore.GetActivePlanIdAsync(user.Id)).Should().Be(savedPlan!.Id);
    }

    [Test]
    public async Task RegisterAsync_PersistsUserAndInitialElo()
    {
        var dbName = $"service-commit-register-{Id<User>.New():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using var dbContext = new AppDbContext(options);

        dbContext.Roles.Add(new Role
        {
            Id = Id<Role>.New(),
            Name = AuthConstants.Roles.User,
            Description = "Default user role"
        });
        await dbContext.SaveChangesAsync();

        IUserRepository userRepository = CreateUserRepository(dbContext);
        IRoleRepository roleRepository = CreateRoleRepository(dbContext);
        IWorkoutEloPersistence eloRepository = new WorkoutEloPersistenceRepository(dbContext);
        ILegacyPasswordService legacyPasswordService = new LegacyPasswordService();
        IUnitOfWork unitOfWork = new EfUnitOfWork(dbContext);
        ICommandDispatcher commandDispatcher = new NoOpCommandDispatcher();

        var userRegistrationService = new UserRegistrationService(new UserRegistrationServiceDependencies(
            userRepository,
            roleRepository,
            legacyPasswordService,
            commandDispatcher,
            unitOfWork,
            NullLogger<UserRegistrationService>.Instance,
            new AppDefaultsOptions(),
            new NoOpTutorialService()));
        var progress = new WorkoutProgressReadWriteService(new WorkoutProgressReadWriteServiceDependencies(
            Substitute.For<IWorkoutExercisePersistence>(),
            Substitute.For<IWorkoutExerciseScorePersistence>(),
            Substitute.For<IWorkoutMeasurementPersistence>(),
            Substitute.For<IWorkoutMainRecordPersistence>(),
            eloRepository,
            Substitute.For<IAccountAccessReader>(),
            Substitute.For<IUnitConverter<HeightUnits>>(),
            Substitute.For<IUnitConverter<WeightUnits>>(),
            unitOfWork));
        var service = new EloRegistryService(progress, userRegistrationService, unitOfWork);

        var registerResult = await service.RegisterUserAsync(new RegisterUserInput(
            "newuser",
            "newuser@example.com",
            "password123",
            "password123",
            true,
            PreferredLanguage: null), trainer: false);

        registerResult.IsSuccess.Should().BeTrue();

        var savedUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Name == "newuser");
        savedUser.Should().NotBeNull();

        var savedElo = await dbContext.EloRegistries.FirstOrDefaultAsync(e => e.UserId == savedUser!.Id);
        savedElo.Should().NotBeNull();
        savedElo!.Elo.Value.Should().Be(1000);
    }

    [Test]
    public async Task RegisterAsync_UsesPrimaryCultureFromAcceptLanguageHeader()
    {
        var dbName = $"service-commit-register-culture-header-{Id<User>.New():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using var dbContext = new AppDbContext(options);

        dbContext.Roles.Add(new Role
        {
            Id = Id<Role>.New(),
            Name = AuthConstants.Roles.User,
            Description = "Default user role"
        });
        await dbContext.SaveChangesAsync();

        IUserRepository userRepository = CreateUserRepository(dbContext);
        IRoleRepository roleRepository = CreateRoleRepository(dbContext);
        ILegacyPasswordService legacyPasswordService = new LegacyPasswordService();
        IUnitOfWork unitOfWork = new EfUnitOfWork(dbContext);
        ICommandDispatcher commandDispatcher = new NoOpCommandDispatcher();

        var service = new UserRegistrationService(new UserRegistrationServiceDependencies(
            userRepository,
            roleRepository,
            legacyPasswordService,
            commandDispatcher,
            unitOfWork,
            NullLogger<UserRegistrationService>.Instance,
            new AppDefaultsOptions(),
            new NoOpTutorialService()));

        var registerResult = await service.RegisterAsync(new RegisterUserInput(
            "lang-user",
            "lang-user@example.com",
            "password123",
            "password123",
            true,
            "pl-PL,pl;q=0.9"));

        registerResult.IsSuccess.Should().BeTrue();

        var savedUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Name == "lang-user");
        savedUser.Should().NotBeNull();
        savedUser!.PreferredLanguage.Should().Be("pl-PL");
    }

    [Test]
    public async Task RegisterAsync_FallsBackToConfiguredPreferredLanguage_WhenHeaderInvalid()
    {
        var dbName = $"service-commit-register-culture-fallback-{Id<User>.New():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using var dbContext = new AppDbContext(options);

        dbContext.Roles.Add(new Role
        {
            Id = Id<Role>.New(),
            Name = AuthConstants.Roles.User,
            Description = "Default user role"
        });
        await dbContext.SaveChangesAsync();

        IUserRepository userRepository = CreateUserRepository(dbContext);
        IRoleRepository roleRepository = CreateRoleRepository(dbContext);
        ILegacyPasswordService legacyPasswordService = new LegacyPasswordService();
        IUnitOfWork unitOfWork = new EfUnitOfWork(dbContext);
        ICommandDispatcher commandDispatcher = new NoOpCommandDispatcher();
        var defaults = new AppDefaultsOptions { PreferredLanguage = "de-DE", PreferredTimeZone = "UTC" };

        var service = new UserRegistrationService(new UserRegistrationServiceDependencies(
            userRepository,
            roleRepository,
            legacyPasswordService,
            commandDispatcher,
            unitOfWork,
            NullLogger<UserRegistrationService>.Instance,
            defaults,
            new NoOpTutorialService()));

        var registerResult = await service.RegisterAsync(new RegisterUserInput(
            "fallback-user",
            "fallback-user@example.com",
            "password123",
            "password123",
            true,
            "@@invalid-culture@@"));

        registerResult.IsSuccess.Should().BeTrue();

        var savedUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Name == "fallback-user");
        savedUser.Should().NotBeNull();
        savedUser!.PreferredLanguage.Should().Be("de-DE");
        savedUser.PreferredTimeZone.Should().Be("UTC");
    }

    [Test]
    public async Task UpdateTimeZoneAsync_PersistsUserPreference()
    {
        var dbName = $"service-commit-timezone-{Id<User>.New():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using var dbContext = new AppDbContext(options);

        dbContext.Roles.Add(new Role
        {
            Id = Id<Role>.New(),
            Name = AuthConstants.Roles.User,
            Description = "Default user role"
        });

        var user = new User
        {
            Id = Id<User>.New(),
            Name = "timezone-user",
            Email = "timezone-user@example.com",
            ProfileRank = "Junior 1",
            PreferredTimeZone = "Europe/Warsaw",
            LegacyHash = "hash",
            LegacySalt = "salt",
            LegacyDigest = "sha256",
            LegacyIterations = LegacyPasswordConstants.Iterations,
            LegacyKeyLength = LegacyPasswordConstants.KeyLength
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        IUserRepository userRepository = CreateUserRepository(dbContext);
        IRoleRepository roleRepository = CreateRoleRepository(dbContext);
        IUnitOfWork unitOfWork = new EfUnitOfWork(dbContext);

        var service = new UserProfileService(new UserProfileServiceDependencies(
            userRepository,
            roleRepository,
            new RankService(),
            unitOfWork,
            new AppDefaultsOptions(),
            new NoOpTutorialService(),
            Substitute.For<IMapper>()));

        var updateTimeZoneResult = await service.UpdateTimeZoneAsync(user, "Europe/Paris");
        updateTimeZoneResult.IsSuccess.Should().BeTrue();

        var savedUser = await dbContext.Users.SingleAsync(u => u.Id == user.Id);
        savedUser.PreferredTimeZone.Should().Be("Europe/Paris");
    }

    [Test]
    public async Task UpdateTimeZoneAsync_ThrowsBadRequest_WhenTimeZoneInvalid()
    {
        var dbName = $"service-commit-timezone-invalid-{Id<User>.New():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using var dbContext = new AppDbContext(options);

        dbContext.Roles.Add(new Role
        {
            Id = Id<Role>.New(),
            Name = AuthConstants.Roles.User,
            Description = "Default user role"
        });

        var user = new User
        {
            Id = Id<User>.New(),
            Name = "timezone-invalid-user",
            Email = "timezone-invalid-user@example.com",
            ProfileRank = "Junior 1",
            PreferredTimeZone = "Europe/Warsaw",
            LegacyHash = "hash",
            LegacySalt = "salt",
            LegacyDigest = "sha256",
            LegacyIterations = LegacyPasswordConstants.Iterations,
            LegacyKeyLength = LegacyPasswordConstants.KeyLength
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        IUserRepository userRepository = CreateUserRepository(dbContext);
        IRoleRepository roleRepository = CreateRoleRepository(dbContext);
        IUnitOfWork unitOfWork = new EfUnitOfWork(dbContext);

        var service = new UserProfileService(new UserProfileServiceDependencies(
            userRepository,
            roleRepository,
            new RankService(),
            unitOfWork,
            new AppDefaultsOptions(),
            new NoOpTutorialService(),
            Substitute.For<IMapper>()));

        var updateTimeZoneResult = await service.UpdateTimeZoneAsync(user, "Not/ARealTimeZone");
        updateTimeZoneResult.IsFailure.Should().BeTrue();
        updateTimeZoneResult.Error.Should().BeOfType<InvalidUserError>();
    }

    [Test]
    public async Task CreateRoleAsync_PersistsRoleAndClaims()
    {
        var dbName = $"service-commit-role-create-{Id<Plan>.New():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using var dbContext = new AppDbContext(options);

        IRoleRepository roleRepository = CreateRoleRepository(dbContext);
        IUserRepository userRepository = CreateUserRepository(dbContext);
        IUnitOfWork unitOfWork = new EfUnitOfWork(dbContext);

        var service = new RoleService(roleRepository, userRepository, unitOfWork);

        var result = await service.CreateRoleAsync(
            "Coach",
            "Role for coaching",
            [AuthConstants.Permissions.ManageGlobalExercises, AuthConstants.Permissions.ManageAppConfig]);

        var created = result.Value;

        var savedRole = await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == (Id<Role>)created.Id);
        savedRole.Should().NotBeNull();

        var savedClaims = await dbContext.RoleClaims
            .Where(rc => rc.RoleId == (Id<Role>)created.Id)
            .Select(rc => rc.ClaimValue)
            .OrderBy(v => v)
            .ToListAsync();

        savedClaims.Should().BeEquivalentTo(new[]
        {
            AuthConstants.Permissions.ManageAppConfig,
            AuthConstants.Permissions.ManageGlobalExercises
        });
    }

    [Test]
    public async Task UpdateUserRolesAsync_PersistsUserRoleAssignments()
    {
        var dbName = $"service-commit-role-assign-{Id<User>.New():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using var dbContext = new AppDbContext(options);

        var user = new User
        {
            Id = Id<User>.New(),
            Name = "role-user",
            Email = "role-user@example.com",
            ProfileRank = "Junior 1",
            LegacyHash = "hash",
            LegacySalt = "salt",
            LegacyDigest = "sha256",
            LegacyIterations = 25000,
            LegacyKeyLength = 512
        };

        dbContext.Users.Add(user);
        dbContext.Roles.Add(new Role { Id = Id<Role>.New(), Name = AuthConstants.Roles.User, Description = "Default" });
        dbContext.Roles.Add(new Role { Id = Id<Role>.New(), Name = "Coach", Description = "Custom" });
        await dbContext.SaveChangesAsync();

        IRoleRepository roleRepository = CreateRoleRepository(dbContext);
        IUserRepository userRepository = CreateUserRepository(dbContext);
        IUnitOfWork unitOfWork = new EfUnitOfWork(dbContext);

        var service = new RoleService(roleRepository, userRepository, unitOfWork);

        await service.UpdateUserRolesAsync(user.Id, [AuthConstants.Roles.User, "Coach"]);

        var assignedRoleNames = await dbContext.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Select(ur => ur.Role.Name)
            .OrderBy(name => name)
            .ToListAsync();

        assignedRoleNames.Should().BeEquivalentTo(new[] { "Coach", AuthConstants.Roles.User });
    }

    private static UserRepository CreateUserRepository(AppDbContext db) =>
        new(db, null!, new MapperRegistry());

    private static RoleRepository CreateRoleRepository(AppDbContext db) =>
        new(db, null!, new MapperRegistry());

    private sealed class NoOpWelcomeEmailScheduler : IEmailScheduler<WelcomeEmailPayload>
    {
        public Task ScheduleAsync(WelcomeEmailPayload payload, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpCommandDispatcher : ICommandDispatcher
    {
        public Task EnqueueAsync<TCommand>(TCommand command) where TCommand : class, IActionCommand
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpTutorialService : ITutorialService
    {
        public Task<Result<Unit, AppError>> InitializeOnboardingTutorialAsync(Id<User> userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, AppError>.Success(Unit.Value));
        }

        public Task<bool> HasActiveTutorialsAsync(Id<User> userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<Result<List<TutorialProgressResult>, AppError>> GetActiveTutorialsAsync(Id<User> userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<List<TutorialProgressResult>, AppError>.Success(new List<TutorialProgressResult>()));
        }

        public Task<Result<TutorialProgressResult?, AppError>> GetTutorialProgressAsync(Id<User> userId, TutorialType tutorialType, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<TutorialProgressResult?, AppError>.Success(null));
        }

        public Task<Result<Unit, AppError>> CompleteStepAsync(Id<User> userId, TutorialType tutorialType, TutorialStep step, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, AppError>.Success(Unit.Value));
        }

        public Task<Result<Unit, AppError>> CompleteTutorialAsync(Id<User> userId, TutorialType tutorialType, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<Unit, AppError>.Success(Unit.Value));
        }
    }
}
