using System;
using System.Collections.Generic;
using System.Linq;
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
/// then the general upgrades, the weapons (and each one's skill tree), or the trade contracts on offer. Every action is sent as a command; the simulation decides
/// whether it's allowed. While contracts are showing, <see cref="CurrentRoutes"/> says what to chart beside the panel.
/// </summary>
public sealed class ShipyardPanel
{
    private enum Page
    {
        Choice,
        Upgrades,
        Weapons,
        Tree,
        Contracts,
    }

    /// <param name="Tree">With <see cref="GoTo"/> <see cref="Page.Tree"/>: whose tree to open.</param>
    /// <param name="Skill">A skill tree node: drawn as a node rather than a button.</param>
    private sealed record Button(Rectangle Bounds, string Label, bool Enabled, Command? Command = null, Page? GoTo = null,
        string? Tree = null, SkillDefinition? Skill = null);

    // Weapons and skill tree pages are wider, to fit a tree's two branches side by side.
    private const int WidePanelWidth = 540;
    private const int WeaponRowHeight = 100;
    private const int NodeHeight = 58;
    private const int TierGap = 30;
    private const int NodeGap = 40;
    private const int DetailHeight = 128;

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
    private static readonly Color Owned = new(240, 200, 90);
    private static readonly Color NodeOwned = new Color(120, 92, 30) * 0.9f;
    private static readonly Color NodeExcluded = new Color(70, 30, 30) * 0.9f;
    private static readonly Color Link = new(130, 140, 165);
    private static readonly Color Good = new(130, 220, 140);

    private readonly PrimitiveBatch _batch;
    private Page _page;
    private int? _islandId;
    private string _treeAbilityId = WeaponCatalog.All[0].Id;

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
            if (button.Tree is { } tree)
                _treeAbilityId = tree;
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

