using System;
using Microsoft.Xna.Framework;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Top-right, under the counters: the sea we're in and its level, how close the storm is, and, while any are about
/// or we're in the storm, its bounty hunters. Reads the director's <see cref="RunStatus"/>, which clients mirror from
/// the server.
/// </summary>
public sealed class RunForecast
{
    private const float RightMargin = 12f;
    private const float Gap = 16f;
    private const float LineGap = 6f;
    private const float PanelPadding = 6f;
    private const float Scale = 2f;

    /// <summary>The storm line turns urgent when the storm is this close behind (tiles).</summary>
    private const float StormWarningDistance = 30f;

    private static readonly Color Panel = new Color(12, 16, 24) * 0.75f;
    private static readonly Color SeaText = new(235, 235, 240);
    private static readonly Color StormText = new(170, 190, 225);
    private static readonly Color HunterText = new(240, 190, 120);
    private static readonly Color Urgent = new(240, 95, 80);

    private readonly PrimitiveBatch _batch;

    public RunForecast(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    /// <param name="here">Where we are (our ship, or the camera while we're sunk): what the storm's distance is measured from.</param>
    public void Draw(RunStatus status, Sea sea, NVector2 here, HudView hud)
    {
        _batch.Begin(hud.Transform);
        var right = hud.Viewport.Width - RightMargin;
        var top = HudCounters.Bottom + Gap;
        var step = PixelFont.Height(Scale) + 2 * PanelPadding + LineGap;

        DrawLine($"{sea.Name}  LV {sea.Level}", right, top, SeaText);

        var (storm, stormUrgent) = StormLine(status, here);
        DrawLine(storm, right, top + step, stormUrgent ? Urgent : StormText);

        var inStorm = status.TicksUntilStorm == 0 && here.Y > status.StormY;
        if (inStorm)
            DrawLine(status.Hunters == 0 ? "BOUNTY HUNTERS COMING" : $"BOUNTY HUNTERS - {Ships(status.Hunters)}", right, top + 2 * step, Urgent);
        else if (status.Hunters > 0)
            DrawLine($"BOUNTY HUNTERS IN THE STORM - {Ships(status.Hunters)}", right, top + 2 * step, HunterText);

        _batch.Flush();
    }

    private static (string Text, bool Urgent) StormLine(RunStatus s, NVector2 here)
    {
        if (s.TicksUntilStorm > 0)
            return ($"STORM BUILDING - MOVES IN {Clock(s.TicksUntilStorm)}", false);
        var behind = s.StormY - here.Y;
        if (behind <= 0f)
            return ("IN THE STORM - GET OUT NORTH", true);
        return ($"STORM {MathF.Ceiling(behind):0} TILES SOUTH", behind <= StormWarningDistance);
    }

    private static string Ships(int count) => count == 1 ? "1 SHIP" : $"{count} SHIPS";

    private static int Seconds(int ticks) => (int)Math.Ceiling(Math.Max(0, ticks) / (double)SimConstants.TickRate);

    /// <summary>M:SS, rounded up so it never shows 0:00 before it happens.</summary>
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
