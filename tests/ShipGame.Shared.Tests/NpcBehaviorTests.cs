using System.Numerics;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Simulation;

namespace ShipGame.Shared.Tests;

public class NpcBehaviorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CirclePatrol_SailsLapsAroundTheCircle(bool clockwise)
    {
        var world = new World(new Vector2(64, 64)); // default wind: patrollers under way shouldn't care
        var patrol = new CirclePatrol(new Vector2(32, 32), 10f, clockwise);
        var (position, heading) = patrol.StartPose(0f);
        var ship = world.SpawnShip(position, heading, ShipStats.Sloop);
        ship.Behavior = patrol;
        ship.Throttle = 3;
        ship.Speed = ship.CruiseSpeed;

        var minRadius = float.MaxValue;
        var maxRadius = 0f;
        var swept = 0f;
        var lastAngle = 0f;
        for (var t = 0; t < SimConstants.TickRate * 60; t++)
        {
            world.Step();
            var offset = ship.Position - patrol.Center;
            var radius = offset.Length();
            minRadius = MathF.Min(minRadius, radius);
            maxRadius = MathF.Max(maxRadius, radius);
            var angle = MathF.Atan2(offset.Y, offset.X);
            swept += Angles.Delta(lastAngle, angle);
            lastAngle = angle;

            Assert.True(ship.Speed > 0.5f, $"tick {t}: patroller lost way ({ship.Speed})");
        }

        Assert.InRange(minRadius, 8.5f, 10.5f);
        Assert.InRange(maxRadius, 9.5f, 11.5f);
        var laps = swept / MathF.Tau * (clockwise ? 1f : -1f);
        Assert.True(laps > 2f, $"only {laps} laps in a minute"); // ~63-tile lap at 3 tiles/s is ~21 s
    }
}
