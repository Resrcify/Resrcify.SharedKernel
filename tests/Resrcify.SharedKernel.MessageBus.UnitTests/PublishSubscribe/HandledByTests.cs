using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.MessageBus.PublishSubscribe;
using Resrcify.SharedKernel.MessageBus.UnitTests.Events;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.PublishSubscribe;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class HandledByTests
{
    [Fact]
    public void KeysOf_ShouldNameEachHandlerByItsType_WithoutTheAssemblyVersion()
        => HandledBy.KeysOf([typeof(ScriptedEventFirstHandler), typeof(NotificationForwarder<ScriptedEvent>)])
            .ShouldBe([
                "Resrcify.SharedKernel.MessageBus.UnitTests.Events.ScriptedEventFirstHandler",
                "Resrcify.SharedKernel.MessageBus.PublishSubscribe.NotificationForwarder<Resrcify.SharedKernel.MessageBus.UnitTests.Events.ScriptedEvent>",
            ]);

    [Fact]
    public void KeysOf_ShouldTellTwoHandlersOfOneTypeApart_ByTheirOrder()
        => HandledBy.KeysOf([typeof(CommandForwarder<ScriptedEvent>), typeof(CommandForwarder<ScriptedEvent>)])
            .ShouldBe([
                "Resrcify.SharedKernel.MessageBus.PublishSubscribe.CommandForwarder<Resrcify.SharedKernel.MessageBus.UnitTests.Events.ScriptedEvent>",
                "Resrcify.SharedKernel.MessageBus.PublishSubscribe.CommandForwarder<Resrcify.SharedKernel.MessageBus.UnitTests.Events.ScriptedEvent>#2",
            ]);
}
