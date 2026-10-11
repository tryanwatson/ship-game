using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework.Audio;
using NLayer;
using ShipGame.Shared.Ai;
using ShipGame.Shared.Simulation;

namespace ShipGame.Client.Audio;

/// <summary>
/// Background music: calm tracks while sailing, battle tracks in a fight, crossfading between them. Tracks are MP3s
/// shipped beside the game in <c>Music/</c>, named <c>calm-*.mp3</c> and <c>battle-*.mp3</c> (drop in more and
/// they join the rotation), and decoded as they play, so nothing goes through the content pipeline.
/// </summary>
/// <remarks>
/// A fight is the AI's own idea of one: a pirate ship or fort within <see cref="HunterBehavior.AggroRange"/> of us,
/// or an enemy's attack landing on us (a ball, a shell or a ram: not running aground, a fire, or a crewmate's stray
/// shot), which catches a pirate firing from further off. It's over once nothing hostile is within <see cref="HunterBehavior.DisengageRange"/>
/// for a few seconds, so a running fight doesn't flap between the two. The calm music picks up where it left off; each
/// fight starts the next battle track from the top.
/// </remarks>
public sealed class MusicPlayer : IDisposable
{
    private const double IntoBattleSeconds = 1.5;
    private const double OutOfBattleSeconds = 4;
    private const double CalmAfterSeconds = 6;

    private readonly Deck? _calm;
    private readonly Deck? _battle;
    private bool _inBattle;
    private double _quietFor;
    private bool _attacked;
    // Whose each shot and shell in flight is, from its launch: an impact event only says what it hit.
    private readonly Dictionary<int, Team> _shotTeams = new();
    private readonly Dictionary<int, (Team Team, float Radius)> _strikeTeams = new();

    private MusicPlayer(Deck? calm, Deck? battle)
    {
        _calm = calm;
        _battle = battle;
        if (_calm is not null)
            _calm.Gain = 1f;
    }

