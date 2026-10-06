using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Results.Primitives;

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

        public Task<Result> TryCompleteAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());

        public Task BeginTransactionAsync(
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            TimeSpan? commandTimeout = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            TimeSpan? commandTimeout = null,
            CancellationToken cancellationToken = default)
            => operation(cancellationToken);

        public Task<TResponse> ExecuteInTransactionAsync<TResponse>(
            Func<CancellationToken, Task<TResponse>> operation,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            TimeSpan? commandTimeout = null,
            CancellationToken cancellationToken = default)
            where TResponse : Result
            => operation(cancellationToken);

        public void Dispose()
        {
        }
    }
}
