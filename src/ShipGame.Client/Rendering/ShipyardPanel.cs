using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ShipGame.Client.Input;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Trading;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Pops up while the player is anchored at a shipyard: first a choice between plundering the island and trading,
/// then the upgrade list or the trade contracts on offer. Every action is sent as a command; the simulation decides
/// whether it's allowed. While contracts are showing, <see cref="CurrentRoutes"/> says what to chart beside the panel.
/// </summary>
public sealed class ShipyardPanel
{
    private enum Page
    {
        Choice,
        Upgrades,
        Contracts,
    }

    private sealed record Button(Rectangle Bounds, string Label, bool Enabled, Command? Command = null, Page? GoTo = null);

    private const int PanelWidth = 400;
    private const int Padding = 16;
    private const int ButtonHeight = 40;
    private const int RowHeight = 52;
    private const int ContractRowHeight = 64;
    private const int BadgeSize = 22;
    private const int PanelLeft = 24;
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
    private static readonly Color Short = new(235, 95, 80);
    private static readonly Color RowHover = new Color(255, 255, 255) * 0.06f;

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

        var title = _page switch { Page.Choice => "SHIPYARD", Page.Upgrades => "UPGRADES", _ => "CONTRACTS" };
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
        else if (_page == Page.Upgrades)
        {
            DrawUpgradeRows(ship, panel);
        }

        var mouse = hud.FromScreen(input.Mouse.Position);
        if (_page == Page.Contracts)
            DrawContractRows(world, ship, shipyard, panel, HoveredContract(world, shipyard, viewport, mouse));
        foreach (var button in buttons)
            DrawButton(button, button.Enabled && button.Bounds.Contains(mouse));

