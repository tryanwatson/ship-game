using System;
using Microsoft.Xna.Framework;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Top-right, under the counters: when the next wave and the next raid arrive and how many pirates each brings, or,
/// while one is afloat (its countdown waits for it to be sunk), how many of it are left. Reads the director's <see cref="WaveStatus"/>, which clients
/// mirror from the server.
/// </summary>
public sealed class WaveForecast
{
    private const float RightMargin = 12f;
    private const float Gap = 16f;
    private const float LineGap = 6f;
    private const float PanelPadding = 6f;
    private const float Scale = 2f;

    /// <summary>The raid line turns urgent in its last this-many seconds.</summary>
    private const int WarningSeconds = 10;

    private static readonly Color Panel = new Color(12, 16, 24) * 0.75f;
    private static readonly Color WaveText = new(235, 235, 240);
    private static readonly Color RaidText = new(240, 190, 120);
    private static readonly Color Urgent = new(240, 95, 80);

    private readonly PrimitiveBatch _batch;

    public WaveForecast(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public void Draw(WaveStatus status, HudView hud)
    {
        _batch.Begin(hud.Transform);
        var right = hud.Viewport.Width - RightMargin;
        var top = HudCounters.Bottom + Gap;

        DrawLine(WaveLine(status), right, top, WaveText);
        var urgent = status.RaidersLeft == 0 && Seconds(status.TicksUntilNextRaid) <= WarningSeconds;
        DrawLine(RaidLine(status), right, top + PixelFont.Height(Scale) + 2 * PanelPadding + LineGap, urgent ? Urgent : RaidText);

        _batch.Flush();
    }

    private static string WaveLine(WaveStatus s)
    {
        if (s.WavePiratesLeft > 0)
            return $"WAVE {s.Wave} - {Pirates(s.WavePiratesLeft, "PIRATE")} LEFT";
        var which = s.Wave == 0 ? "FIRST WAVE" : $"WAVE {s.Wave + 1}";
        return $"{which} IN {Clock(s.TicksUntilNextWave)} - {Pirates(s.NextWaveSize, "PIRATE")}";
    }

    private static string RaidLine(WaveStatus s) => s.RaidersLeft > 0
        ? $"RAID {s.Raid} - {Pirates(s.RaidersLeft, "RAIDER")} LEFT"
        : $"RAID IN {Clock(s.TicksUntilNextRaid)} - {Pirates(s.NextRaidSize, "RAIDER")}";

    private static string Pirates(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}S";

    private static int Seconds(int ticks) => (int)Math.Ceiling(Math.Max(0, ticks) / (double)SimConstants.TickRate);

    /// <summary>M:SS, rounded up so it never shows 0:00 before the spawn.</summary>
    private static string Clock(int ticks)
    {
        var seconds = Seconds(ticks);
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    private void DrawLine(string text, float right, float top, Color color)
    {
        var width = PixelFont.Measure(text, Scale);
        var height = PixelFont.Height(Scale);
        var left = right - width - 2 * PanelPadding;
        Span<Vector2> panel = stackalloc Vector2[]
        {
            new(left, top), new(right, top), new(right, top + height + 2 * PanelPadding), new(left, top + height + 2 * PanelPadding),
        };
        _batch.FillConvex(panel, Panel);
        PixelFont.Draw(_batch, text, new Vector2(left + PanelPadding, top + PanelPadding), Scale, color);
    }
}
