using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>Cosmetic combat feedback driven by the existing event stream, with bounded lifetimes and counts.</summary>
public sealed class CombatVisuals
{
    private const int MaxParticles = 768;
    private const int MaxRings = 64;
    private const int MaxWrecks = 16;
    private const float HitSeconds = 0.18f;
    private enum Kind { Flash, Smoke, Spray, Splinter, Spark }
    private readonly record struct Pose(Ship Ship, NVector2 Position, float Heading, float Health);
    private readonly record struct Shot(NVector2 Position, NVector2 Velocity, float Radius, long Tick);
    private record struct Particle(Kind Kind, NVector2 Position, NVector2 Velocity, float Height, float Lift,
        float Size, Color Color, float Life, float Age = 0f);
    private record struct Ring(NVector2 Position, float Radius, Color Color, float Life, float Age = 0f);
    public sealed class Wreck
    {
        public required Ship Ship { get; init; }
        public required NVector2 Position { get; init; }
        public required float Heading { get; init; }
        public float Age { get; set; }
        public float Progress => Math.Clamp(Age / 1.8f, 0f, 1f);
    }

    private readonly PrimitiveBatch _batch;
    private readonly List<Particle> _particles = new();
    private readonly List<Ring> _rings = new();
    private readonly List<Wreck> _wrecks = new();
    private readonly Dictionary<int, Pose> _poses = new();
    private readonly Dictionary<int, Shot> _shots = new();
    private readonly Dictionary<int, NVector2> _launchDirections = new();
    private readonly Dictionary<int, float> _hits = new();
    private readonly HashSet<int> _impacted = new();
    private readonly List<int> _expiredHits = new();
    private World? _world;
    private float _smokeClock;
    private int _seed;

    public CombatVisuals(PrimitiveBatch batch) => _batch = batch;
    public IReadOnlyList<Wreck> Wrecks => _wrecks;
    public float HitFlash(int shipId) => _hits.GetValueOrDefault(shipId) / HitSeconds;

    public void EnsureWorld(World world)
    {
        if (ReferenceEquals(world, _world))
            return;
        _world = world;
        _particles.Clear(); _rings.Clear(); _wrecks.Clear();
        _poses.Clear(); _shots.Clear(); _launchDirections.Clear(); _hits.Clear(); _impacted.Clear();
        _smokeClock = 0f;
        _seed = 0;
    }

    /// <summary>Capture before the session advances: impact/sinking events can refer to entities it removes.</summary>
    public void Capture(World world, float alpha)
    {
        EnsureWorld(world);
        _poses.Clear(); _shots.Clear(); _launchDirections.Clear(); _impacted.Clear();
        foreach (var ship in world.Ships)
            _poses[ship.Id] = new Pose(ship, NVector2.Lerp(ship.PreviousPosition, ship.Position, alpha),
                Angles.Lerp(ship.PreviousHeading, ship.Heading, alpha), ship.Health);
        foreach (var shot in world.Projectiles)
            _shots[shot.Id] = new Shot(NVector2.Lerp(shot.PreviousPosition, shot.Position, alpha), shot.Velocity, shot.Radius, world.Tick);
    }

