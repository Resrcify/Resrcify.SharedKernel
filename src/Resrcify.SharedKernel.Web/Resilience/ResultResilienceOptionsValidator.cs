using System.Linq;
using Microsoft.Extensions.Options;

namespace Resrcify.SharedKernel.Web.Resilience;

/// <summary>
/// Validates a client's <see cref="ResultResilienceOptions"/> (named after the client) when the host starts, naming the
/// client in each failure.
/// </summary>
internal sealed class ResultResilienceOptionsValidator
    : IValidateOptions<ResultResilienceOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        ResultResilienceOptions options)
    {
        var problems = options
            .Problems()
            .Select(problem => $"AddResultResilience on the HttpClient '{name}': {problem}")
            .ToList();
        return problems.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(problems);
    }
}
