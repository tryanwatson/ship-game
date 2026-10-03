using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ShipGame.Client.Input;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Pops up while the player is anchored at a shipyard: first a choice between plundering the island and trading,
/// then the upgrade list. Every action is sent as a command; the simulation decides whether it's allowed.
/// </summary>
public sealed class ShipyardPanel
{
    private enum Page
    {
        Choice,
        Upgrades,
    }

    private sealed record Button(Rectangle Bounds, string Label, bool Enabled, Command? Command = null, Page? GoTo = null);

    private const int PanelWidth = 400;
    private const int Padding = 16;
    private const int ButtonHeight = 40;
    private const int RowHeight = 52;
    private const float TitleScale = 3f;
    private const float LabelScale = 2f;
    private const float SmallScale = 1.5f;

    private static readonly Color PanelBack = new Color(14, 18, 28) * 0.92f;
    private static readonly Color PanelBorder = new(110, 95, 70);
    private static readonly Color Title = new(240, 215, 150);
    private static readonly Color Text = new(225, 228, 235);
    private static readonly Color Muted = new(150, 155, 170);
    private static readonly Color ButtonBack = new(60, 70, 92);
    private static readonly Color ButtonHover = new(84, 98, 128);
    private static readonly Color ButtonDisabled = new(38, 42, 52);
    private static readonly Color ButtonBorder = new(130, 140, 165);
    private static readonly Color PipOn = new(240, 200, 90);
    private static readonly Color PipOff = new Color(240, 200, 90) * 0.18f;

    private readonly PrimitiveBatch _batch;
    private Page _page;
    private int? _islandId;

