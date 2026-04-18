using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Primitives;

namespace Resrcify.SharedKernel.PerformanceTests.UnitOfWork;

[MemoryDiagnoser]
public class UnitOfWorkBenchmarks
{
    private DbContextOptions<TestDbContext> _options = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: $"perf-uow-{Guid.NewGuid()}")
            .Options;
    }

    [Benchmark(Baseline = true)]
    public async Task<int> DbContext_SaveChangesAsync()
    {
        await using var context = CreateContextWithEntity();
        return await context.SaveChangesAsync();
    }

    [Benchmark]
    public async Task UnitOfWork_CompleteAsync()
    {
        await using var context = CreateContextWithEntity();
        using var unitOfWork = new UnitOfWork<TestDbContext>(context);
        await unitOfWork.CompleteAsync();
    }

    public static void SelfTest()
    {
        var instance = new UnitOfWorkBenchmarks();
        instance.GlobalSetup();
        _ = instance.DbContext_SaveChangesAsync().GetAwaiter().GetResult();
        instance.UnitOfWork_CompleteAsync().GetAwaiter().GetResult();
    }

    private TestDbContext CreateContextWithEntity()
    {
        var context = new TestDbContext(_options);
        context.Entries.Add(new TestEntry());
        return context;
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestEntry> Entries { get; set; } = default!;
    }

    private sealed class TestEntry
    {
        public int Id { get; set; }

        public string Name { get; set; } = "entry";
    }
}
