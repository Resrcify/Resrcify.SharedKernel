using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

internal sealed record ScatterReply<TResponse>(string Key, Result<TResponse> Result) : IScatterReply<TResponse>
    where TResponse : class;
