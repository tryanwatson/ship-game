using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>World-anchored wave highlights and short, bounded trails recorded from interpolated ship movement.</summary>
public sealed class SeaVisuals
{
    private const float WakeLifetime = 2.4f;
    private const float SampleInterval = 0.08f;
    private const int MaxSamples = 32;
    private static readonly Color Foam = new(193, 231, 216);
    private static readonly Color Wave = new(98, 176, 175);
    private readonly PrimitiveBatch _batch;
    private readonly Dictionary<int, WakeTrack> _tracks = new();
    private readonly List<int> _expired = new();
    private World? _world;

    private readonly record struct WakeSample(NVector2 Position, float Time, float Strength);
    private sealed class WakeTrack
    {
        public readonly List<WakeSample> Samples = new(MaxSamples);
        public NVector2 LastPosition;
        public float LastSampleTime;
        public float LastSeen;
    }

    public SeaVisuals(PrimitiveBatch batch) => _batch = batch;

    public void DrawSurface(World world, Matrix view, float time)
    {
        // Only generate detail within the view. Positions stay fixed in the world as the camera pans and zooms.
        var inverse = Matrix.Invert(view);
        var viewport = _batch.Viewport;
        var min = Vector2.Transform(Vector2.Zero, inverse) - new Vector2(100, 60);
        var max = Vector2.Transform(new Vector2(viewport.Width, viewport.Height), inverse) + new Vector2(100, 60);
        for (var row = (int)MathF.Floor(min.Y / 52f); row <= (int)MathF.Ceiling(max.Y / 52f); row++)
        {
            for (var column = (int)MathF.Floor(min.X / 108f); column <= (int)MathF.Ceiling(max.X / 108f); column++)
            {
                var seed = Hash(column, row);
                if (seed < 0.28f)
                    continue;
                var at = new Vector2(column * 108f + Hash(row, column + 71) * 74f,
                    row * 52f + Hash(column + 19, row) * 35f);
                var worldAt = IsoProjection.IsoToWorld(at);
                if (worldAt.X < 1f || worldAt.Y < 1f || worldAt.X > world.WorldSize.X - 1f || worldAt.Y > world.WorldSize.Y - 1f)
                    continue;
                var phase = time * (0.65f + seed * 0.3f) + seed * MathF.Tau;
                at.Y += MathF.Sin(phase) * 1.6f;
                var opacity = 0.12f + 0.1f * (0.5f + 0.5f * MathF.Sin(phase));
                var width = 9f + seed * 20f;
                _batch.Stroke(at - new Vector2(width * 0.5f, 0), at + new Vector2(width * 0.12f, -1.4f), 1.2f, Wave * opacity);
                _batch.Stroke(at + new Vector2(width * 0.12f, -1.4f), at + new Vector2(width * 0.5f, 0), 0.9f, Wave * opacity);
                if (seed > 0.65f)
                {
                    _batch.Stroke(at + new Vector2(-width * 0.1f, 4), at + new Vector2(width * 0.32f, 4), 0.8f, Wave * (opacity * 0.6f));
                    _batch.FillEllipse(at + new Vector2(18, 10), new Vector2(62, 18), Wave * 0.025f);
                }
            }
        }
    }

