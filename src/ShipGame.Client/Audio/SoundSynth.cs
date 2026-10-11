using System;

namespace ShipGame.Client.Audio;

/// <summary>Every sound the game makes. Each has a few takes (see <see cref="SoundSynth.Takes"/>) so repeats don't sound canned.</summary>
public enum Cue
{
    Cannon,
    LongGun,
    MortarLaunch,
    Explosion,
    HullHit,
    ShoreHit,
    Splash,
    Sink,
    FortCollapse,
    Ram,
    Grounded,
    Fire,
    Coin,
    Plunder,
    Purchase,
    Card,
    CardsOffered,
    Click,
    Reject,
    Bell,
    Alarm,
    Fanfare,
    Victory,
    Defeat,
    // The sea (see SeaAmbience): three loops under everything, and events laid over them at random.
    SeaBed,
    Wind,
    BowWash,
    Lap,
    Swell,
    Creak,
    Surf,
}

/// <summary>
/// Builds the game's sounds from noise and tones at startup, the way the visuals are drawn from primitives: no asset
/// files. Mono floats at <see cref="SampleRate"/>, evened out in loudness; <see cref="GameAudio"/> sets the mix.
/// </summary>
/// <remarks>
/// The look is a toy diorama: flat-shaded wooden boats on a soft teal sea. The sound follows it. Everything is made of
/// three materials (wood that knocks, water that bubbles, air that puffs), with the highs rolled off and no hard
/// cracks; anything with a pitch is a marimba (a glockenspiel for coins) in D major pentatonic, so any two cues sound
/// right together; and everything shares one small room.
/// </remarks>
public static class SoundSynth
{
    public const int SampleRate = 44100;
    private const float Tau = MathF.PI * 2f;

    // D major pentatonic, two and a bit octaves: everything tuned sits on these.
    private const float B3 = 246.94f, D4 = 293.66f, E4 = 329.63f, Fs4 = 369.99f, A4 = 440f, B4 = 493.88f;
    private const float D5 = 587.33f, E5 = 659.26f, Fs5 = 739.99f, A5 = 880f, B5 = 987.77f, D6 = 1174.66f;

    /// <summary>How many different takes of <paramref name="cue"/> to make.</summary>
    public static int Takes(Cue cue) => cue switch
    {
        Cue.Cannon => 5,
        Cue.HullHit or Cue.Splash => 4,
        Cue.LongGun or Cue.Explosion or Cue.ShoreHit => 3,
        Cue.MortarLaunch or Cue.Sink or Cue.Ram or Cue.Grounded or Cue.Fire or Cue.Card => 2,
        Cue.Lap or Cue.Creak => 8,
        Cue.Swell or Cue.Surf => 5,
        _ => 1,
    };

    /// <summary>Whether <paramref name="cue"/> is tuned to the key, so mustn't be pitched up or down when played.</summary>
    public static bool IsTuned(Cue cue) => cue is Cue.Coin or Cue.Plunder or Cue.Purchase or Cue.Card or Cue.CardsOffered
        or Cue.Reject or Cue.Bell or Cue.Alarm or Cue.Fanfare or Cue.Victory or Cue.Defeat;

    public static float[] Make(Cue cue, int take)
    {
        var mix = new Mix(seed: (int)cue * 1000 + take);
        switch (cue)
        {
            case Cue.Cannon: Cannon(mix, heavy: false); break;
            case Cue.LongGun: Cannon(mix, heavy: true); break;
            case Cue.MortarLaunch: MortarLaunch(mix); break;
            case Cue.Explosion: Explosion(mix); break;
            case Cue.HullHit: HullHit(mix); break;
            case Cue.ShoreHit: ShoreHit(mix); break;
            case Cue.Splash: Splash(mix); break;
            case Cue.Sink: Sink(mix); break;
            case Cue.FortCollapse: FortCollapse(mix); break;
            case Cue.Ram: Ram(mix); break;
            case Cue.Grounded: Grounded(mix); break;
            case Cue.Fire: Fire(mix); break;
            case Cue.Coin: Coin(mix); break;
            case Cue.Plunder: Plunder(mix); break;
            case Cue.Purchase: Purchase(mix); break;
            case Cue.Card: Card(mix); break;
            case Cue.CardsOffered: CardsOffered(mix); break;
            case Cue.Click: Click(mix); break;
            case Cue.Reject: Reject(mix); break;
            case Cue.Bell: Bell(mix); break;
            case Cue.Alarm: Alarm(mix); break;
            case Cue.Fanfare: Fanfare(mix); break;
            case Cue.Victory: Victory(mix); break;
            case Cue.Defeat: Defeat(mix); break;
            case Cue.SeaBed: return SeaBed(mix);
            case Cue.Wind: return Wind(mix);
            case Cue.BowWash: return BowWash(mix);
            case Cue.Lap: Lap(mix); break;
            case Cue.Swell: Swell(mix); break;
            case Cue.Creak: Creak(mix); break;
            case Cue.Surf: Surf(mix); break;
        }
        return mix.Finish();
    }