    public ShipyardPanel(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public void Update(World world, Ship? ship, InputState input, HudView hud, Action<Command> send)
    {
        var viewport = hud.Viewport;
        var shipyard = ship is null ? null : Shipyards.DockedAt(world, ship);
        if (shipyard is null || ship is null)
        {
            _islandId = null;
            _page = Page.Choice;
            return;
        }

        if (shipyard.Id != _islandId)
        {
            _islandId = shipyard.Id;
            _page = Page.Choice;
        }

        if (!input.WasLeftMousePressed)
            return;

        var mouse = hud.FromScreen(input.Mouse.Position);
        foreach (var button in Layout(world, ship, shipyard, viewport, out _))
        {
            if (!button.Enabled || !button.Bounds.Contains(mouse))
                continue;
            if (button.Command is not null)
                send(button.Command);
            if (button.GoTo is { } page)
                _page = page;
            break;
        }
    }

    public void Draw(World world, Ship? ship, InputState input, HudView hud)
    {
        var viewport = hud.Viewport;
        var shipyard = ship is null ? null : Shipyards.DockedAt(world, ship);
        if (shipyard is null || ship is null)
            return;

        _batch.Begin(hud.Transform);
        var buttons = Layout(world, ship, shipyard, viewport, out var panel);

        FillRect(panel, PanelBack);
        OutlineRect(panel, PanelBorder);

        var title = _page == Page.Choice ? "SHIPYARD" : "UPGRADES";
        PixelFont.Draw(_batch, title, new Vector2(panel.X + Padding, panel.Y + Padding), TitleScale, Title);

        var gold = ship.OwnerPlayerId is { } id && world.Players.TryGetValue(id, out var player) ? player.Gold : 0;
        var goldText = $"GOLD {gold}";
        PixelFont.Draw(_batch, goldText,
            new Vector2(panel.Right - Padding - PixelFont.Measure(goldText, LabelScale), panel.Y + Padding + 4), LabelScale, PipOn);

        if (_page == Page.Choice)
        {
            var hint = "RAISE ANCHOR X TO LEAVE";
            PixelFont.Draw(_batch, hint, new Vector2(panel.X + Padding, panel.Bottom - Padding - PixelFont.Height(SmallScale)), SmallScale, Muted);
        }
        else
        {
            DrawUpgradeRows(ship, panel);
        }

        var mouse = hud.FromScreen(input.Mouse.Position);
        foreach (var button in buttons)
            DrawButton(button, button.Enabled && button.Bounds.Contains(mouse));

        _batch.Flush();
    }

    /// <summary>The panel rectangle and its buttons for the current page. Shared by drawing and click handling.</summary>
    private List<Button> Layout(World world, Ship ship, Island shipyard, Viewport viewport, out Rectangle panel)
    {
        var buttons = new List<Button>();
        var headerHeight = (int)PixelFont.Height(TitleScale) + Padding * 2;
        var x = 24;

        if (_page == Page.Choice)
        {
            var height = headerHeight + ButtonHeight * 2 + Padding * 3 + (int)PixelFont.Height(SmallScale) + Padding;
            panel = new Rectangle(x, (viewport.Height - height) / 2, PanelWidth, height);
            var y = panel.Y + headerHeight;

            var cooldown = world.PlunderCooldownTicks(shipyard);
            var plundering = ship.PlunderIslandId == shipyard.Id;
            var plunderLabel = plundering ? "PLUNDERING..."
                : cooldown > 0 ? $"PLUNDER  READY IN {(int)MathF.Ceiling(cooldown / (float)SimConstants.TickRate)}S"
                : $"PLUNDER  +{shipyard.PlunderGold} GOLD";
            buttons.Add(new Button(new Rectangle(panel.X + Padding, y, PanelWidth - Padding * 2, ButtonHeight), plunderLabel,
                Enabled: !plundering && cooldown == 0, Command: new ChoosePlunderCommand(ship.OwnerPlayerId ?? 0)));

            y += ButtonHeight + Padding;
            buttons.Add(new Button(new Rectangle(panel.X + Padding, y, PanelWidth - Padding * 2, ButtonHeight), "UPGRADE SHIP",
                Enabled: true, GoTo: Page.Upgrades));
            return buttons;
        }

        var listHeight = UpgradeCatalog.All.Count * RowHeight;
        var pageHeight = headerHeight + listHeight + Padding + ButtonHeight + Padding;
        panel = new Rectangle(x, (viewport.Height - pageHeight) / 2, PanelWidth, pageHeight);

        var gold = ship.OwnerPlayerId is { } id && world.Players.TryGetValue(id, out var player) ? player.Gold : 0;
        for (var i = 0; i < UpgradeCatalog.All.Count; i++)
        {
            var upgrade = UpgradeCatalog.All[i];
            var level = Shipyards.Level(ship, upgrade);
            var maxed = level >= upgrade.MaxLevel;
            var cost = upgrade.CostAt(level);
            var rowTop = panel.Y + headerHeight + i * RowHeight;
            var bounds = new Rectangle(panel.Right - Padding - 84, rowTop + (RowHeight - ButtonHeight) / 2 - 4, 84, ButtonHeight);
            buttons.Add(new Button(bounds, maxed ? "MAX" : $"{cost}G", Enabled: !maxed && gold >= cost,
                Command: new PurchaseUpgradeCommand(ship.OwnerPlayerId ?? 0, upgrade.Id)));
        }

        buttons.Add(new Button(new Rectangle(panel.X + Padding, panel.Bottom - Padding - ButtonHeight, 120, ButtonHeight), "BACK",
            Enabled: true, GoTo: Page.Choice));
        return buttons;
    }

    private void DrawUpgradeRows(Ship ship, Rectangle panel)
    {
        var headerHeight = (int)PixelFont.Height(TitleScale) + Padding * 2;
        for (var i = 0; i < UpgradeCatalog.All.Count; i++)
        {
            var upgrade = UpgradeCatalog.All[i];
            var level = Shipyards.Level(ship, upgrade);
            var top = panel.Y + headerHeight + i * RowHeight;

            PixelFont.Draw(_batch, upgrade.Name, new Vector2(panel.X + Padding, top), LabelScale, Text);
            PixelFont.Draw(_batch, upgrade.Effect, new Vector2(panel.X + Padding, top + PixelFont.Height(LabelScale) + 6), SmallScale, Muted);

            // Level pips between the name and the buy button.
            for (var pip = 0; pip < upgrade.MaxLevel; pip++)
            {
                var pipRect = new Rectangle(panel.X + 196 + pip * 14, top + 4, 10, 10);
                FillRect(pipRect, pip < level ? PipOn : PipOff);
            }
        }
    }

    private void DrawButton(Button button, bool hovered)
    {
        FillRect(button.Bounds, !button.Enabled ? ButtonDisabled : hovered ? ButtonHover : ButtonBack);
        OutlineRect(button.Bounds, ButtonBorder * (button.Enabled ? 1f : 0.4f));

        var width = PixelFont.Measure(button.Label, LabelScale);
        var position = new Vector2(
            button.Bounds.Center.X - width / 2f,
            button.Bounds.Center.Y - PixelFont.Height(LabelScale) / 2f);
        PixelFont.Draw(_batch, button.Label, position, LabelScale, button.Enabled ? Text : Muted);
    }

    private void FillRect(Rectangle rect, Color color)
    {
        Span<Vector2> corners = stackalloc Vector2[] { new(rect.Left, rect.Top), new(rect.Right, rect.Top), new(rect.Right, rect.Bottom), new(rect.Left, rect.Bottom) };
        _batch.FillConvex(corners, color);
    }

    private void OutlineRect(Rectangle rect, Color color)
    {
        Span<Vector2> corners = stackalloc Vector2[] { new(rect.Left, rect.Top), new(rect.Right, rect.Top), new(rect.Right, rect.Bottom), new(rect.Left, rect.Bottom) };
        _batch.Outline(corners, color);
    }
}
