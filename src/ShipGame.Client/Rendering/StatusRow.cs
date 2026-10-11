using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using ShipGame.Client.Input;
using ShipGame.Shared.Simulation;

namespace ShipGame.Client.Rendering;

/// <summary>
/// The local ship's buffs and debuffs, in a row centred over the ability bar: buffs first, then debuffs, each framed
/// green or red so good and bad read at a glance. Each shows its card's emblem in the status's color, darkens from the
/// top as it wears off, and carries its stacks in the corner. Hovering one explains it above the row: what it is,
/// what it's doing to the ship right now, its stacks, and how long it has left.
/// </summary>
public sealed class StatusRow
{
    private const float IconSize = 30f;
    private const float IconGap = 6f;
    private const float GroupGap = 14f;
    private const float AboveBar = 12f;
    private const float TipWidth = 270f;
    private const float TipPadding = 12f;
    private const float TipGap = 8f;
    private const float EmblemRadius = 16f;

    private static readonly Color IconBack = new Color(12, 16, 24) * 0.85f;
    private static readonly Color Elapsed = new Color(0, 0, 0) * 0.6f;
    private static readonly Color BuffFrame = new(120, 210, 130);
    private static readonly Color DebuffFrame = new(230, 80, 70);
    private static readonly Color Hover = new Color(255, 255, 255) * 0.12f;
    private static readonly Color StackText = new(255, 255, 255);
    private static readonly Color Shadow = new(0, 0, 0);
    private static readonly Color TipBack = new Color(14, 18, 30) * 0.96f;
    private static readonly Color EmblemBack = new(8, 10, 20);
    private static readonly Color Text = new(220, 224, 232);
    private static readonly Color Muted = new(150, 155, 170);

    private readonly PrimitiveBatch _batch;

