using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using ShipGame.Client.Rendering;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Audio;

/// <summary>
/// The sea, put together live so it never comes round the same way twice. Three loops run underneath: a low wash
/// that breathes, wind that gusts, and water rushing past the hull as fast as our ship sails. Over them, events at
/// random intervals, each a random take at a random pitch, level and pan (and never the same take twice running):
/// waves lapping, bigger swells, the hull creaking (more as it heels into a turn, ropes as it lies at anchor), and
/// surf breaking on whatever shore the camera is near, from that side.
/// </summary>
public sealed class SeaAmbience : IDisposable
{
    // Each layer's level at full volume, before the ambience slider.
    private const float BedLevel = 0.5f;
    private const float WindLevel = 0.22f;
    private const float WashLevel = 0.5f;
    private const float LapLevel = 0.55f;
    private const float SwellLevel = 0.6f;
    private const float CreakLevel = 0.35f;
    private const float SurfLevel = 0.75f;

    // Surf is heard within this many world units of the shore, loudest within SurfNear.
    private const float SurfFar = 16f;
    private const float SurfNear = 3f;
    // Turning this fast (radians a second) or faster creaks the most.
    private const float HardTurn = 0.8f;

    private readonly IReadOnlyDictionary<Cue, SoundEffect[]> _sounds;
    private readonly Random _random = new();
    private readonly SoundEffectInstance _bed;
    private readonly SoundEffectInstance _wind;
    private readonly SoundEffectInstance _wash;
    private readonly Dictionary<Cue, int> _lastTake = new();
    private float _breath = 1f, _breathTarget = 1f;
    private float _gust = 0.5f, _gustTarget = 0.5f;
    private double _breathIn, _gustIn;
    private float _speed;
    private double _lapIn, _swellIn, _creakIn, _surfIn;

    public SeaAmbience(IReadOnlyDictionary<Cue, SoundEffect[]> sounds)
    {
        _sounds = sounds;
        _bed = Loop(Cue.SeaBed);
        _wind = Loop(Cue.Wind);
        _wash = Loop(Cue.BowWash);
        _lapIn = Between(0.5, 2);
        _swellIn = Between(3, 8);
        _creakIn = Between(4, 10);
        _surfIn = Between(1, 3);
    }

    private SoundEffectInstance Loop(Cue cue)
    {
        var instance = _sounds[cue][0].CreateInstance();
        instance.IsLooped = true;
        instance.Volume = 0f;
        instance.Play();
        return instance;
    }

