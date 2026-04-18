using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;

namespace Resrcify.SharedKernel.PerformanceTests.Abstractions;

[MemoryDiagnoser]
public class AbstractionsBenchmarks : IDisposable
{
    private readonly NoopUnitOfWork _unitOfWorkConcrete = new();
    private bool _disposed;

    [Benchmark(Baseline = true)]
    public Task Interface_CompleteAsync()
    {
        IUnitOfWork unitOfWork = _unitOfWorkConcrete;
        return unitOfWork.CompleteAsync(CancellationToken.None);
    }

    [Benchmark]
    public Task Concrete_CompleteAsync()
        => _unitOfWorkConcrete.CompleteAsync(CancellationToken.None);

    public static void SelfTest()
    {
        var instance = new AbstractionsBenchmarks();
        instance.Interface_CompleteAsync().GetAwaiter().GetResult();
        instance.Concrete_CompleteAsync().GetAwaiter().GetResult();
        instance.Dispose();
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
            _unitOfWorkConcrete.Dispose();

        _disposed = true;
    }

    private sealed class NoopUnitOfWork : IUnitOfWork
    {
        public Task CompleteAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task BeginTransactionAsync(
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            TimeSpan? commandLifetime = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