        _batch.Flush();
    }

    /// <summary>The panel rectangle and its buttons for the current page. Shared by drawing and click handling.</summary>
    private List<Button> Layout(World world, Ship ship, Island shipyard, Viewport viewport, out Rectangle panel)
    {
        var buttons = new List<Button>();
        var headerHeight = (int)PixelFont.Height(TitleScale) + Padding * 2;
        var x = PanelLeft;

        if (_page == Page.Choice)
        {
            var height = headerHeight + ButtonHeight * 3 + Padding * 4 + (int)PixelFont.Height(SmallScale) + Padding;
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

            y += ButtonHeight + Padding;
            buttons.Add(new Button(new Rectangle(panel.X + Padding, y, PanelWidth - Padding * 2, ButtonHeight), "TRADE CONTRACTS",
                Enabled: true, GoTo: Page.Contracts));
            return buttons;
        }

        if (_page == Page.Contracts)
        {
            var offers = world.Trade.OffersAt(shipyard.Id);
            panel = ContractsPanel(viewport, offers.Count);
            var cash = Gold(world, ship);
            for (var i = 0; i < offers.Count; i++)
            {
                var offer = offers[i];
                var row = ContractRow(panel, i);
                var bounds = new Rectangle(row.Right - 84, row.Y + (row.Height - ButtonHeight) / 2, 84, ButtonHeight);
                buttons.Add(new Button(bounds, "BUY", Enabled: cash >= offer.Cost && ship.FreeCargo >= offer.CargoUnits,
                    Command: new PurchaseContractCommand(ship.OwnerPlayerId ?? 0, offer.Id)));
            }
            buttons.Add(new Button(new Rectangle(panel.X + Padding, panel.Bottom - Padding - ButtonHeight, 120, ButtonHeight), "BACK",
                Enabled: true, GoTo: Page.Choice));
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

    private static int HeaderHeight => (int)PixelFont.Height(TitleScale) + Padding * 2;

    /// <summary>Header, a line for the hold, one row per offer, and the back button.</summary>
    private static Rectangle ContractsPanel(Viewport viewport, int offers)
    {
        var height = HeaderHeight + HoldLineHeight + offers * ContractRowHeight + Padding + ButtonHeight + Padding;
        return new Rectangle(PanelLeft, (viewport.Height - height) / 2, PanelWidth, height);
    }

    private static int HoldLineHeight => (int)PixelFont.Height(SmallScale) + Padding;

    private static Rectangle ContractRow(Rectangle panel, int index) =>
        new(panel.X + Padding / 2, panel.Y + HeaderHeight + HoldLineHeight + index * ContractRowHeight, PanelWidth - Padding, ContractRowHeight - 4);

    private static int Gold(World world, Ship ship) =>
        ship.OwnerPlayerId is { } id && world.Players.TryGetValue(id, out var player) ? player.Gold : 0;

    /// <summary>The offer whose row the mouse is over, if any.</summary>
    private TradeContract? HoveredContract(World world, Island shipyard, Viewport viewport, Point mouse)
    {
        var offers = world.Trade.OffersAt(shipyard.Id);
        var panel = ContractsPanel(viewport, offers.Count);
        for (var i = 0; i < offers.Count; i++)
        {
            if (ContractRow(panel, i).Contains(mouse))
                return offers[i];
        }
        return null;
    }

    /// <summary>
    /// While the contracts page is up: the offers to chart on the map, the one under the mouse highlighted. Null
    /// on any other page, or away from a shipyard.
    /// </summary>
    public RoutePreview? CurrentRoutes(World world, Ship? ship, InputState input, HudView hud)
    {
        if (_page != Page.Contracts || ship is null || Shipyards.DockedAt(world, ship) is not { } shipyard || shipyard.Id != _islandId)
            return null;
        var hovered = HoveredContract(world, shipyard, hud.Viewport, hud.FromScreen(input.Mouse.Position));
        return new RoutePreview(shipyard, world.Trade.OffersAt(shipyard.Id), hovered?.Id);
    }

    /// <summary>Where the route map goes: the rest of the screen to the right of the panel.</summary>
    public static Rectangle RouteMapArea(HudView hud)
    {
        var left = PanelLeft + PanelWidth + Padding;
        return new Rectangle(left, 24, Math.Max(0, hud.Viewport.Width - left - 24), Math.Max(0, hud.Viewport.Height - 48));
    }

    private void DrawContractRows(World world, Ship ship, Island shipyard, Rectangle panel, TradeContract? hovered)
    {
        var holdText = $"HOLD {ship.CargoUsed}/{ship.CargoCapacity}";
        PixelFont.Draw(_batch, holdText, new Vector2(panel.X + Padding, panel.Y + HeaderHeight - Padding / 2), SmallScale, Muted);

        var offers = world.Trade.OffersAt(shipyard.Id);
        if (offers.Count == 0)
            PixelFont.Draw(_batch, "NO CONTRACTS ON OFFER", new Vector2(panel.X + Padding, ContractRow(panel, 0).Y), LabelScale, Muted);

        var gold = Gold(world, ship);
        for (var i = 0; i < offers.Count; i++)
        {
            var offer = offers[i];
            var row = ContractRow(panel, i);
            if (offer == hovered)
                FillRect(row, RowHover);

            var color = TradeMarkers.OfferColor(i);
            var badge = new Vector2(row.X + Padding / 2 + BadgeSize / 2f, row.Y + 4 + BadgeSize / 2f);
            TradeMarkers.DrawBadge(_batch, badge, TradeMarkers.Letter(i), color, BadgeSize, LabelScale);

            var left = row.X + Padding / 2 + BadgeSize + 10;
            var destination = world.FindIsland(offer.DestinationIslandId);
            var name = destination?.Name ?? "?";
            PixelFont.Draw(_batch, name, new Vector2(left, row.Y + 8), LabelScale, color);

            // Terms, with whatever we can't cover picked out.
            var y = row.Y + 8 + PixelFont.Height(LabelScale) + 6;
            var x = (float)left;
            x = DrawPiece($"COST {offer.Cost}  ", x, y, gold >= offer.Cost ? Text : Short);
            x = DrawPiece($"PAYS {offer.Payout}  ", x, y, PipOn);
            DrawPiece($"CARGO {offer.CargoUnits}", x, y, ship.FreeCargo >= offer.CargoUnits ? Text : Short);

            if (destination is not null)
            {
                var distance = Contracts.RouteDistance(shipyard, destination);
                var route = $"{distance:0} TILES {TradeMarkers.BearingName(shipyard.Center, destination.Center)}";
                PixelFont.Draw(_batch, route, new Vector2(left, y + PixelFont.Height(SmallScale) + 5), SmallScale, Muted);
            }
        }
    }

    private float DrawPiece(string text, float x, float y, Color color)
    {
        PixelFont.Draw(_batch, text, new Vector2(x, y), SmallScale, color);
        return x + PixelFont.Measure(text, SmallScale);
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
