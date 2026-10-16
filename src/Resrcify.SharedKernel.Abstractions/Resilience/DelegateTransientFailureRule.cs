using System;

namespace Resrcify.SharedKernel.Abstractions.Resilience;

/// <summary>A rule given as a function (<c>AddTransientFailureRule(Func&lt;Exception, bool&gt;)</c>).</summary>
internal sealed class DelegateTransientFailureRule(
    Func<Exception, bool> isTransient)
    : ITransientFailureRule
{
    public bool IsTransient(Exception exception)
        => isTransient(exception);
}
