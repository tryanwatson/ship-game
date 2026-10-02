using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Simulation;

namespace ShipGame.Client.Rendering;

/// <summary>Screen-space 1/2/3/4 ability bar with cooldown fill. Glyphs are drawn as line strokes until we have fonts.</summary>
public sealed class AbilityBar
{
    private const float SlotSize = 56f;
    private const float SlotGap = 8f;
    private const float BottomMargin = 24f;

    private static readonly Color SlotBack = new Color(12, 16, 24) * 0.85f;
    private static readonly Color SlotBorder = new(90, 100, 120);
    private static readonly Color ReadyIcon = new(255, 230, 150);
    private static readonly Color CoolingOverlay = new Color(0, 0, 0) * 0.65f;
    private static readonly Color KeyLabel = new(220, 220, 230);
    private static readonly Color SailOn = new(240, 240, 225);
    private static readonly Color SailOff = new Color(240, 240, 225) * 0.15f;

    private const float SailPipWidth = 14f;
    private const float SailPipGap = 3f;

    // Key glyphs as polylines in a unit box (x right, y down).
    private static readonly Vector2[][] Glyph1 =
    {
        new Vector2[] { new(0.2f, 0.25f), new(0.55f, 0), new(0.55f, 1) },
        new Vector2[] { new(0.2f, 1), new(0.9f, 1) },
    };
    private static readonly Vector2[][] Glyph2 =
    {
        new Vector2[] { new(0, 0), new(1, 0), new(1, 0.5f), new(0, 0.5f), new(0, 1), new(1, 1) },
    };
    private static readonly Vector2[][] Glyph3 =
    {
        new Vector2[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1) },
        new Vector2[] { new(0.25f, 0.5f), new(1, 0.5f) },
    };
    private static readonly Vector2[][] Glyph4 =
    {
        new Vector2[] { new(0, 0), new(0, 0.6f), new(1, 0.6f) },
        new Vector2[] { new(0.75f, 0), new(0.75f, 1) },
    };
    private static readonly Vector2[][][] Glyphs = { Glyph1, Glyph2, Glyph3, Glyph4 };

    private readonly PrimitiveBatch _batch;

    public AbilityBar(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public void Draw(Ship? ship, Viewport viewport)
    {
        _batch.Begin(Matrix.Identity);

        var totalWidth = Ship.AbilitySlotCount * SlotSize + (Ship.AbilitySlotCount - 1) * SlotGap;
        var origin = new Vector2((viewport.Width - totalWidth) / 2f, viewport.Height - BottomMargin - SlotSize);

        for (var i = 0; i < Ship.AbilitySlotCount; i++)
        {
            var topLeft = origin + new Vector2(i * (SlotSize + SlotGap), 0);
            var ability = ship?.Abilities[i];

            FillRect(topLeft, new Vector2(SlotSize), SlotBack);
            if (ability is not null)
            {
                DrawIcon(ability.Definition, topLeft + new Vector2(SlotSize / 2f), ReadyIcon);

                // Cooldown darkens the slot from the top down, shrinking as it recovers.
                if (!ability.IsReady)
                    FillRect(topLeft, new Vector2(SlotSize, SlotSize * ability.CooldownFraction), CoolingOverlay);
            }

            Outline(topLeft, new Vector2(SlotSize), SlotBorder);
            DrawGlyph(Glyphs[i], topLeft + new Vector2(5, 5), new Vector2(8, 10), KeyLabel);
        }

        if (ship is not null)
            DrawSailGauge(ship, origin - new Vector2(SlotGap * 2 + SailPipWidth, 0));

        _batch.Flush();
    }

    /// <summary>Stacked pips left of the bar, filled bottom-up to the current sail setting.</summary>
    private void DrawSailGauge(Ship ship, Vector2 topLeft)
    {
        var levels = ShipMovement.ThrottleLevels;
        var pipHeight = (SlotSize - SailPipGap * (levels - 1)) / levels;
        for (var level = 1; level <= levels; level++)
        {
            var y = topLeft.Y + SlotSize - level * pipHeight - (level - 1) * SailPipGap;
            FillRect(new Vector2(topLeft.X, y), new Vector2(SailPipWidth, pipHeight), level <= ship.Throttle ? SailOn : SailOff);
        }
    }

    private void DrawIcon(Ability ability, Vector2 center, Color color)
    {
        if (ability is BroadsideVolley volley)
        {
            // An arrow pointing toward the side the volley fires.
            var dir = volley.Side == BroadsideSide.Port ? -1f : 1f;
            var tip = center + new Vector2(14 * dir, 4);
            Span<Vector2> head = stackalloc Vector2[] { tip, tip + new Vector2(-10 * dir, -8), tip + new Vector2(-10 * dir, 8) };
            _batch.FillConvex(head, color);
            FillRect(new Vector2(Math.Min(center.X - 12 * dir, tip.X - 10 * dir), center.Y + 1), new Vector2(18, 6), color);
        }
    }

    private void DrawGlyph(Vector2[][] strokes, Vector2 topLeft, Vector2 size, Color color)
    {
        foreach (var stroke in strokes)
        {
            for (var i = 0; i < stroke.Length - 1; i++)
                _batch.Line(topLeft + stroke[i] * size, topLeft + stroke[i + 1] * size, color);
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
