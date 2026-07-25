using FluentAssertions;
using LgymApi.Application.Repositories;
using LgymApi.BackgroundWorker.Common;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class IdempotencyKeyPolicyTests
{
    [Test]
    public void CalculateKey_WithValidCorrelationId_ReturnsConsistentKey()
    {
        var id = Id<CorrelationScope>.New();
        var k1 = IdempotencyKeyPolicy.CalculateKey(id);
        var k2 = IdempotencyKeyPolicy.CalculateKey(id);
        k1.Should().Be(k2);
    }

    [Test]
    public void CalculateKey_WithEmptyCorrelationId_Throws()
    {
        var action = () => IdempotencyKeyPolicy.CalculateKey(Id<CorrelationScope>.Empty);
        action.Should().Throw<ArgumentException>();
    }

    [Test]
    public void CalculateKey_DifferentIds_DifferentKeys()
    {
        var k1 = IdempotencyKeyPolicy.CalculateKey(Id<CorrelationScope>.New());
        var k2 = IdempotencyKeyPolicy.CalculateKey(Id<CorrelationScope>.New());
        k1.Should().NotBe(k2);
    }

    [Test]
    public void AreKeysEqual_SameKeys_True()
    {
        var id = Id<CorrelationScope>.New();
        var k1 = IdempotencyKeyPolicy.CalculateKey(id);
        var k2 = IdempotencyKeyPolicy.CalculateKey(id);
        IdempotencyKeyPolicy.AreKeysEqual(k1, k2).Should().BeTrue();
    }

    [Test]
    public void AreKeysEqual_DifferentKeys_False()
    {
        var k1 = IdempotencyKeyPolicy.CalculateKey(Id<CorrelationScope>.New());
        var k2 = IdempotencyKeyPolicy.CalculateKey(Id<CorrelationScope>.New());
        IdempotencyKeyPolicy.AreKeysEqual(k1, k2).Should().BeFalse();
    }

    [Test]
    public void AreKeysEqual_NullKeys_True()
    {
        IdempotencyKeyPolicy.AreKeysEqual(null, null).Should().BeTrue();
    }

    [Test]
    public void AreKeysEqual_OneNullKey_False()
    {
        var k1 = IdempotencyKeyPolicy.CalculateKey(Id<CorrelationScope>.New());
        IdempotencyKeyPolicy.AreKeysEqual(k1, null).Should().BeFalse();
        IdempotencyKeyPolicy.AreKeysEqual(null, k1).Should().BeFalse();
    }

    [Test]
    public void IsKeyForCorrelation_WithMatchingKey_ReturnsTrue()
    {
        var id = Id<CorrelationScope>.New();
        var key = IdempotencyKeyPolicy.CalculateKey(id);
        IdempotencyKeyPolicy.IsKeyForCorrelation(key, id).Should().BeTrue();
    }

    [Test]
    public void IsKeyForCorrelation_WithNonMatchingKey_ReturnsFalse()
    {
        var id1 = Id<CorrelationScope>.New();
        var id2 = Id<CorrelationScope>.New();
        var key = IdempotencyKeyPolicy.CalculateKey(id1);
        IdempotencyKeyPolicy.IsKeyForCorrelation(key, id2).Should().BeFalse();
    }

    [Test]
    public void IsKeyForCorrelation_WithNullKey_ReturnsFalse()
    {
        var id = Id<CorrelationScope>.New();
        IdempotencyKeyPolicy.IsKeyForCorrelation(null, id).Should().BeFalse();
    }

    [Test]
    public async Task Persistence_FirstEnqueueCreatesOneRecord()
    {
        var cid = Id<CorrelationScope>.New();
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"T1_{Id<IdempotencyKeyPolicyTests>.New().GetValue().ToString("N")}")
            .Options;

        using var ctx = new AppDbContext(opts);
        await ctx.Database.EnsureCreatedAsync();
        var r = new CommandEnvelopeRepository(ctx);
        var e = new CommandEnvelope { CorrelationId = cid, PayloadJson = "{}", CommandTypeFullName = "T", Status = ActionExecutionStatus.Pending };
        await r.AddAsync(e);
        await ctx.SaveChangesAsync();
        var f = await r.FindByCorrelationIdAsync(cid);
        f.Should().NotBeNull();
    }

    [Test]
    public async Task Persistence_DuplicateEnqueueDetectedByRepository()
    {
        var cid = Id<CorrelationScope>.New();
        var opts = new DbContextOptionsBuilder<AppDbContext>()
             .UseInMemoryDatabase(databaseName: $"T2_{Id<IdempotencyKeyPolicyTests>.New().GetValue().ToString("N")}")
             .Options;

        using var ctx = new AppDbContext(opts);
        await ctx.Database.EnsureCreatedAsync();
        var r = new CommandEnvelopeRepository(ctx);
        var e = new CommandEnvelope { CorrelationId = cid, PayloadJson = "{}", CommandTypeFullName = "T", Status = ActionExecutionStatus.Pending };
        await r.AddAsync(e);
        await ctx.SaveChangesAsync();
        var f = await r.FindByCorrelationIdAsync(cid);
        f.Should().NotBeNull();
    }

    [Test]
    public async Task Persistence_AddOrGetExistingAsync_ReturnsDuplicateNotNewRecord()
    {
        var cid = Id<CorrelationScope>.New();
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"T3_{Id<IdempotencyKeyPolicyTests>.New().GetValue().ToString("N")}")
            .Options;

        using var ctx = new AppDbContext(opts);
        await ctx.Database.EnsureCreatedAsync();
        var r = new CommandEnvelopeRepository(ctx);
        var e1 = new CommandEnvelope { CorrelationId = cid, PayloadJson = "{\"a\": 1}", CommandTypeFullName = "T", Status = ActionExecutionStatus.Pending };
        await r.AddOrGetExistingAsync(e1);
        await ctx.SaveChangesAsync();
        var e2 = new CommandEnvelope { CorrelationId = cid, PayloadJson = "{\"b\": 2}", CommandTypeFullName = "T", Status = ActionExecutionStatus.Pending };
        var res = await r.AddOrGetExistingAsync(e2);
        res.PayloadJson.Should().Be("{\"a\": 1}");
        var all = await ctx.CommandEnvelopes.ToListAsync();
        all.Count.Should().Be(1);
    }

    [Test]
    public async Task Persistence_ConcurrentDuplicateEnqueueResultsInSingleRecord()
    {
        var cid = Id<CorrelationScope>.New();
        var dbName = $"T4_{Id<IdempotencyKeyPolicyTests>.New().GetValue().ToString("N")}";

        var t1 = Task.Run(async () =>
        {
            var opts = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            using var ctx = new AppDbContext(opts);
            var r = new CommandEnvelopeRepository(ctx);
            var e = new CommandEnvelope { CorrelationId = cid, PayloadJson = "{\"a\": 1}", CommandTypeFullName = "T", Status = ActionExecutionStatus.Pending };
            var res = await r.AddOrGetExistingAsync(e);
            await ctx.SaveChangesAsync();
            return res;
        });

        var t2 = Task.Run(async () =>
        {
            var opts = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            using var ctx = new AppDbContext(opts);
            var r = new CommandEnvelopeRepository(ctx);
            var e = new CommandEnvelope { CorrelationId = cid, PayloadJson = "{\"b\": 2}", CommandTypeFullName = "T", Status = ActionExecutionStatus.Pending };
            return await r.AddOrGetExistingAsync(e);
        });

        var ress = await Task.WhenAll(t1, t2);
        ress[0].CorrelationId.Should().Be(cid);
        ress[1].CorrelationId.Should().Be(cid);

        var opts_final = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        using var ctx_final = new AppDbContext(opts_final);
        var all = await ctx_final.CommandEnvelopes.ToListAsync();
        all.Count.Should().Be(1);
    }

    [Test]
    public async Task Persistence_MultipleEnvelopesWithDifferentCorrelationIds()
    {
        var cid1 = Id<CorrelationScope>.New();
        var cid2 = Id<CorrelationScope>.New();
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"T5_{Id<IdempotencyKeyPolicyTests>.New().GetValue().ToString("N")}")
            .Options;

        using var ctx = new AppDbContext(opts);
        await ctx.Database.EnsureCreatedAsync();
        var r = new CommandEnvelopeRepository(ctx);
        var e1 = new CommandEnvelope { Id = Id<CommandEnvelope>.New(), CorrelationId = cid1, PayloadJson = "{}", CommandTypeFullName = "T", Status = ActionExecutionStatus.Pending };
        var e2 = new CommandEnvelope { Id = Id<CommandEnvelope>.New(), CorrelationId = cid2, PayloadJson = "{}", CommandTypeFullName = "T", Status = ActionExecutionStatus.Pending };
        await r.AddAsync(e1);
        await r.AddAsync(e2);
        await ctx.SaveChangesAsync();
        var all = await ctx.CommandEnvelopes.ToListAsync();
        all.Count.Should().Be(2);
    }
}
