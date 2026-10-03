using System.Numerics;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

public class ShipMovementTests
{
    private const int PlayerId = 1;

    private static (World world, Ship ship) CreateWorld(Vector2 position, float heading = 0f)
    {
        var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
        var ship = world.SpawnShip(position, heading, ShipStats.Sloop, PlayerId);
        return (world, ship);
    }

    private static void RunTicks(World world, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            world.Step();
    }

    [Fact]
    public void MoveCommand_ShipArrivesAndStops()
    {
        var (world, ship) = CreateWorld(new Vector2(10, 10));
        var target = new Vector2(30, 10);

        world.Enqueue(new MoveCommand(PlayerId, target));
        RunTicks(world, SimConstants.TickRate * 15);

        Assert.True(Vector2.Distance(ship.Position, target) <= ShipMovement.ArriveRadius + 0.05f);
        Assert.Null(ship.MoveTarget);
        Assert.Equal(0f, ship.Speed, 3);
    }

    [Fact]
    public void MoveCommand_TargetDirectlyBehind_ArrivesWithoutOrbiting()
    {
        var (world, ship) = CreateWorld(new Vector2(30, 30), heading: 0f);
        ship.Speed = ship.Stats.MaxSpeed;
        var target = new Vector2(26, 30);

        world.Enqueue(new MoveCommand(PlayerId, target));
        RunTicks(world, SimConstants.TickRate * 20);

        Assert.Null(ship.MoveTarget);
        Assert.True(Vector2.Distance(ship.Position, target) <= ShipMovement.ArriveRadius + 0.05f);
    }

    [Fact]
    public void Turning_NeverTighterThanTurnRadius()
    {
        var (world, ship) = CreateWorld(new Vector2(30, 30), heading: 0f);
        world.Enqueue(new MoveCommand(PlayerId, new Vector2(28, 31))); // behind and close: a hard maneuver

        for (var i = 0; i < SimConstants.TickRate * 10; i++)
        {
            var headingBefore = ship.Heading;
            var positionBefore = ship.Position;
            world.Step();

            var turned = MathF.Abs(Angles.Delta(headingBefore, ship.Heading));
            var travelled = Vector2.Distance(positionBefore, ship.Position);
            Assert.True(turned <= travelled / ship.Stats.TurnRadiusAt(ship.Speed) + 1e-5f,
                $"tick {i}: turned {turned} rad over {travelled} units");
        }
    }

