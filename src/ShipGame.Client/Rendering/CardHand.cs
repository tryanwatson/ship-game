using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using ShipGame.Client.Input;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Down the right edge, under the run forecast: the cards this player holds, one row each (a count for any held more
/// than once), under a small "CARDS" label. Each row is marked and colored by tier. Hovering a row shows the card
/// beside the list: its emblem, tier, what it's for, and what it does at the level it's held (every copy, if stacked),
/// and for a card that depends on the ship (Ram, on its speed), what that comes to right now.
/// </summary>
public sealed class CardHand
{
    private const float RightMargin = 12f;
    private const float Scale = 1.5f;
    private const float HeaderScale = 1f;
    private const float Pip = 6f;
    private const float RowPadding = 8f;
    private const float TipWidth = 270f;
    private const float TipPadding = 12f;
    private const float TipGap = 8f;
    private const float EmblemRadius = 16f;

    private static readonly Color Header = new(140, 145, 160);
    private static readonly Color Rule = new Color(140, 145, 160) * 0.4f;
    private static readonly Color Back = new Color(12, 16, 24) * 0.75f;
    private static readonly Color RowHover = new Color(255, 255, 255) * 0.08f;
    private static readonly Color TipBack = new Color(14, 18, 30) * 0.96f;
    private static readonly Color EmblemBack = new(8, 10, 20);
    private static readonly Color Text = new(220, 224, 232);
    private static readonly Color Muted = new(150, 155, 170);

    private readonly PrimitiveBatch _batch;

    public CardHand(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public void Draw(PlayerState? player, Ship? ship, InputState input, HudView hud)
    {
        if (player is null || player.Cards.Count == 0)
            return;

        // One row per card, its copies together, in the order first taken.
        var rows = player.Cards.GroupBy(c => c.Id).Select(g => g.ToList()).ToList();
        var lineHeight = PixelFont.Height(Scale) + 6f;
        var headerHeight = PixelFont.Height(HeaderScale) + 10f;
        var width = rows.Max(r => PixelFont.Measure(Label(r), Scale)) + Pip + 8f + 2 * RowPadding;
        width = MathF.Max(width, PixelFont.Measure("CARDS", HeaderScale) + 2 * RowPadding);
        var left = hud.Viewport.Width - RightMargin - width;
        var top = RunForecast.Bottom + 10f;
        var height = headerHeight + rows.Count * lineHeight + 6f;
        var mouse = hud.FromScreen(input.Mouse.Position);

        _batch.Begin(hud.Transform);
        Fill(left, top, width, height, Back);

        // The label: small and grey, ruled off from the cards themselves.
        PixelFont.Draw(_batch, "CARDS", new Vector2(left + RowPadding, top + 5f), HeaderScale, Header);
        _batch.Line(new Vector2(left + RowPadding, top + headerHeight - 3f), new Vector2(left + width - RowPadding, top + headerHeight - 3f), Rule);

        List<CardPick>? hovered = null;
        var hoveredTop = 0f;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var card = row[0].Definition;
            var y = top + headerHeight + i * lineHeight;
            var color = TierColor(card.Tier, i);
            if (mouse.X >= left && mouse.X <= left + width && mouse.Y >= y - 2f && mouse.Y < y + lineHeight - 2f)
            {
                Fill(left + 2f, y - 2f, width - 4f, lineHeight, RowHover);
                hovered = row;
                hoveredTop = y;
            }
            var mid = y + PixelFont.Height(Scale) / 2f;
            Fill(left + RowPadding, mid - Pip / 2f, Pip, Pip, color);
            PixelFont.Draw(_batch, Label(row), new Vector2(left + RowPadding + Pip + 8f, y), Scale, color);
        }

        if (hovered is not null)
            DrawTip(hovered, ship, left - TipGap, hoveredTop - TipPadding, hud);
        _batch.Flush();
    }

    private static string Label(List<CardPick> copies) =>
        copies.Count > 1 ? $"{copies[0].Definition.Name} X{copies.Count}" : copies[0].Definition.Name;

    private static Color TierColor(CardTier tier, int index) =>
        tier == CardTier.Prismatic ? CardLook.Shimmer(index * 0.15f) : CardLook.TierColor(tier);

    /// <summary>The card, to the left of the list with its right edge at <paramref name="right"/>, kept on screen.</summary>
    private void DrawTip(List<CardPick> copies, Ship? ship, float right, float top, HudView hud)
    {
        var card = copies[0].Definition;
        var frame = TierColor(card.Tier, 0);
        var tag = CardLook.TagColor(card);
        var textWidth = TipWidth - 2 * TipPadding;

        var name = PixelFont.Wrap(card.Name, 2f, textWidth - 2 * EmblemRadius - 10f);
        var kind = $"{card.Tier.ToString().ToUpperInvariant()} - {CardLook.Category(card)}";
        var lines = new List<(string Text, Color Color)>();
        foreach (var (pick, n) in copies.Select((p, n) => (p, n)))
        {
            var prefix = copies.Count > 1 ? $"{n + 1}. " : "";
            foreach (var line in PixelFont.Wrap(prefix + pick.Description, 1.5f, textWidth))
                lines.Add((line, Text));
            if (ship is not null && pick.DescriptionOn(ship) is { } now)
            {
                foreach (var line in PixelFont.Wrap(now, 1.5f, textWidth))
                    lines.Add((line, frame));
            }
        }
        if (card.Improves && copies[0].Level > card.LevelRange.Max)
            lines.Add(($"IMPROVED PAST LEVEL {card.LevelRange.Max}", frame));
        if (card.OneShot)
            lines.Add(("USED WHEN CHOSEN", Muted));

        var headHeight = MathF.Max(2 * EmblemRadius, name.Count * (PixelFont.Height(2f) + 4f) + PixelFont.Height(1.5f) + 4f);
        var height = TipPadding + headHeight + 12f + lines.Count * (PixelFont.Height(1.5f) + 5f) + TipPadding;
        var left = right - TipWidth;
        top = Math.Clamp(top, 4f, hud.Viewport.Height - height - 4f);

        Fill(left, top, TipWidth, height, TipBack);
        Outline(left, top, TipWidth, height, frame);
        Fill(left, top, TipWidth, 4f, frame);

        var emblem = new Vector2(left + TipPadding + EmblemRadius, top + TipPadding + EmblemRadius + 2f);
        _batch.FillEllipse(emblem, new Vector2(EmblemRadius + 2f), frame * 0.6f);
        _batch.FillEllipse(emblem, new Vector2(EmblemRadius), EmblemBack);
        CardIcons.Draw(_batch, CardLook.Icon(card, copies[0].Values), emblem, EmblemRadius * 0.62f, frame);

        var x = left + TipPadding + 2 * EmblemRadius + 10f;
        var y = top + TipPadding;
        foreach (var line in name)
        {
            PixelFont.Draw(_batch, line, new Vector2(x, y), 2f, frame);
            y += PixelFont.Height(2f) + 4f;
        }
        PixelFont.Draw(_batch, kind, new Vector2(x, y), 1.5f, tag);

        y = top + TipPadding + headHeight + 6f;
        _batch.Line(new Vector2(left + TipPadding, y), new Vector2(left + TipWidth - TipPadding, y), frame * 0.4f);
        y += 6f;
        foreach (var (text, color) in lines)
        {
            PixelFont.Draw(_batch, text, new Vector2(left + TipPadding, y), 1.5f, color);
            y += PixelFont.Height(1.5f) + 5f;
        }
    }

    private void Fill(float x, float y, float width, float height, Color color)
    {
        Span<Vector2> rect = stackalloc Vector2[] { new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height) };
        _batch.FillConvex(rect, color);
    }

    private void Outline(float x, float y, float width, float height, Color color)
    {
        Span<Vector2> rect = stackalloc Vector2[] { new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height) };
        _batch.Outline(rect, color);
    }
}
