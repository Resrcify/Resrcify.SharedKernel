using Resrcify.SharedKernel.ResultFramework.Primitives;

namespace Resrcify.SharedKernel.ResultFramework.Abstractions;

public interface IValidationResult
{
    public static readonly Error ValidationError = new(
        "ValidationError",
        "A validation problem occurred.", ErrorType.Validation);

    Error[] Errors { get; }
}