    [Fact]
    public void StationaryShip_DoesNotTurnInPlace()
    {
        var (world, ship) = CreateWorld(new Vector2(30, 30), heading: 0f);
        world.Enqueue(new MoveCommand(PlayerId, new Vector2(20, 30))); // dead astern

        // Over the first second the ship must gather way bow-first before it can swing round.
        for (var i = 0; i < SimConstants.TickRate; i++)
            world.Step();

        Assert.True(ship.Position.X > 30.5f);
        Assert.True(MathF.Abs(ship.Heading) < MathF.PI / 4f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(5f)]
    public void MoveCommand_ReachesTargetsInEveryDirection(float initialSpeed)
    {
        // Covers targets inside the turning circles, which naive steering orbits forever.
        foreach (var distance in new[] { 1.5f, 4f, 10f })
        {
            for (var i = 0; i < 16; i++)
            {
                var angle = MathF.Tau * i / 16;
                var (world, ship) = CreateWorld(new Vector2(32, 32), heading: 0f);
                ship.Throttle = ShipMovement.ThrottleLevels;
                ship.Speed = initialSpeed;
                var target = ship.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;

                world.Enqueue(new MoveCommand(PlayerId, target));
                RunTicks(world, SimConstants.TickRate * 20);

                Assert.True(ship.MoveTarget is null,
                    $"never arrived: distance {distance}, angle {angle * 180 / MathF.PI:0} deg, speed {initialSpeed}, " +
                    $"ended at {ship.Position} heading {ship.Heading:0.00}");
            }
        }
    }

    [Fact]
    public void Commands_OnlyControlTheIssuingPlayersShip()
    {
        var (world, ship) = CreateWorld(new Vector2(10, 10));
        var other = world.SpawnShip(new Vector2(40, 40), 0f, ShipStats.Sloop, ownerPlayerId: 2);

        world.Enqueue(new MoveCommand(2, new Vector2(50, 50)));
        world.Step();

        Assert.Null(ship.MoveTarget);
        Assert.Equal(new Vector2(50, 50), other.MoveTarget);
    }

    [Fact]
    public void MoveTarget_IsClampedToWorld()
    {
        var (world, ship) = CreateWorld(new Vector2(10, 10));

        world.Enqueue(new MoveCommand(PlayerId, new Vector2(-100, 500)));
        world.Step();

        Assert.Equal(new Vector2(0, 64), ship.MoveTarget);
    }

    [Fact]
    public void OverlappingShips_ArePushedApart()
    {
        var (world, a) = CreateWorld(new Vector2(20, 20));
        var b = world.SpawnShip(new Vector2(20.5f, 20), 0f, ShipStats.Sloop);

        world.Step();

        Assert.True(Vector2.Distance(a.Position, b.Position) >= a.Stats.Radius + b.Stats.Radius - 1e-4f);
    }

    [Fact]
    public void SameCommands_ProduceSameState()
    {
        var (worldA, shipA) = CreateWorld(new Vector2(10, 10));
        var (worldB, shipB) = CreateWorld(new Vector2(10, 10));

        foreach (var world in new[] { worldA, worldB })
        {
            world.Enqueue(new MoveCommand(PlayerId, new Vector2(40, 25)));
            RunTicks(world, 45);
            world.Enqueue(new MoveCommand(PlayerId, new Vector2(5, 50)));
            RunTicks(world, 90);
        }

        Assert.Equal(shipA.Position, shipB.Position);
        Assert.Equal(shipA.Heading, shipB.Heading);
    }

    [Fact]
    public void Throttle_IsClampedToValidLevels()
    {
        var (world, ship) = CreateWorld(new Vector2(10, 10));

        world.Enqueue(new AdjustThrottleCommand(PlayerId, +10));
        world.Step();
        Assert.Equal(ShipMovement.ThrottleLevels, ship.Throttle);

        world.Enqueue(new AdjustThrottleCommand(PlayerId, -10));
        world.Step();
        Assert.Equal(0, ship.Throttle);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(ShipMovement.AutopilotThrottle)]
    [InlineData(ShipMovement.ThrottleLevels)]
    public void Throttle_CapsCruisingSpeed(int level)
    {
        var (world, ship) = CreateWorld(new Vector2(5, 30));
        ship.Throttle = level;

        world.Enqueue(new MoveCommand(PlayerId, new Vector2(60, 30)));
        var topSpeed = 0f;
        for (var i = 0; i < SimConstants.TickRate * 6; i++)
        {
            world.Step();
            topSpeed = MathF.Max(topSpeed, ship.Speed);
        }

        var expected = ship.Stats.MaxSpeed * level / ShipMovement.ThrottleLevels;
        Assert.Equal(expected, topSpeed, 3);
    }

    [Fact]
    public void MoveCommand_RandomManeuvers_AllArriveWithoutTurningInPlace()
    {
        // Seeded stress test over the whole map: random start pose, speed, sail setting and target.
        // Guards against the orbiting / dithering failure modes of curvature-limited steering.
        var rng = new Random(20261002);
        for (var k = 0; k < 500; k++)
        {
            var world = new World(new Vector2(64, 64)) { Wind = Vector2.Zero };
            var start = new Vector2(rng.NextSingle() * 64, rng.NextSingle() * 64);
            var ship = world.SpawnShip(start, rng.NextSingle() * MathF.Tau - MathF.PI, ShipStats.Sloop, PlayerId);
            ship.Throttle = rng.Next(1, ShipMovement.ThrottleLevels + 1);
            ship.Speed = rng.NextSingle() * ship.CruiseSpeed;
            var angle = rng.NextSingle() * MathF.Tau;
            var target = start + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (0.5f + rng.NextSingle() * 15f);

            world.Enqueue(new MoveCommand(PlayerId, target));
            for (var t = 0; t < SimConstants.TickRate * 40 && (t == 0 || ship.MoveTarget is not null); t++)
            {
                var headingBefore = ship.Heading;
                var positionBefore = ship.Position;
                world.Step();

                var turned = MathF.Abs(Angles.Delta(headingBefore, ship.Heading));
                Assert.True(turned <= Vector2.Distance(positionBefore, ship.Position) / ship.Stats.MinTurnRadius + 1e-4f,
                    $"case {k}: turned without travelling");
            }

            Assert.True(ship.MoveTarget is null, $"case {k}: never arrived (start {start}, target {target}, sail {ship.Throttle})");
        }
    }

    [Fact]
    public void ShipsCanLoopThroughTheOutOfBoundsMargin()
    {
        // Hugging the edge with the target dead astern: the loop has to swing past the playable area.
        var (world, ship) = CreateWorld(new Vector2(60, 63), heading: 0f);
        ship.Throttle = ShipMovement.ThrottleLevels;
        ship.Speed = ship.CruiseSpeed;
        var target = new Vector2(55, 64);

        world.Enqueue(new MoveCommand(PlayerId, target));
        RunTicks(world, SimConstants.TickRate * 20);

        Assert.Null(ship.MoveTarget);
        Assert.True(Vector2.Distance(ship.Position, target) <= ShipMovement.ArriveRadius + 0.05f);
    }

    [Fact]
    public void NewShip_IsAtRestWithSailsFurled()
    {
        var (world, ship) = CreateWorld(new Vector2(30, 30));

        RunTicks(world, SimConstants.TickRate);

        Assert.Equal(0, ship.Throttle);
        Assert.Equal(new Vector2(30, 30), ship.Position);
    }

    [Fact]
    public void WithNoMoveOrder_ShipSailsStraightAtCruise()
    {
        var (world, ship) = CreateWorld(new Vector2(10, 30));

        world.Enqueue(new AdjustThrottleCommand(PlayerId, +2));
        RunTicks(world, SimConstants.TickRate * 5);

        Assert.Equal(ship.CruiseSpeed, ship.Speed, 3);
        Assert.Equal(0f, ship.Heading);
        Assert.Equal(30f, ship.Position.Y, 3);
    }

    [Theory]
    [InlineData(1, 1f)]   // starboard: clockwise in a Y-down world
    [InlineData(-1, -1f)] // port
    public void Rudder_TurnsTheShipAlongItsTurningCircle(int rudder, float expectedTurnSign)
    {
        var (world, ship) = CreateWorld(new Vector2(30, 30));
        ship.Throttle = ShipMovement.ThrottleLevels;
        ship.Speed = ship.CruiseSpeed;

        world.Enqueue(new SetRudderCommand(PlayerId, rudder));
        world.Step();
        var turned = Angles.Delta(0f, ship.Heading);

        Assert.Equal(expectedTurnSign, MathF.Sign(turned));
        var expected = ship.Speed * SimConstants.TickDelta / ship.Stats.TurnRadiusAt(ship.Speed);
        Assert.Equal(expected, MathF.Abs(turned), 4);
    }

    [Theory]
    [InlineData(1, 1f)]
    [InlineData(-1, -1f)]
    public void Rudder_RowsAStoppedShipRoundOnTheSpot(int rudder, float expectedTurnSign)
    {
        var (world, ship) = CreateWorld(new Vector2(30, 30));

        world.Enqueue(new SetRudderCommand(PlayerId, rudder));
        RunTicks(world, SimConstants.TickRate * 2);

        // Two seconds at the rowing rate, without going anywhere.
        Assert.Equal(expectedTurnSign * ShipMovement.RowingTurnRate * 2f, Angles.Delta(0f, ship.Heading), 3);
        Assert.Equal(new Vector2(30, 30), ship.Position);
        Assert.Equal(0f, ship.Speed);
    }

    [Fact]
    public void Rowing_StopsWhenTheHelmIsCentered()
    {
        var (world, ship) = CreateWorld(new Vector2(30, 30));

        world.Enqueue(new SetRudderCommand(PlayerId, 1));
        RunTicks(world, SimConstants.TickRate);
        world.Enqueue(new SetRudderCommand(PlayerId, 0));
        world.Step();
        var heading = ship.Heading;
        RunTicks(world, SimConstants.TickRate);

        Assert.Equal(heading, ship.Heading);
    }

    [Fact]
    public void Rowing_IsOnlyWithTheSailsFurled_AndNotAtAnchor()
    {
        var (world, ship) = CreateWorld(new Vector2(30, 30));
        Assert.False(ShipMovement.IsRowing(ship)); // helm amidships

        ship.Rudder = 1;
        Assert.True(ShipMovement.IsRowing(ship));
        ship.Throttle = 1;
        Assert.False(ShipMovement.IsRowing(ship)); // under sail it steers along its turning circle
        ship.Throttle = 0;
        ship.IsAnchored = true;
        Assert.False(ShipMovement.IsRowing(ship));

        RunTicks(world, SimConstants.TickRate);
        Assert.Equal(0f, ship.Heading); // held fast
    }

    [Fact]
    public void Rudder_TakesTheHelmFromAMoveOrder()
    {
        var (world, ship) = CreateWorld(new Vector2(10, 10));
        world.Enqueue(new MoveCommand(PlayerId, new Vector2(40, 10)));
        world.Step();
        Assert.Equal(ShipMovement.AutopilotThrottle, ship.Throttle);

        world.Enqueue(new SetRudderCommand(PlayerId, -1));
        world.Step();

        Assert.Null(ship.MoveTarget);
        Assert.Equal(ShipMovement.AutopilotThrottle, ship.Throttle); // keeps sailing, now under manual helm
    }

    [Fact]
    public void Stop_FurlsSailsAndCancelsMoveOrder()
    {
        var (world, ship) = CreateWorld(new Vector2(10, 10));
        world.Enqueue(new MoveCommand(PlayerId, new Vector2(40, 10)));
        RunTicks(world, SimConstants.TickRate * 2);

        world.Enqueue(new StopCommand(PlayerId));
        RunTicks(world, SimConstants.TickRate * 6);

        Assert.Null(ship.MoveTarget);
        Assert.Equal(0f, ship.Speed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void AllStop_DriftsToRestAsPredicted(int throttle)
    {
        var (world, ship) = CreateWorld(new Vector2(5, 30));
        ship.Throttle = throttle;
        ship.Speed = ship.CruiseSpeed;
        var predicted = ship.Stats.StoppingDistance(ship.Speed);

        world.Enqueue(new StopCommand(PlayerId));
        var ticks = 0;
        do { world.Step(); ticks++; } while (ship.Speed > 0f && ticks < SimConstants.TickRate * 10);

        var drifted = ship.Position.X - 5f;
        Assert.Equal(0f, ship.Speed);
        Assert.InRange(drifted, predicted * 0.95f, predicted * 1.05f);
        Assert.True(drifted > 0.5f, "ship should coast, not stop dead");
    }

    [Fact]
    public void Drift_SlowsFastAtFirstThenEasesOut()
    {
        var (world, ship) = CreateWorld(new Vector2(5, 30));
        ship.Throttle = ShipMovement.ThrottleLevels;
        ship.Speed = ship.CruiseSpeed;
        world.Enqueue(new StopCommand(PlayerId));

        var startSpeed = ship.Speed;
        world.Step();
        var earlyLoss = startSpeed - ship.Speed;
        while (ship.Speed > 0.5f)
            world.Step();
        var before = ship.Speed;
        world.Step();
        var lateLoss = before - ship.Speed;

        Assert.True(earlyLoss > lateLoss * 2f, $"early {earlyLoss} vs late {lateLoss}");
    }

    [Fact]
    public void MoveCommand_GlidesInAndRestsNearTarget()
    {
        var (world, ship) = CreateWorld(new Vector2(10, 30));
        ship.Throttle = ShipMovement.ThrottleLevels;
        var target = new Vector2(40, 30);

        world.Enqueue(new MoveCommand(PlayerId, target));
        RunTicks(world, SimConstants.TickRate * 20);

        Assert.Equal(0f, ship.Speed);
        Assert.True(Vector2.Distance(ship.Position, target) <= ShipMovement.ArriveRadius);
    }
}
