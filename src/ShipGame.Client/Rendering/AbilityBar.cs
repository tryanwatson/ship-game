using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Simulation;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Screen-space 1/2/3/4 ability bar. A ready ability gets a bright border; one reloading is dimmed, drains a dark
/// overlay as it recovers, and shows a big countdown. The broadside's slot is split into its two decks (port on the
/// left, starboard on the right), each with its own reload.
/// </summary>
public sealed class AbilityBar
{
    private const float SlotSize = 56f;
    private const float SlotGap = 8f;
    private const float BottomMargin = 24f;

    private static readonly Color SlotBack = new Color(12, 16, 24) * 0.85f;
    private static readonly Color SlotBorder = new(90, 100, 120);
    private static readonly Color ReadyIcon = new(255, 230, 150);
    private static readonly Color CoolingOverlay = new Color(0, 0, 0) * 0.65f;
    private static readonly Color CooldownTint = new Color(0, 0, 0) * 0.55f;
    private static readonly Color CooldownDrain = new Color(0, 0, 0) * 0.8f;
    private static readonly Color CooldownText = new(255, 255, 255);
    private static readonly Color CooldownShadow = new(0, 0, 0);
    private static readonly Color DimIcon = new Color(255, 230, 150) * 0.35f;
    private static readonly Color ReadyBorder = new(255, 215, 110);
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

    private static readonly Vector2[][] GlyphX =
    {
        new Vector2[] { new(0, 0), new(1, 1) },
        new Vector2[] { new(1, 0), new(0, 1) },
    };

    // An anchor in a unit box: ring, shank, stock, curved arms with flukes.
    private static readonly Vector2[][] AnchorIcon =
    {
        new Vector2[] { new(0.5f, 0.05f), new(0.6f, 0.15f), new(0.5f, 0.25f), new(0.4f, 0.15f), new(0.5f, 0.05f) },
        new Vector2[] { new(0.5f, 0.25f), new(0.5f, 0.9f) },
        new Vector2[] { new(0.3f, 0.34f), new(0.7f, 0.34f) },
        new Vector2[] { new(0.15f, 0.6f), new(0.28f, 0.8f), new(0.5f, 0.9f), new(0.72f, 0.8f), new(0.85f, 0.6f) },
        new Vector2[] { new(0.08f, 0.68f), new(0.15f, 0.6f), new(0.24f, 0.65f) },
        new Vector2[] { new(0.92f, 0.68f), new(0.85f, 0.6f), new(0.76f, 0.65f) },
    };

    private static readonly Color ChestWood = new(150, 95, 45);
    private static readonly Color ChestBand = new(235, 190, 60);
    private static readonly Color AnchorUp = new Color(180, 190, 210) * 0.45f;
    private static readonly Color AnchorDown = new(170, 220, 255);

    private readonly PrimitiveBatch _batch;

