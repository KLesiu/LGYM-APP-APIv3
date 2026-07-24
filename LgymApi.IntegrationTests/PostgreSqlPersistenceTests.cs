using FluentAssertions;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Contracts;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
[Category("PostgreSql")]
public sealed class PostgreSqlPersistenceTests : PostgreSqlIntegrationTestBase
{
    private const string ActiveIdempotencyIndexName = "IX_ApiIdempotencyRecords_ScopeTuple_IdempotencyKey";
    private const string UserRoleUserForeignKeyName = "FK_UserRoles_Users_UserId";
    private const string UserRoleRoleForeignKeyName = "FK_UserRoles_Roles_RoleId";

    [Test]
    public async Task MigratedDatabase_HasNoPendingMigrationsOrModelChanges_AndContainsAllRuntimeEntities()
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        database.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
        (await database.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        database.Database.HasPendingModelChanges().Should().BeFalse();
        database.Model.GetEntityTypes().Should().HaveCount(48);
    }

    [Test]
    public async Task ApiIdempotencyRecord_UsesTypedIdForEfQueries_AndStoresTheIdAsUuid()
    {
        var recordId = Id<ApiIdempotencyRecord>.New();

        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.ApiIdempotencyRecords.Add(CreateIdempotencyRecord(recordId, "uuid-scope", "uuid-key"));
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var storedRecord = await database.ApiIdempotencyRecords.SingleAsync(record => record.Id == recordId);
        storedRecord.Id.Should().Be(recordId);

        await database.Database.OpenConnectionAsync();
        try
        {
            using var command = database.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT pg_typeof("Id")::text
                FROM "ApiIdempotencyRecords"
                WHERE "Id" = @id
                """;

            var idParameter = command.CreateParameter();
            idParameter.ParameterName = "id";
            idParameter.Value = recordId.Value;
            command.Parameters.Add(idParameter);

            var providerType = await command.ExecuteScalarAsync();
            providerType.Should().Be("uuid");
        }
        finally
        {
            await database.Database.CloseConnectionAsync();
        }
    }

    [Test]
    public async Task ApiIdempotencyRecord_FilteredUniqueIndex_RejectsDuplicateActiveRowsAndAllowsReplacementAfterSoftDelete()
    {
        const string scopeTuple = "POST|/api/persistence-proof|user";
        const string idempotencyKey = "persistence-proof-key";
        var originalRecordId = Id<ApiIdempotencyRecord>.New();

        using (var createScope = Factory.Services.CreateScope())
        {
            var database = createScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.ApiIdempotencyRecords.Add(CreateIdempotencyRecord(originalRecordId, scopeTuple, idempotencyKey));
            await database.SaveChangesAsync();
        }

        using (var duplicateScope = Factory.Services.CreateScope())
        {
            var database = duplicateScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.ApiIdempotencyRecords.Add(CreateIdempotencyRecord(Id<ApiIdempotencyRecord>.New(), scopeTuple, idempotencyKey));

            await AssertConstraintViolationAsync(database, ActiveIdempotencyIndexName, "23505");
        }

        using (var deleteScope = Factory.Services.CreateScope())
        {
            var database = deleteScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await database.ApiIdempotencyRecords.SingleAsync(item => item.Id == originalRecordId);
            record.IsDeleted = true;
            await database.SaveChangesAsync();
            database.ChangeTracker.Clear();

            (await database.ApiIdempotencyRecords.SingleOrDefaultAsync(item => item.Id == originalRecordId)).Should().BeNull();
            var softDeletedRecord = await database.ApiIdempotencyRecords
                .IgnoreQueryFilters()
                .SingleAsync(item => item.Id == originalRecordId);
            softDeletedRecord.IsDeleted.Should().BeTrue();
        }

        using (var replacementScope = Factory.Services.CreateScope())
        {
            var database = replacementScope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.ApiIdempotencyRecords.Add(CreateIdempotencyRecord(Id<ApiIdempotencyRecord>.New(), scopeTuple, idempotencyKey));
            await database.SaveChangesAsync();
            database.ChangeTracker.Clear();

            (await database.ApiIdempotencyRecords
                .Where(item => item.ScopeTuple == scopeTuple && item.IdempotencyKey == idempotencyKey)
                .ToListAsync())
                .Should().ContainSingle();
            (await database.ApiIdempotencyRecords
                .IgnoreQueryFilters()
                .Where(item => item.ScopeTuple == scopeTuple && item.IdempotencyKey == idempotencyKey)
                .ToListAsync())
                .Should().HaveCount(2);
        }
    }

    [Test]
    public async Task SupplementIntakeCheckOff_ConcurrentRequestsRecoverTheWinnerAndLeaveOneActiveRow()
    {
        var trainee = await SeedUserAsync(
            name: $"intake-trainee-{Id<User>.New()}",
            email: $"intake-trainee-{Id<User>.New()}@test.local");
        var intakeDate = new DateOnly(2026, 7, 27);
        Id<SupplementPlanItem> planItemId;

        using (var seedScope = Factory.Services.CreateScope())
        {
            var database = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var plan = new SupplementPlan
            {
                Id = Id<SupplementPlan>.New(),
                TrainerId = trainee.Id,
                TraineeId = trainee.Id,
                Name = "Concurrent intake plan",
                IsActive = true
            };
            var item = new SupplementPlanItem
            {
                Id = Id<SupplementPlanItem>.New(),
                PlanId = plan.Id,
                SupplementName = "Magnesium",
                Dosage = "1 tablet",
                Order = 1,
                DaysOfWeekMask = DaysOfWeekSet.Monday,
                TimeOfDay = new TimeSpan(8, 0, 0)
            };
            plan.Items.Add(item);
            database.SupplementPlans.Add(plan);
            await database.SaveChangesAsync();
            planItemId = item.Id;
        }

        using var firstScope = Factory.Services.CreateScope();
        using var secondScope = Factory.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ICheckOffSupplementIntakeUseCase>();
        var second = secondScope.ServiceProvider.GetRequiredService<ICheckOffSupplementIntakeUseCase>();
        var command = new CheckOffSupplementIntakeCommand(
            trainee.Id,
            planItemId,
            intakeDate,
            new DateTimeOffset(2026, 7, 27, 8, 30, 0, TimeSpan.Zero));
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(
            Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await first.ExecuteAsync(command);
            }),
            Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await second.ExecuteAsync(command);
            }));

        results.Should().OnlyContain(result => result.IsSuccess);
        results.Select(result => result.Value.Taken).Should().OnlyContain(taken => taken);
        results.Select(result => result.Value.PlanItemId).Should().OnlyContain(id => id == planItemId);
        results.Select(result => result.Value.TakenAt).Distinct().Should().ContainSingle();

        using var readScope = Factory.Services.CreateScope();
        var readDatabase = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var intakeLogs = await readDatabase.SupplementIntakeLogs
            .AsNoTracking()
            .Where(log => log.TraineeId == trainee.Id && log.PlanItemId == planItemId && log.IntakeDate == intakeDate)
            .ToListAsync();
        intakeLogs.Should().ContainSingle();
        intakeLogs[0].TakenAt.Should().Be(results[0].Value.TakenAt);
    }

    [Test]
    public async Task UserRole_WithNonexistentTypedForeignKeys_IsRejectedByPostgreSqlConstraints()
    {
        var user = await SeedUserAsync(
            name: $"fk-user-{Id<User>.New()}",
            email: $"fk-user-{Id<User>.New()}@test.local");
        Id<Role> roleId;

        using (var roleScope = Factory.Services.CreateScope())
        {
            var database = roleScope.ServiceProvider.GetRequiredService<AppDbContext>();
            roleId = await database.Roles.OrderBy(role => role.Name).Select(role => role.Id).FirstAsync();
        }

        await AssertUserRoleForeignKeyViolationAsync(
            new UserRole { UserId = Id<User>.New(), RoleId = roleId },
            UserRoleUserForeignKeyName);
        await AssertUserRoleForeignKeyViolationAsync(
            new UserRole { UserId = user.Id, RoleId = Id<Role>.New() },
            UserRoleRoleForeignKeyName);
    }

    private static ApiIdempotencyRecord CreateIdempotencyRecord(
        Id<ApiIdempotencyRecord> id,
        string scopeTuple,
        string idempotencyKey)
    {
        return new ApiIdempotencyRecord
        {
            Id = id,
            ScopeTuple = scopeTuple,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = "persistence-proof-fingerprint",
            ResponseStatusCode = 200,
            ResponseBodyJson = "{}",
            ProcessedAt = DateTimeOffset.UtcNow
        };
    }

    private static async Task AssertConstraintViolationAsync(
        AppDbContext database,
        string expectedConstraintName,
        string expectedSqlState)
    {
        try
        {
            Func<Task> saveChanges = async () => await database.SaveChangesAsync();
            var exception = await saveChanges.Should().ThrowAsync<DbUpdateException>();
            var postgresException = exception.Which.InnerException.Should().BeOfType<PostgresException>().Which;

            postgresException.SqlState.Should().Be(expectedSqlState);
            postgresException.ConstraintName.Should().Be(expectedConstraintName);
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    private async Task AssertUserRoleForeignKeyViolationAsync(UserRole userRole, string expectedConstraintName)
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.UserRoles.Add(userRole);

        await AssertConstraintViolationAsync(database, expectedConstraintName, "23503");
    }
}
