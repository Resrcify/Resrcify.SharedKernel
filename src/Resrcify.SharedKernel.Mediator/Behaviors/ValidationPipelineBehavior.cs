using FluentValidation;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.Mediator.Behaviors;

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
        Error[] errors = await GetValidationErrorsAsync(request, cancellationToken);

        if (errors.Length != 0)
            return ResultFactory.Failure<TResponse>(errors);

        return await next(cancellationToken);
    }

    // One validator at a time: validators often share the request's scoped services (a DbContext
    // is not safe to use from two validators at once).
    private async Task<Error[]> GetValidationErrorsAsync(
        TRequest request,
        CancellationToken cancellationToken)
    {
        List<Error>? errors = null;

        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            foreach (var failure in result.Errors)
            {
                if (failure is null)
                    continue;

                errors ??= [];
                errors.Add(new Error(
                    failure.PropertyName,
                    failure.ErrorMessage,
                    ErrorType.Validation));
            }
        }

        return errors is null ? [] : [.. errors.Distinct()];
    }
}
