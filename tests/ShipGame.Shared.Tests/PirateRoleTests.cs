using System.Numerics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

/// <summary>Buccaneers, snipers and bombers: one weapon each, named for it, and fought the way it wants.</summary>
public class PirateRoleTests
{
    private const int PlayerId = 1;

    private static Ship SpawnPirate(World world, Vector2 position, PirateRole role)
    {
        var pirate = world.SpawnShip(position, 0f, ShipStats.PirateSloop, abilities: PirateRoles.Loadout(role));
        pirate.Behavior = new HunterBehavior(position);
        pirate.IsAnchored = true;
        return pirate;
    }

    [Theory]
    [InlineData(PirateRole.Buccaneer, typeof(BroadsideVolley), "Buccaneer")]
    [InlineData(PirateRole.Sniper, typeof(LongGun), "Sniper")]
    [InlineData(PirateRole.Bomber, typeof(Mortar), "Bomber")]
    public void EachRole_CarriesOneWeapon_AndIsKnownByIt(PirateRole role, Type weapon, string name)
    {
        var world = new World(new Vector2(64, 64));
        var pirate = SpawnPirate(world, new Vector2(30, 30), role);

        Assert.IsType(weapon, pirate.Abilities[0]!.Definition);
        Assert.All(pirate.Abilities.Skip(1), Assert.Null);
        Assert.Equal(role, PirateRoles.Of(pirate));
        Assert.Equal(name, PirateRoles.Name(role));
    }

    [Fact]
    public void PlayersAndTheFlagship_HaveNoRole()
    {
        var world = new World(new Vector2(64, 64));
        var player = world.SpawnShip(new Vector2(10, 10), 0f, ShipStats.Sloop, PlayerId, Loadouts.Starting(new LongGun()));
        var flagship = world.SpawnShip(new Vector2(40, 40), 0f, ShipStats.Flagship, abilities: Loadouts.Pirate);
        flagship.IsBoss = true;

        Assert.Null(PirateRoles.Of(player));
        Assert.Null(PirateRoles.Of(flagship));
    }

    [Fact]
    public void ARun_PutsEveryRoleToSea_AndTheFlagshipKeepsItsBroadside()
    {
        var world = Runs.Create(seed: 1, new List<(int, Ability)> { (PlayerId, new BroadsideVolley()) });
        var pirates = world.Ships.Where(s => s.Team == Team.Pirates).ToList();

        var roles = pirates.Where(s => !s.IsBoss).Select(s => PirateRoles.Of(s)).ToList();
        Assert.All(roles, r => Assert.NotNull(r));
        Assert.Equal(PirateRoles.All.OrderBy(r => r), roles.Distinct().Select(r => r!.Value).OrderBy(r => r));
        Assert.IsType<BroadsideVolley>(pirates.Single(s => s.IsBoss).Abilities[0]!.Definition);
    }

    [Fact]
    public void Sniper_HitsASailingTarget_FromBeyondBroadsideRange()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(60, 100), 0f, ShipStats.Sloop, PlayerId);
        player.Throttle = ShipMovement.ThrottleLevels; // sailing east, straight on
        var sniper = SpawnPirate(world, new Vector2(60, 86), PirateRole.Sniper);

        var closest = float.MaxValue;
        for (var t = 0; t < SimConstants.TickRate * 8 && player.LastHitTick is null; t++)
        {
            world.Step();
            closest = MathF.Min(closest, Vector2.Distance(sniper.Position, player.Position));
        }

        Assert.Equal(sniper.Id, player.LastHitByShipId);
        Assert.True(closest > BroadsideVolley.Range + 2f, $"closed to {closest}");
    }

    [Fact]
    public void Bomber_ShellsATargetBehindAnIsland_WithoutClosing()
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        world.AddIsland(new Island(1, new[] { new Vector2(99, 85), new Vector2(102, 85), new Vector2(102, 115), new Vector2(99, 115) }));
        var player = world.SpawnShip(new Vector2(110, 100), 0f, ShipStats.Sloop, PlayerId);
        player.IsAnchored = true;
        var bomber = SpawnPirate(world, new Vector2(92, 100), PirateRole.Bomber); // in sight, across the island

        var closest = float.MaxValue;
        for (var t = 0; t < SimConstants.TickRate * 8 && player.LastHitTick is null; t++)
        {
            world.Step();
            closest = MathF.Min(closest, Vector2.Distance(bomber.Position, player.Position));
        }

        Assert.Equal(bomber.Id, player.LastHitByShipId);
        Assert.True(closest > BroadsideVolley.Range + 2f, $"closed to {closest}");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void BombersShells_HitAShipHoldingItsCourse_ButNotOneThatTurnsAway(bool dodge, bool hit)
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(60, 100), 0f, ShipStats.Sloop, PlayerId);
        player.Throttle = ShipMovement.ThrottleLevels;
        player.Speed = player.Stats.MaxSpeed; // under full sail, east
        SpawnPirate(world, new Vector2(70, 82), PirateRole.Bomber);

        // Run until the first shell lands; a dodger puts the helm hard over a moment after seeing it fly.
        var reactionTicks = (int)(0.4f * SimConstants.TickRate);
        AreaStrike? shell = null;
        for (var t = 0; t < SimConstants.TickRate * 6 && (shell is null || world.Tick <= shell.ImpactTick); t++)
        {
            world.Step();
            shell ??= world.Strikes.FirstOrDefault();
            if (dodge && shell is not null && world.Tick - shell.LaunchTick >= reactionTicks)
                player.Rudder = 1;
        }

        Assert.NotNull(shell);
        Assert.Equal(hit, player.LastHitTick is not null);
    }

    [Theory]
    [InlineData(PirateRole.Sniper)]
    [InlineData(PirateRole.Bomber)]
    public void StandoffPirates_KeepTheirDistance(PirateRole role)
    {
        var world = new World(new Vector2(192, 192)) { Wind = Vector2.Zero };
        var player = world.SpawnShip(new Vector2(100, 100), 0f, ShipStats.Sloop, PlayerId);
        player.IsAnchored = true;
        var pirate = SpawnPirate(world, new Vector2(100, 88), role);
        var (_, preferred) = HunterBehavior.FightingRanges(pirate);

        for (var t = 0; t < SimConstants.TickRate * 20; t++)
        {
            world.Step();
            player.Health = player.Stats.MaxHealth; // stay afloat to be circled
        }

        var distance = Vector2.Distance(pirate.Position, player.Position);
        Assert.InRange(distance, preferred * 0.6f, preferred * 1.4f);
    }
}
