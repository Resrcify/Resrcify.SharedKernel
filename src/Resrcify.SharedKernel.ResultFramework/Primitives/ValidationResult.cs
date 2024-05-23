using Resrcify.SharedKernel.ResultFramework.Abstractions;

namespace Resrcify.SharedKernel.ResultFramework.Shared;

public sealed class ValidationResult : Result, IValidationResult
{
    private ValidationResult(Error[] errors)
        : base(false, IValidationResult.ValidationError) =>
        Errors = errors;

    public new Error[] Errors { get; }

    public static ValidationResult WithErrors(Error[] errors) => new(errors);
}
