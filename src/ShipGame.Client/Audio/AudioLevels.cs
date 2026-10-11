using System;

namespace ShipGame.Client.Audio;

/// <summary>What a volume slider controls. <see cref="Master"/> scales all the rest.</summary>
public enum Channel
{
    Master,
    Music,
    Combat,
    Ambience,
    Interface,
    Alerts,
}

/// <summary>
/// The volume sliders, as the player left them (0..1 each, saved in <see cref="ClientSettings"/>). One instance is
/// shared by the settings panel, <see cref="GameAudio"/> and <see cref="MusicPlayer"/>, so a slider takes effect as
/// it moves.
/// </summary>
public sealed class AudioLevels
{
    public float Master { get; set; } = 1f;
    public float Music { get; set; } = 0.65f;
    public float Combat { get; set; } = 1f;
    public float Ambience { get; set; } = 1f;
    public float Interface { get; set; } = 1f;
    public float Alerts { get; set; } = 1f;

    public float this[Channel channel]
    {
        get => channel switch
        {
            Channel.Master => Master,
            Channel.Music => Music,
            Channel.Combat => Combat,
            Channel.Ambience => Ambience,
            Channel.Interface => Interface,
            _ => Alerts,
        };
        set
        {
            var level = Math.Clamp(value, 0f, 1f);
            switch (channel)
            {
                case Channel.Master: Master = level; break;
                case Channel.Music: Music = level; break;
                case Channel.Combat: Combat = level; break;
                case Channel.Ambience: Ambience = level; break;
                case Channel.Interface: Interface = level; break;
                default: Alerts = level; break;
            }
        }
    }

    /// <summary>
    /// What a slider's position does to the sound: cubed. Ears hear loudness logarithmically, so a straight line
    /// crams all the change into the bottom of the slider (80% to 100% is barely 2 dB); cubed, equal steps sound like
    /// equal steps, over about 60 dB, and halfway is clearly quieter (-18 dB).
    /// </summary>
    public static float Loudness(float position)
    {
        var p = Math.Clamp(position, 0f, 1f);
        return p * p * p;
    }

    /// <summary>The gain to play <paramref name="channel"/> at: its slider, under the master's.</summary>
    public float Gain(Channel channel) =>
        Loudness(this[channel]) * (channel == Channel.Master ? 1f : Loudness(Master));

    /// <summary>The slider each sound answers to.</summary>
    public static Channel ChannelOf(Cue cue) => cue switch
    {
        Cue.SeaBed or Cue.Wind or Cue.BowWash or Cue.Lap or Cue.Swell or Cue.Creak or Cue.Surf => Channel.Ambience,
        Cue.Coin or Cue.Plunder or Cue.Purchase or Cue.Card or Cue.CardsOffered or Cue.Click or Cue.Reject => Channel.Interface,
        Cue.Bell or Cue.Alarm or Cue.Fanfare or Cue.Victory or Cue.Defeat => Channel.Alerts,
        _ => Channel.Combat,
    };

    /// <summary>What the settings panel calls <paramref name="channel"/>.</summary>
    public static string Name(Channel channel) => channel switch
    {
        Channel.Ambience => "SEA",
        _ => channel.ToString().ToUpperInvariant(),
    };
}
