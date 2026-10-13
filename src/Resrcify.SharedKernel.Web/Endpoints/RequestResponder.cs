using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Web.Endpoints;

/// <summary>
/// Sends the request an endpoint's delegate returned and answers with its result. Built once per endpoint, when it is
/// mapped, for the request's response type, so a request does no reflection.
/// </summary>
internal abstract class RequestResponder
{
    /// <summary>The value the request answers with (<c>T</c> of <c>Result&lt;T&gt;</c>); <see langword="null"/> for a plain <c>Result</c>.</summary>
    public abstract Type? ResponseType { get; }

    public abstract Task<IResult> RespondAsync(ISender sender, object request, CancellationToken cancellationToken);

    /// <summary>
    /// The responder for requests of <paramref name="requestType"/> (what the delegate returns), or why there can't be
    /// one: it must be an <c>IRequest&lt;Result&gt;</c> or an <c>IRequest&lt;Result&lt;T&gt;&gt;</c> for exactly one <c>T</c>.
    /// </summary>
    public static RequestResponder For(Type requestType, Delegate? onSuccess, Func<Result, IResult> onFailure)
    {
        var responses = (requestType.IsInterface ? new[] { requestType } : [])
            .Concat(requestType.GetInterfaces())
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRequest<>))
            .Select(type => type.GetGenericArguments()[0])
            .ToList();
        var values = responses
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>))
            .Select(type => type.GetGenericArguments()[0])
            .Distinct()
            .ToList();

        if (values.Count > 1)
            throw new ArgumentException(
                $"{requestType.Name} answers with more than one Result<T> ({string.Join(", ", values.Select(value => value.Name))}): an endpoint can't tell which.");
        if (values.Count == 1)
        {
            // Checked here: thrown from the responder's constructor, it would reach the caller wrapped by reflection.
            if (onSuccess is not null and not Func<IResult>
                && !typeof(Func<,>).MakeGenericType(values[0], typeof(IResult)).IsInstanceOfType(onSuccess))
                throw new ArgumentException(
                    $"onSuccess must take the request's {values[0].Name} (or nothing) and return an IResult.",
                    nameof(onSuccess));
            return (RequestResponder)Activator.CreateInstance(
                typeof(ValueResponder<>).MakeGenericType(values[0]),
                onSuccess,
                onFailure)!;
        }

        if (responses.Contains(typeof(Result)))
            return new NoValueResponder(onSuccess, onFailure);

        throw new ArgumentException(
            $"The endpoint's delegate returns {requestType.Name}, which isn't a request answered with a Result: return an " +
            "IRequest<Result> or IRequest<Result<T>> (ICommand, ICommand<T>, IQuery<T>, …), or a Task of one.");
    }

    /// <summary>Declares what <paramref name="onSuccess"/> answers for OpenAPI, when its type says (the <c>TypedResults</c> types do).</summary>
    public static void DescribeSuccess(Delegate onSuccess, EndpointBuilder endpoint)
    {
        var resultType = onSuccess.Method.ReturnType;
        if (!typeof(IEndpointMetadataProvider).IsAssignableFrom(resultType))
            return;
        typeof(RequestResponder)
            .GetMethod(nameof(PopulateMetadata), BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(resultType)
            .Invoke(null, [onSuccess.Method, endpoint]);
    }

    // Public only to be found without bypassing accessibility; the class is internal.
    public static void PopulateMetadata<TResult>(MethodInfo method, EndpointBuilder endpoint)
        where TResult : IEndpointMetadataProvider
        => TResult.PopulateMetadata(method, endpoint);

    private sealed class ValueResponder<TResponse>(Delegate? onSuccess, Func<Result, IResult> onFailure) : RequestResponder
    {
        private readonly Func<TResponse, IResult> _onSuccess = SuccessOf(onSuccess);

        public override Type? ResponseType
            => typeof(TResponse);

        public override async Task<IResult> RespondAsync(ISender sender, object request, CancellationToken cancellationToken)
        {
            var result = await sender.Send((IRequest<Result<TResponse>>)request, cancellationToken);
            return result.IsSuccess ? _onSuccess(result.Value) : onFailure(result);
        }

        // 200 with the value unless told otherwise: an answer from the value, or one without it.
        private static Func<TResponse, IResult> SuccessOf(Delegate? onSuccess)
            => onSuccess switch
            {
                null => value => TypedResults.Ok(value),
                Func<TResponse, IResult> fromValue => fromValue,
                Func<IResult> answer => _ => answer(),
                _ => throw new ArgumentException(
                    $"onSuccess must take the request's {typeof(TResponse).Name} (or nothing) and return an IResult."),
            };
    }

    private sealed class NoValueResponder(Delegate? onSuccess, Func<Result, IResult> onFailure) : RequestResponder
    {
        private readonly Func<IResult> _onSuccess = onSuccess switch
        {
            null => TypedResults.NoContent,
            Func<IResult> answer => answer,
            _ => throw new ArgumentException("onSuccess of a request without a value takes nothing and returns an IResult."),
        };

        public override Type? ResponseType
            => null;

        public override async Task<IResult> RespondAsync(ISender sender, object request, CancellationToken cancellationToken)
        {
            var result = await sender.Send((IRequest<Result>)request, cancellationToken);
            return result.IsSuccess ? _onSuccess() : onFailure(result);
        }
    }
}
