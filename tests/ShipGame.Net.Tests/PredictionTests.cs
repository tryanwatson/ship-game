using System.Numerics;
using ShipGame.Net;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;

namespace ShipGame.Net.Tests;

/// <summary>
/// <see cref="LocalShipPredictor"/> against a real "server" world: with the same commands landing on the same ticks,
/// the prediction should match what the server goes on to simulate.
/// </summary>
public class PredictionTests
{
    private const int PlayerId = 1;
    private const float Frame = 1f / 60f;

    private readonly World _server;
    private readonly Ship _serverShip;
    private readonly LocalShipPredictor _predictor = new(Archipelago.Size);

    public PredictionTests()
    {
        _server = new World(Archipelago.Size);
        foreach (var island in Archipelago.CreateIslands())
            _server.AddIsland(island);
        _serverShip = _server.SpawnShip(Archipelago.Size / 2f, 0f, ShipStats.Sloop, PlayerId, Loadouts.Sloop);
        _server.Enqueue(new AdjustThrottleCommand(PlayerId, 3));
        Step(10);
    }

    private void Step(int ticks)
    {
        for (var i = 0; i < ticks; i++)
            _server.Step();
    }

    /// <summary>What a client would know: the server's ship as of now, mirrored onto a replica ship.</summary>
    private (Ship Replica, ShipState State) Snapshot()
    {
        var snapshot = ShipGame.Net.Snapshot.Capture(_server);
        var state = snapshot.Find(_serverShip.Id)!;
        var replicaWorld = new World(Archipelago.Size);
        var replica = replicaWorld.SpawnShip(state.Position, state.Heading, _serverShip.BaseStats, PlayerId,
            _serverShip.Abilities.Select(a => a?.Definition).ToList(), _serverShip.Id);
        replica.ReplaceModifiers(_serverShip.Modifiers);
        return (replica, state);
    }

    private void Predict(Ship replica, ShipState state, long baseTick, uint acked, double predictTick) =>
        _predictor.Update(replica, state, baseTick, acked, _server.Wind, predictTick, Frame);

    [Fact]
    public void WithNothingPending_PredictionIsTheServersFuture()
    {
        var baseTick = _server.Tick;
        var (replica, state) = Snapshot();

        Predict(replica, state, baseTick, acked: 0, predictTick: baseTick + 9);
        Step(9);

        Assert.Equal(_serverShip.Position.X, _predictor.Position.X, 3);
        Assert.Equal(_serverShip.Position.Y, _predictor.Position.Y, 3);
        Assert.Equal(_serverShip.Heading, _predictor.Heading, 3);
    }

    [Fact]
    public void UnconfirmedCommands_AreReplayedOnTheTickTheyWillLand()
    {
        var baseTick = _server.Tick;
        var (replica, state) = Snapshot();
        var helm = new SetRudderCommand(PlayerId, 1);
        var sail = new AdjustThrottleCommand(PlayerId, 2);
        _predictor.Record(1, helm, baseTick + 3);
        _predictor.Record(2, sail, baseTick + 6);

        Predict(replica, state, baseTick, acked: 0, predictTick: baseTick + 12);

        Step(2);
        _server.Enqueue(helm); // lands in the step that makes baseTick + 3
        Step(3);
        _server.Enqueue(sail); // ...and baseTick + 6
        Step(7);
        Assert.Equal(_serverShip.Position.X, _predictor.Position.X, 3);
        Assert.Equal(_serverShip.Position.Y, _predictor.Position.Y, 3);
        Assert.Equal(_serverShip.Heading, _predictor.Heading, 3);
        Assert.Equal(_serverShip.Throttle, _predictor.Predicted!.Throttle);
    }

    [Fact]
    public void ConfirmedCommands_AreNotReplayedTwice()
    {
        _predictor.Record(1, new AdjustThrottleCommand(PlayerId, 2), _server.Tick + 1);
        _server.Enqueue(new AdjustThrottleCommand(PlayerId, 2));
        Step(5);

        // The snapshot already includes it (acked 1), so replaying it again would make throttle 5 + 2.
        var baseTick = _server.Tick;
        var (replica, state) = Snapshot();
        Predict(replica, state, baseTick, acked: 1, predictTick: baseTick + 4);

        Assert.Equal(0, _predictor.PendingCount);
        Assert.Equal(5, _predictor.Predicted!.Throttle);
    }

