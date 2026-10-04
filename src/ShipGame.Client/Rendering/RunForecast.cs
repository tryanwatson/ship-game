using System;
using Microsoft.Xna.Framework;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Top-right, under the counters: the waters we're in and their level, how many fortresses the crew has taken and
/// how many more until the next boss, and the boss itself once it's coming. Reads the director's
/// <see cref="RunStatus"/>, which clients mirror from the server.
/// </summary>
public sealed class RunForecast
{
    private const float RightMargin = 12f;
    private const float Gap = 16f;
    private const float LineGap = 6f;
    private const float PanelPadding = 6f;
    private const float Scale = 2f;

    private static readonly Color Panel = new Color(12, 16, 24) * 0.75f;
    private static readonly Color SeaText = new(235, 235, 240);
    private static readonly Color FortressText = new(225, 205, 160);
    private static readonly Color Urgent = new(240, 95, 80);

    private readonly PrimitiveBatch _batch;

    public RunForecast(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    /// <summary>Where the lines end (HUD units from the top), for readouts stacked beneath them.</summary>
    public static float Bottom => HudCounters.Bottom + Gap + 3 * Step;

    private static float Step => PixelFont.Height(Scale) + 2 * PanelPadding + LineGap;

    public void Draw(RunStatus status, Sea sea, HudView hud)
    {
        _batch.Begin(hud.Transform);
        var right = hud.Viewport.Width - RightMargin;
        var top = HudCounters.Bottom + Gap;

        DrawLine($"{sea.Name}  LV {sea.Level}", right, top, SeaText);
        DrawLine(FortressLine(status), right, top + Step, FortressText);
        if (BossLine(status) is { } boss)
            DrawLine(boss, right, top + 2 * Step, Urgent);

        _batch.Flush();
    }

    private static string FortressLine(RunStatus s)
    {
        var taken = s.FortressesTaken == 1 ? "1 FORTRESS TAKEN" : $"{s.FortressesTaken} FORTRESSES TAKEN";
        var next = s.FortressesForNextBoss;
        return next > s.FortressesTaken ? $"{taken} - BOSS AT {next}" : taken;
    }

    private static string? BossLine(RunStatus s)
    {
        var round = $"BOSS {s.BossesSpawned + (s.BossAfloat ? 0 : 1)} OF {RunDirector.BossCount}";
        if (s.BossAfloat)
            return $"{round} IS HUNTING THE CREW";
        if (s.BossCountdownTicks > 0)
            return $"{round} ARRIVES IN {Clock(s.BossCountdownTicks)}";
        return null;
    }

    /// <summary>M:SS, rounded up so it never shows 0:00 before it happens.</summary>
    private static string Clock(int ticks)
    {
        var seconds = (int)Math.Ceiling(Math.Max(0, ticks) / (double)SimConstants.TickRate);
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