        var title = _page switch
        {
            Page.Choice => "SHIPYARD",
            Page.Upgrades => "UPGRADES",
            Page.Weapons => "WEAPONS",
            Page.Tree => WeaponCatalog.Find(_treeAbilityId)?.Ability.Name ?? "SKILLS",
            _ => "CONTRACTS",
        };
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
            DrawUpgradeRows(ship, shipyard, panel);
        }

        var mouse = hud.FromScreen(input.Mouse.Position);
        if (_page == Page.Weapons)
            DrawWeaponRows(ship, panel);
        else if (_page == Page.Tree)
            DrawTree(world, ship, panel, buttons, mouse);
        if (_page == Page.Contracts)
            DrawContractRows(world, ship, shipyard, panel, HoveredContract(world, shipyard, viewport, mouse));
        foreach (var button in buttons.Where(b => b.Skill is null))
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
            var height = headerHeight + ButtonHeight * 5 + Padding * 6 + (int)PixelFont.Height(SmallScale) + Padding;
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
            var repairCost = Shipyards.RepairCost(ship);
            buttons.Add(new Button(new Rectangle(panel.X + Padding, y, PanelWidth - Padding * 2, ButtonHeight),
                repairCost == 0 ? "FULL HEALTH" : $"REPAIR HULL  {repairCost}G",
                Enabled: repairCost > 0 && Gold(world, ship) >= repairCost, Command: new PurchaseRepairCommand(ship.OwnerPlayerId ?? 0)));

            y += ButtonHeight + Padding;
            buttons.Add(new Button(new Rectangle(panel.X + Padding, y, PanelWidth - Padding * 2, ButtonHeight), "UPGRADE SHIP",
                Enabled: true, GoTo: Page.Upgrades));

            y += ButtonHeight + Padding;
            buttons.Add(new Button(new Rectangle(panel.X + Padding, y, PanelWidth - Padding * 2, ButtonHeight), "WEAPONS AND SKILLS",
                Enabled: true, GoTo: Page.Weapons));

            y += ButtonHeight + Padding;
            buttons.Add(new Button(new Rectangle(panel.X + Padding, y, PanelWidth - Padding * 2, ButtonHeight), "TRADE CONTRACTS",
                Enabled: true, GoTo: Page.Contracts));
            return buttons;
        }

        if (_page == Page.Weapons)
            return WeaponButtons(world, ship, viewport, out panel);
        if (_page == Page.Tree)
            return TreeButtons(world, ship, viewport, out panel);

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
            var stocked = level < Shipyards.StockedLevels(shipyard, upgrade);
            var cost = upgrade.CostAt(level);
            var rowTop = panel.Y + headerHeight + i * RowHeight;
            var bounds = new Rectangle(panel.Right - Padding - 84, rowTop + (RowHeight - ButtonHeight) / 2 - 4, 84, ButtonHeight);
            // Past what this yard stocks, the next level is sold further north.
            buttons.Add(new Button(bounds, maxed ? "MAX" : stocked ? $"{cost}G" : "NORTH", Enabled: !maxed && stocked && gold >= cost,
                Command: new PurchaseUpgradeCommand(ship.OwnerPlayerId ?? 0, upgrade.Id)));
        }

        buttons.Add(new Button(new Rectangle(panel.X + Padding, panel.Bottom - Padding - ButtonHeight, 120, ButtonHeight), "BACK",
            Enabled: true, GoTo: Page.Choice));
        return buttons;
    }

    private static int HeaderHeight => (int)PixelFont.Height(TitleScale) + Padding * 2;

    // ---- Weapons -----------------------------------------------------------------------------------------

    private static Rectangle WeaponsPanel(Viewport viewport)
    {
        var height = HeaderHeight + WeaponCatalog.All.Count * WeaponRowHeight + Padding + ButtonHeight + Padding;
        return new Rectangle(PanelLeft, (viewport.Height - height) / 2, WidePanelWidth, height);
    }

    private static Rectangle WeaponRow(Rectangle panel, int index) =>
        new(panel.X + Padding, panel.Y + HeaderHeight + index * WeaponRowHeight, panel.Width - Padding * 2, WeaponRowHeight - 8);

    /// <summary>Per weapon: buy it (while locked) and open its skill tree.</summary>
    private static List<Button> WeaponButtons(World world, Ship ship, Viewport viewport, out Rectangle panel)
    {
        panel = WeaponsPanel(viewport);
        var buttons = new List<Button>();
        var gold = Gold(world, ship);
        for (var i = 0; i < WeaponCatalog.All.Count; i++)
        {
            var weapon = WeaponCatalog.All[i];
            var row = WeaponRow(panel, i);
            buttons.Add(new Button(new Rectangle(row.Right - 100, row.Y, 100, ButtonHeight), "SKILLS", Enabled: true,
                GoTo: Page.Tree, Tree: weapon.Id));
            if (!ship.HasAbility(weapon.Id))
            {
                buttons.Add(new Button(new Rectangle(row.Right - 100 - 8 - 100, row.Y, 100, ButtonHeight), $"{weapon.UnlockCost}G",
                    Enabled: gold >= weapon.UnlockCost && ship.FreeAbilitySlot is not null,
                    Command: new UnlockAbilityCommand(ship.OwnerPlayerId ?? 0, weapon.Id)));
            }
        }
        buttons.Add(new Button(new Rectangle(panel.X + Padding, panel.Bottom - Padding - ButtonHeight, 120, ButtonHeight), "BACK",
            Enabled: true, GoTo: Page.Choice));
        return buttons;
    }

    private void DrawWeaponRows(Ship ship, Rectangle panel)
    {
        for (var i = 0; i < WeaponCatalog.All.Count; i++)
        {
            var weapon = WeaponCatalog.All[i];
            var row = WeaponRow(panel, i);
            PixelFont.Draw(_batch, weapon.Ability.Name, new Vector2(row.X, row.Y + 2), LabelScale, Text);

            var slot = Array.FindIndex(ship.Abilities.ToArray(), a => a?.Definition.Id == weapon.Id);
            var owned = ship.Skills.Count(s => s.AbilityId == weapon.Id);
            var status = slot >= 0 ? $"UNLOCKED - KEY {slot + 1} - {owned} SKILLS" : "LOCKED";
            PixelFont.Draw(_batch, status, new Vector2(row.X, row.Y + 8 + PixelFont.Height(LabelScale)), SmallScale, slot >= 0 ? Owned : Muted);

            var y = row.Y + ButtonHeight + 8f;
            foreach (var line in PixelFont.Wrap(weapon.Ability.Description, SmallScale, row.Width))
            {
                PixelFont.Draw(_batch, line, new Vector2(row.X, y), SmallScale, Muted);
                y += PixelFont.Height(SmallScale) + 5;
            }
        }
    }

    // ---- Skill tree --------------------------------------------------------------------------------------

    /// <summary>The tree's skills by tier (1 first), each tier in catalog order.</summary>
    private static List<List<SkillDefinition>> Tiers(string abilityId)
    {
        var tiers = new List<List<SkillDefinition>>();
        foreach (var skill in SkillTrees.For(abilityId))
        {
            var tier = SkillTrees.TierOf(skill);
            while (tiers.Count < tier)
                tiers.Add(new List<SkillDefinition>());
            tiers[tier - 1].Add(skill);
        }
        return tiers;
    }

    private static int StatusLineHeight => (int)PixelFont.Height(LabelScale) + Padding;

    private static Rectangle TreePanel(Viewport viewport, int tiers)
    {
        var height = HeaderHeight + StatusLineHeight + tiers * (NodeHeight + TierGap) + DetailHeight + ButtonHeight + Padding;
        return new Rectangle(PanelLeft, (viewport.Height - height) / 2, WidePanelWidth, height);
    }

    /// <summary>The weapon's unlock button (if locked), a node per skill (click to buy), and back.</summary>
    private List<Button> TreeButtons(World world, Ship ship, Viewport viewport, out Rectangle panel)
    {
        var tiers = Tiers(_treeAbilityId);
        panel = TreePanel(viewport, tiers.Count);
        var buttons = new List<Button>();
        var gold = Gold(world, ship);
        var playerId = ship.OwnerPlayerId ?? 0;

        if (WeaponCatalog.Find(_treeAbilityId) is { } weapon && !ship.HasAbility(weapon.Id))
        {
            buttons.Add(new Button(new Rectangle(panel.Right - Padding - 160, panel.Y + HeaderHeight - 8, 160, ButtonHeight - 6),
                $"UNLOCK {weapon.UnlockCost}G", Enabled: gold >= weapon.UnlockCost && ship.FreeAbilitySlot is not null,
                Command: new UnlockAbilityCommand(playerId, weapon.Id)));
        }

        var columnWidth = (panel.Width - Padding * 2 - NodeGap) / 2;
        for (var t = 0; t < tiers.Count; t++)
        {
            var top = panel.Y + HeaderHeight + StatusLineHeight + t * (NodeHeight + TierGap);
            var count = tiers[t].Count;
            for (var i = 0; i < count; i++)
            {
                var skill = tiers[t][i];
                // Two to a tier side by side; a lone skill (the capstone) centered beneath them.
                var width = count == 1 ? columnWidth : (panel.Width - Padding * 2 - NodeGap * (count - 1)) / count;
                var left = count == 1 ? panel.X + (panel.Width - width) / 2 : panel.X + Padding + i * (width + NodeGap);
                var status = Shipyards.StatusOf(ship, skill);
                buttons.Add(new Button(new Rectangle(left, top, width, NodeHeight), skill.Name,
                    Enabled: status == SkillStatus.Available && gold >= skill.Cost,
                    Command: new PurchaseSkillCommand(playerId, skill.Id), Skill: skill));
            }
        }

        buttons.Add(new Button(new Rectangle(panel.X + Padding, panel.Bottom - Padding - ButtonHeight, 120, ButtonHeight), "BACK",
            Enabled: true, GoTo: Page.Weapons));
        return buttons;
    }

    private void DrawTree(World world, Ship ship, Rectangle panel, List<Button> buttons, Point mouse)
    {
        var weapon = WeaponCatalog.Find(_treeAbilityId);
        var slot = weapon is null ? -1 : Array.FindIndex(ship.Abilities.ToArray(), a => a?.Definition.Id == weapon.Id);
        var statusText = slot >= 0 ? $"UNLOCKED - KEY {slot + 1}" : "WEAPON LOCKED";
        PixelFont.Draw(_batch, statusText, new Vector2(panel.X + Padding, panel.Y + HeaderHeight), LabelScale, slot >= 0 ? Owned : Muted);

        var nodes = buttons.Where(b => b.Skill is not null).ToList();
        Rectangle? NodeOf(string id) => nodes.FirstOrDefault(n => n.Skill!.Id == id)?.Bounds;

        // Links from each prerequisite down to what it leads to, then "OR" between rival choices on a tier.
        foreach (var node in nodes)
        {
            foreach (var prerequisite in node.Skill!.Prerequisites)
            {
                if (NodeOf(prerequisite) is not { } from)
                    continue;
                var owned = ship.HasSkill(prerequisite) && ship.HasSkill(node.Skill.Id);
                _batch.Line(new Vector2(from.Center.X, from.Bottom), new Vector2(node.Bounds.Center.X, node.Bounds.Top), owned ? Owned : Link * 0.6f);
            }
            foreach (var rival in node.Skill.Excludes)
            {
                if (NodeOf(rival) is not { } other || other.X <= node.Bounds.X || other.Y != node.Bounds.Y)
                    continue;
                var gapCenter = (node.Bounds.Right + other.Left) / 2f;
                PixelFont.Draw(_batch, "OR", new Vector2(gapCenter - PixelFont.Measure("OR", SmallScale) / 2f,
                    node.Bounds.Center.Y - PixelFont.Height(SmallScale) / 2f), SmallScale, Short);
            }
        }

        var gold = Gold(world, ship);
        SkillDefinition? hovered = null;
        foreach (var node in nodes)
        {
            var skill = node.Skill!;
            var status = Shipyards.StatusOf(ship, skill);
            var hover = node.Bounds.Contains(mouse);
            if (hover)
                hovered = skill;

            var back = status switch
            {
                SkillStatus.Owned => NodeOwned,
                SkillStatus.Excluded => NodeExcluded,
                SkillStatus.Available when node.Enabled => hover ? ButtonHover : ButtonBack,
                _ => ButtonDisabled,
            };
            FillRect(node.Bounds, back);
            OutlineRect(node.Bounds, status == SkillStatus.Owned ? Owned : hover ? Text : ButtonBorder * 0.6f);

            var nameColor = status is SkillStatus.Owned or SkillStatus.Available ? Text : Muted;
            PixelFont.Draw(_batch, skill.Name, new Vector2(node.Bounds.X + 10, node.Bounds.Y + 10), LabelScale, nameColor);
            var (line, color) = status switch
            {
                SkillStatus.Owned => ("OWNED", Owned),
                SkillStatus.Excluded => ("LOCKED OUT", Short),
                SkillStatus.WeaponLocked => ($"{skill.Cost}G - WEAPON LOCKED", Muted),
                SkillStatus.NeedsPrerequisite => ($"{skill.Cost}G - NEEDS PREVIOUS", Muted),
                _ => ($"{skill.Cost}G - CLICK TO BUY", gold >= skill.Cost ? Good : Short),
            };
            PixelFont.Draw(_batch, line, new Vector2(node.Bounds.X + 10, node.Bounds.Bottom - 10 - PixelFont.Height(SmallScale)), SmallScale, color);
        }

        DrawSkillDetail(ship, hovered, new Rectangle(panel.X + Padding,
            nodes.Count == 0 ? panel.Y + HeaderHeight + StatusLineHeight : nodes.Max(n => n.Bounds.Bottom) + TierGap / 2,
            panel.Width - Padding * 2, DetailHeight - TierGap / 2));
    }

    /// <summary>What the skill under the mouse does, what it needs, what it leads to, and what buying it rules out.</summary>
    private void DrawSkillDetail(Ship ship, SkillDefinition? skill, Rectangle area)
    {
        _batch.Line(new Vector2(area.X, area.Y), new Vector2(area.Right, area.Y), PanelBorder);
        var y = area.Y + 10f;
        if (skill is null)
        {
            foreach (var hint in PixelFont.Wrap("POINT AT A SKILL TO SEE WHAT IT DOES. CHOICES JOINED BY OR SHUT EACH OTHER OUT.", SmallScale, area.Width))
            {
                PixelFont.Draw(_batch, hint, new Vector2(area.X, y), SmallScale, Muted);
                y += PixelFont.Height(SmallScale) + 5;
            }
            return;
        }

        foreach (var line in PixelFont.Wrap(skill.Description, SmallScale, area.Width))
        {
            PixelFont.Draw(_batch, line, new Vector2(area.X, y), SmallScale, Text);
            y += PixelFont.Height(SmallScale) + 5;
        }
        y += 4;

        string Names(IEnumerable<string> ids) => string.Join(", ", ids.Select(id => SkillTrees.Find(id)?.Name ?? id));
        var facts = new List<(string Text, Color Color)>();
        if (skill.Requires.Count > 0)
            facts.Add(($"REQUIRES {Names(skill.Requires)}", Muted));
        if (skill.RequiresAny.Count > 0)
            facts.Add(($"REQUIRES ONE OF {Names(skill.RequiresAny)}", Muted));
        var leadsTo = SkillTrees.LeadsTo(skill);
        if (leadsTo.Count > 0)
            facts.Add(($"LEADS TO {Names(leadsTo.Select(s => s.Id))}", Muted));
        if (!ship.HasSkill(skill.Id))
        {
            var closes = SkillTrees.WouldCloseOff(ship.Skills.Select(s => s.Id).ToList(), skill);
            if (closes.Count > 0)
                facts.Add(($"LOCKS OUT {Names(closes.Select(s => s.Id))}", Short));
        }
        foreach (var (text, color) in facts)
        {
            foreach (var line in PixelFont.Wrap(text, SmallScale, area.Width))
            {
                PixelFont.Draw(_batch, line, new Vector2(area.X, y), SmallScale, color);
                y += PixelFont.Height(SmallScale) + 5;
            }
        }
    }

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

    private void DrawUpgradeRows(Ship ship, Island shipyard, Rectangle panel)
    {
        var headerHeight = (int)PixelFont.Height(TitleScale) + Padding * 2;
        for (var i = 0; i < UpgradeCatalog.All.Count; i++)
        {
            var upgrade = UpgradeCatalog.All[i];
            var level = Shipyards.Level(ship, upgrade);
            var top = panel.Y + headerHeight + i * RowHeight;

            PixelFont.Draw(_batch, upgrade.Name, new Vector2(panel.X + Padding, top), LabelScale, Text);
            PixelFont.Draw(_batch, upgrade.Effect, new Vector2(panel.X + Padding, top + PixelFont.Height(LabelScale) + 6), SmallScale, Muted);

            // Level pips between the name and the buy button: owned, for sale here, and only sold further north.
            var stocked = Shipyards.StockedLevels(shipyard, upgrade);
            for (var pip = 0; pip < upgrade.MaxLevel; pip++)
            {
                var pipRect = new Rectangle(panel.X + 190 + pip * 12, top + 4, 9, 9);
                if (pip < level)
                    FillRect(pipRect, PipOn);
                else if (pip < stocked)
                    FillRect(pipRect, PipOff);
                else
                    OutlineRect(pipRect, PipOff);
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