    public void DrawShipWater(World world, float alpha, float time)
    {
        if (!ReferenceEquals(world, _world))
        {
            _world = world;
            _tracks.Clear();
        }
        foreach (var ship in world.Ships)
        {
            if (ship.IsFort)
                continue; // built on land: no wake, no shadow on the water
            var position = NVector2.Lerp(ship.PreviousPosition, ship.Position, alpha);
            var heading = Angles.Lerp(ship.PreviousHeading, ship.Heading, alpha);
            var forward = new NVector2(MathF.Cos(heading), MathF.Sin(heading));
            var side = new NVector2(-forward.Y, forward.X);
            var stern = position - forward * ship.Stats.Length * 0.45f;
            if (!_tracks.TryGetValue(ship.Id, out var track))
            {
                track = new WakeTrack { LastPosition = stern, LastSampleTime = time };
                _tracks.Add(ship.Id, track);
            }
            track.LastSeen = time;
            while (track.Samples.Count > 0 && time - track.Samples[0].Time > WakeLifetime)
                track.Samples.RemoveAt(0);
            var dt = time - track.LastSampleTime;
            if (dt < 0f || NVector2.DistanceSquared(stern, track.LastPosition) > 64f)
            {
                track.Samples.Clear(); // A new run, teleport, or authoritative correction must not draw a streak.
                track.LastPosition = stern;
                track.LastSampleTime = time;
            }
            else if (dt >= SampleInterval)
            {
                var speed = NVector2.Distance(stern, track.LastPosition) / dt;
                if (!ship.IsAnchored && speed > 0.12f)
                {
                    if (track.Samples.Count == MaxSamples)
                        track.Samples.RemoveAt(0);
                    track.Samples.Add(new WakeSample(stern, time, Math.Clamp(speed / ship.Stats.MaxSpeed, 0.05f, 1f)));
                }
                track.LastPosition = stern;
                track.LastSampleTime = time;
            }

            var center = IsoProjection.WorldToIso(position);
            _batch.FillEllipse(center + new Vector2(2, 3), new Vector2(25, 9), new Color(7, 35, 42) * 0.22f);
            var strength = ship.IsAnchored ? 0f : Math.Clamp(ship.Velocity.Length() / ship.Stats.MaxSpeed, 0f, 1f);
            if (strength > 0.04f)
            {
                var bow = position + forward * ship.Stats.Length * 0.48f;
                for (var sign = -1; sign <= 1; sign += 2)
                {
                    var shoulder = position + forward * ship.Stats.Length * 0.12f + side * sign * ship.Stats.Beam * 0.65f;
                    _batch.Stroke(IsoProjection.WorldToIso(bow), IsoProjection.WorldToIso(shoulder), 1.2f + strength, Foam * (strength * 0.7f));
                    _batch.FillEllipse(IsoProjection.WorldToIso(shoulder), new Vector2(3, 1.4f), Foam * (strength * 0.5f));
                }
            }
            else
            {
                // Quiet lapping water still grounds a stopped or anchored ship in the sea.
                var ripple = 0.6f + 0.1f * MathF.Sin(time * 1.5f + ship.Id);
                _batch.Stroke(IsoProjection.WorldToIso(position - forward * 0.6f + side * ripple),
                    IsoProjection.WorldToIso(position + forward * 0.3f + side * ripple), 0.9f, Foam * 0.17f);
            }
        }

        _expired.Clear();
        foreach (var (id, track) in _tracks)
        {
            DrawWake(track, time);
            if (time - track.LastSeen > WakeLifetime)
                _expired.Add(id);
        }
        foreach (var id in _expired)
            _tracks.Remove(id);
    }

    private void DrawWake(WakeTrack track, float time)
    {
        Span<Vector2> ribbon = stackalloc Vector2[4];
        for (var i = 1; i < track.Samples.Count; i++)
        {
            var a = track.Samples[i - 1];
            var b = track.Samples[i];
            var along = b.Position - a.Position;
            if (along.LengthSquared() < 1e-5f)
                continue;
            var side = NVector2.Normalize(new NVector2(-along.Y, along.X));
            var ageA = Math.Clamp((time - a.Time) / WakeLifetime, 0f, 1f);
            var ageB = Math.Clamp((time - b.Time) / WakeLifetime, 0f, 1f);
            var widthA = 0.2f + ageA * 0.8f;
            var widthB = 0.2f + ageB * 0.8f;
            var fade = (1f - ageA) * MathF.Min(a.Strength, b.Strength);
            ribbon[0] = IsoProjection.WorldToIso(a.Position + side * widthA);
            ribbon[1] = IsoProjection.WorldToIso(b.Position + side * widthB);
            ribbon[2] = IsoProjection.WorldToIso(b.Position - side * widthB);
            ribbon[3] = IsoProjection.WorldToIso(a.Position - side * widthA);
            _batch.FillConvex(ribbon, Foam * (fade * 0.1f));
            _batch.Stroke(ribbon[0], ribbon[1], 1.2f, Foam * (fade * 0.45f));
            _batch.Stroke(ribbon[3], ribbon[2], 1.2f, Foam * (fade * 0.45f));
            if (i % 3 == 0)
                _batch.FillEllipse(IsoProjection.WorldToIso(a.Position), new Vector2(2.5f, 1f), Foam * (fade * 0.28f));
        }
    }

    public void DrawShoreFoam(Island island, float time)
    {
        var outline = island.Outline;
        for (var i = 0; i < outline.Length; i++)
        {
            var start = outline[i];
            var end = outline[(i + 1) % outline.Length];
            var segments = Math.Max(1, (int)MathF.Ceiling(NVector2.Distance(start, end) / 0.7f));
            for (var j = 0; j < segments; j++)
            {
                var a = NVector2.Lerp(start, end, j / (float)segments);
                var b = NVector2.Lerp(start, end, (j + 0.72f) / segments);
                var pulse = 0.5f + 0.5f * MathF.Sin(time * 1.3f + island.Id + i * 0.8f + j * 0.35f);
                var outward = NVector2.Normalize((a + b) / 2f - island.Center) * (0.08f + pulse * 0.12f);
                _batch.Stroke(IsoProjection.WorldToIso(a + outward), IsoProjection.WorldToIso(b + outward),
                    1.4f + pulse * 0.9f, Foam * (0.3f + pulse * 0.23f));
            }
        }
    }

    private static float Hash(int x, int y)
    {
        var value = unchecked((uint)x * 374761393u + (uint)y * 668265263u);
        value = unchecked((value ^ (value >> 13)) * 1274126177u);
        return (value ^ (value >> 16)) / (float)uint.MaxValue;
    }
}
