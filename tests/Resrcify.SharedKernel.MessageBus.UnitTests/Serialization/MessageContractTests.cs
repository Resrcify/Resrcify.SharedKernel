using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.Serialization;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Serialization;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MessageContractTests
{
    private static readonly Sentinel.SentinelArenaUnitsChanged Sample = new(
        "guild-1",
        [new Sentinel.UnitChange("vader", OldRank: 3, NewRank: 1), new Sentinel.UnitChange("rey", OldRank: 1, NewRank: 2)]);

    [Fact]
    public void Differences_ShouldBeEmpty_WhenTheCopiesMatchByPropertyThoughTheirNestedClassesDiffer()
        => MessageContract.Differences<Sentinel.SentinelArenaUnitsChanged, Discord.SentinelArenaUnitsChanged>(Sample).ShouldBeEmpty();

    [Fact]
    public void Differences_ShouldNameEveryDrift_WhenTheReceiversCopyHasDrifted()
    {
        var differences = MessageContract.Differences<Sentinel.SentinelArenaUnitsChanged, DriftedDiscord.SentinelArenaUnitsChanged>(Sample);

        differences.ShouldBe(
        [
            "$.changes[0].newRank: sent, but SentinelArenaUnitsChanged has no such property (dropped)",
            "$.changes[0].rank: SentinelArenaUnitsChanged has it, but it isn't sent (gets its default)",
            "$.changes[1].newRank: sent, but SentinelArenaUnitsChanged has no such property (dropped)",
            "$.changes[1].rank: SentinelArenaUnitsChanged has it, but it isn't sent (gets its default)",
        ]);
    }

    [Fact]
    public void Differences_ShouldWriteAsTheSendersStrategyDoes_WhenItHasOne()
        => MessageContract.Differences<Sentinel.SentinelArenaUnitsChanged, Discord.SentinelArenaUnitsChanged>(Sample, new LeaveOutGuildId())
            .ShouldBe(["$.guildId: SentinelArenaUnitsChanged has it, but it isn't sent (gets its default)"]);

    /// <summary>The publisher's copy.</summary>
    internal static class Sentinel
    {
        internal sealed record SentinelArenaUnitsChanged(string GuildId, IReadOnlyList<UnitChange> Changes);

        internal sealed record UnitChange(string UnitId, int OldRank, int NewRank);
    }

    /// <summary>A subscriber's copy: other nested class, same properties.</summary>
    internal static class Discord
    {
        internal sealed record SentinelArenaUnitsChanged(string GuildId, List<ArenaUnitChange> Changes);

        internal sealed record ArenaUnitChange(string UnitId, int OldRank, int NewRank);
    }

    /// <summary>A subscriber's copy that renamed a property.</summary>
    internal static class DriftedDiscord
    {
        internal sealed record SentinelArenaUnitsChanged(string GuildId, List<ArenaUnitChange> Changes);

        internal sealed record ArenaUnitChange(string UnitId, int OldRank, int Rank);
    }

    private sealed class LeaveOutGuildId : IMessageSerializationStrategy
    {
        public void Configure(JsonSerializerOptions options)
            => options.TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    info =>
                    {
                        for (var i = info.Properties.Count - 1; i >= 0; i--)
                            if (info.Properties[i].Name == "guildId")
                                info.Properties.RemoveAt(i);
                    },
                },
            };
    }
}
