using FluentValidation;
using MediatR;
using Resrcify.SharedKernel.ResultFramework.Shared;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Titan.GalaxyOfHeroesWrapper.Application.Behaviors;

public sealed class ValidationPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
        where TResponse : Result
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationPipelineBehavior(IEnumerable<IValidator<TRequest>> validators) =>
        _validators = validators;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var errorList = new List<Error>();
        foreach (var validator in _validators)
        {
            var validationResult = await validator.ValidateAsync(request, cancellationToken);
            foreach (var failure in validationResult.Errors)
            {
                errorList.Add(new Error(
                    failure.PropertyName,
                    failure.ErrorMessage,
                    ErrorType.Validation));
            }
        }

        Error[] errors = errorList.Distinct().ToArray();

        if (errors.Length != 0)
        {
            return CreateValidationResult<TResponse>(errors);
        }

        return await next();
    }

    private static TResult CreateValidationResult<TResult>(Error[] errors)
        where TResult : Result
    {
        if (typeof(TResult) == typeof(Result))
        {
            return (Result.Failure(errors) as TResult)!;
        }

        object result = typeof(Result)
            .GetMethods()
            .First(m =>
                m is { IsGenericMethod: true, Name: nameof(Result.Failure) } &&
                m.GetParameters().First().ParameterType == typeof(Error[]))!
            .MakeGenericMethod(typeof(TResult).GenericTypeArguments[0])
            .Invoke(null, [errors])!;

        return (TResult)result;
    }
}