    public void HandleEvents(World world, IReadOnlyList<WorldEvent> events)
    {
        EnsureWorld(world);
        foreach (var worldEvent in events)
        {
            switch (worldEvent)
            {
                case ProjectileSpawned shot:
                    _shots[shot.ProjectileId] = new Shot(shot.Position, shot.Velocity, shot.Radius, shot.Tick);
                    if (shot.Radius > Projectile.DefaultRadius * 1.2f)
                    {
                        var direction = SafeDirection(shot.Velocity);
                        var owner = world.FindShip(shot.OwnerShipId);
                        var muzzle = owner is null ? shot.Position : owner.Position + direction * owner.Stats.Beam / 2f;
                        Fire(muzzle, direction, 1.4f);
                    }
                    else if (TryPose(world, shot.OwnerShipId, out var gunner))
                    {
                        // A broadside ball, from its own muzzle along the hull (moved to where the ship's drawn, and on
                        // with it if it's arrived late), pointing where it was laid: its velocity less the way the ship gave it.
                        var carried = gunner.Ship.Forward * gunner.Ship.Speed;
                        var late = MathF.Max(0f, world.Tick - shot.Tick) * SimConstants.TickDelta;
                        Fire(shot.Position + carried * late + (gunner.Position - gunner.Ship.Position), SafeDirection(shot.Velocity - carried), 1f);
                    }
                    break;
                case AreaStrikeLaunched launch:
                    _launchDirections.TryAdd(launch.OwnerShipId, SafeDirection(launch.Target - launch.Origin));
                    break;
                case AbilityCast cast when TryPose(world, cast.ShipId, out var mortar)
                    && mortar.Ship.GetAbility(cast.Slot)?.Definition is Mortar:
                    // Cluster bomblets launch at the impact site without a cast; they must not flash the ship's gun.
                    Fire(mortar.Position, _launchDirections.GetValueOrDefault(cast.ShipId, mortar.Ship.Forward), 1.5f);
                    break;
                case ProjectileImpact impact:
                    _impacted.Add(impact.ProjectileId);
                    if (!_shots.TryGetValue(impact.ProjectileId, out var projectile))
                        break;
                    var position = ShotAt(projectile, impact.Tick);
                    if (impact.ShipId is { } id && TryPose(world, id, out var victim))
                    {
                        var f = new NVector2(MathF.Cos(victim.Heading), MathF.Sin(victim.Heading));
                        var side = new NVector2(-f.Y, f.X);
                        var offset = position - victim.Position;
                        position = victim.Position + f * Math.Clamp(NVector2.Dot(offset, f), -victim.Ship.Stats.Length * 0.45f, victim.Ship.Stats.Length * 0.45f)
                            + side * Math.Clamp(NVector2.Dot(offset, side), -victim.Ship.Stats.Beam * 0.5f, victim.Ship.Stats.Beam * 0.5f);
                        Hit(position, true);
                        _hits[id] = HitSeconds;
                    }
                    else
                        Hit(NearestShore(world, position), false);
                    break;
                case AreaStrikeImpact impact:
                    Explosion(impact.Target, impact.Radius);
                    break;
                case ShipGrounded grounded when TryPose(world, grounded.ShipId, out var ship):
                    Hit(ship.Position, true);
                    _hits[grounded.ShipId] = HitSeconds;
                    break;
                case ShipRammed rammed when TryPose(world, rammed.TargetShipId, out var rammedShip):
                    Hit(rammedShip.Position, true);
                    _hits[rammed.TargetShipId] = HitSeconds;
                    break;
                case ShipSunk sunk when TryPose(world, sunk.ShipId, out var lost) && lost.Ship.IsFort:
                    // A fort doesn't sink: it blows up, and the stones fly.
                    Explosion(lost.Position, 2.2f);
                    for (var i = 0; i < 10; i++)
                        Add(new Particle(Kind.Splinter, lost.Position, Direction() * (0.5f + Random()), 7, 22 + Random() * 24,
                            3.5f, new Color(150, 146, 128), 1.4f));
                    break;
                case ShipSunk sunk when TryPose(world, sunk.ShipId, out var lost):
                    var ghost = new Ship(lost.Ship.Id, lost.Ship.OwnerPlayerId, lost.Ship.Stats)
                    { Team = lost.Ship.Team, Throttle = 0 };
                    foreach (var lot in lost.Ship.Cargo)
                        ghost.LoadCargo(lot);
                    if (_wrecks.Count == MaxWrecks)
                        _wrecks.RemoveAt(0);
                    _wrecks.Add(new Wreck { Ship = ghost, Position = lost.Position, Heading = lost.Heading });
                    Splash(lost.Position, 1.7f);
                    for (var i = 0; i < 8; i++)
                        Add(new Particle(Kind.Splinter, lost.Position, Direction() * (0.4f + Random()), 5, 18 + Random() * 20,
                            3f, new Color(159, 113, 61), 1.4f));
                    break;
            }
        }
        // Natural expiry has no impact event: a missed cannonball leaves a small water splash.
        foreach (var (id, shot) in _shots)
        {
            if (!_impacted.Contains(id) && !world.Projectiles.Any(p => p.Id == id))
                Splash(ShotAt(shot, world.Tick), 0.45f);
        }
        foreach (var ship in world.Ships)
        {
            if (_poses.TryGetValue(ship.Id, out var old) && ship.Health < old.Health)
                _hits[ship.Id] = HitSeconds;
        }
    }