    /// <summary>The tracks in <paramref name="folder"/>, or null (and quiet) if there are none or they won't open.</summary>
    public static MusicPlayer? TryCreate(string folder)
    {
        try
        {
            var calm = Deck.TryCreate(Tracks(folder, "calm-"));
            var battle = Deck.TryCreate(Tracks(folder, "battle-"));
            return calm is null && battle is null ? null : new MusicPlayer(calm, battle);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"No music ({ex.Message}).");
            return null;
        }
    }

    private static string[] Tracks(string folder, string prefix) => Directory.Exists(folder)
        ? Directory.GetFiles(folder, prefix + "*.mp3").OrderBy(p => p, StringComparer.Ordinal).ToArray()
        : Array.Empty<string>();

    /// <summary>The player's volume sliders: the music slider (under the master) is read every update.</summary>
    public AudioLevels Levels { get; set; } = new();

    /// <summary>
    /// Whether we're in a fight, from <paramref name="world"/> (null at the title menu: calm). Call every frame;
    /// the crossfade follows.
    /// </summary>
    public void Watch(World? world, int localPlayerId, double elapsedSeconds)
    {
        var ours = world?.GetPlayerShip(localPlayerId);
        if (world is null || world.IsRunOver)
        {
            _inBattle = false;
        }
        else if (ours is not null)
        {
            if (_attacked || Hostile(world, ours, HunterBehavior.AggroRange))
            {
                _inBattle = true;
                _quietFor = 0;
            }
            else if (_inBattle && !Hostile(world, ours, HunterBehavior.DisengageRange))
            {
                _quietFor += elapsedSeconds;
                if (_quietFor >= CalmAfterSeconds)
                    _inBattle = false;
            }
            else
                _quietFor = 0;
        }
        // Sunk and waiting to respawn: the music stays where it was.
        _attacked = false;
    }

    /// <summary>Notes an enemy's attack landing on our ship among <paramref name="events"/>, for <see cref="Watch"/>.</summary>
    public void Hear(World world, IReadOnlyList<WorldEvent> events, int localPlayerId)
    {
        if (world.GetPlayerShip(localPlayerId) is not { } ours)
            return;
        foreach (var worldEvent in events)
        {
            switch (worldEvent)
            {
                case RegionEntered:
                    _shotTeams.Clear();
                    _strikeTeams.Clear();
                    break;
                case ProjectileSpawned shot:
                    if (_shotTeams.Count > 4096)
                        _shotTeams.Clear(); // balls that ran out of flight never say so: don't keep them for ever
                    _shotTeams[shot.ProjectileId] = shot.Team;
                    break;
                case AreaStrikeLaunched strike:
                    _strikeTeams[strike.StrikeId] = (strike.Team, strike.Radius);
                    break;
                case ProjectileImpact impact:
                    if (impact.ShipId == ours.Id && _shotTeams.TryGetValue(impact.ProjectileId, out var shotTeam) && shotTeam != ours.Team)
                        _attacked = true;
                    if (!impact.PassedThrough)
                        _shotTeams.Remove(impact.ProjectileId);
                    break;
                case AreaStrikeImpact impact:
                    if (_strikeTeams.Remove(impact.StrikeId, out var shell) && shell.Team != ours.Team
                        && System.Numerics.Vector2.Distance(impact.Target, ours.Position) <= shell.Radius + ours.Stats.Length / 2f)
                        _attacked = true;
                    break;
                case ShipRammed rammed when rammed.TargetShipId == ours.Id && world.FindShip(rammed.RammerShipId) is { } rammer
                    && rammer.Team != ours.Team:
                    _attacked = true;
                    break;
            }
        }
    }

    private static bool Hostile(World world, Ship ours, float range)
    {
        foreach (var ship in world.Ships)
        {
            if (ship.Team != ours.Team && ship.Health > 0 && System.Numerics.Vector2.DistanceSquared(ship.Position, ours.Position) <= range * range)
                return true;
        }
        return false;
    }

    /// <summary>Moves the crossfade on and keeps each playing deck fed; call every frame.</summary>
    public void Update(double elapsedSeconds)
    {
        var volume = Levels.Gain(Channel.Music);
        var battle = _inBattle && _battle is not null;
        var into = (float)(elapsedSeconds / IntoBattleSeconds);
        var outOf = (float)(elapsedSeconds / OutOfBattleSeconds);
        if (_battle is not null)
        {
            var wasSilent = _battle.Gain <= 0f;
            _battle.Gain = Math.Clamp(_battle.Gain + (battle ? into : -outOf), 0f, 1f);
            if (_battle.Gain <= 0f && !wasSilent)
                _battle.Next(); // the next fight starts on a fresh track, from the top
            _battle.Update(volume);
        }
        if (_calm is not null)
        {
            _calm.Gain = Math.Clamp(_calm.Gain + (battle ? -into : outOf), 0f, 1f);
            _calm.Update(volume);
        }
    }

    public void Dispose()
    {
        _calm?.Dispose();
        _battle?.Dispose();
    }

    /// <summary>
    /// One stream of music: its tracks in turn, each fading in, with a rest on the sea between them. Paused (where it
    /// is) while faded right out, so a silent deck costs nothing.
    /// </summary>
    private sealed class Deck : IDisposable
    {
        private const double FadeInSeconds = 2.5;
        private const double RestSeconds = 4;
        private const double ChunkSeconds = 0.1;
        private const int BuffersAhead = 3;

        private readonly IReadOnlyList<string> _tracks;
        private readonly DynamicSoundEffectInstance _output;
        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly float[] _decoded;
        private readonly byte[] _pcm;
        private MpegFile _file;
        private int _track;
        private long _framesPlayed;
        private long _restFramesLeft;

        private Deck(IReadOnlyList<string> tracks, MpegFile first)
        {
            _tracks = tracks;
            _file = first;
            _sampleRate = first.SampleRate;
            _channels = Math.Clamp(first.Channels, 1, 2);
            var frames = (int)(_sampleRate * ChunkSeconds);
            _decoded = new float[frames * _channels];
            _pcm = new byte[frames * _channels * 2];
            _output = new DynamicSoundEffectInstance(_sampleRate, _channels == 2 ? AudioChannels.Stereo : AudioChannels.Mono);
        }

        /// <summary>A deck of the <paramref name="tracks"/> that match the first one's format (one stream can't change rate).</summary>
        public static Deck? TryCreate(string[] tracks)
        {
            if (tracks.Length == 0)
                return null;
            var first = new MpegFile(tracks[0]);
            var playable = tracks.Where(path =>
            {
                using var file = new MpegFile(path);
                if (file.SampleRate == first.SampleRate && file.Channels == first.Channels)
                    return true;
                Console.WriteLine($"Skipping {Path.GetFileName(path)}: {file.SampleRate} Hz x{file.Channels}, not {first.SampleRate} Hz x{first.Channels} like the rest.");
                return false;
            }).ToArray();
            return new Deck(playable, first);
        }

        /// <summary>0..1: where this deck is in the crossfade.</summary>
        public float Gain { get; set; }

        public void Update(float volume)
        {
            if (Gain <= 0f)
            {
                if (_output.State == SoundState.Playing)
                    _output.Pause();
                return;
            }
            _output.Volume = Math.Clamp(volume * Gain * Gain, 0f, 1f); // squared: an even-sounding crossfade
            while (_output.PendingBufferCount < BuffersAhead)
                _output.SubmitBuffer(_pcm, 0, NextChunk() * 2);
            if (_output.State != SoundState.Playing)
                _output.Play(); // starts, or resumes from a pause
        }

        /// <summary>Drops what's queued and cues the next track from the top.</summary>
        public void Next()
        {
            _output.Stop();
            Open((_track + 1) % _tracks.Count);
        }

        private void Open(int track)
        {
            _file.Dispose();
            _track = track;
            _file = new MpegFile(_tracks[track]);
            _framesPlayed = 0;
            _restFramesLeft = 0;
        }

        /// <summary>Fills <see cref="_pcm"/> with the next chunk (music, or the rest between tracks); returns how many samples.</summary>
        private int NextChunk()
        {
            var samples = _decoded.Length;
            if (_restFramesLeft > 0)
            {
                Array.Clear(_decoded);
                _restFramesLeft -= samples / _channels;
                if (_restFramesLeft <= 0)
                    Open((_track + 1) % _tracks.Count);
            }
            else
            {
                var read = _file.ReadSamples(_decoded, 0, samples);
                if (read <= 0)
                {
                    _restFramesLeft = (long)(RestSeconds * _sampleRate);
                    Array.Clear(_decoded);
                }
                else
                {
                    Array.Clear(_decoded, read, samples - read);
                    var fadeFrames = FadeInSeconds * _sampleRate;
                    for (var i = 0; i < read; i++)
                    {
                        var frame = _framesPlayed + i / _channels;
                        if (frame < fadeFrames)
                            _decoded[i] *= (float)(frame / fadeFrames);
                    }
                    _framesPlayed += read / _channels;
                }
            }
            for (var i = 0; i < samples; i++)
            {
                var s = (short)Math.Clamp((int)(_decoded[i] * short.MaxValue), short.MinValue, short.MaxValue);
                _pcm[i * 2] = (byte)s;
                _pcm[i * 2 + 1] = (byte)(s >> 8);
            }
            return samples;
        }

        public void Dispose()
        {
            _output.Dispose();
            _file.Dispose();
        }
    }
}
