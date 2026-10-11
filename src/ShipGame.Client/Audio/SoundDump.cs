using System;
using System.IO;
using System.Text;

namespace ShipGame.Client.Audio;

/// <summary>Writes every take of every sound to WAV files, to listen to them outside the game (no audio device needed).</summary>
public static class SoundDump
{
    public static void Write(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var cue in Enum.GetValues<Cue>())
        {
            for (var take = 0; take < SoundSynth.Takes(cue); take++)
            {
                var samples = SoundSynth.Make(cue, take);
                var peak = 0f;
                foreach (var s in samples)
                    peak = float.IsFinite(s) ? MathF.Max(peak, MathF.Abs(s)) : float.NaN;
                var path = Path.Combine(folder, $"{cue}-{take + 1}.wav");
                WriteWav(path, SoundSynth.ToPcm16(samples));
                Console.WriteLine($"{path}  {samples.Length / (float)SoundSynth.SampleRate:0.00}s  peak {peak:0.00}");
            }
        }
    }

    private static void WriteWav(string path, byte[] pcm)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + pcm.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(SoundSynth.SampleRate);
        writer.Write(SoundSynth.SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(pcm.Length);
        writer.Write(pcm);
    }
}
