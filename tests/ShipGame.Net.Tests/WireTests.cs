using System.Numerics;
using LiteNetLib.Utils;
using ShipGame.Net;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;

namespace ShipGame.Net.Tests;

public class WireTests
{
    private static NetDataReader ReaderFor(NetDataWriter writer) => new(writer.CopyData());

    public static IEnumerable<object[]> Commands() => new[]
    {
        new object[] { new MoveCommand(9, new Vector2(12.5f, -3f)) },
        new object[] { new StopCommand(9) },
        new object[] { new AdjustThrottleCommand(9, -2) },
        new object[] { new SetRudderCommand(9, 1) },
        new object[] { new CastAbilityCommand(9, AbilitySlot.Three, new Vector2(1, 2)) },
        new object[] { new AnchorKeyCommand(9, true) },
        new object[] { new AnchorKeyCommand(9, false) },
        new object[] { new ChoosePlunderCommand(9) },
        new object[] { new PurchaseUpgradeCommand(9, "shot-speed") },
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public void Commands_RoundTrip(Command command)
    {
        var writer = new NetDataWriter();
        writer.PutCommand(command);

        Assert.Equal(command, ReaderFor(writer).GetCommand(9));
    }

    [Fact]
    public void Commands_AlwaysComeBackAsTheSendersPlayer()
    {
        var writer = new NetDataWriter();
        writer.PutCommand(new MoveCommand(PlayerId: 1, new Vector2(5, 5))); // claims to be player 1

        var read = ReaderFor(writer).GetCommand(playerId: 4); // ...but arrived on player 4's connection

        Assert.Equal(4, read.PlayerId);
    }

    public static IEnumerable<object[]> Events() => new[]
    {
        new object[] { new ShipSpawned(10, 3) },
        new object[] { new ShipSunk(10, 3, 7) },
        new object[] { new ShipSunk(10, 3, null) },
        new object[] { new ProjectileSpawned(10, 40, 3, Team.Pirates, new Vector2(1, 2), new Vector2(-14, 0.5f), 12.5f, 18) },
        new object[] { new ProjectileSpawned(10, 41, 3, Team.Players, new Vector2(1, 2), new Vector2(20, -5), 22f, 19, 0.3f) },
        new object[] { new AreaStrikeLaunched(10, 50, 3, Team.Players, new Vector2(60, 60), new Vector2(75, 61), 2.5f, 35f, 42) },
        new object[] { new AreaStrikeImpact(42, 50, new Vector2(75, 61), 2.5f) },
        new object[] { new AreaDiscovered(10, Team.Players, new[] { 0, 47, 1200, 2303 }) },
        new object[] { new ProjectileImpact(10, 40, 5) },
        new object[] { new ProjectileImpact(10, 40, null) },
        new object[] { new AbilityCast(10, 3, AbilitySlot.One, 71, 1) },
        new object[] { new AbilityCast(10, 3, AbilitySlot.Three, 360) },
        new object[] { new ShipGrounded(10, 3) },
        new object[] { new GoldChanged(10, 2, 35, -15) },
        new object[] { new IslandPlundered(10, 4, 2, 10, 1800) },
        new object[] { new UpgradePurchased(10, 3, "agility", 2) },
        new object[] { new WaveStarted(10, 3, 5) },
        new object[] { new CommandRejected(10, 2, new PurchaseUpgradeCommand(2, "speed"), RejectionReason.NotEnoughGold) },
        new object[] { new PlayerSunk(10, 2, 300) },
        new object[] { new PlayerRespawned(10, 2, 55) },
        new object[] { new RunEnded(10) },
    };

    [Theory]
    [MemberData(nameof(Events))]
    public void Events_RoundTrip(WorldEvent worldEvent)
    {
        var writer = new NetDataWriter();
        writer.PutEvent(worldEvent);

        Assert.Equal(worldEvent, ReaderFor(writer).GetEvent());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LobbyAndRunStart_CarryTheFriendlyFireSetting(bool friendlyFire)
    {
        var writer = new NetDataWriter();
        writer.PutLobby(new LobbyState(false, new[] { new LobbyPlayer(1, true), new LobbyPlayer(2, false) }, friendlyFire));
        writer.PutRunStart(new RunStart(42, new Vector2(192, 192), new Vector2(0, 0.75f), friendlyFire));

        var reader = ReaderFor(writer);
        var lobby = reader.GetLobby();
        var start = reader.GetRunStart();

        Assert.Equal(friendlyFire, lobby.FriendlyFire);
        Assert.Equal(new[] { new LobbyPlayer(1, true), new LobbyPlayer(2, false) }, lobby.Players);
        Assert.Equal(new RunStart(42, new Vector2(192, 192), new Vector2(0, 0.75f), friendlyFire), start);
    }

    [Fact]
    public void ShipInfo_RoundTrips()
    {
        var info = new ShipInfo(
            123, 7, 2, Team.Players, ShipStats.Sloop with { WeaponRange = 1.3f },
            new[] { "broadside", null, "long-gun", "mortar" },
            new[] { new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 20, "upgrade:hull") },
            new Vector2(96, 90), 1.25f);
        var writer = new NetDataWriter();
        writer.PutShipInfo(info);

        var read = ReaderFor(writer).GetShipInfo();

        Assert.Equal(info.Tick, read.Tick);
        Assert.Equal(info.ShipId, read.ShipId);
        Assert.Equal(info.OwnerPlayerId, read.OwnerPlayerId);
        Assert.Equal(info.BaseStats, read.BaseStats);
        Assert.Equal(info.AbilityIds, read.AbilityIds);
        Assert.Equal(info.Modifiers, read.Modifiers);
        Assert.Equal((info.Position, info.Heading), (read.Position, read.Heading));
    }

    [Fact]
    public void Snapshots_SplitIntoChunks_AndReassemble()
    {
        var world = new World(new Vector2(192, 192));
        world.SpawnShip(new Vector2(10, 10), 0.5f, ShipStats.Sloop, 1, Loadouts.Sloop);
        for (var i = 0; i < 30; i++)
            world.SpawnShip(new Vector2(20 + i, 40), 0f, ShipStats.Sloop);
        world.AddGold(1, 25);
        var snapshot = Snapshot.Capture(world);

        var chunks = Wire.WriteSnapshotChunks(snapshot);
        Assert.Equal(3, chunks.Count); // 31 ships, 12 per chunk
        Assert.All(chunks, c => Assert.True(c.Length < 1100, $"chunk of {c.Length} bytes"));

        var ships = new List<ShipState>();
        Snapshot? header = null;
        foreach (var chunk in chunks)
        {
            var reader = ReaderFor(chunk);
            Assert.Equal(MessageType.SnapshotChunk, (MessageType)reader.GetByte());
            var read = reader.GetSnapshotChunk();
            Assert.Equal(snapshot.Tick, read.Tick);
            if (read.Index == 0)
                header = read.Partial;
            ships.AddRange(read.Partial.Ships);
        }

        Assert.Equal(snapshot.Ships.Select(s => (s.ShipId, s.Position, s.Heading)), ships.Select(s => (s.ShipId, s.Position, s.Heading)));
        // Per-slot, per-channel cooldowns survive: the player's broadside has two decks, empty pirate slots none.
        Assert.Equal(2, ships[0].Cooldowns[0].Length);           // the player's broadside: one per deck
        Assert.Single(ships[0].Cooldowns[1]);                    // the player's long gun: one cooldown
        Assert.Empty(ships[1].Cooldowns[1]);                     // a pirate's empty slot 2
        Assert.Equal(25, Assert.Single(header!.Players).Gold);
    }
}
