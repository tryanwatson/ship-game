using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Client.Rendering;

/// <summary>
/// A hand of cards at a glance, wherever a reward is shown (the chart, the map, a fortress's label): a little card per
/// card in the hand, framed in its tier's color like the cards themselves. A card that may come a tier better is
/// split, the better tier's color across its top, with the chance after the cards ("25%").
/// </summary>
public static class HandPips
{
    private static readonly Color Edge = new(12, 14, 22);

    /// <summary>A pip's height at text <paramref name="scale"/>: a little taller than a line of text.</summary>
    public static float PipHeight(float scale) => PixelFont.Height(scale) + 2f * scale;

    public static float PipWidth(float scale) => PipHeight(scale) * 0.68f;

    private static float Gap(float scale) => 2.5f * scale;

    /// <summary>How wide <see cref="Draw"/> draws <paramref name="hand"/>, chances and all.</summary>
    public static float Measure(IReadOnlyList<HandSlot> hand, float scale)
    {
        var width = hand.Count * (PipWidth(scale) + Gap(scale)) - Gap(scale);
        foreach (var slot in hand)
        {
            if (slot.Upgrade > 0f)
                width += 2f * Gap(scale) + PixelFont.Measure(Chance(slot), scale);
        }
        return width;
    }

    /// <summary>Draws <paramref name="hand"/> with its middle at <paramref name="left"/>'s height; returns the width drawn.</summary>
    public static float Draw(PrimitiveBatch batch, IReadOnlyList<HandSlot> hand, Vector2 left, float scale, float opacity = 1f)
    {
        var x = left.X;
        for (var i = 0; i < hand.Count; i++)
            x += DrawPip(batch, hand[i], i, new Vector2(x, left.Y), scale, opacity) + Gap(scale);
        x -= Gap(scale);
        foreach (var slot in hand)
        {
            if (slot.Upgrade <= 0f)
                continue;
            var text = Chance(slot);
            x += 2f * Gap(scale);
            PixelFont.Draw(batch, text, new Vector2(x, left.Y - PixelFont.Height(scale) / 2f), scale, ColorOf(slot.Tier + 1, 0) * opacity);
            x += PixelFont.Measure(text, scale);
        }
        return x - left.X;
    }

    /// <summary>One card of a hand, without its chance, for a legend; returns the width drawn.</summary>
    public static float DrawPip(PrimitiveBatch batch, HandSlot slot, int index, Vector2 left, float scale, float opacity = 1f)
    {
        var (width, height) = (PipWidth(scale), PipHeight(scale));
        var top = left.Y - height / 2f;
        Rect(batch, new Vector2(left.X - scale, top - scale), new Vector2(width + 2f * scale, height + 2f * scale), Edge * opacity);
        Rect(batch, new Vector2(left.X, top), new Vector2(width, height), ColorOf(slot.Tier, index) * opacity);
        if (slot.Upgrade > 0f)
            Rect(batch, new Vector2(left.X, top), new Vector2(width, height * 0.45f), ColorOf(slot.Tier + 1, index) * opacity);
        return width;
    }

    private static string Chance(HandSlot slot) => $"{CardRewards.Percent(slot.Upgrade)}%";

    private static Color ColorOf(CardTier tier, int index) =>
        tier >= CardTier.Prismatic ? CardLook.Shimmer(index * 0.15f) : CardLook.TierColor(tier);

    private static void Rect(PrimitiveBatch batch, Vector2 topLeft, Vector2 size, Color color)
    {
        Span<Vector2> rect = stackalloc Vector2[] { topLeft, topLeft + new Vector2(size.X, 0f), topLeft + size, topLeft + new Vector2(0f, size.Y) };
        batch.FillConvex(rect, color);
    }
}