    public AbilityBar(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    /// <param name="plunderReady">An island is in plunder range: the anchor slot shows a chest instead.</param>
    /// <param name="shipyardReady">A shipyard is in range: the anchor slot shows a hammer (takes precedence).</param>
    /// <param name="anchorDropProgress">0..1 while X is held to drop anchor: the anchor slot fills to match.</param>
    public void Draw(Ship? ship, bool plunderReady, bool shipyardReady, HudView hud, float anchorDropProgress = 0f)
    {
        var viewport = hud.Viewport;
        _batch.Begin(hud.Transform);

        var totalWidth = Ship.AbilitySlotCount * SlotSize + (Ship.AbilitySlotCount - 1) * SlotGap;
        var origin = new Vector2((viewport.Width - totalWidth) / 2f, viewport.Height - BottomMargin - SlotSize);

        for (var i = 0; i < Ship.AbilitySlotCount; i++)
        {
            var topLeft = origin + new Vector2(i * (SlotSize + SlotGap), 0);
            var ability = ship?.Abilities[i];

            FillRect(topLeft, new Vector2(SlotSize), SlotBack);
            if (ability?.Definition is BroadsideVolley)
            {
                DrawBroadsideSlot(ability, topLeft);
            }
            else if (ability is not null)
            {
                DrawIcon(ability.Definition, topLeft + new Vector2(SlotSize / 2f), ability.IsReady ? ReadyIcon : DimIcon);
                if (!ability.IsChannelReady(0))
                    DrawCooldown(topLeft, new Vector2(SlotSize), ability.CooldownFraction(0), ability.RemainingTicks(0), 3f);
            }

            DrawBorder(topLeft, ability?.IsReady == true);
            DrawGlyph(Glyphs[i], topLeft + new Vector2(5, 5), new Vector2(8, 10), KeyLabel);
        }

        if (ship is not null)
        {
            DrawSailGauge(ship, origin - new Vector2(SlotGap * 2 + SailPipWidth, 0));
            DrawAnchorSlot(ship, plunderReady, shipyardReady, origin + new Vector2(totalWidth + SlotGap * 2, 0), anchorDropProgress);
        }

        _batch.Flush();
    }

    /// <summary>
    /// The broadside's slot, split down the middle: port deck on the left, starboard on the right. Each half shows its
    /// arrow when loaded, or its own drain and countdown while reloading.
    /// </summary>
    private void DrawBroadsideSlot(AbilityState ability, Vector2 topLeft)
    {
        var half = new Vector2(SlotSize / 2f, SlotSize);
        foreach (var (channel, direction) in new[] { (BroadsideVolley.PortChannel, -1f), (BroadsideVolley.StarboardChannel, 1f) })
        {
            var halfLeft = topLeft + new Vector2(direction < 0 ? 0f : SlotSize / 2f, 0f);
            var ready = ability.IsChannelReady(channel);
            DrawSideArrow(halfLeft + half / 2f + new Vector2(0, 6), direction, ready ? ReadyIcon : DimIcon);
            if (!ready)
                DrawCooldown(halfLeft, half, ability.CooldownFraction(channel), ability.RemainingTicks(channel), 2f);
        }
        _batch.Line(topLeft + new Vector2(SlotSize / 2f, 4), topLeft + new Vector2(SlotSize / 2f, SlotSize - 4), SlotBorder);
    }

    /// <summary>An arrow pointing left (<paramref name="direction"/> -1, port) or right (+1, starboard).</summary>
    private void DrawSideArrow(Vector2 center, float direction, Color color)
    {
        var tip = center + new Vector2(9 * direction, 0);
        Span<Vector2> head = stackalloc Vector2[] { tip, tip + new Vector2(-8 * direction, -7), tip + new Vector2(-8 * direction, 7) };
        _batch.FillConvex(head, color);
        FillRect(new Vector2(MathF.Min(center.X - 9 * direction, tip.X - 8 * direction), center.Y - 2.5f), new Vector2(10, 5), color);
    }

    /// <summary>
    /// High-contrast reload display over an area: a dark tint over the whole area, a darker band that drains from the
    /// top as the reload finishes, and the time left in large shadowed digits (whole seconds, tenths under one).
    /// </summary>
    private void DrawCooldown(Vector2 topLeft, Vector2 size, float fraction, int remainingTicks, float textScale)
    {
        FillRect(topLeft, size, CooldownTint);
        FillRect(topLeft, new Vector2(size.X, size.Y * Math.Clamp(fraction, 0f, 1f)), CooldownDrain);

        var seconds = remainingTicks / (float)SimConstants.TickRate;
        var text = seconds >= 1f ? ((int)MathF.Ceiling(seconds)).ToString() : $".{Math.Clamp((int)MathF.Ceiling(seconds * 10f), 1, 9)}";
        var textSize = new Vector2(PixelFont.Measure(text, textScale), PixelFont.Height(textScale));
        var position = topLeft + (size - textSize) / 2f + new Vector2(0, 4);
        PixelFont.Draw(_batch, text, position + new Vector2(textScale * 0.75f), textScale, CooldownShadow);
        PixelFont.Draw(_batch, text, position, textScale, CooldownText);
    }

    /// <summary>Bright double border when ready to use, the plain one otherwise.</summary>
    private void DrawBorder(Vector2 topLeft, bool ready)
    {
        Outline(topLeft, new Vector2(SlotSize), ready ? ReadyBorder : SlotBorder);
        if (ready)
            Outline(topLeft + Vector2.One, new Vector2(SlotSize - 2f), ReadyBorder * 0.6f);
    }

    /// <summary>
    /// The X slot. A treasure chest when anchoring here would plunder an island (and while plundering);
    /// otherwise an anchor, dim when weighed and bright when down. While hauling, it fills bottom-up.
    /// </summary>
    private void DrawAnchorSlot(Ship ship, bool plunderReady, bool shipyardReady, Vector2 topLeft, float dropProgress)
    {
        FillRect(topLeft, new Vector2(SlotSize), SlotBack);

        // Holding X to let go: the slot fills bottom-up as the hold completes.
        if (dropProgress > 0f && ship.Anchor == AnchorState.Weighed)
        {
            var height = SlotSize * dropProgress;
            FillRect(topLeft + new Vector2(0, SlotSize - height), new Vector2(SlotSize, height), AnchorDown * 0.35f);
        }

        if (shipyardReady && ship.Anchor != AnchorState.Raising)
        {
            DrawHammer(topLeft + new Vector2(SlotSize / 2f), SlotSize * 0.6f);
        }
        else if (plunderReady && ship.Anchor != AnchorState.Raising)
        {
            DrawChest(topLeft + new Vector2(SlotSize / 2f, SlotSize * 0.56f), SlotSize * 0.6f);
        }
        else
        {
            var color = ship.Anchor == AnchorState.Weighed ? AnchorUp : AnchorDown;
            DrawGlyph(AnchorIcon, topLeft + new Vector2(SlotSize * 0.2f), new Vector2(SlotSize * 0.6f), color);
        }
        if (ship.Anchor == AnchorState.Raising)
            FillRect(topLeft, new Vector2(SlotSize, SlotSize * (1f - Anchoring.RaiseProgress(ship))), CoolingOverlay);

        Outline(topLeft, new Vector2(SlotSize), SlotBorder);
        DrawGlyph(GlyphX, topLeft + new Vector2(5, 5), new Vector2(8, 10), KeyLabel);
    }

    /// <summary>A shipwright's hammer: steel head on a wooden handle, angled.</summary>
    private void DrawHammer(Vector2 center, float size)
    {
        var along = Vector2.Normalize(new Vector2(1f, 1f));   // handle runs top-left to bottom-right
        var across = new Vector2(-along.Y, along.X);
        var handleStart = center - along * size * 0.15f;
        var handleEnd = center + along * size * 0.5f;
        Span<Vector2> handle = stackalloc Vector2[]
        {
            handleStart + across * 2.5f, handleEnd + across * 2.5f, handleEnd - across * 2.5f, handleStart - across * 2.5f,
        };
        _batch.FillConvex(handle, ChestWood);

        var headCenter = center - along * size * 0.25f;
        Span<Vector2> head = stackalloc Vector2[]
        {
            headCenter + across * size * 0.32f + along * 5f, headCenter + across * size * 0.32f - along * 5f,
            headCenter - across * size * 0.32f - along * 5f, headCenter - across * size * 0.32f + along * 5f,
        };
        _batch.FillConvex(head, AnchorDown);
    }

    /// <summary>A treasure chest: wooden body and domed lid, with gold bands and lock.</summary>
    private void DrawChest(Vector2 center, float width)
    {
        var height = width * 0.62f;
        var lidHeight = height * 0.38f;
        var left = center.X - width / 2f;
        var top = center.Y - height / 2f;

        Span<Vector2> lid = stackalloc Vector2[]
        {
            new(left, top + lidHeight), new(left + width * 0.12f, top), new(left + width * 0.88f, top), new(left + width, top + lidHeight),
        };
        _batch.FillConvex(lid, ChestWood);
        FillRect(new Vector2(left, top + lidHeight), new Vector2(width, height - lidHeight), ChestWood);

        FillRect(new Vector2(left, top + lidHeight - 1.5f), new Vector2(width, 3f), ChestBand);              // lid seam
        FillRect(new Vector2(left + width * 0.18f, top + 2f), new Vector2(3f, height - 2f), ChestBand);      // straps
        FillRect(new Vector2(left + width * 0.82f - 3f, top + 2f), new Vector2(3f, height - 2f), ChestBand);
        FillRect(new Vector2(center.X - 3.5f, top + lidHeight - 1f), new Vector2(7f, 8f), ChestBand);       // lock
    }

    /// <summary>
    /// Stacked pips left of the bar, filled bottom-up to the current sail setting. Rowing astern, a down arrow
    /// under them instead.
    /// </summary>
    private void DrawSailGauge(Ship ship, Vector2 topLeft)
    {
        var levels = ShipMovement.ThrottleLevels;
        var pipHeight = (SlotSize - SailPipGap * (levels - 1)) / levels;
        for (var level = 1; level <= levels; level++)
        {
            var y = topLeft.Y + SlotSize - level * pipHeight - (level - 1) * SailPipGap;
            FillRect(new Vector2(topLeft.X, y), new Vector2(SailPipWidth, pipHeight), level <= ship.Throttle ? SailOn : SailOff);
        }

        if (ship.Throttle < 0)
        {
            var top = topLeft.Y + SlotSize + 4f;
            Span<Vector2> arrow = stackalloc Vector2[]
            {
                new(topLeft.X - 1f, top), new(topLeft.X + SailPipWidth + 1f, top), new(topLeft.X + SailPipWidth / 2f, top + 9f),
            };
            _batch.FillConvex(arrow, SailOn);
        }
    }

    private void DrawIcon(Ability ability, Vector2 center, Color color)
    {
        if (ability is LongGun)
        {
            // A long barrel pointing up-right, with the ball leaving it.
            var along = Vector2.Normalize(new Vector2(1f, -1f));
            var across = new Vector2(-along.Y, along.X);
            var breech = center - along * 14f;
            var muzzle = center + along * 8f;
            Span<Vector2> barrel = stackalloc Vector2[] { breech + across * 4f, muzzle + across * 3f, muzzle - across * 3f, breech - across * 4f };
            _batch.FillConvex(barrel, color);
            Span<Vector2> ball = stackalloc Vector2[8];
            var ballCenter = center + along * 16f;
            for (var i = 0; i < ball.Length; i++)
                ball[i] = ballCenter + new Vector2(MathF.Cos(MathF.Tau * i / 8), MathF.Sin(MathF.Tau * i / 8)) * 3.5f;
            _batch.FillConvex(ball, color);
            return;
        }

        if (ability is Mortar)
        {
            // A target reticle: ring, cross hairs, and a dot.
            Span<Vector2> ring = stackalloc Vector2[16];
            for (var i = 0; i < ring.Length; i++)
                ring[i] = center + new Vector2(MathF.Cos(MathF.Tau * i / 16), MathF.Sin(MathF.Tau * i / 16)) * 13f;
            _batch.Outline(ring, color);
            _batch.Line(center + new Vector2(-18, 0), center + new Vector2(-7, 0), color);
            _batch.Line(center + new Vector2(7, 0), center + new Vector2(18, 0), color);
            _batch.Line(center + new Vector2(0, -18), center + new Vector2(0, -7), color);
            _batch.Line(center + new Vector2(0, 7), center + new Vector2(0, 18), color);
            FillRect(center - new Vector2(2, 2), new Vector2(4, 4), color);
            return;
        }

        if (ability is BroadsideVolley)
        {
            // A double-headed arrow: fires out of either side.
            var y = center.Y + 4;
            Span<Vector2> head = stackalloc Vector2[3];
            foreach (var dir in new[] { -1f, 1f })
            {
                var tip = new Vector2(center.X + 18 * dir, y);
                head[0] = tip;
                head[1] = tip + new Vector2(-9 * dir, -7);
                head[2] = tip + new Vector2(-9 * dir, 7);
                _batch.FillConvex(head, color);
            }
            FillRect(new Vector2(center.X - 10, y - 2.5f), new Vector2(20, 5), color);
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