    public void Update(float elapsedSeconds)
    {
        var dt = Math.Clamp(elapsedSeconds, 0f, 0.25f);
        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += dt;
            if (p.Age >= p.Life) { _particles.RemoveAt(i); continue; }
            p.Position += p.Velocity * dt;
            p.Height = MathF.Max(0f, p.Height + p.Lift * dt);
            if (p.Kind is Kind.Spray or Kind.Splinter or Kind.Spark)
                p.Lift -= 90f * dt;
            _particles[i] = p;
        }
        for (var i = _rings.Count - 1; i >= 0; i--)
        {
            var ring = _rings[i]; ring.Age += dt;
            if (ring.Age >= ring.Life) _rings.RemoveAt(i); else _rings[i] = ring;
        }
        for (var i = _wrecks.Count - 1; i >= 0; i--)
        {
            _wrecks[i].Age += dt;
            if (_wrecks[i].Progress >= 1f) _wrecks.RemoveAt(i);
        }
        _expiredHits.Clear();
        foreach (var id in _hits.Keys)
            _expiredHits.Add(id);
        foreach (var id in _expiredHits)
        {
            var left = _hits[id] - dt;
            if (left <= 0f) _hits.Remove(id); else _hits[id] = left;
        }
        _smokeClock += dt;
        if (_smokeClock >= 0.28f && _world is { } world)
        {
            _smokeClock %= 0.28f;
            foreach (var ship in world.Ships)
            {
                if (ship.Health > 0 && ship.Health < ship.Stats.MaxHealth * 0.35f)
                    Add(new Particle(Kind.Smoke, ship.Position - ship.Forward * 0.5f, world.Wind * 0.22f,
                        ShipVisuals.DeckHeight + 3, 12, 3.8f, new Color(63, 66, 62), 1.3f));
            }
        }
    }

    public void DrawGround()
    {
        foreach (var ring in _rings)
        {
            var t = ring.Age / ring.Life;
            var radius = ring.Radius * (0.35f + 0.9f * t);
            var last = IsoProjection.WorldToIso(ring.Position + new NVector2(radius, 0));
            for (var i = 1; i <= 32; i++)
            {
                var angle = MathF.Tau * i / 32;
                var next = IsoProjection.WorldToIso(ring.Position + new NVector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
                _batch.Stroke(last, next, 1.5f * (1f - t) + 0.5f, ring.Color * ((1f - t) * 0.5f));
                last = next;
            }
        }
    }

    public void DrawAir()
    {
        foreach (var p in _particles.OrderBy(p => IsoProjection.Depth(p.Position)))
        {
            var t = p.Age / p.Life;
            var at = IsoProjection.WorldToIso(p.Position) - new Vector2(0, p.Height);
            var fade = (1f - t) * (1f - t);
            switch (p.Kind)
            {
                case Kind.Smoke:
                    var radius = p.Size * (0.8f + t * 2.5f);
                    _batch.FillEllipse(at, new Vector2(radius, radius * 0.8f), p.Color * ((1f - t) * 0.38f));
                    _batch.FillEllipse(at + new Vector2(radius * 0.5f, -radius * 0.2f), new Vector2(radius * 0.6f), p.Color * ((1f - t) * 0.2f));
                    break;
                case Kind.Flash:
                    _batch.FillEllipse(at, new Vector2(p.Size * (1f + t), p.Size * 0.65f), new Color(255, 146, 59) * (fade * 0.55f));
                    _batch.FillEllipse(at, new Vector2(p.Size * 0.6f, p.Size * 0.45f), p.Color * fade);
                    break;
                case Kind.Spray:
                    _batch.Stroke(at, at + new Vector2(-1, -p.Size * 2), 1.3f, p.Color * fade);
                    break;
                case Kind.Splinter:
                case Kind.Spark:
                    var direction = IsoProjection.WorldToIso(p.Velocity);
                    direction = direction.LengthSquared() > 1e-5f ? Vector2.Normalize(direction) : Vector2.UnitX;
                    _batch.Stroke(at, at - direction * p.Size, p.Kind == Kind.Spark ? 1.2f : 2f, p.Color * fade);
                    break;
            }
        }
    }

    public void Explosion(NVector2 position, float radius)
    {
        var scale = Math.Clamp(radius / Mortar.BlastRadius, 0.3f, 2.5f);
        AddRing(new Ring(position, radius, new Color(255, 192, 92), 0.6f));
        Add(new Particle(Kind.Flash, position, NVector2.Zero, 5, 0, 18 * scale, new Color(255, 234, 162), 0.2f));
        var land = _world is { } world && world.DistanceToLand(position) < 0.05f;
        for (var i = 0; i < 22; i++)
            Add(new Particle(land ? Kind.Splinter : Kind.Spray, position, Direction() * (1.3f + Random() * 2f) * scale,
                3, (28 + Random() * 38) * MathF.Sqrt(scale), (2 + Random() * 2) * scale,
                land ? new Color(196, 164, 112) : new Color(209, 237, 222), 0.8f));
        for (var i = 0; i < 5; i++)
            Add(new Particle(Kind.Smoke, position, Direction() * 0.4f * scale, 8, 15, (6 + Random() * 3) * scale,
                new Color(89, 85, 73), 1.1f));
    }

    private void Fire(NVector2 position, NVector2 direction, float scale)
    {
        Add(new Particle(Kind.Flash, position, direction * 0.7f, ShipVisuals.DeckHeight + 2, 0,
            5f * scale, new Color(255, 235, 163), 0.13f));
        for (var i = 0; i < 2; i++)
            Add(new Particle(Kind.Smoke, position + direction * (0.1f * i), direction * (0.5f + i * 0.2f),
                ShipVisuals.DeckHeight + 2, 7, 3f * scale, new Color(205, 202, 175), 0.7f));
    }

    private void Hit(NVector2 position, bool wood)
    {
        Add(new Particle(Kind.Flash, position, NVector2.Zero, 8, 0, 6, new Color(255, 215, 125), 0.12f));
        for (var i = 0; i < 8; i++)
            Add(new Particle(wood ? Kind.Splinter : Kind.Spark, position, Direction() * (0.6f + Random()),
                8, 16 + Random() * 25, 3 + Random() * 3,
                wood ? new Color(229, 178, 106) : new Color(237, 210, 156), 0.55f));
        Add(new Particle(Kind.Smoke, position, NVector2.Zero, 10, 9, 4, new Color(126, 115, 93), 0.6f));
    }

    private void Splash(NVector2 position, float scale)
    {
        AddRing(new Ring(position, scale, new Color(202, 233, 218), 0.8f));
        for (var i = 0; i < 6; i++)
            Add(new Particle(Kind.Spray, position, Direction() * (0.4f + Random()) * scale, 1, 20 + Random() * 20,
                1.5f + scale, new Color(211, 238, 226), 0.65f));
    }

    private bool TryPose(World world, int id, out Pose pose)
    {
        if (world.FindShip(id) is { } ship)
        {
            pose = new Pose(ship, ship.Position, ship.Heading, ship.Health);
            return true;
        }
        return _poses.TryGetValue(id, out pose);
    }

    private static NVector2 ShotAt(Shot shot, long tick) => shot.Position + shot.Velocity * Math.Clamp((tick - shot.Tick) * SimConstants.TickDelta, 0f, 0.25f);
    private static NVector2 SafeDirection(NVector2 direction) => direction.LengthSquared() > 1e-5f ? NVector2.Normalize(direction) : NVector2.UnitX;
    private static NVector2 NearestShore(World world, NVector2 position)
    {
        var closest = position;
        var distance = float.MaxValue;
        foreach (var island in world.Islands)
        {
            var outline = island.Outline;
            for (var i = 0; i < outline.Length; i++)
            {
                var a = outline[i]; var edge = outline[(i + 1) % outline.Length] - a;
                var point = a + edge * Math.Clamp(NVector2.Dot(position - a, edge) / MathF.Max(edge.LengthSquared(), 1e-5f), 0f, 1f);
                var d = NVector2.DistanceSquared(position, point);
                if (d < distance) { distance = d; closest = point; }
            }
        }
        return closest;
    }
    private void Add(Particle particle)
    {
        if (_particles.Count == MaxParticles) _particles.RemoveAt(0);
        _particles.Add(particle);
    }
    private void AddRing(Ring ring)
    {
        if (_rings.Count == MaxRings) _rings.RemoveAt(0);
        _rings.Add(ring);
    }
    private float Random()
    {
        _seed = unchecked(_seed * 1664525 + 1013904223);
        return (uint)_seed / (float)uint.MaxValue;
    }
    private NVector2 Direction()
    {
        var angle = Random() * MathF.Tau;
        return new NVector2(MathF.Cos(angle), MathF.Sin(angle));
    }
}
