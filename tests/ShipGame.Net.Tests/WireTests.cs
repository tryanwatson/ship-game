using System.Numerics;
using LiteNetLib.Utils;
using ShipGame.Net;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;
using ShipGame.Shared.Upgrades;

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
        new object[] { new UnlockAbilityCommand(9, "mortar") },
        new object[] { new PurchaseSkillCommand(9, "heavy-volley") },
        new object[] { new PurchaseRepairCommand(9) },
        new object[] { new BuyCardPackCommand(9) },
        new object[] { new ChooseCardCommand(9, "double-battery") },
        new object[] { new RerollCardsCommand(9) },
        new object[] { new RerollCardsCommand(9, CardTier.Silver) },
        new object[] { new RerollCardsCommand(9, CardTier.Prismatic) },
        new object[] { new ChooseCourseCommand(9, 17) },
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
        new object[] { new ProjectileImpact(10, 40, 5, PassedThrough: true) },
        new object[] { new AbilityUnlocked(10, 3, "long-gun", AbilitySlot.Two) },
        new object[] { new SkillPurchased(10, 3, "piercing-shot") },
        new object[] { new CommandRejected(10, 2, new PurchaseSkillCommand(2, "deadeye"), RejectionReason.MissingPrerequisite) },
        new object[] { new AbilityCast(10, 3, AbilitySlot.One, 71, 1) },
        new object[] { new AbilityCast(10, 3, AbilitySlot.Three, 360) },
        new object[] { new ShipGrounded(10, 3) },
        new object[] { new GoldChanged(10, 2, 35, -15) },
        new object[] { new IslandPlundered(10, 4, 2, 10) },
        new object[] { new UpgradePurchased(10, 3, "agility", 2) },
        new object[] { new ShipHidden(10, 77) },
        new object[] { new CommandRejected(10, 2, new PurchaseUpgradeCommand(2, "speed"), RejectionReason.NotEnoughGold) },
        new object[] { new PlayerSunk(10, 2, 300) },
        new object[] { new PlayerRespawned(10, 2, 55) },
        new object[] { new RunEnded(10) },
        new object[] { new RunEnded(10, Victory: true) },
        new object[] { new FortressTaken(10, 12) },
        new object[] { new CardsOffered(10, 3, new CardOffer(OfferSource.Fortress, 8,
            new[] { new CardPick("twin-decks", 8), new CardPick("treasure-map", 7, 0.625f), new CardPick("echo", 8), new CardPick("ram", 8) }, 1)) },
        new object[] { new CardChosen(10, 3, new CardPick("ironclad", 4)) },
        new object[] { new CardsRerolled(10, 3, new CardOffer(OfferSource.Boss, 6, new[] { new CardPick("railgun", 6) }), 4) },
        new object[] { new ShipRammed(10, 3, 812) },
        new object[] { new FireStarted(10, 99, 3, Team.Players, new Vector2(40, 41.5f), 2.5f, 17.5f, 190) },
        new object[] { new BossSpawned(10, 812, 3, 2) },
        new object[] { new BossPhaseChanged(10, 812, 2) },
        new object[] { new ReliefFleetSighted(10, 7) },
        new object[] { new CardsOffered(10, 3, new CardOffer(OfferSource.Shop, 4, new[] { new CardPick("ironclad", 4) })) },
        new object[] { new VoyageCharted(10, ShipGame.Shared.Maps.SeaChart.Generate(4)) },
        new object[] { new RegionEntered(10, 7, new Vector2(180, 180), ShipGame.Shared.Maps.Regions.Build(
            ShipGame.Shared.Maps.SeaChart.Generate(4).Nodes.First(n => n.Kind == ShipGame.Shared.Maps.NodeKind.Fortress), seed: 4, firstIslandId: 30).Islands, 4321) },
        new object[] { new ShotWarned(10, 812, AbilitySlot.Two, new Vector2(3.5f, -7f), 26, BroadsideVolley.StarboardChannel) },
    };


    [Fact]
    public void Islands_RoundTrip_WithEverythingThatMakesThem()
    {
        var fortress = Island.FromTemplate(41, ShipGame.Shared.Maps.IslandShapes.Wedge, new Vector2(90.5f, 57.25f), 380f, 1.3f,
            name: "SKULL ROCK", plunderGold: 210, level: 7, isFortress: true);
        var yard = Island.FromTemplate(42, ShipGame.Shared.Maps.IslandShapes.Round, new Vector2(20, 30), 200f, 0.2f, hasShipyard: true, name: "HAVEN", level: 3);
        var writer = new NetDataWriter();
        writer.Put(fortress);
        writer.Put(yard);
        var reader = ReaderFor(writer);

        foreach (var sent in new[] { fortress, yard })
        {
            var read = reader.GetIsland();
            Assert.Equal((sent.Id, sent.Name, sent.PlunderGold, sent.Level, sent.HasShipyard, sent.IsFortress),
                (read.Id, read.Name, read.PlunderGold, read.Level, read.HasShipyard, read.IsFortress));
            Assert.Equal(sent.Outline.ToArray(), read.Outline.ToArray());
        }
    }

    [Theory]
    [MemberData(nameof(Events))]
    public void Events_RoundTrip(WorldEvent worldEvent)
    {
        var writer = new NetDataWriter();
        writer.PutEvent(worldEvent);

        Assert.Equal(worldEvent, ReaderFor(writer).GetEvent());
    }

    [Fact]
    public void Events_SendAVolleyOfShotsAsOneRecord_AndReadThemBackApart()
    {
        // A ring of 20, then a lone shot of another ship's, then a broken run (an id skipped), round a couple of others.
        var events = new List<WorldEvent> { new ShipRammed(10, 3, 812) };
        for (var i = 0; i < 20; i++)
            events.Add(new ProjectileSpawned(10, 100 + i, 3, Team.Players, new Vector2(i, -i), new Vector2(14f, i * 0.5f), 6f, 18));
        events.Add(new ProjectileSpawned(10, 120, 4, Team.Players, new Vector2(1, 2), new Vector2(3, 4), 6f, 18));
        events.Add(new ProjectileSpawned(10, 122, 3, Team.Players, new Vector2(1, 2), new Vector2(3, 4), 6f, 18));
        events.Add(new FireStarted(10, 99, 3, Team.Players, new Vector2(40, 41.5f), 2.5f, 17.5f, 190));

        var batched = new NetDataWriter();
        batched.PutEvents(events);
        var oneByOne = new NetDataWriter();
        foreach (var e in events)
            oneByOne.PutEvent(e);

        Assert.Equal(events, ReaderFor(batched).GetEvents());
        Assert.True(batched.Length < oneByOne.Length / 2, $"{batched.Length} bytes batched against {oneByOne.Length}");
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
    public void Lobby_CarriesTheStartingGold()
    {
        var writer = new NetDataWriter();
        writer.PutLobby(new LobbyState(false, new[] { new LobbyPlayer(1, false) }, StartingGold: 2500));

        Assert.Equal(2500, ReaderFor(writer).GetLobby().StartingGold);
    }

    [Fact]
    public void Lobby_CarriesTheTestingOption()
    {
        var writer = new NetDataWriter();
        writer.PutLobby(new LobbyState(false, new[] { new LobbyPlayer(1, false) }, Testing: true));

        Assert.True(ReaderFor(writer).GetLobby().Testing);
    }

    [Fact]
    public void Lobby_CarriesEachPlayersName()
    {
        var players = new[] { new LobbyPlayer(1, true, "ANNE BONNY"), new LobbyPlayer(2, false) };
        var writer = new NetDataWriter();
        writer.PutLobby(new LobbyState(false, players));

        Assert.Equal(players, ReaderFor(writer).GetLobby().Players);
    }

    [Fact]
    public void RunStart_CarriesTheCrewsNames()
    {
        var crew = new List<(int, string)> { (1, "ANNE"), (4, "MARY READ") };
        var writer = new NetDataWriter();
        writer.PutRunStart(new RunStart(10, new Vector2(640, 640), Vector2.One, true, crew));

        Assert.Equal(crew, ReaderFor(writer).GetRunStart().Crew);
    }

    [Fact]
    public void ShipInfo_RoundTrips()
    {
        var info = new ShipInfo(
            123, 7, 2, Team.Players, ShipStats.Sloop with { WeaponRange = 1.3f, HealthRegen = 1.5f },
            new[] { "broadside", null, "long-gun", "mortar" },
            new[] { new StatModifier(StatId.MaxHealth, ModifierKind.Flat, 20, "upgrade:hull") },
            new Vector2(96, 90), 1.25f, new[] { "heavy-volley", "point-blank" }, Level: 6, IsBoss: true, FortIslandId: 14,
            Cards: new[] { new CardPick("full-sail", 2), new CardPick("full-sail", 5), new CardPick("treasure-map", 3, 0.25f) });
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
        Assert.Equal(info.SkillIds, read.SkillIds);
        Assert.Equal((6, true, (int?)14), (read.Level, read.IsBoss, read.FortIslandId));
        Assert.Equal(info.Cards, read.Cards);
    }

    [Fact]
    public void Snapshots_SplitIntoChunks_AndReassemble()
    {
        var world = new World(new Vector2(192, 192));
        var player = world.SpawnShip(new Vector2(10, 10), 0.5f, ShipStats.Sloop, 1, Loadouts.FullArsenal);
        player.IsHoldingCourse = true;
        player.WindDrift = new Vector2(0.25f, -0.5f);
        player.AnchorDropTicksRemaining = 17;
        player.Throttle = ShipMovement.AsternThrottle;
        for (var i = 0; i < 30; i++)
            world.SpawnShip(new Vector2(20 + i, 40), 0f, ShipStats.Sloop);
        world.AddGold(1, 25);
        world.Players[1].CourseVote = 12;
        world.ApplyStatus(player, StatusId.Burning, 4.5f, sourceShipId: 7, stacks: 3);
        player.AddCard(new CardPick("sea-miles", 1));
        player.AddToTally(Tally.TilesSailed, 612.5f);
        player.AddToTally(Tally.Kills, 3f);
        world.Ships.Last().AddCard(new CardPick("bounty-hunter", 1));
        world.Ships.Last().AddToTally(Tally.Kills, 2f);
        world.Ships.Last().AddToTally(Tally.TilesSailed, 9f); // a pirate's: only what its cards grow with is sent
        var snapshot = Snapshot.Capture(world);
        snapshot.CommandAcks.Add((1, 4_000_000_000u));
        snapshot.Run = new ShipGame.Shared.Progression.RunStatus(NodeId: 17, Cleared: true, FortressesTaken: 5, BossesSunk: 2, BossCountdownTicks: 287, BossAfloat: true);
        snapshot.RunOver = true;
        snapshot.Victory = true;

        var (header, ships) = RoundTrip(snapshot, out var chunks);
        Assert.True(chunks > 1);

        Assert.Equal(snapshot.Ships.Select(s => (s.ShipId, s.Position, s.Heading)), ships.Select(s => (s.ShipId, s.Position, s.Heading)));
        // Per-slot, per-channel cooldowns survive: the player's broadside has two decks, empty pirate slots none.
        Assert.Equal(2, ships[0].Cooldowns[0].Length);           // the player's broadside: one per deck
        Assert.Single(ships[0].Cooldowns[1]);                    // the player's long gun: one cooldown
        Assert.Empty(ships[1].Cooldowns[1]);                     // a pirate's empty slot 2
        Assert.Equal((25, (int?)12), (Assert.Single(header.Players).Gold, header.Players[0].CourseVote));
        // What prediction needs to carry on from the server's state exactly.
        Assert.True(ships[0].IsHoldingCourse);
        Assert.Equal(new Vector2(0.25f, -0.5f), ships[0].WindDrift);
        Assert.Equal(17, ships[0].AnchorDropTicks);
        Assert.Equal(-1, ships[0].Throttle); // rowing astern
        Assert.Equal(new[] { (StatusId.Burning, 3, 4.5f, Statuses.Get(StatusId.Burning).Ticks) }, ships[0].Statuses);
        Assert.Empty(ships[1].Statuses);
        Assert.Equal(new[] { (Tally.Kills, 3f), (Tally.TilesSailed, 612.5f), (Tally.GoldEarned, 25f) }, ships[0].Tallies.OrderBy(t => t.Tally));
        Assert.Equal(new[] { (Tally.Kills, 2f) }, ships[^1].Tallies);
        Assert.Equal(4_000_000_000u, header.AckFor(1));
        Assert.Equal(0u, header.AckFor(2));
        Assert.Equal(snapshot.Run, header.Run); // the HUD's fortress and boss forecast
        Assert.True(header.RunOver && header.Victory);
    }

    [Fact]
    public void Snapshots_FitLiteNetLibsUnreliableLimit_AtFullSize()
    {
        // The worst case: a full server (12 players, all acked), a full wave of pirates with players' loadouts,
        // every island plundered, everyone moving somewhere and plundering.
        var world = new World(new Vector2(192, 192));
        foreach (var island in ShipGame.Shared.Maps.Archipelago.CreateIslands())
        {
            world.AddIsland(island);
            world.MarkPlundered(island);
        }
        for (var i = 0; i < 52; i++)
        {
            var ship = world.SpawnShip(new Vector2(10 + i, 10 + i), 0f, ShipStats.Sloop, i < 12 ? i + 1 : null, Loadouts.FullArsenal);
            ship.MoveTarget = new Vector2(100, 100);
            ship.PlunderIslandId = 1;
        }
        foreach (var player in world.Players.Values)
            player.CourseVote = 40; // everyone's voted
        var snapshot = Snapshot.Capture(world);
        for (var i = 1; i <= 12; i++)
            snapshot.CommandAcks.Add((i, uint.MaxValue));

        foreach (var chunk in Wire.WriteSnapshotChunks(snapshot))
            Assert.InRange(chunk.Length, 1, Protocol.MaxSnapshotChunkBytes);

        var (header, ships) = RoundTrip(snapshot, out _);
        Assert.Equal(52, ships.Count);
        Assert.Equal(12, header.CommandAcks.Count);
    }

    private static (Snapshot Header, List<ShipState> Ships) RoundTrip(Snapshot snapshot, out int chunkCount)
    {
        var chunks = Wire.WriteSnapshotChunks(snapshot);
        chunkCount = chunks.Count;
        var ships = new List<ShipState>();
        Snapshot? header = null;
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Length <= Protocol.MaxSnapshotChunkBytes, $"chunk of {chunk.Length} bytes");
            var reader = ReaderFor(chunk);
            Assert.Equal(MessageType.SnapshotChunk, (MessageType)reader.GetByte());
            var read = reader.GetSnapshotChunk();
            Assert.Equal(snapshot.Tick, read.Tick);
            Assert.Equal(chunks.Count, read.Count);
            if (read.Index == 0)
                header = read.Partial;
            ships.AddRange(read.Partial.Ships);
        }
        return (header!, ships);
    }
}
