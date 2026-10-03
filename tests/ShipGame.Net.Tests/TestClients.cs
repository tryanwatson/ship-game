using ShipGame.Shared.Abilities;

namespace ShipGame.Net.Tests;

internal static class TestClients
{
    /// <summary>Chooses a starting weapon (required first) and readies up.</summary>
    public static void ReadyUp(this ClientConnection client, string weapon = BroadsideVolley.AbilityId)
    {
        client.ChooseStartingWeapon(weapon);
        client.SetReady();
    }
}