    /// <summary>16-bit little-endian PCM, as <c>SoundEffect</c> and WAV files take it.</summary>
    public static byte[] ToPcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var s = (short)Math.Clamp((int)MathF.Round(samples[i] * short.MaxValue), short.MinValue, short.MaxValue);
            bytes[i * 2] = (byte)s;
            bytes[i * 2 + 1] = (byte)(s >> 8);
        }
        return bytes;
    }

    // The room everything shares, and how much of each kind of sound is in it.
    private const float WorldRoom = 0.18f;
    private const float InterfaceRoom = 0.12f;
    private const float MusicRoom = 0.28f;

    // ---------------------------------------------------------------- Guns: soft "pomf"s, more air than bang

    private static void Cannon(Mix m, bool heavy)
    {
        var k = heavy ? 1.5f : 1f;
        var low = (heavy ? 75f : 92f) * m.Random(0.9f, 1.1f);
        m.Tone(0f, 0.6f * k, t => low * (0.7f + 1.3f * MathF.Exp(-t / 0.02f)), Decay(0.11f * k, 0.003f));
        m.Noise(0f, 0.4f * k, Pass.Band, _ => heavy ? 260f : 320f, 1f, Decay(0.07f * k, 0.003f), 0.8f); // the thump small speakers can play
        m.Noise(0f, 0.6f * k, Pass.Low, t => 250f + (heavy ? 2000f : 1400f) * MathF.Exp(-t / 0.04f), 0.7f, Decay(0.1f * k, 0.004f), 0.9f);
        m.Noise(0f, 0.8f * k, Pass.Band, t => 550f + 400f * MathF.Exp(-t / 0.1f), 0.8f, Swell(0.012f, 0.18f * k), 0.5f); // the smoke
        m.Noise(0.02f, 1f * k, Pass.Low, _ => 160f, 0.7f, Swell(0.05f, 0.35f * k), 0.4f); // the boom carrying
        if (heavy)
            m.Noise(0f, 0.1f, Pass.Band, _ => 1600f, 1f, Decay(0.012f, 0.002f), 0.3f);
        m.Warm(3500f);
        m.Drive = 1.3f;
        m.Room = WorldRoom;
    }

    private static void MortarLaunch(Mix m)
    {
        // Hollow: a "thoonk" down a tube, falling in pitch.
        m.Tone(0f, 0.5f, t => 75f + 140f * MathF.Exp(-t / 0.06f), Decay(0.12f, 0.003f));
        m.Noise(0f, 0.4f, Pass.Band, t => 120f + 260f * MathF.Exp(-t / 0.15f), 5f, Decay(0.09f, 0.004f), 0.9f);
        m.Noise(0f, 0.5f, Pass.Low, t => 200f + 800f * MathF.Exp(-t / 0.03f), 0.7f, Decay(0.08f, 0.003f), 0.6f);
        m.Noise(0.03f, 0.9f, Pass.Low, _ => 100f, 0.7f, Swell(0.05f, 0.3f), 0.4f);
        m.Warm(3000f);
        m.Drive = 1.2f;
        m.Room = WorldRoom;
    }

    // ---------------------------------------------------------------- Landing: wood knocks, sand thuds, water bloops

    private static void Explosion(Mix m)
    {
        // A shell landing at sea: a rolling boom, and a column of water going up and pattering down.
        m.Punch(0f, m.Random(55f, 65f), 0.3f, 1.1f);
        m.Noise(0f, 1.5f, Pass.Low, t => 200f + 1500f * MathF.Exp(-t / 0.06f), 0.7f, Decay(0.26f, 0.004f), 1.1f);
        m.Noise(0f, 0.8f, Pass.Band, _ => 350f, 0.8f, Decay(0.15f, 0.004f), 0.8f);
        m.Noise(0f, 1.8f, Pass.Low, _ => 140f, 0.7f, Swell(0.06f, 0.6f), 0.45f);
        m.Noise(0.04f, 1.2f, Pass.Band, t => 400f + 1600f * Bump(t, 0.12f), 0.8f, Swell(0.03f, 0.35f), 0.5f);
        m.Noise(0.25f, 1.3f, Pass.Band, _ => 2200f, 0.7f, Swell(0.15f, 0.4f), 0.14f);
        for (var i = 0; i < 6; i++)
            m.Bubble(m.Random(0.3f, 1.1f), m.Random(400f, 900f), m.Random(0.1f, 0.2f));
        m.Warm(4000f);
        m.Drive = 1.4f;
        m.Room = WorldRoom;
    }

    private static void HullHit(Mix m)
    {
        var f = m.Random(150f, 210f);
        m.Punch(0f, m.Random(72f, 88f), 0.14f, 1f); // the weight of the ball
        m.Wood(0f, f, 0.9f, ring: 2.8f);
        m.Noise(0f, 0.3f, Pass.Band, _ => 450f, 1f, Decay(0.05f, 0.002f), 0.6f); // the hull taking it
        m.Noise(0f, 0.25f, Pass.Band, _ => 1100f, 0.9f, Decay(0.035f, 0.002f), 0.5f); // the crunch
        m.Clicks(0.01f, 0.12f, 5, Pass.Band, 2200f, 1.5f, 0.004f, 0.25f); // splinters
        for (var i = 0; i < 4; i++)
            m.Wood(m.Random(0.08f, 0.35f), m.Random(400f, 700f), m.Random(0.08f, 0.16f), ring: 0.6f); // bits landing on deck
        m.Warm(4500f);
        m.Drive = 1.4f;
        m.Room = WorldRoom * 0.8f;
    }

    private static void ShoreHit(Mix m)
    {
        m.Punch(0f, m.Random(65f, 78f), 0.16f, 1f);
        m.Noise(0f, 0.4f, Pass.Low, t => 300f + 700f * MathF.Exp(-t / 0.03f), 0.7f, Decay(0.1f, 0.003f), 0.9f);
        m.Noise(0f, 0.3f, Pass.Band, _ => 380f, 1f, Decay(0.07f, 0.002f), 0.7f);
        m.Noise(0.01f, 0.6f, Pass.Band, _ => 1800f, 0.7f, Swell(0.01f, 0.15f), 0.2f); // sand thrown up
        m.Clicks(0.02f, 0.25f, 6, Pass.Band, 900f, 1.2f, 0.006f, 0.3f); // pebbles
        for (var i = 0; i < 6; i++)
            m.Stone(0.1f + m.Random(0f, 0.4f), m.Random(350f, 650f), m.Random(0.06f, 0.14f)); // and coming back down
        m.Warm(3500f);
        m.Drive = 1.4f;
        m.Room = WorldRoom * 0.8f;
    }

    private static void Splash(Mix m)
    {
        var f = m.Random(280f, 480f);
        m.Tone(0f, 0.2f, t => f * (1f + 3f * t), Decay(0.05f, 0.002f), 0.8f); // the plunk
        m.Noise(0f, 0.5f, Pass.Band, t => 500f + 1400f * Bump(t, 0.04f), 0.9f, Swell(0.006f, 0.09f), 0.5f);
        var drops = 3 + (int)m.Random(0f, 3f);
        for (var i = 0; i < drops; i++)
            m.Bubble(m.Random(0.1f, 0.45f), m.Random(700f, 1500f), m.Random(0.08f, 0.18f));
        m.Warm(4000f);
        m.Room = WorldRoom;
    }

    private static void Sink(Mix m)
    {
        m.Tone(0f, 0.4f, t => 140f * (1f + 1.5f * t), Decay(0.15f, 0.003f), 0.6f);
        m.Noise(0f, 1.2f, Pass.Band, t => 350f + 1200f * Bump(t, 0.12f), 0.8f, Swell(0.02f, 0.3f), 0.8f);
        // The hull groaning as it goes...
        var wobble = m.Random(0f, Tau);
        m.Creak(0.15f, 1.2f, t => 45f + 10f * MathF.Sin(t * 4f + wobble), 380f, 3f, Swell(0.25f, 0.5f), 0.6f);
        m.Creak(1f, 1f, t => 60f - 25f * t, 300f, 3f, Swell(0.2f, 0.4f), 0.45f);
        // ...and glugging under, the gulps further apart and deeper as it fills.
        var at = 0.5f;
        var gap = 0.12f;
        var pitch = 320f;
        while (at < 3f)
        {
            var fade = 1f - (at - 0.5f) / 2.6f;
            m.Bubble(at, pitch * m.Random(0.85f, 1.15f), 0.5f * fade, size: 2.5f);
            at += gap * m.Random(0.7f, 1.3f);
            gap *= 1.12f;
            pitch *= 0.95f;
        }
        m.Noise(0f, 2.5f, Pass.Low, _ => 120f, 0.7f, Swell(0.1f, 0.8f), 0.4f);
        m.Warm(3000f);
        m.Room = WorldRoom * 1.2f;
    }

    private static void FortCollapse(Mix m)
    {
        m.Punch(0f, 52f, 0.38f, 1.1f);
        m.Noise(0f, 1.6f, Pass.Low, t => 200f + 1200f * MathF.Exp(-t / 0.07f), 0.7f, Decay(0.25f, 0.004f), 1.1f);
        m.Noise(0f, 1f, Pass.Band, _ => 300f, 0.8f, Decay(0.2f, 0.004f), 0.8f);
        m.Noise(0f, 2.5f, Pass.Low, _ => 130f, 0.7f, Swell(0.08f, 0.9f), 0.5f);
        // The stones coming down, each a dull knock, fewer and quieter as it settles.
        for (var i = 0; i < 14; i++)
        {
            var at = 0.1f + MathF.Pow(m.Random(0f, 1f), 1.6f) * 1.5f;
            m.Stone(at, m.Random(200f, 450f), 0.6f * (1f - at / 1.8f));
        }
        m.Warm(3000f);
        m.Drive = 1.4f;
        m.Room = WorldRoom * 1.2f;
    }

    private static void Ram(Mix m)
    {
        m.Punch(0f, m.Random(62f, 72f), 0.2f, 1f);
        m.Wood(0f, m.Random(130f, 160f), 1f, ring: 3.5f);
        m.Noise(0f, 0.4f, Pass.Band, _ => 800f, 1f, Decay(0.06f, 0.002f), 0.8f);
        m.Clicks(0.01f, 0.2f, 7, Pass.Band, 2000f, 1.5f, 0.004f, 0.3f);
        m.Creak(0.08f, 0.6f, _ => 50f + m.Random(-3f, 3f), 350f, 3f, Swell(0.05f, 0.3f), 0.5f);
        m.Warm(4000f);
        m.Drive = 1.4f;
        m.Room = WorldRoom;
    }

    private static void Grounded(Mix m)
    {
        var grain = new float[64];
        for (var i = 0; i < grain.Length; i++)
            grain[i] = m.Random(0.4f, 1f);
        float Grain(float t) => grain[Math.Min((int)(t * 60f), grain.Length - 1)];
        m.Tone(0f, 0.3f, t => 80f + 30f * MathF.Exp(-t / 0.03f), Decay(0.08f, 0.003f), 0.5f);
        m.Noise(0f, 0.8f, Pass.Band, _ => 700f, 0.9f, t => Swell(0.04f, 0.35f)(t) * Grain(t), 0.6f); // keel over sand
        m.Creak(0.05f, 0.6f, _ => 70f + m.Random(-8f, 8f), 320f, 3f, Swell(0.05f, 0.3f), 0.45f);
        m.Warm(3000f);
        m.Room = WorldRoom;
    }

    private static void Fire(Mix m)
    {
        m.Noise(0f, 1.1f, Pass.Band, t => 250f + 950f * MathF.Min(1f, t / 0.3f), 0.8f, Swell(0.15f, 0.4f), 0.7f);
        m.Noise(0f, 1f, Pass.Low, _ => 300f, 0.7f, Swell(0.06f, 0.4f), 0.4f);
        m.Clicks(0.1f, 0.8f, 10, Pass.Band, 1800f, 1f, 0.003f, 0.15f); // crackle
        m.Warm(3500f);
        m.Room = WorldRoom;
    }

    // ---------------------------------------------------------------- Gold, cards and the interface: marimba and glockenspiel

    private static void Coin(Mix m)
    {
        m.Glock(0f, A5, 0.6f);
        m.Glock(0.06f, D6, 0.7f);
        m.Room = InterfaceRoom;
    }

    private static void Plunder(Mix m)
    {
        var notes = new[] { D5, E5, Fs5, A5, B5, D6 };
        for (var i = 0; i < notes.Length; i++)
            m.Glock(i * 0.045f, notes[i], 0.5f);
        m.Glock(0.3f, D6, 0.6f);
        m.Glock(0.3f, A5, 0.35f);
        m.Room = MusicRoom;
    }

    private static void Purchase(Mix m)
    {
        m.Wood(0f, 900f, 0.4f); // the till
        m.Marimba(0.03f, A4, 0.6f);
        m.Marimba(0.11f, D5, 0.7f);
        m.Room = InterfaceRoom;
    }

    private static void Card(Mix m)
    {
        m.Noise(0f, 0.15f, Pass.Band, t => 900f + 1500f * t / 0.12f, 0.8f, Swell(0.03f, 0.05f), 0.4f); // the card sliding out
        var second = m.Random(0f, 1f) < 0.5f ? Fs5 : A5;
        m.Marimba(0.04f, D5, 0.6f);
        m.Marimba(0.1f, second, 0.55f);
        m.Warm(5000f);
        m.Room = InterfaceRoom;
    }

    private static void CardsOffered(Mix m)
    {
        var notes = new[] { D4, Fs4, A4, D5 };
        for (var i = 0; i < notes.Length; i++)
            m.Marimba(i * 0.06f, notes[i], 0.55f);
        m.Room = MusicRoom;
    }

    private static void Click(Mix m)
    {
        m.Wood(0f, 1400f, 0.6f, ring: 0.5f);
        m.Warm(6000f);
        m.Room = 0.05f;
    }

    private static void Reject(Mix m)
    {
        // Two muted low notes, down: "nuh-uh", not an error buzzer.
        m.Marimba(0f, E4, 0.6f, length: 0.25f);
        m.Marimba(0.1f, B3, 0.6f, length: 0.25f);
        m.Room = InterfaceRoom;
    }

    // ---------------------------------------------------------------- The run's moments

    private static void Bell(Mix m)
    {
        m.ShipsBell(0f, D5, 0.6f);
        m.ShipsBell(0.3f, D5, 0.55f);
        m.Room = MusicRoom;
    }

    private static void Alarm(Mix m)
    {
        for (var i = 0; i < 3; i++)
            m.Drum(i * 0.42f, 0.8f - i * 0.1f);
        m.ShipsBell(0f, D4, 0.5f);
        m.ShipsBell(0.84f, D4, 0.5f);
        m.Marimba(0f, B3, 0.4f);
        m.Marimba(0.42f, D4, 0.4f);
        m.Marimba(0.84f, Fs4, 0.45f);
        m.Room = MusicRoom;
    }

    private static void Fanfare(Mix m)
    {
        var notes = new[] { D4, A4, D5, Fs5, A5 };
        for (var i = 0; i < notes.Length; i++)
            m.Marimba(i * 0.07f, notes[i], 0.55f);
        m.Glock(0.35f, D6, 0.4f);
        m.Marimba(0.35f, D5, 0.4f);
        m.Room = MusicRoom;
    }

    private static void Victory(Mix m)
    {
        var run = new[] { D4, E4, Fs4, A4, B4, D5, E5, Fs5, A5 };
        for (var i = 0; i < run.Length; i++)
            m.Marimba(i * 0.06f, run[i], 0.45f);
        // Then the chord, rolled the way a marimba holds a note: struck again and again, dying away.
        var start = run.Length * 0.06f;
        var chord = new[] { D5, Fs5, A5, D6 };
        for (var roll = 0; roll < 14; roll++)
        {
            var level = 0.5f * MathF.Exp(-roll / 6f);
            for (var n = 0; n < chord.Length; n++)
                m.Marimba(start + roll * 0.085f + n * 0.012f, chord[n], level * m.Random(0.8f, 1f));
        }
        m.Glock(start, D6, 0.4f);
        m.Drum(start, 0.6f);
        m.Room = MusicRoom;
    }

    private static void Defeat(Mix m)
    {
        var notes = new[] { Fs4, E4, D4, B3 };
        for (var i = 0; i < notes.Length; i++)
            m.Marimba(i * 0.3f, notes[i], 0.55f, length: i == notes.Length - 1 ? 1.5f : 1f);
        m.Drum(0.9f, 0.5f);
        m.Room = MusicRoom;
    }

    // ---------------------------------------------------------------- The sea
    //
    // Nothing here repeats on its own: the loops are featureless (a wash, wind, water past the hull), and everything
    // you'd notice twice (a lap, a swell, a creak, surf on a beach) is a separate event with several takes, which
    // SeaAmbience lays over the loops at random, in response to the game.

    /// <summary>Thirty seconds of low, slowly shifting wash: the floor everything else sits on.</summary>
    private static float[] SeaBed(Mix m)
    {
        const float loop = 30f;
        const float fade = 3f;
        var drift = m.Wander(loop + fade, 0.2f);
        var air = m.Wander(loop + fade, 0.15f);
        m.Noise(0f, loop + fade, Pass.Low, t => 190f + 140f * drift(t), 0.7f, t => 0.7f + 0.3f * drift(t), 0.5f);
        m.Noise(0f, loop + fade, Pass.Band, t => 480f + 220f * air(t), 0.6f, t => 0.3f + 0.7f * air(t), 0.1f);
        m.Warm(2500f);
        return Mix.Level(Seamless(m.Raw(), loop, fade), rms: 0.12f, peak: 0.6f);
    }

    /// <summary>Wind over open water, gusting: soft, airy, with a faint whistle when it's strong.</summary>
    private static float[] Wind(Mix m)
    {
        const float loop = 24f;
        const float fade = 3f;
        var gust = m.Wander(loop + fade, 0.35f);
        var tone = m.Wander(loop + fade, 0.25f);
        m.Noise(0f, loop + fade, Pass.Band, t => 450f + 550f * gust(t), 0.8f, t => 0.25f + 0.75f * gust(t), 0.5f);
        m.Noise(0f, loop + fade, Pass.Band, t => 850f + 350f * tone(t), 8f, t => gust(t) * gust(t) * gust(t), 0.12f);
        m.Warm(3500f);
        return Mix.Level(Seamless(m.Raw(), loop, fade), rms: 0.12f, peak: 0.6f);
    }

    /// <summary>Water rushing past the hull, churning and bubbling: played louder the faster the ship sails.</summary>
    private static float[] BowWash(Mix m)
    {
        const float loop = 8f;
        const float fade = 1f;
        var churn = m.Wander(loop + fade, 10f);
        var surge = m.Wander(loop + fade, 0.8f);
        m.Noise(0f, loop + fade, Pass.Band, t => 750f + 450f * surge(t), 0.7f, t => 0.35f + 0.65f * churn(t), 0.6f);
        m.Noise(0f, loop + fade, Pass.Low, _ => 330f, 0.7f, t => 0.6f + 0.4f * surge(t), 0.5f);
        m.Warm(4000f);
        var wash = Seamless(m.Raw(), loop, fade);

        // Bubbles laid round the loop, so one near the end carries on from the start.
        var bubbles = new Mix(m.Seed + 1);
        for (var i = 0; i < 50; i++)
            bubbles.Bubble(bubbles.Random(0f, loop), bubbles.Random(500f, 1500f), bubbles.Random(0.03f, 0.08f));
        var layer = bubbles.Raw();
        for (var i = 0; i < layer.Length; i++)
            wash[i % wash.Length] += layer[i];
        return Mix.Level(wash, rms: 0.12f, peak: 0.6f);
    }

    /// <summary>A small wave slapping the hull: up, a slap at the top, and a few bubbles as it drains away.</summary>
    private static void Lap(Mix m)
    {
        var rise = m.Random(0.2f, 0.45f);
        var fall = m.Random(0.3f, 0.6f);
        var pitch = m.Random(0.8f, 1.25f);
        float Shape(float t) => t < rise ? Smooth(t / rise) : MathF.Exp(-(t - rise) / fall);
        m.Noise(0f, rise + fall * 6f, Pass.Low, t => (250f + 700f * Shape(t)) * pitch, 0.7f, Shape, 0.7f);
        m.Noise(MathF.Max(0f, rise - 0.02f), 0.5f, Pass.Band, _ => m.Random(500f, 900f) * pitch, 1.2f, Swell(0.01f, 0.08f), 0.35f);
        var drops = 3 + (int)m.Random(0f, 4f);
        for (var i = 0; i < drops; i++)
            m.Bubble(rise + m.Random(0.02f, 0.5f), m.Random(600f, 1400f), m.Random(0.05f, 0.12f));
        m.Warm(3500f);
        m.Room = WorldRoom * 0.5f;
    }

    /// <summary>A bigger wave going by: a slow rise and fall, breaking a little at the crest.</summary>
    private static void Swell(Mix m)
    {
        var rise = m.Random(0.9f, 1.8f);
        var fall = m.Random(0.9f, 1.4f);
        float Shape(float t) => t < rise ? Smooth(t / rise) : MathF.Exp(-(t - rise) / fall);
        m.Noise(0f, rise + fall * 4f, Pass.Low, t => 250f + 850f * Shape(t), 0.7f, Shape, 0.8f);
        m.Noise(rise - 0.1f, 1f, Pass.Band, _ => m.Random(650f, 950f), 0.9f, Swell(0.06f, 0.25f), 0.3f); // the crest
        var drops = 4 + (int)m.Random(0f, 5f);
        for (var i = 0; i < drops; i++)
            m.Bubble(rise + m.Random(0f, 0.8f), m.Random(550f, 1300f), m.Random(0.04f, 0.09f));
        m.Warm(3000f);
        m.Room = WorldRoom * 0.5f;
    }

    /// <summary>The ship's timbers or rigging under strain: a low groan, or a higher rope creak, sometimes twice.</summary>
    private static void Creak(Mix m)
    {
        var rope = m.Random(0f, 1f) < 0.35f;
        var times = m.Random(0f, 1f) < 0.3f ? 2 : 1;
        var at = 0f;
        for (var i = 0; i < times; i++)
        {
            var length = rope ? m.Random(0.25f, 0.5f) : m.Random(0.5f, 1.1f);
            var rate = rope ? m.Random(90f, 160f) : m.Random(35f, 60f);
            var glide = m.Random(-0.4f, 0.4f) * rate;
            var hz = rope ? m.Random(700f, 1100f) : m.Random(280f, 450f);
            m.Creak(at, length, t => rate + glide * t / length, hz, rope ? 5f : 3.5f,
                Swell(length * m.Random(0.2f, 0.4f), length * 0.35f), rope ? 0.35f : 0.5f);
            at += length * m.Random(0.7f, 1.1f);
        }
        m.Warm(3000f);
        m.Room = WorldRoom * 0.4f;
    }

    /// <summary>A wave breaking on a beach: the rush in, the crash, and the long fizzing hiss as it draws back.</summary>
    private static void Surf(Mix m)
    {
        var rise = m.Random(0.9f, 1.4f);
        var back = m.Random(2.4f, 3.4f);
        m.Noise(0f, rise + 0.1f, Pass.Low, t => 300f + 1300f * Smooth(MathF.Min(1f, t / rise)),
            0.7f, t => Smooth(MathF.Min(1f, t / rise)) * MathF.Min(1f, (rise + 0.1f - t) / 0.1f), 0.6f);
        m.Noise(rise, 1.2f, Pass.Low, t => 400f + 2400f * MathF.Exp(-t / 0.15f), 0.7f, Decay(0.35f, 0.02f), 1f); // the crash
        m.Noise(rise, 1.5f, Pass.Low, _ => 130f, 0.7f, Swell(0.03f, 0.4f), 0.5f);
        m.Noise(rise + 0.1f, back, Pass.Band, t => 2100f - 800f * t / back, 0.7f, Swell(0.2f, back * 0.35f), 0.45f); // drawing back
        m.Clicks(rise + 0.2f, back * 0.8f, 60, Pass.Band, 2400f, 1f, 0.002f, 0.12f); // the fizz on the sand
        for (var i = 0; i < 15; i++)
            m.Bubble(rise + m.Random(0.1f, back * 0.7f), m.Random(700f, 1600f), m.Random(0.03f, 0.07f));
        m.Warm(4500f);
        m.Room = WorldRoom;
    }

    /// <summary>
    /// The first <paramref name="loop"/> seconds of <paramref name="raw"/>, with the <paramref name="fade"/> seconds
    /// after them faded into the start, so it plays round without a seam. Filter before this, not after: a filter
    /// starting afresh at the top would put the seam back.
    /// </summary>
    private static float[] Seamless(float[] raw, float loop, float fade)
    {
        Mix.HighPass(raw, 50f);
        var length = (int)(loop * SampleRate);
        var overlap = Math.Min((int)(fade * SampleRate), raw.Length - length);
        var looped = new float[length];
        Array.Copy(raw, looped, length);
        for (var i = 0; i < overlap; i++)
        {
            var w = (float)i / overlap;
            looped[i] = raw[i] * MathF.Sqrt(w) + raw[length + i] * MathF.Sqrt(1f - w);
        }
        return looped;
    }

    // ---------------------------------------------------------------- Building blocks

    /// <summary>A hit's envelope: up in <paramref name="attack"/> seconds, then dying away with time constant <paramref name="tau"/>.</summary>
    private static Func<float, float> Decay(float tau, float attack = 0.002f) =>
        t => MathF.Min(1f, t / attack) * MathF.Exp(-t / tau);

    /// <summary>A slower rise and fall, for rumbles and washes.</summary>
    private static Func<float, float> Swell(float attack, float tau) =>
        t => t < attack ? t / attack : MathF.Exp(-(t - attack) / tau);

    /// <summary>0 at t = 0, peaking at 1 at t = <paramref name="peak"/>, then falling.</summary>
    private static float Bump(float t, float peak) => t / peak * MathF.Exp(1f - t / peak);

    private static float Smooth(float x) => x * x * (3f - 2f * x);

    private enum Pass { Low, Band, High }

    /// <summary>A state-variable filter (topology-preserving: stable at any cutoff, so it can sweep).</summary>
    private struct Filter
    {
        private float _ic1, _ic2;

        public float Step(float x, Pass pass, float hz, float q)
        {
            var g = MathF.Tan(MathF.PI * Math.Clamp(hz, 20f, SampleRate * 0.45f) / SampleRate);
            var k = 1f / q;
            var a1 = 1f / (1f + g * (g + k));
            var a2 = g * a1;
            var a3 = g * a2;
            var v3 = x - _ic2;
            var v1 = a1 * _ic1 + a2 * v3;
            var v2 = _ic2 + a2 * _ic1 + a3 * v3;
            _ic1 = 2f * v1 - _ic1;
            _ic2 = 2f * v2 - _ic2;
            return pass switch
            {
                Pass.Low => v2,
                Pass.Band => k * v1,
                _ => x - k * v1 - v2,
            };
        }
    }

    /// <summary>A sound being put together: layers are added in, then <see cref="Finish"/> rooms, shapes and levels it.</summary>
    private sealed class Mix
    {
        private float[] _data = new float[SampleRate];
        private readonly Random _random;

        public Mix(int seed)
        {
            Seed = seed;
            _random = new Random(seed);
        }

        public int Seed { get; }

        /// <summary>Saturation applied when finishing (0 for none): rounds off peaks so booms sound full without clipping.</summary>
        public float Drive { get; set; }

        /// <summary>How much of the shared room to add when finishing (0 for none).</summary>
        public float Room { get; set; }

        public float Random(float min, float max) => min + _random.NextSingle() * (max - min);

        /// <summary>
        /// A smooth random wander between 0 and 1 over <paramref name="seconds"/>, through a new random point
        /// <paramref name="perSecond"/> times a second: for things that drift (wind, a wash) rather than cycle.
        /// </summary>
        public Func<float, float> Wander(float seconds, float perSecond)
        {
            var points = new float[(int)MathF.Ceiling(seconds * perSecond) + 2];
            for (var i = 0; i < points.Length; i++)
                points[i] = _random.NextSingle();
            return t =>
            {
                var x = Math.Clamp(t * perSecond, 0f, points.Length - 1.001f);
                var i = (int)x;
                var f = Smooth(x - i);
                return points[i] + (points[i + 1] - points[i]) * f;
            };
        }

        private Span<float> Span(float at, float seconds)
        {
            var start = (int)(at * SampleRate);
            var end = start + (int)(seconds * SampleRate);
            if (end > _data.Length)
                Array.Resize(ref _data, Math.Max(end, _data.Length * 2));
            return _data.AsSpan(start, end - start);
        }

        public void Tone(float at, float seconds, Func<float, float> hz, Func<float, float> amp, float gain = 1f)
        {
            var span = Span(at, seconds);
            var phase = 0f;
            for (var i = 0; i < span.Length; i++)
            {
                var t = (float)i / SampleRate;
                phase += Tau * hz(t) / SampleRate;
                if (phase > Tau)
                    phase -= Tau;
                span[i] += MathF.Sin(phase) * amp(t) * gain;
            }
        }

        /// <summary>A struck partial: <paramref name="hz"/> dying away with time constant <paramref name="tau"/>.</summary>
        public void Ring(float at, float hz, float tau, float gain, float attack = 0.0015f) =>
            Tone(at, tau * 6f, _ => hz, Decay(tau, attack), gain);

        /// <summary>The soft knock of a mallet: a few milliseconds of muffled noise, under the bar's ring.</summary>
        private void Mallet(float at, float hz, float gain) =>
            Noise(at, 0.03f, Pass.Low, _ => hz, 0.7f, Decay(0.004f, 0.0005f), gain);

        /// <summary>A marimba bar: the fundamental, a quiet overtone two octaves up, and a faint one above that.</summary>
        public void Marimba(float at, float hz, float gain, float length = 1f)
        {
            var tau = 0.4f * MathF.Sqrt(440f / hz) * length;
            Ring(at, hz, tau, gain, 0.003f);
            Ring(at, hz * 3.93f, tau * 0.25f, gain * 0.22f);
            Ring(at, hz * 9.2f, tau * 0.08f, gain * 0.05f);
            Mallet(at, MathF.Min(hz * 4f, 3000f), gain * 0.25f);
        }

        /// <summary>A glockenspiel bar: bright but rounded, for coins.</summary>
        public void Glock(float at, float hz, float gain)
        {
            Ring(at, hz, 0.5f, gain, 0.001f);
            Ring(at, hz * 2.71f, 0.12f, gain * 0.18f);
            Ring(at, hz * 5.15f, 0.04f, gain * 0.06f);
            Mallet(at, 4000f, gain * 0.15f);
        }

        /// <summary>A knock on wood: a short, low-pitched block, ringing for longer with a bigger <paramref name="ring"/>.</summary>
        public void Wood(float at, float hz, float gain, float ring = 1f)
        {
            Ring(at, hz, 0.035f * ring, gain);
            Ring(at, hz * 2.57f, 0.02f * ring, gain * 0.45f);
            Ring(at, hz * 4.2f, 0.012f * ring, gain * 0.2f);
            Ring(at, hz * 6.3f, 0.007f * ring, gain * 0.1f);
            Mallet(at, MathF.Min(hz * 5f, 4000f), gain * 0.4f);
        }

        /// <summary>Stone on stone: a dull, quickly damped knock.</summary>
        public void Stone(float at, float hz, float gain)
        {
            Ring(at, hz, 0.03f, gain);
            Ring(at, hz * 2.3f, 0.015f, gain * 0.4f);
            Noise(at, 0.08f, Pass.Low, _ => 1200f, 0.7f, Decay(0.012f, 0.001f), gain * 0.6f);
        }

        /// <summary>A ship's bell, softened: a hum an octave down and the bell's minor third, with the high partials damped early.</summary>
        public void ShipsBell(float at, float hz, float gain)
        {
            ReadOnlySpan<(float Ratio, float Tau, float Amp)> partials = stackalloc (float, float, float)[]
            {
                (0.5f, 1.4f, 0.3f), (1f, 1.1f, 1f), (1.19f, 0.7f, 0.3f), (1.5f, 0.5f, 0.2f), (2f, 0.4f, 0.25f), (2.5f, 0.2f, 0.08f),
            };
            foreach (var p in partials)
                Ring(at, hz * p.Ratio, p.Tau, gain * p.Amp, 0.002f);
            Mallet(at, 2500f, gain * 0.2f);
        }

        /// <summary>
        /// The weight under an impact: a low thump that drops in pitch as it lands, rounded towards a square so it has
        /// overtones. Small speakers can't play the thump itself, but they play those, and the ear fills in the rest.
        /// </summary>
        public void Punch(float at, float hz, float tau, float gain)
        {
            var span = Span(at, tau * 6f);
            var phase = 0f;
            const float drive = 2.5f;
            for (var i = 0; i < span.Length; i++)
            {
                var t = (float)i / SampleRate;
                phase += Tau * hz * (1f + 1.6f * MathF.Exp(-t / 0.012f)) / SampleRate;
                if (phase > Tau)
                    phase -= Tau;
                // The edge softens as it dies, so the tail is a clean low hum rather than a buzz.
                var edge = 1f + drive * MathF.Exp(-t / (tau * 0.5f));
                var envelope = MathF.Min(1f, t / 0.001f) * MathF.Exp(-t / tau);
                span[i] += MathF.Tanh(MathF.Sin(phase) * edge) / MathF.Tanh(edge) * envelope * gain;
            }
        }

        /// <summary>A low drum, felt more than heard.</summary>
        public void Drum(float at, float gain)
        {
            Tone(at, 0.8f, t => 80f + 40f * MathF.Exp(-t / 0.03f), Decay(0.2f, 0.003f), gain);
            Noise(at, 0.3f, Pass.Band, _ => 250f, 1.2f, Decay(0.06f, 0.002f), gain * 0.5f);
            Noise(at, 0.3f, Pass.Low, _ => 400f, 0.7f, Decay(0.04f, 0.002f), gain * 0.4f);
        }

        /// <summary>
        /// Filtered white noise. The level is evened out across cutoffs (a narrow or low band of noise is much quieter
        /// than the whole), so a gain of 1 is about as loud whatever the filter.
        /// </summary>
        public void Noise(float at, float seconds, Pass pass, Func<float, float> hz, float q, Func<float, float> amp, float gain = 1f)
        {
            var span = Span(at, seconds);
            var filter = new Filter();
            const float nyquist = SampleRate / 2f;
            for (var i = 0; i < span.Length; i++)
            {
                var t = (float)i / SampleRate;
                var cutoff = hz(t);
                var evenOut = pass switch
                {
                    Pass.Low => MathF.Sqrt(nyquist / MathF.Max(cutoff, 20f)),
                    Pass.Band => MathF.Sqrt(nyquist * q / MathF.Max(cutoff, 20f)),
                    _ => 1f,
                };
                span[i] += filter.Step(_random.NextSingle() * 2f - 1f, pass, cutoff, q) * evenOut * 0.5f * amp(t) * gain;
            }
        }

        /// <summary><paramref name="count"/> tiny filtered bursts at random moments: splinters, pebbles, crackle.</summary>
        public void Clicks(float at, float seconds, int count, Pass pass, float hz, float q, float tau, float gain)
        {
            for (var i = 0; i < count; i++)
            {
                var jitter = Random(0.7f, 1.4f);
                Noise(at + Random(0f, seconds), tau * 6f, pass, _ => hz * jitter, q, Decay(tau, 0.0005f), gain * Random(0.3f, 1f));
            }
        }

        /// <summary>A bubble: a short sine whose pitch rises as it closes. Bigger ones are lower and last longer.</summary>
        public void Bubble(float at, float hz, float gain, float size = 1f) =>
            Tone(at, 0.08f * size, t => hz * (1f + 6f * t / size), Decay(0.018f * size, 0.001f), gain);

        /// <summary>Wood under strain: stick-slip pulses at <paramref name="rate"/> per second, rung through a resonance.</summary>
        public void Creak(float at, float seconds, Func<float, float> rate, float hz, float q, Func<float, float> amp, float gain)
        {
            var span = Span(at, seconds);
            var filter = new Filter();
            var phase = 1f;
            for (var i = 0; i < span.Length; i++)
            {
                var t = (float)i / SampleRate;
                phase += rate(t) / SampleRate;
                var pulse = 0f;
                if (phase >= 1f)
                {
                    phase -= 1f;
                    pulse = Random(0.5f, 1f) * 40f;
                }
                span[i] += filter.Step(pulse, Pass.Band, hz, q) * amp(t) * gain;
            }
        }

        /// <summary>Rolls off the highs of everything so far (two gentle poles): the "soft" in the house sound.</summary>
        public void Warm(float hz)
        {
            var a = 1f - MathF.Exp(-Tau * hz / SampleRate);
            float y1 = 0f, y2 = 0f;
            for (var i = 0; i < _data.Length; i++)
            {
                y1 += a * (_data[i] - y1);
                y2 += a * (y1 - y2);
                _data[i] = y2;
            }
        }

        /// <summary>Takes out what's under <paramref name="hz"/> (two gentle poles).</summary>
        public static void HighPass(float[] samples, float hz)
        {
            var a = 1f - MathF.Exp(-Tau * hz / SampleRate);
            float y1 = 0f, y2 = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                y1 += a * (samples[i] - y1);
                var x2 = samples[i] - y1;
                y2 += a * (x2 - y2);
                samples[i] = x2 - y2;
            }
        }

        /// <summary>Everything written so far, unshaped (for layering into something bigger).</summary>
        public float[] Raw() => (float[])_data.Clone();

        /// <summary>Adds the room, saturates by <see cref="Drive"/>, trims the silence off the end and levels it.</summary>
        public float[] Finish()
        {
            if (Room > 0f)
                Reverb(Room);
            if (Drive > 0f)
            {
                var max = 0f;
                foreach (var s in _data)
                    max = MathF.Max(max, MathF.Abs(s));
                if (max > 0f)
                {
                    var norm = 1f / MathF.Tanh(Drive);
                    for (var i = 0; i < _data.Length; i++)
                        _data[i] = MathF.Tanh(_data[i] / max * Drive) * norm;
                }
            }
            var peak = 0f;
            foreach (var s in _data)
                peak = MathF.Max(peak, MathF.Abs(s));
            var length = _data.Length;
            while (length > 1 && MathF.Abs(_data[length - 1]) < peak * 0.002f)
                length--;
            var result = _data.AsSpan(0, length).ToArray();
            HighPass(result, 45f); // nothing plays below this, and it eats headroom
            var fade = Math.Min(length, (int)(0.01f * SampleRate));
            for (var i = 0; i < fade; i++)
                result[length - 1 - i] *= (float)i / fade;
            return Level(result);
        }

        /// <summary>
        /// Scales to a common loudness, so the mix in <see cref="GameAudio"/> is a choice rather than a correction:
        /// the loudest 50 ms comes to <paramref name="rms"/>, unless that would push a peak past <paramref name="peak"/>.
        /// Loudness is measured with the lows taken out, the way small speakers (and ears) hear it, so a boom isn't
        /// credited for bass nobody hears.
        /// </summary>
        public static float[] Level(float[] samples, float rms = 0.2f, float peak = 0.95f)
        {
            var heard = (float[])samples.Clone();
            HighPass(heard, 150f);
            var window = SampleRate / 20;
            var loudest = 0.0;
            var sum = 0.0;
            for (var i = 0; i < samples.Length; i++)
            {
                sum += heard[i] * heard[i];
                if (i >= window)
                    sum -= heard[i - window] * heard[i - window];
                loudest = Math.Max(loudest, sum / Math.Min(i + 1, window));
            }
            var top = 0f;
            foreach (var s in samples)
                top = MathF.Max(top, MathF.Abs(s));
            if (top <= 0f)
                return samples;
            var gain = MathF.Min(rms / (float)Math.Sqrt(loudest), peak / top);
            for (var i = 0; i < samples.Length; i++)
                samples[i] *= gain;
            return samples;
        }

        /// <summary>The room: a small Freeverb (eight damped combs into four all-passes), mixed in at <paramref name="wet"/>.</summary>
        private void Reverb(float wet)
        {
            ReadOnlySpan<int> combTunings = stackalloc[] { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
            ReadOnlySpan<int> allPassTunings = stackalloc[] { 556, 441, 341, 225 };
            const float feedback = 0.84f;
            const float damp = 0.45f;
            var dry = _data.Length;
            Span(0f, (float)dry / SampleRate + 1.8f);
            var combs = new float[8][];
            var stores = new float[8];
            var indices = new int[8];
            for (var c = 0; c < 8; c++)
                combs[c] = new float[combTunings[c]];
            var allPasses = new float[4][];
            var allPassIndices = new int[4];
            for (var a = 0; a < 4; a++)
                allPasses[a] = new float[allPassTunings[a]];
            for (var i = 0; i < _data.Length; i++)
            {
                var input = (i < dry ? _data[i] : 0f) * 0.015f;
                var output = 0f;
                for (var c = 0; c < 8; c++)
                {
                    var buffer = combs[c];
                    var y = buffer[indices[c]];
                    stores[c] = y * (1f - damp) + stores[c] * damp;
                    buffer[indices[c]] = input + stores[c] * feedback;
                    indices[c] = (indices[c] + 1) % buffer.Length;
                    output += y;
                }
                for (var a = 0; a < 4; a++)
                {
                    var buffer = allPasses[a];
                    var y = buffer[allPassIndices[a]];
                    buffer[allPassIndices[a]] = output + y * 0.5f;
                    allPassIndices[a] = (allPassIndices[a] + 1) % buffer.Length;
                    output = y - output;
                }
                _data[i] += output * wet * 3f;
            }
        }
    }
}
