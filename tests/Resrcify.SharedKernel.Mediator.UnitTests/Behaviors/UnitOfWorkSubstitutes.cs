using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Mediator.UnitTests.Behaviors;

/// <summary>Substitute units of work that run the operations they are given, as the real one does.</summary>
internal static class UnitOfWorkSubstitutes
{
    /// <summary>Makes <see cref="IUnitOfWork.ExecuteAsync{TResponse}"/> run its operation (it undoes nothing).</summary>
    public static IUnitOfWork RunningOperations<TResponse>(
        this IUnitOfWork unitOfWork)
        where TResponse : Result
    {
        unitOfWork
            .ExecuteAsync(
                Arg.Any<Func<CancellationToken, Task<TResponse>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<TResponse>>>()(call.Arg<CancellationToken>()));
        return unitOfWork;
    }
}
