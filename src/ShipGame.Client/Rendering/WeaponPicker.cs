using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using ShipGame.Client.Input;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Client.Rendering;

/// <summary>
/// The choice of starting weapon before a run: one card per weapon in <see cref="WeaponCatalog"/>, picked with a click
/// or its number key. Sits under the status banner (which says what the choice is for).
/// </summary>
public sealed class WeaponPicker
{
    private const int CardWidth = 300;
    private const int CardHeight = 150;
    private const int Gap = 16;
    private const int Padding = 14;
    private const float NameScale = 3f;
    private const float SmallScale = 1.5f;

    private static readonly Color CardBack = new Color(14, 18, 28) * 0.92f;
    private static readonly Color CardHover = new Color(30, 38, 56) * 0.95f;
    private static readonly Color Border = new(110, 95, 70);
    private static readonly Color Selected = new(255, 215, 110);
    private static readonly Color Name = new(240, 215, 150);
    private static readonly Color Text = new(225, 228, 235);
    private static readonly Color Muted = new(150, 155, 170);

    private static readonly Keys[] NumberKeys = { Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6 };

    private readonly PrimitiveBatch _batch;

    public WeaponPicker(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    /// <summary>The weapon picked this frame (clicked, or its number pressed), if any.</summary>
    public WeaponOffer? Update(InputState input, HudView hud)
    {
        for (var i = 0; i < WeaponCatalog.All.Count && i < NumberKeys.Length; i++)
        {
            if (input.WasKeyPressed(NumberKeys[i]))
                return WeaponCatalog.All[i];
        }

        if (!input.WasLeftMousePressed)
            return null;
        var mouse = hud.FromScreen(input.Mouse.Position);
        for (var i = 0; i < WeaponCatalog.All.Count; i++)
        {
            if (Card(hud, i).Contains(mouse))
                return WeaponCatalog.All[i];
        }
        return null;
    }

    /// <param name="selectedId">The weapon already chosen, outlined; null for none.</param>
    public void Draw(InputState input, HudView hud, string? selectedId)
    {
        _batch.Begin(hud.Transform);
        var mouse = hud.FromScreen(input.Mouse.Position);
        for (var i = 0; i < WeaponCatalog.All.Count; i++)
        {
            var weapon = WeaponCatalog.All[i];
            var card = Card(hud, i);
            var selected = weapon.Id == selectedId;
            Fill(card, card.Contains(mouse) ? CardHover : CardBack);
            Outline(card, selected ? Selected : Border);
            if (selected)
                Outline(new Rectangle(card.X + 2, card.Y + 2, card.Width - 4, card.Height - 4), Selected * 0.6f);

            var x = card.X + Padding;
            var y = card.Y + Padding;
            PixelFont.Draw(_batch, $"{i + 1}", new Vector2(card.Right - Padding - PixelFont.Measure($"{i + 1}", 2f), y), 2f, Muted);
            PixelFont.Draw(_batch, weapon.Ability.Name, new Vector2(x, y), NameScale, Name);
            y += (int)PixelFont.Height(NameScale) + 12;
            foreach (var line in PixelFont.Wrap(weapon.Ability.Description, SmallScale, CardWidth - Padding * 2))
            {
                PixelFont.Draw(_batch, line, new Vector2(x, y), SmallScale, Text);
                y += (int)PixelFont.Height(SmallScale) + 5;
            }

            var tree = $"{SkillTrees.For(weapon.Id).Count} SKILLS TO BUY ALONG THE WAY";
            var footer = selected ? "CHOSEN" : tree;
            PixelFont.Draw(_batch, footer, new Vector2(x, card.Bottom - Padding - PixelFont.Height(SmallScale)), SmallScale,
                selected ? Selected : Muted);
        }
        _batch.Flush();
    }

    private static Rectangle Card(HudView hud, int index)
    {
        var count = WeaponCatalog.All.Count;
        var total = count * CardWidth + (count - 1) * Gap;
        var left = (hud.Viewport.Width - total) / 2;
        var top = (int)(hud.Viewport.Height * 0.3f) + 120; // under the status banner
        return new Rectangle(left + index * (CardWidth + Gap), top, CardWidth, CardHeight);
    }

    private void Fill(Rectangle rect, Color color)
    {
        Span<Vector2> corners = stackalloc Vector2[] { new(rect.Left, rect.Top), new(rect.Right, rect.Top), new(rect.Right, rect.Bottom), new(rect.Left, rect.Bottom) };
        _batch.FillConvex(corners, color);
    }

    private void Outline(Rectangle rect, Color color)
    {
        Span<Vector2> corners = stackalloc Vector2[] { new(rect.Left, rect.Top), new(rect.Right, rect.Top), new(rect.Right, rect.Bottom), new(rect.Left, rect.Bottom) };
        _batch.Outline(corners, color);
    }
}