    [Fact]
    public void AnchorHold_IsPredicted_SoTheDropShowsOnTime()
    {
        var baseTick = _server.Tick;
        var (replica, state) = Snapshot();
        _predictor.Record(1, new AnchorKeyCommand(PlayerId, true), baseTick + 1);

        Predict(replica, state, baseTick, acked: 0, predictTick: baseTick + Anchoring.DropTicks - 1);
        Assert.Equal(AnchorState.Weighed, _predictor.Predicted!.Anchor);

        Predict(replica, state, baseTick, acked: 0, predictTick: baseTick + Anchoring.DropTicks + 1);
        Assert.Equal(AnchorState.Down, _predictor.Predicted!.Anchor);
    }

    [Fact]
    public void AMisprediction_IsCorrectedSmoothly_NotByAJump()
    {
        var baseTick = _server.Tick;
        var (replica, state) = Snapshot();
        var predictTick = baseTick + 10.0;
        Predict(replica, state, baseTick, 0, predictTick);

        // Something the client couldn't foresee: another ship (not in the replay) shoves ours sideways.
        Step(2);
        _serverShip.Position += new Vector2(0, 1.5f);
        Step(2);

        // The next snapshot shows it. The drawn ship carries on smoothly from the last frame...
        var before = _predictor.Position;
        (replica, state) = Snapshot();
        predictTick += Frame * SimConstants.TickRate;
        Predict(replica, state, _server.Tick, 0, predictTick);
        var step = Vector2.Distance(before, _predictor.Position);
        var unsmoothed = new LocalShipPredictor(Archipelago.Size);
        unsmoothed.Update(replica, state, _server.Tick, 0, _server.Wind, predictTick, Frame);
        var gap = Vector2.Distance(before, unsmoothed.Position);
        Assert.True(gap > 1f, $"the misprediction should be big enough to matter, was {gap} tiles");
        // The first frame takes about 1 - e^(-frame / time constant) = 15% of the gap, plus the ship's own motion.
        Assert.True(step < gap * 0.25f, $"drawn ship jumped {step} of {gap} tiles in one frame");

        // ...and converges on the truth within a few correction time constants.
        for (var i = 0; i < 40; i++)
        {
            predictTick += Frame * SimConstants.TickRate;
            Predict(replica, state, _server.Tick, 0, predictTick);
            unsmoothed.Update(replica, state, _server.Tick, 0, _server.Wind, predictTick, Frame);
        }
        Assert.True(Vector2.Distance(unsmoothed.Position, _predictor.Position) < 0.01f);
    }

    [Fact]
    public void ATeleport_Snaps()
    {
        var baseTick = _server.Tick;
        var (replica, state) = Snapshot();
        Predict(replica, state, baseTick, 0, baseTick + 3);

        _serverShip.Position += new Vector2(20, 0); // e.g. respawned elsewhere under the same id
        (replica, state) = Snapshot();
        Predict(replica, state, _server.Tick, 0, baseTick + 4);

        Assert.True(Vector2.Distance(_predictor.Position, state.Position) < 1f);
    }

    [Fact]
    public void StaleCommands_TheServerNeverConfirmed_AreDropped()
    {
        _predictor.Record(1, new SetRudderCommand(PlayerId, 1), _server.Tick + 1);
        Step(SimConstants.TickRate * 2);
        var (replica, state) = Snapshot();
        Predict(replica, state, _server.Tick, acked: 0, predictTick: _server.Tick + 3);
        Assert.Equal(0, _predictor.PendingCount);
    }

    [Fact]
    public void NonMovementCommands_AreNotReplayed()
    {
        _predictor.Record(1, new CastAbilityCommand(PlayerId, AbilitySlot.One, Vector2.Zero), _server.Tick + 1);
        _predictor.Record(2, new PurchaseUpgradeCommand(PlayerId, "speed"), _server.Tick + 1);
        Assert.Equal(0, _predictor.PendingCount);
    }
}
