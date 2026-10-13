using System;

namespace Resrcify.SharedKernel.MessageBus.PublishSubscribe;

/// <summary>
/// An event that waited in its partition behind one that went back to the queue goes back too, unhandled, so that it is
/// handled after that one rather than before it.
/// </summary>
public sealed class SentBackBehindException : Exception
{
    public SentBackBehindException()
        : base("An event before this one in its partition went back to the queue; this one goes back after it, unhandled, to keep their order.")
    {
    }

    public SentBackBehindException(string message)
        : base(message)
    {
    }

    public SentBackBehindException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
