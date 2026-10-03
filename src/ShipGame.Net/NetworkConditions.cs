namespace ShipGame.Net;

/// <summary>
/// Simulated bad network for testing on a LAN or one machine (the client's --lag/--jitter/--loss options).
/// LiteNetLib's own simulation is compiled out of its release package, so <see cref="ClientConnection"/> does it
/// above the transport: every message in each direction is held for half of <see cref="LagMs"/> plus up to half of
/// <see cref="JitterMs"/>, and <see cref="LossPercent"/> of the unreliable ones (snapshots) are dropped. Reliable
/// messages are only delayed: dropping them here would lose them for good, since the transport has already acked them.
/// </summary>
public sealed record NetworkConditions(int LagMs = 0, int JitterMs = 0, int LossPercent = 0)
{
    public bool IsPerfect => LagMs <= 0 && JitterMs <= 0 && LossPercent <= 0;

    public override string ToString() => $"+{LagMs} ms lag, {JitterMs} ms jitter, {LossPercent}% snapshot loss";
}
