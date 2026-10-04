using System.Runtime.CompilerServices;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;

namespace ShipGame.Net.Tests;

internal static class TestClients
{
    // Clients that have made their starting choices this run, so each is sent once.
    private static readonly ConditionalWeakTable<ClientConnection, object> Sailed = new();

    /// <summary>Gives a name (required first) and readies up.</summary>
    public static void ReadyUp(this ClientConnection client, string name = "SAILOR")
    {
        client.SetName(name);
        client.SetReady();
    }

    /// <summary>
    /// For pumping until: takes the first starting card on offer and then <paramref name="weapon"/>, once they're on
    /// offer; true once the run is under way (everyone's chosen).
    /// </summary>
    public static bool SetSail(this ClientConnection client, string weapon = BroadsideVolley.AbilityId)
    {
        var world = client.Replica.World;
        if (!world.Players.TryGetValue(client.LocalPlayerId, out var me))
            return false;
        if (me.NeedsStartingWeapon && me.CardOffers.Count > 0 && !Sailed.TryGetValue(client, out _))
        {
            Sailed.Add(client, new object());
            client.Send(new ChooseCardCommand(0, me.CardOffers[0].Cards[0].Id));
            client.Send(new ChooseStartingWeaponCommand(0, weapon));
        }
        return !me.NeedsStartingWeapon && !world.IsPaused;
    }
}
