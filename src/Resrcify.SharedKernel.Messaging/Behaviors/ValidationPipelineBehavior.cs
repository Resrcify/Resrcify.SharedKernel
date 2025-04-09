using FluentValidation;
using MediatR;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ValidationResult = FluentValidation.Results.ValidationResult;

namespace Resrcify.SharedKernel.Messaging.Behaviors;

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
            return await next(cancellationToken);

        Error[] errors = await GetValidationErrorsAsync(request);

        if (errors.Length != 0)
            return CreateValidationResult<TResponse>(errors);

        return await next(cancellationToken);
    }

    public async Task<Error[]> GetValidationErrorsAsync(TRequest request)
    {
        var validationTasks = _validators
            .Select(validator => validator.ValidateAsync(request));

        var validationResults = await Task.WhenAll(validationTasks);

        Error[] errors = ExtractErrors(validationResults);

        return errors;
    }

    private static Error[] ExtractErrors(ValidationResult[] validationResults)
        => validationResults
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .Select(failure => new Error(
                failure.PropertyName,
                failure.ErrorMessage,
                ErrorType.Validation
            ))
            .Distinct()
            .ToArray();

    private static TResult CreateValidationResult<TResult>(Error[] errors)
        where TResult : Result
    {
        if (!typeof(TResult).IsGenericType)
            return (TResult)Result.Failure(errors);

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