    /// <param name="world">The sea we're listening to (the title menu has one too).</param>
    /// <param name="listener">Where the camera is looking, in iso space, and its zoom: surf is heard from there.</param>
    /// <param name="gain">The ambience slider, under the master.</param>
    public void Update(double elapsedSeconds, World world, int localPlayerId, Vector2 listener, float zoom, float gain)
    {
        var dt = Math.Clamp(elapsedSeconds, 0, 0.25);
        var ours = world.GetPlayerShip(localPlayerId);

        // The loops: the wash breathes, the wind gusts, both drifting towards new random levels every few seconds.
        Drift(ref _breath, ref _breathTarget, ref _breathIn, dt, 0.75f, 1f, 4, 9, 3.0);
        Drift(ref _gust, ref _gustTarget, ref _gustIn, dt, 0.25f, 1f, 3, 8, 2.0);
        var speed = ours is { Stats.MaxSpeed: > 0f } ? Math.Clamp(MathF.Abs(ours.Speed) / ours.Stats.MaxSpeed, 0f, 1f) : 0f;
        _speed += (speed - _speed) * (float)(1 - Math.Exp(-dt / 0.5));
        _bed.Volume = Math.Clamp(BedLevel * _breath * gain, 0f, 1f);
        _wind.Volume = Math.Clamp(WindLevel * _gust * gain, 0f, 1f);
        _wash.Volume = Math.Clamp(WashLevel * MathF.Pow(_speed, 1.3f) * gain, 0f, 1f);
        _wash.Pitch = -0.1f + 0.25f * _speed;
        if (gain <= 0.001f)
            return;

        // Laps: often when the ship lies still (the water slaps a hull that isn't moving), less under way, where the
        // wash covers them.
        if ((_lapIn -= dt) <= 0)
        {
            Play(Cue.Lap, LapLevel * Range(0.5f, 1f) * gain, Range(-0.7f, 0.7f), Range(-0.15f, 0.15f));
            var still = ours is null || ours.IsAnchored || _speed < 0.15f;
            _lapIn = still ? Between(1.5, 4) : Between(2.5, 5) * (1 + _speed * 0.6);
        }
        if ((_swellIn -= dt) <= 0)
        {
            Play(Cue.Swell, SwellLevel * Range(0.5f, 1f) * gain, Range(-0.4f, 0.4f), Range(-0.1f, 0.1f));
            _swellIn = Between(6, 14);
        }

        // Creaks are our ship's: none without one. A hard turn works the timbers; at anchor the ropes take the strain.
        if (ours is not null && (_creakIn -= dt) <= 0)
        {
            var turn = MathF.Min(1f, MathF.Abs(Angles.Delta(ours.PreviousHeading, ours.Heading)) / SimConstants.TickDelta / HardTurn);
            Play(Cue.Creak, CreakLevel * Range(0.6f, 1f) * (0.7f + 0.5f * turn) * gain, Range(-0.2f, 0.2f), Range(-0.2f, 0.2f));
            _creakIn = ours.IsAnchored ? Between(5, 12) : Between(7, 18) * (1 - 0.8 * turn);
        }

        // Surf: the nearest shore to where the camera's looking, from its side, louder the closer it is.
        if ((_surfIn -= dt) <= 0)
        {
            _surfIn = Between(2.5, 6);
            if (NearestShore(world, IsoProjection.IsoToWorld(listener)) is { } shore)
            {
                var distance = NVector2.Distance(shore, IsoProjection.IsoToWorld(listener));
                var reach = Math.Clamp(1f - (distance - SurfNear) / (SurfFar - SurfNear), 0f, 1f);
                if (reach > 0.05f)
                {
                    var across = (IsoProjection.WorldToIso(shore).X - listener.X) / (Camera.ReferenceWidth / 2f / zoom);
                    Play(Cue.Surf, SurfLevel * reach * reach * Range(0.6f, 1f) * gain, Math.Clamp(across * 0.7f, -0.8f, 0.8f), Range(-0.1f, 0.1f));
                }
            }
        }
    }

    /// <summary>Plays a take of <paramref name="cue"/>, never the one it played last.</summary>
    private void Play(Cue cue, float volume, float pan, float pitch)
    {
        var takes = _sounds[cue];
        var take = _random.Next(takes.Length);
        if (takes.Length > 1 && _lastTake.TryGetValue(cue, out var last) && take == last)
            take = (take + 1 + _random.Next(takes.Length - 1)) % takes.Length;
        _lastTake[cue] = take;
        if (volume > 0.001f)
            takes[take].Play(Math.Clamp(volume, 0f, 1f), pitch, pan);
    }

    /// <summary>A level easing towards a target that's picked afresh, between min and max, every so often.</summary>
    private void Drift(ref float value, ref float target, ref double nextIn, double dt, float min, float max,
        double minSeconds, double maxSeconds, double ease)
    {
        if ((nextIn -= dt) <= 0)
        {
            target = Range(min, max);
            nextIn = Between(minSeconds, maxSeconds);
        }
        value += (target - value) * (float)(1 - Math.Exp(-dt / ease));
    }

    /// <summary>The closest point on any island's shore to <paramref name="point"/>, or null if the sea has no islands.</summary>
    private static NVector2? NearestShore(World world, NVector2 point)
    {
        NVector2? best = null;
        var bestDistance = float.MaxValue;
        foreach (var island in world.Islands)
        {
            if (island.DistanceTo(point) > SurfFar + 1f)
                continue;
            var outline = island.Outline;
            for (var i = 0; i < outline.Length; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Length];
                var edge = b - a;
                var t = Math.Clamp(NVector2.Dot(point - a, edge) / MathF.Max(edge.LengthSquared(), 1e-6f), 0f, 1f);
                var onEdge = a + edge * t;
                var distance = NVector2.DistanceSquared(point, onEdge);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = onEdge;
                }
            }
        }
        return best;
    }

    private float Range(float min, float max) => min + _random.NextSingle() * (max - min);

    private double Between(double min, double max) => min + _random.NextDouble() * (max - min);

    public void Dispose()
    {
        _bed.Dispose();
        _wind.Dispose();
        _wash.Dispose();
    }
}
