using System;
using System.Collections.Generic;
using System.Linq;
using Resrcify.SharedKernel.Results.Diagnostics;

namespace Resrcify.SharedKernel.Abstractions.Resilience;

/// <summary>
/// The default <see cref="ITransientFailureClassifier"/>: the built-in rule (<see cref="TransientFailures"/>) OR every
/// registered <see cref="ITransientFailureRule"/>, applied to the exception and each of its causes.
/// </summary>
internal sealed class TransientFailureClassifier(
    IEnumerable<ITransientFailureRule> rules)
    : ITransientFailureClassifier
{
    private readonly ITransientFailureRule[] _rules = [.. rules];

    public bool IsTransient(Exception exception)
        => _rules.Length == 0
            ? TransientFailures.IsTransient(exception)
            : TransientFailures.IsTransient(exception, IsTransientByARule);

    private bool IsTransientByARule(Exception exception)
        => _rules.Any(rule => rule.IsTransient(exception));
}
