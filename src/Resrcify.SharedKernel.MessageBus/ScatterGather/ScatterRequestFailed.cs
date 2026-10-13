using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Results.Serialization;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>The reply a responder sends when its handler returned a failure: the errors, as they were.</summary>
public sealed record ScatterRequestFailed(IReadOnlyList<ScatterRequestFailed.ErrorData> Errors)
{
    public const string WireName = "resrcify.scatter-request-failed.v2";

    public static ScatterRequestFailed From(IEnumerable<Error> errors)
        => new([.. errors.Select(error => new ErrorData(error.Code, error.Message, error.Type))]);

    public Error[] ToErrors()
        => [.. Errors.Select(error => new Error(error.Code, error.Message, error.Type))];

    /// <summary>
    /// One <see cref="Error"/> on the wire. Its type is written as its name and read as a name or a number, whatever
    /// enum settings the responder's and the requester's serialization have: the bus' own reply mustn't become
    /// unreadable because one service writes enums as numbers and the other refuses them, nor because a newer service
    /// answers with a type this one doesn't know (read as a <see cref="ErrorType.Failure"/>).
    /// </summary>
    public sealed record ErrorData(
        string Code,
        string Message,
        [property: JsonConverter(typeof(ErrorTypeJsonConverter))] ErrorType Type);
}
