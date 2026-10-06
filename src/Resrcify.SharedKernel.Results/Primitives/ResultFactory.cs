using System;
using System.Collections.Generic;
using System.Linq;

namespace Resrcify.SharedKernel.Results.Primitives;

/// <summary>
/// Makes a failed result of a type known only as a type parameter: <see cref="Result"/> or a <see cref="Result{TValue}"/>.
/// For code generic over the result type, such as a pipeline behavior that short-circuits with its
/// <c>TResponse</c>.
/// </summary>
/// <remarks>The way to make each result type is found once per type and kept.</remarks>
public static class ResultFactory
{
    /// <summary>A failed <typeparamref name="TResult"/> with <paramref name="errors"/>.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="errors"/> is empty or contains <see cref="Error.None"/> (as <see cref="Result.Failure(IReadOnlyList{Error})"/>).
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <typeparamref name="TResult"/> is neither <see cref="Result"/> nor a <see cref="Result{TValue}"/> (a class derived
    /// from them can't be made here).
    /// </exception>
    public static TResult Failure<TResult>(
        IReadOnlyList<Error> errors)
        where TResult : Result
        => FailureOf<TResult>.Create(errors);

    /// <summary>A failed <typeparamref name="TResult"/> with <paramref name="error"/>.</summary>
    /// <inheritdoc cref="Failure{TResult}(IReadOnlyList{Error})"/>
    public static TResult Failure<TResult>(
        Error error)
        where TResult : Result
        => FailureOf<TResult>.Create([error]);

    /// <summary>Holds the failure factory of one result type; the runtime builds it once, on first use.</summary>
    private static class FailureOf<TResult>
        where TResult : Result
    {
        public static readonly Func<IReadOnlyList<Error>, TResult> Create = Build();

        private static Func<IReadOnlyList<Error>, TResult> Build()
        {
            var resultType = typeof(TResult);
            if (resultType == typeof(Result))
                return errors => (TResult)Result.Failure(errors);

            if (!resultType.IsGenericType || resultType.GetGenericTypeDefinition() != typeof(Result<>))
                return _ => throw new NotSupportedException(
                    $"A failed {resultType.Name} can't be made: only Result and Result<TValue> can.");

            return typeof(Result)
                .GetMethods()
                .First(method =>
                    method is { IsGenericMethod: true, Name: nameof(Result.Failure) } &&
                    method.GetParameters()[0].ParameterType == typeof(IReadOnlyList<Error>))
                .MakeGenericMethod(resultType.GenericTypeArguments[0])
                .CreateDelegate<Func<IReadOnlyList<Error>, TResult>>();
        }
    }
}
