using System.Collections.Generic;
using System.Linq;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>The reply a responder sends when its handler returned a failure: the errors, as they were.</summary>
public sealed record ScatterRequestFailed(IReadOnlyList<ScatterRequestFailed.ErrorData> Errors)
{
    public const string WireName = "resrcify.scatter-request-failed.v2";

    public static ScatterRequestFailed From(IEnumerable<Error> errors)
        => new([.. errors.Select(error => new ErrorData(error.Code, error.Message, error.Type))]);

    public Error[] ToErrors()
        => [.. Errors.Select(error => new Error(error.Code, error.Message, error.Type))];

    /// <summary>One <see cref="Error"/> on the wire.</summary>
    public sealed record ErrorData(string Code, string Message, ErrorType Type);
}
