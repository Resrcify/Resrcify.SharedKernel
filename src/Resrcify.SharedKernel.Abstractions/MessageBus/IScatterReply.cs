using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Abstractions.MessageBus;

/// <summary>One item's reply: its response, or the errors its responder answered with.</summary>
public interface IScatterReply<TItemResponse>
    where TItemResponse : class
{
    string Key { get; }

    Result<TItemResponse> Result { get; }
}
