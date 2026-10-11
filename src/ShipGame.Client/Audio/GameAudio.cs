using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using ShipGame.Client.Rendering;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Audio;

/// <summary>
/// The game's sound, driven by the same event stream as <see cref="CombatVisuals"/>. Sounds in the world are panned
/// and faded by where they are on (or off) the screen, and each kind is capped so a volley doesn't stack into a roar.
/// Without an audio device it's just quiet: <see cref="TryCreate"/> gives null.
/// </summary>
public sealed class GameAudio : IDisposable
{
    private readonly record struct Shot(NVector2 Position, NVector2 Velocity, long Tick, long ExpiryTick, bool Big)
    {
        public NVector2 At(long tick) => Position + Velocity * ((tick - Tick) * SimConstants.TickDelta);
    }
    private readonly record struct Pending(double At, Cue Cue, float Volume, float Pan, float Pitch);
    private readonly record struct Limit(int Count, double Seconds);

    private readonly Dictionary<Cue, SoundEffect[]> _sounds;
    private readonly SoundEffectInstance _sea;
    private readonly Random _random = new();
    private readonly List<Pending> _pending = new();
    private readonly Dictionary<Cue, List<double>> _recent = new();
    private readonly Dictionary<int, (NVector2 Position, bool IsFort)> _ships = new();
    private readonly Dictionary<int, Shot> _shots = new();
    private readonly HashSet<int> _live = new();
    private readonly HashSet<int> _seen = new();
    private readonly List<int> _gone = new();
    private World? _world;
    private int _region;
    private double _clock;
    private Vector2 _listener;
    private float _zoom = 1f;
    private float _volume = 1f;

    private GameAudio(Dictionary<Cue, SoundEffect[]> sounds)
    {
        _sounds = sounds;
        _sea = sounds[Cue.Sea][0].CreateInstance();
        _sea.IsLooped = true;
        _sea.Volume = BaseVolume(Cue.Sea);
        _sea.Play();
    }

