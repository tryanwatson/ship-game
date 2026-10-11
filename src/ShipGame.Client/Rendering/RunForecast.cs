using System;
using Microsoft.Xna.Framework;
using System.Linq;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Top-right, under the counters: where the crew is on the chart (act, stop, difficulty, level), what's to be done
/// there (the fortress to take, the boss on its way or hunting), and once it's done, the call to chart a course with
/// how many have voted. Reads the director, which clients mirror from the server.
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
    private static readonly Color ChartText = new(150, 215, 240);
    private static readonly Color Urgent = new(240, 95, 80);

    private readonly PrimitiveBatch _batch;

    public RunForecast(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    /// <summary>Where the lines end (HUD units from the top), for readouts stacked beneath them.</summary>
    public static float Bottom => HudCounters.Bottom + Gap + 3 * Step;

    private static float Step => PixelFont.Height(Scale) + 2 * PanelPadding + LineGap;

    public void Draw(World world, RunDirector director, HudView hud)
    {
        if (director.CurrentNode is not { } node)
            return;
        _batch.Begin(hud.Transform);
        var right = hud.Viewport.Width - RightMargin;
        var top = HudCounters.Bottom + Gap;

        DrawLine(Where(node), right, top, SeaText);
        if (Task(world, director, node) is var (task, color))
            DrawLine(task, right, top + Step, color);
        DrawLine($"{Count(director.FortressesTaken, "FORTRESS", "FORTRESSES")} TAKEN - BOSS {Math.Min(director.BossesSunk + 1, RunDirector.BossCount)} OF {RunDirector.BossCount}",
            right, top + 2 * Step, FortressText);

        _batch.Flush();
    }

    /// <summary>"ACT II - ROUGH FORTRESS - LV 4".</summary>
    public static string Where(ChartNode node)
    {
        var act = $"ACT {SeaChart.ActNumeral(node.Act)}";
        return node.Kind switch
        {
            NodeKind.Start => $"{act} - OPEN WATER",
            NodeKind.Fortress => $"{act} - {SeaChart.Name(node.Difficulty)} FORTRESS - LV {node.Level}",
            NodeKind.Port => $"{act} - PORT",
            _ => $"{act} - FLAGSHIP - LV {node.Level}",
        };
    }

    /// <summary>What there is to do here, or the call to vote once it's done.</summary>
    private static (string, Color)? Task(World world, RunDirector director, ChartNode node)
    {
        if (director.Cleared)
        {
            var votes = world.Players.Values.Count(p => p.CourseVote is not null);
            var voted = $"{votes}/{world.Players.Count} VOTED";
            if (votes == world.Players.Count && Plundering.UnderWay(world))
                return ("SETTING SAIL ONCE THE PLUNDERING'S DONE", ChartText);
            if (node.Kind == NodeKind.Port && world.Islands.FirstOrDefault(i => i.HasShipyard) is { } yard)
                return ($"ANCHOR AT {yard.Name} TO SHOP - TAB: SAIL ON ({voted})", ChartText);
            var loot = Plundering.LeftToPlunder(world).Count();
            if (loot > 0)
                return ($"{Count(loot, "ISLAND", "ISLANDS")} TO PLUNDER - TAB: SAIL ON ({voted})", ChartText);
            return ($"TAB: CHART YOUR COURSE - {voted}", ChartText);
        }
        if (node.Kind == NodeKind.Fortress)
        {
            // Not a count of the forts left: online, only the ones near someone are sent, so it would come up short.
            var name = world.Islands.FirstOrDefault(i => i.IsFortress)?.Name ?? "THE FORTRESS";
            return ($"TAKE {name} - SINK EVERY FORT", FortressText);
        }
        if (node.Kind == NodeKind.Boss)
        {
            var round = $"BOSS {node.Act} OF {RunDirector.BossCount}";
            if (director.BossAfloat)
                return ($"{round} IS HUNTING THE CREW", Urgent);
            if (director.BossCountdownTicks > 0)
                return ($"{round} ARRIVES IN {Clock(director.BossCountdownTicks)}", Urgent);
        }
        return null;
    }

    private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

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
