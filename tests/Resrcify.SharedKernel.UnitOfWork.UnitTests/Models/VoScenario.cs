using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

/// <summary>
/// The rows and the queries the value-object conversion tests compare two contexts on (hand-written conversions against
/// the convention, a built model against a compiled one).
/// </summary>
internal static class VoScenario
{
    public static readonly PlayerId First = PlayerId.Create("P1").Value;
    public static readonly PlayerId Second = PlayerId.Create("P2").Value;
    public static readonly AllyCode FirstAllyCode = AllyCode.Create(111_111_111).Value;
    public static readonly AllyCode SecondAllyCode = AllyCode.Create(222_222_222).Value;
    public static readonly AllyCode FormerAllyCode = AllyCode.Create(333_333_333).Value;
    public static readonly TrackingId Tracking = TrackingId.Create(new Guid("6f1c2d6e-0f4a-4f4f-9a51-3f1f8d3b2a10")).Value;

    public static readonly Dictionary<string, Func<VoDbContext, IQueryable<VoPlayer>>> PlayerQueries = new()
    {
        ["key equals"] = context => context.Players.Where(player => player.Id == First),
        ["equals"] = context => context.Players.Where(player => player.AllyCode == SecondAllyCode),
        ["Equals"] = context => context.Players.Where(player => player.AllyCode.Equals(SecondAllyCode)),
        ["is null"] = context => context.Players.Where(player => player.SimulatedPlayerId == null),
        ["nullable equals"] = context => context.Players.Where(player => player.SimulatedPlayerId == First),
        ["not null, ordered"] = context => context.Players
            .Where(player => player.FormerAllyCode != null)
            .OrderBy(player => player.AllyCode),
        ["contains"] = context => context.Players.Where(player => new[] { First, Second }.Contains(player.Id)),
        ["owned"] = context => context.Players.Where(player => player.Address.ForwardTo == Second),
        ["complex"] = context => context.Players.Where(player => player.Stats.Referrer == SecondAllyCode),
        ["navigation"] = context => context.Players.Where(player => player.Memberships.Any(m => m.TrackingId == Tracking)),
        ["primitive collection"] = context => context.Players.Where(player => player.Friends.Contains(Second)),
        ["include"] = context => context.Players.Include(player => player.Memberships).OrderBy(player => player.Id),
    };

    public static async Task SeedAsync(VoDbContext context)
    {
        var first = new VoPlayer(First, FirstAllyCode)
        {
            Code = TrustedCode.Create("ABC").Value,
            Legacy = LegacyCode.Create("legacy").Value,
            Address = new VoAddress { Street = "First street", ForwardTo = Second },
            Stats = new VoStats { Level = 85, Referrer = SecondAllyCode },
            Friends = [Second],
        };
        first.Memberships.Add(new VoMembership(OrderNumber.Create(1).Value, First, Tracking));
        var second = new VoPlayer(Second, SecondAllyCode)
        {
            FormerAllyCode = FormerAllyCode,
            SimulatedPlayerId = First,
            Stats = new VoStats { Level = 12 },
        };
        context.Players.AddRange(first, second);
        context.Tickets.Add(new VoTicket(OrderNumber.Create(7).Value, Tracking));
        context.Badges.Add(new VoBadge(Tracking));
        await context.SaveChangesAsync();
    }

    /// <summary>A player as a line of every value it holds, to compare what two contexts read.</summary>
    public static List<string> Describe(IEnumerable<VoPlayer> players)
        => players
            .Select(player => string.Join(
                "|",
                player.Id.Value,
                player.AllyCode.Value.ToString(CultureInfo.InvariantCulture),
                player.FormerAllyCode?.Value.ToString(CultureInfo.InvariantCulture),
                player.SimulatedPlayerId?.Value,
                player.Code?.Value,
                player.Legacy?.Value,
                player.Address.Street,
                player.Address.ForwardTo?.Value,
                player.Stats.Level.ToString(CultureInfo.InvariantCulture),
                player.Stats.Referrer?.Value.ToString(CultureInfo.InvariantCulture),
                string.Join(",", player.Friends.Select(friend => friend.Value)),
                string.Join(",", player.Memberships.Select(m => $"{m.Number.Value}/{m.PlayerId.Value}/{m.TrackingId.Value}"))))
            .ToList();
}
