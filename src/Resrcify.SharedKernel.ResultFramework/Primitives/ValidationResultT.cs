using Resrcify.SharedKernel.ResultFramework.Abstractions;

namespace Resrcify.SharedKernel.ResultFramework.Primitives;

public sealed class ValidationResult<TValue> : Result<TValue>, IValidationResult
{
    private ValidationResult(Error[] errors)
        : base(default, false, IValidationResult.ValidationError) =>
        Errors = errors;

    public new Error[] Errors { get; }

    public static ValidationResult<TValue> WithErrors(Error[] errors) => new(errors);
}