    public StatusRow(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    /// <param name="renderTick">The tick being drawn (fractional between ticks), to time each status's wearing off.</param>
    public void Draw(Ship? ship, double renderTick, InputState input, HudView hud)
    {
        if (ship is null || ship.IsSunk || ship.Statuses.Count == 0)
            return;

        var statuses = ship.Statuses.OrderByDescending(s => s.Definition.IsBuff).ThenBy(s => s.Id).ToList();
        var groups = statuses.Select(s => s.Definition.IsBuff).Distinct().Count();
        var width = statuses.Count * IconSize + (statuses.Count - 1) * IconGap + (groups - 1) * (GroupGap - IconGap);
        var bar = AbilityBar.Origin(hud);
        var x = hud.Viewport.Width / 2f - width / 2f;
        var top = bar.Y - AboveBar - IconSize;
        var mouse = hud.FromScreen(input.MousePosition);

        _batch.Begin(hud.Transform);
        StatusEffect? hovered = null;
        var hoveredLeft = 0f;
        for (var i = 0; i < statuses.Count; i++)
        {
            var status = statuses[i];
            if (i > 0 && status.Definition.IsBuff != statuses[i - 1].Definition.IsBuff)
                x += GroupGap - IconGap;
            var hover = mouse.X >= x && mouse.X < x + IconSize && mouse.Y >= top && mouse.Y < top + IconSize;
            if (hover)
            {
                hovered = status;
                hoveredLeft = x;
            }
            DrawIcon(status, new Vector2(x, top), TimeLeft(status, renderTick), hover);
            x += IconSize + IconGap;
        }

        if (hovered is not null)
            DrawTip(hovered, renderTick, hoveredLeft + IconSize / 2f, top - TipGap, hud);
        _batch.Flush();
    }

    /// <summary>The fraction of its full time it has left, 1 when just applied.</summary>
    private static float TimeLeft(StatusEffect status, double renderTick) =>
        (float)Math.Clamp((status.UntilTick - renderTick) / Math.Max(1, status.Definition.Ticks), 0, 1);

    private void DrawIcon(StatusEffect status, Vector2 topLeft, float left, bool hover)
    {
        var size = new Vector2(IconSize);
        var color = StatusLook.Color(status.Id);
        FillRect(topLeft, size, IconBack);
        if (hover)
            FillRect(topLeft, size, Hover);
        CardIcons.Draw(_batch, StatusLook.Icon(status.Id), topLeft + size / 2f, IconSize * 0.32f, color);
        FillRect(topLeft, new Vector2(IconSize, IconSize * (1f - left)), Elapsed);

        var frame = status.Definition.IsBuff ? BuffFrame : DebuffFrame;
        Outline(topLeft, size, frame);
        if (hover)
            Outline(topLeft + Vector2.One, size - new Vector2(2f), frame * 0.6f);

        if (status.Stacks > 1)
        {
            var text = status.Stacks.ToString();
            var position = topLeft + size - new Vector2(PixelFont.Measure(text, 1.5f) + 3f, PixelFont.Height(1.5f) + 3f);
            PixelFont.Draw(_batch, text, position + Vector2.One, 1.5f, Shadow);
            PixelFont.Draw(_batch, text, position, 1.5f, StackText);
        }
    }

    /// <summary>The status explained, centred over <paramref name="centerX"/> with its bottom at <paramref name="bottom"/>, kept on screen.</summary>
    private void DrawTip(StatusEffect status, double renderTick, float centerX, float bottom, HudView hud)
    {
        var definition = status.Definition;
        var color = StatusLook.Color(status.Id);
        var frame = definition.IsBuff ? BuffFrame : DebuffFrame;
        var textWidth = TipWidth - 2 * TipPadding;

        var lines = new List<(string Text, Color Color)>();
        foreach (var line in PixelFont.Wrap(definition.Summary, 1.5f, textWidth))
            lines.Add((line, Text));
        foreach (var line in PixelFont.Wrap(Statuses.Effect(status.Id, status.Stacks, status.Power), 1.5f, textWidth))
            lines.Add((line, color));
        var seconds = MathF.Max(0f, (float)(status.UntilTick - renderTick) / SimConstants.TickRate);
        var stacks = definition.MaxStacks > 1 ? $"{status.Stacks} OF {definition.MaxStacks} STACKS - " : "";
        lines.Add(($"{stacks}{seconds:0.0}S LEFT", Muted));

        var headHeight = MathF.Max(2 * EmblemRadius, PixelFont.Height(2f) + 4f + PixelFont.Height(1.5f));
        var height = TipPadding + headHeight + 12f + lines.Count * (PixelFont.Height(1.5f) + 5f) + TipPadding;
        var left = Math.Clamp(centerX - TipWidth / 2f, 4f, hud.Viewport.Width - TipWidth - 4f);
        var top = MathF.Max(4f, bottom - height);

        FillRect(new Vector2(left, top), new Vector2(TipWidth, height), TipBack);
        Outline(new Vector2(left, top), new Vector2(TipWidth, height), frame);
        FillRect(new Vector2(left, top), new Vector2(TipWidth, 4f), frame);

        var emblem = new Vector2(left + TipPadding + EmblemRadius, top + TipPadding + EmblemRadius + 2f);
        _batch.FillEllipse(emblem, new Vector2(EmblemRadius + 2f), color * 0.6f);
        _batch.FillEllipse(emblem, new Vector2(EmblemRadius), EmblemBack);
        CardIcons.Draw(_batch, StatusLook.Icon(status.Id), emblem, EmblemRadius * 0.62f, color);

        var x = left + TipPadding + 2 * EmblemRadius + 10f;
        var y = top + TipPadding;
        PixelFont.Draw(_batch, definition.Name, new Vector2(x, y), 2f, color);
        PixelFont.Draw(_batch, definition.IsBuff ? "BUFF" : "DEBUFF", new Vector2(x, y + PixelFont.Height(2f) + 4f), 1.5f, frame);

        y = top + TipPadding + headHeight + 6f;
        _batch.Line(new Vector2(left + TipPadding, y), new Vector2(left + TipWidth - TipPadding, y), frame * 0.4f);
        y += 6f;
        foreach (var (text, lineColor) in lines)
        {
            PixelFont.Draw(_batch, text, new Vector2(left + TipPadding, y), 1.5f, lineColor);
            y += PixelFont.Height(1.5f) + 5f;
        }
    }

    private void FillRect(Vector2 topLeft, Vector2 size, Color color)
    {
        Span<Vector2> rect = stackalloc Vector2[] { topLeft, topLeft + new Vector2(size.X, 0), topLeft + size, topLeft + new Vector2(0, size.Y) };
        _batch.FillConvex(rect, color);
    }

    private void Outline(Vector2 topLeft, Vector2 size, Color color)
    {
        Span<Vector2> rect = stackalloc Vector2[] { topLeft, topLeft + new Vector2(size.X, 0), topLeft + size, topLeft + new Vector2(0, size.Y) };
        _batch.Outline(rect, color);
    }
}