    public static GameAudio? TryCreate()
    {
        try
        {
            var sounds = new Dictionary<Cue, SoundEffect[]>();
            foreach (var cue in Enum.GetValues<Cue>())
            {
                sounds[cue] = Enumerable.Range(0, SoundSynth.Takes(cue))
                    .Select(take => new SoundEffect(SoundSynth.ToPcm16(SoundSynth.Make(cue, take)), SoundSynth.SampleRate, AudioChannels.Mono))
                    .ToArray();
            }
            return new GameAudio(sounds);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"No sound ({ex.Message}).");
            return null;
        }
    }

    /// <summary>Sound effects volume (the sea's too), 0..1. The music has its own (see <see cref="MusicPlayer"/>).</summary>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            _sea.Volume = BaseVolume(Cue.Sea) * _volume;
        }
    }

    /// <summary>How loud each sound plays at full volume, relative to the rest.</summary>
    private static float BaseVolume(Cue cue) => cue switch
    {
        // Every sound comes out of SoundSynth at about the same loudness: this is the mix. Guns and hits lead, the
        // sea and the small stuff sit underneath, and the run's moments rise over the fight.
        Cue.Cannon => 0.45f,
        Cue.LongGun => 0.55f,
        Cue.MortarLaunch => 0.5f,
        Cue.Explosion => 0.68f,
        Cue.HullHit => 0.62f,
        Cue.ShoreHit => 0.42f,
        Cue.Splash => 0.3f,
        Cue.Sink => 0.65f,
        Cue.FortCollapse => 0.78f,
        Cue.Ram => 0.68f,
        Cue.Grounded => 0.45f,
        Cue.Fire => 0.35f,
        Cue.Coin => 0.3f,
        Cue.Plunder => 0.45f,
        Cue.Purchase => 0.45f,
        Cue.Card => 0.45f,
        Cue.CardsOffered => 0.45f,
        Cue.Click => 0.25f,
        Cue.Reject => 0.35f,
        Cue.Bell => 0.45f,
        Cue.Alarm => 0.6f,
        Cue.Fanfare => 0.55f,
        Cue.Victory or Cue.Defeat => 0.6f,
        Cue.Sea => 0.5f,
        _ => 0.5f,
    };

    /// <summary>At most this many of a sound start in this many seconds; the rest are dropped.</summary>
    private static Limit LimitFor(Cue cue) => cue switch
    {
        Cue.Cannon => new(6, 0.25),
        Cue.HullHit => new(5, 0.2),
        Cue.Splash or Cue.ShoreHit or Cue.Explosion => new(4, 0.25),
        Cue.Coin or Cue.Click => new(3, 0.15),
        _ => new(2, 0.3),
    };

    /// <summary>Where the camera is looking (iso space) and its zoom: the listener, for panning and distance.</summary>
    public void SetListener(Vector2 iso, float zoom)
    {
        _listener = iso;
        _zoom = zoom;
    }

    /// <summary>Notes where every ship is before the session advances: a sinking ship is gone by the time its event arrives.</summary>
    public void Capture(World world)
    {
        EnsureWorld(world);
        _ships.Clear();
        foreach (var ship in world.Ships)
            _ships[ship.Id] = (ship.Position, ship.IsFort);
    }

    public void HandleEvents(World world, IReadOnlyList<WorldEvent> events, int localPlayerId)
    {
        EnsureWorld(world);
        var localShip = world.GetPlayerShip(localPlayerId)?.Id;
        var plundered = events.Any(e => e is IslandPlundered p && p.PlayerId == localPlayerId);
        foreach (var worldEvent in events)
        {
            switch (worldEvent)
            {
                case ProjectileSpawned shot:
                    var big = shot.Radius > Projectile.DefaultRadius * 1.2f;
                    _shots[shot.ProjectileId] = new Shot(shot.Position, shot.Velocity, shot.Tick, shot.Tick + shot.LifetimeTicks, big);
                    // A broadside ripples down the hull rather than going off as one.
                    PlayAt(big ? Cue.LongGun : Cue.Cannon, ShipOr(shot.OwnerShipId, shot.Position), delay: big ? 0f : Random(0f, 0.09f));
                    break;
                case AbilityCast cast when world.FindShip(cast.ShipId) is { } caster && caster.GetAbility(cast.Slot)?.Definition is Mortar:
                    PlayAt(Cue.MortarLaunch, caster.Position);
                    break;
                case AreaStrikeImpact impact:
                    // Cluster bomblets are small: higher and quieter.
                    PlayAt(Cue.Explosion, impact.Target, Math.Clamp(0.5f + impact.Radius * 0.25f, 0.5f, 1f),
                        pitch: Math.Clamp((1.5f - impact.Radius) * 0.25f, -0.2f, 0.3f));
                    break;
                case ProjectileImpact impact:
                    var hasShot = _shots.TryGetValue(impact.ProjectileId, out var projectile);
                    if (!impact.PassedThrough && _shots.Remove(impact.ProjectileId))
                        _seen.Remove(impact.ProjectileId);
                    // A long gun's ball lands heavier: louder and lower. Taking a hit ourselves is louder still.
                    var weight = hasShot && projectile.Big ? 1.25f : 1f;
                    var drop = hasShot && projectile.Big ? -0.15f : 0f;
                    if (impact.ShipId is { } victim && _ships.TryGetValue(victim, out var hit))
                        PlayAt(hit.IsFort ? Cue.ShoreHit : Cue.HullHit, hit.Position, weight * (victim == localShip ? 1.3f : 1f), drop,
                            near: victim == localShip);
                    else if (hasShot)
                        PlayAt(Cue.ShoreHit, projectile.At(impact.Tick), weight, drop);
                    break;
                case ShipSunk sunk when _ships.TryGetValue(sunk.ShipId, out var lost):
                    PlayAt(lost.IsFort ? Cue.FortCollapse : Cue.Sink, lost.Position, near: sunk.ShipId == localShip);
                    break;
                case ShipRammed rammed when _ships.TryGetValue(rammed.TargetShipId, out var target):
                    PlayAt(Cue.Ram, target.Position);
                    break;
                case ShipGrounded grounded when _ships.TryGetValue(grounded.ShipId, out var aground):
                    PlayAt(Cue.Grounded, aground.Position);
                    break;
                case FireStarted fire:
                    PlayAt(Cue.Fire, fire.Position);
                    break;

                case GoldChanged gold when gold.PlayerId == localPlayerId && gold.Delta > 0 && !plundered:
                    Play(Cue.Coin);
                    break;
                case IslandPlundered plunder when plunder.PlayerId == localPlayerId:
                    Play(Cue.Plunder);
                    break;
                case UpgradePurchased bought when bought.ShipId == localShip:
                    Play(Cue.Purchase);
                    break;
                case SkillPurchased bought when bought.ShipId == localShip:
                    Play(Cue.Purchase);
                    break;
                case CardsOffered offered when offered.PlayerId == localPlayerId:
                    Play(Cue.CardsOffered);
                    break;
                case CardChosen chosen when chosen.PlayerId == localPlayerId:
                    Play(Cue.Card);
                    break;
                case CardsRerolled rerolled when rerolled.PlayerId == localPlayerId:
                    Play(Cue.Card);
                    break;
                case StartingWeaponChosen chosen when chosen.PlayerId == localPlayerId:
                    Play(Cue.Card);
                    break;
                case CommandRejected rejected when rejected.PlayerId == localPlayerId:
                    Play(Cue.Reject);
                    break;

                case RegionEntered:
                    Play(Cue.Bell);
                    break;
                case BossSpawned or BossPhaseChanged or ReliefFleetSighted:
                    Play(Cue.Alarm);
                    break;
                case FortressTaken:
                    Play(Cue.Fanfare);
                    break;
                case RunEnded ended:
                    Play(ended.Victory ? Cue.Victory : Cue.Defeat);
                    break;
            }
        }

        // A ball that runs out of flight with no impact has gone in the sea. (Only once it's been seen flying: online,
        // its spawn can arrive a moment before the ball itself.)
        _live.Clear();
        foreach (var p in world.Projectiles)
            _live.Add(p.Id);
        _gone.Clear();
        foreach (var (id, shot) in _shots)
        {
            if (_live.Contains(id))
                _seen.Add(id);
            else if (_seen.Contains(id) || world.Tick > shot.ExpiryTick)
                _gone.Add(id);
        }
        foreach (var id in _gone)
        {
            PlayAt(Cue.Splash, _shots[id].At(Math.Min(world.Tick, _shots[id].ExpiryTick)));
            _shots.Remove(id);
            _seen.Remove(id);
        }
    }

    /// <summary>Starts sounds whose delay is up.</summary>
    public void Update(double elapsedSeconds)
    {
        _clock += elapsedSeconds;
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            if (_pending[i].At > _clock)
                continue;
            var p = _pending[i];
            _pending.RemoveAt(i);
            Start(p.Cue, p.Volume, p.Pan, p.Pitch);
        }
    }

    /// <summary>A sound that isn't anywhere in particular: the interface, gold, the run's big moments.</summary>
    public void Play(Cue cue, float volume = 1f) => Start(cue, volume, 0f, 0f);

    /// <param name="near">Ours: full volume and centered wherever the camera is.</param>
    private void PlayAt(Cue cue, NVector2 world, float volume = 1f, float pitch = 0f, float delay = 0f, bool near = false)
    {
        var offset = IsoProjection.WorldToIso(world) - _listener;
        var x = offset.X / (Camera.ReferenceWidth / 2f / _zoom);
        var y = offset.Y / (Camera.ReferenceHeight / 2f / _zoom);
        // Full volume on screen, fading to silence two and a half screens out.
        var reach = MathF.Max(0f, 1f - MathF.Max(0f, MathF.Sqrt(x * x + y * y) - 0.8f) / 1.7f);
        var gain = near ? 1f : reach * reach;
        if (gain < 0.02f)
            return;
        var pan = near ? 0f : Math.Clamp(x * 0.7f, -0.9f, 0.9f);
        pitch += Random(-0.06f, 0.06f);
        if (delay > 0f)
            _pending.Add(new Pending(_clock + delay, cue, volume * gain, pan, pitch));
        else
            Start(cue, volume * gain, pan, pitch);
    }

    private void Start(Cue cue, float volume, float pan, float pitch)
    {
        var limit = LimitFor(cue);
        if (!_recent.TryGetValue(cue, out var recent))
            _recent[cue] = recent = new List<double>();
        recent.RemoveAll(at => _clock - at > limit.Seconds);
        if (recent.Count >= limit.Count)
            return;
        recent.Add(_clock);
        if (SoundSynth.IsTuned(cue))
            pitch = 0f; // in key, and staying there
        var takes = _sounds[cue];
        var level = volume * BaseVolume(cue) * _volume;
        if (level > 0.001f)
            takes[_random.Next(takes.Length)].Play(Math.Clamp(level, 0f, 1f), Math.Clamp(pitch, -1f, 1f), pan);
    }

    private NVector2 ShipOr(int shipId, NVector2 fallback) =>
        _ships.TryGetValue(shipId, out var ship) ? ship.Position : fallback;

    private float Random(float min, float max) => min + _random.NextSingle() * (max - min);

    private void EnsureWorld(World world)
    {
        // A new sea starts quiet: nothing in flight from the last one splashes down in this one.
        if (ReferenceEquals(world, _world) && _region == world.RegionsEntered)
            return;
        _world = world;
        _region = world.RegionsEntered;
        _shots.Clear();
        _seen.Clear();
        _pending.Clear();
    }

    public void Dispose()
    {
        _sea.Dispose();
        foreach (var takes in _sounds.Values)
        {
            foreach (var sound in takes)
                sound.Dispose();
        }
    }
}
