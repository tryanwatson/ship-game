using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using ShipGame.Client.Input;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Client.Rendering;

/// <summary>
/// The sea chart (Tab): one act at a time, from where the crew came in at the bottom up through its rows to the boss at
/// the top, lanes side by side and lines for where each stop leads. Each stop says what it is: a fortress, colored by
/// how hard (calm, rough, dire) with its level and what its cards come in; a port; the flagship. The route sailed so
/// far is traced in, and once the crew is done where it is, the stops it can sail on to light up: click one to vote
/// for it. Everyone's votes show beside the stops as names. The crew sets sail once everyone has voted. The game
/// carries on underneath, so it only ever opens on Tab.
/// </summary>
public sealed class ChartScreen
{
    private const float NodeRadius = 26f;
    private const float BossRadius = 35f;
    private const float LaneSpacing = 250f;
    private const float TopRow = 175f;
    private const float BottomMargin = 120f;
    private const float HoverGrow = 5f;

    /// <summary>Clicks this soon after the chart opens are ignored: they were meant for whatever was there before.</summary>
    private const double ClickGraceSeconds = 0.4;

    private static readonly Color Dim = new Color(4, 8, 16) * 0.82f;
    private static readonly Color Parchment = new Color(26, 30, 44) * 0.92f;
    private static readonly Color Title = new(245, 220, 150);
    private static readonly Color Subtitle = new(225, 228, 235);
    private static readonly Color Muted = new(150, 155, 170);
    private static readonly Color Route = new(245, 220, 150);
    private static readonly Color Line = new(120, 128, 150);
    private static readonly Color Reachable = new(150, 215, 240);
    private static readonly Color OwnVote = new(250, 215, 110);
    private static readonly Color Calm = new(110, 190, 125);
    private static readonly Color Rough = new(235, 175, 75);
    private static readonly Color Dire = new(230, 85, 70);
    private static readonly Color PortColor = new(100, 170, 235);
    private static readonly Color BossColor = new(170, 50, 60);
    private static readonly Color StartColor = new(130, 140, 160);
    private static readonly Color Label = new(12, 14, 22);

    private readonly PrimitiveBatch _batch;
    private long _openedAt;

    public ChartScreen(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public bool IsOpen { get; private set; }

    public void Open()
    {
        if (!IsOpen)
            _openedAt = Environment.TickCount64;
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    /// <summary>The stop clicked to vote for this frame, if any (only ones the crew can sail on to, once it's done here).</summary>
    public int? Update(World world, InputState input, HudView hud)
    {
        if (!IsOpen || !input.WasLeftMousePressed || world.Director is not { Chart: { } chart, CurrentNode: { } here, Cleared: true }
            || (Environment.TickCount64 - _openedAt) / 1000.0 < ClickGraceSeconds)
            return null;
        var act = ShownAct(here, cleared: true);
        var mouse = ToVector(hud.FromScreen(input.Mouse.Position));
        foreach (var id in here.Next)
        {
            if (chart.Find(id) is { } node && Vector2.Distance(mouse, Position(node, act, hud)) <= RadiusOf(node) + HoverGrow)
                return id;
        }
        return null;
    }

    public void Draw(World world, int localPlayerId, InputState input, HudView hud)
    {
        if (!IsOpen || world.Director is not { Chart: { } chart, CurrentNode: { } here } director)
            return;
        var viewport = hud.Viewport;
        var mouse = ToVector(hud.FromScreen(input.Mouse.Position));
        var act = ShownAct(here, director.Cleared);
        var entry = EntryOf(chart, act);
        var nodes = chart.Nodes.Where(n => n.Act == act && n.Kind != NodeKind.Start).Append(entry).Distinct().ToList();
        var route = director.Route.ToHashSet();
        var reachable = director.Cleared ? here.Next.ToHashSet() : new HashSet<int>();
        var votes = world.Players.Values.Where(p => p.CourseVote is not null).ToLookup(p => p.CourseVote!.Value);
        var ownVote = world.Players.TryGetValue(localPlayerId, out var self) ? self.CourseVote : null;

        _batch.Begin(hud.Transform);
        FillRect(new Rectangle(0, 0, viewport.Width, viewport.Height), Dim);
        var left = viewport.Width / 2f - LaneSpacing * 1.5f - 80f;
        FillRect(new Rectangle((int)left, 30, (int)(LaneSpacing * 3f + 160f), viewport.Height - 60), Parchment);

        DrawCentered($"ACT {SeaChart.ActNumeral(act)}  -  SEA CHART", 52f, 3f, Title, viewport.Width);
        var voted = world.Players.Values.Count(p => p.CourseVote is not null);
        var subtitle = !director.Cleared
            ? here.Kind == NodeKind.Boss ? "SINK THE FLAGSHIP TO SAIL ON" : "TAKE THE FORTRESS TO SAIL ON"
            : voted == world.Players.Count && Plundering.UnderWay(world)
                ? "SETTING SAIL ONCE THE PLUNDERING'S DONE"
                : $"CLICK WHERE TO SAIL NEXT  -  THE CREW SETS SAIL ONCE EVERYONE HAS VOTED ({voted}/{world.Players.Count})";
        DrawCentered(subtitle, 52f + PixelFont.Height(3f) + 12f, 1.5f, Subtitle, viewport.Width);
        if (self is not null)
            DrawCentered(Purse(world, self), 52f + PixelFont.Height(3f) + 32f, 1.5f, OwnVote, viewport.Width);

        // Lines first, under the stops: the route sailed in gold, the ways on from here bright, the rest faint.
        foreach (var node in nodes)
        {
            foreach (var nextId in node.Next)
            {
                if (chart.Find(nextId) is not { } next || !nodes.Contains(next))
                    continue;
                var sailed = route.Contains(node.Id) && route.Contains(next.Id);
                var color = sailed ? Route : node.Id == here.Id && reachable.Contains(next.Id) ? Reachable : Line * 0.45f;
                _batch.Stroke(Position(node, act, hud), Position(next, act, hud), sailed ? 4f : 2.5f, color);
            }
        }

        foreach (var node in nodes)
        {
            var center = Position(node, act, hud);
            var canVote = reachable.Contains(node.Id);
            var hovered = canVote && Vector2.Distance(mouse, center) <= RadiusOf(node) + HoverGrow;
            var radius = RadiusOf(node) + (hovered ? HoverGrow : 0f);
            var behind = !canVote && node.Id != here.Id && (node.Act < here.Act || (node.Act == here.Act && node.Row <= here.Row));
            var opacity = route.Contains(node.Id) || canVote || node.Id == here.Id ? 1f : behind ? 0.3f : 0.65f;

            if (node.Id == here.Id)
                Ring(center, radius + 9f, Title, 3f);
            if (ownVote == node.Id)
                Ring(center, radius + 9f, OwnVote, 3f);
            else if (canVote)
                Ring(center, radius + 6f, Reachable * (hovered ? 1f : 0.7f), 2f);
            _batch.FillEllipse(center, new Vector2(radius), ColorOf(node) * opacity);
            var tag = Tag(node);
            PixelFont.Draw(_batch, tag, new Vector2(center.X - PixelFont.Measure(tag, 1.5f) / 2f, center.Y - PixelFont.Height(1.5f) / 2f), 1.5f,
                Label * opacity);

            // Beside it on the right: its name, how hard it is and the hand it deals, then who's voted for it.
            var lines = Details(node)
                .Concat(votes[node.Id].OrderBy(p => p.PlayerId).Select(p => new Detail(p.Name.Length > 0 ? p.Name : PlayerNames.Default,
                    p.PlayerId == localPlayerId ? OwnVote : Reachable, Vote: true)))
                .ToList();
            var lineHeight = PixelFont.Height(1.5f) + 7f;
            var top = center.Y - Math.Max(lines.Count, 2) * lineHeight / 2f;
            foreach (var line in lines)
            {
                var at = new Vector2(center.X + radius + 12f, top);
                var color = line.Vote ? line.Color : line.Color * opacity;
                if (line.Hand is { } hand)
                    at.X += HandPips.Draw(_batch, hand, at + new Vector2(0f, PixelFont.Height(1.5f) / 2f), 1.5f, opacity) + (line.Text.Length > 0 ? 9f : 0f);
                PixelFont.Draw(_batch, line.Text, at, 1.5f, color);
                top += lineHeight;
            }
            if (node.Id == here.Id)
            {
                const string mark = "YOU ARE HERE";
                PixelFont.Draw(_batch, mark, new Vector2(center.X - radius - 14f - PixelFont.Measure(mark, 1.5f), center.Y - PixelFont.Height(1.5f) / 2f),
                    1.5f, Title);
            }
        }

        DrawLegend(viewport.Height - 96f, viewport.Width);
        DrawCentered("HARDER FORTRESSES DEAL BETTER HANDS  -  PORTS REPAIR EVERY HULL", viewport.Height - 80f, 1.5f, Muted, viewport.Width);
        DrawCentered("TAB TO CLOSE  -  THE GAME CARRIES ON", viewport.Height - 60f, 1.5f, Muted, viewport.Width);
        _batch.Flush();
    }

    /// <summary>
    /// What weighs on choosing a port: how battered our hull is, and the gold to spend there. And what weighs on going
    /// at all: the gold still ashore here, left behind for good once the crew sails.
    /// </summary>
    private static string Purse(World world, PlayerState self)
    {
        var hull = world.GetPlayerShip(self.PlayerId) is { } ship
            ? $"YOUR HULL {(int)MathF.Round(100f * ship.Health / MathF.Max(1f, ship.Stats.MaxHealth))}%"
            : "YOUR SHIP IS SUNK";
        var purse = $"{hull}  -  {self.Gold} GOLD";
        if (world.Director is { Cleared: true, CurrentNode.Kind: not NodeKind.Port } && Plundering.LeftToPlunder(world).ToList() is { Count: > 0 } loot)
            purse += $"  -  {loot.Count} {(loot.Count == 1 ? "ISLAND" : "ISLANDS")} LEFT TO PLUNDER ({loot.Sum(i => Plundering.PlunderFor(world, i))} GOLD)";
        return purse;
    }

    /// <summary>The act on show: the one the crew is in, or the next once the boss ending this one is sunk.</summary>
    private static int ShownAct(ChartNode here, bool cleared) =>
        here.Kind == NodeKind.Boss && cleared && here.Act < SeaChart.Acts ? here.Act + 1 : here.Act;

    /// <summary>Where the crew comes into <paramref name="act"/> from: the start, or the last act's boss.</summary>
    private static ChartNode EntryOf(SeaChart chart, int act) =>
        act <= 1 ? chart.Start : chart.Nodes.First(n => n.Act == act - 1 && n.Kind == NodeKind.Boss);

    /// <summary>A stop's place on screen: rows bottom to top (the act's entry lowest), lanes left to right.</summary>
    private static Vector2 Position(ChartNode node, int act, HudView hud)
    {
        var viewport = hud.Viewport;
        var bottom = viewport.Height - BottomMargin - 30f;
        var step = (bottom - TopRow) / (SeaChart.BossRow + 1);
        var row = node.Act < act || node.Kind == NodeKind.Start ? -1 : node.Row;
        var x = viewport.Width / 2f + (node.Lane - SeaChart.MiddleLane) * LaneSpacing;
        return new Vector2(x, bottom - (row + 1) * step);
    }

    private static float RadiusOf(ChartNode node) => node.Kind == NodeKind.Boss ? BossRadius : NodeRadius;

    private static Color ColorOf(ChartNode node) => node.Kind switch
    {
        NodeKind.Fortress => node.Difficulty switch
        {
            Difficulty.Calm => Calm,
            Difficulty.Rough => Rough,
            _ => Dire,
        },
        NodeKind.Port => PortColor,
        NodeKind.Boss => BossColor,
        _ => StartColor,
    };

    private static string Tag(ChartNode node) => node.Kind switch
    {
        NodeKind.Fortress => "FORT",
        NodeKind.Port => "PORT",
        NodeKind.Boss => "BOSS",
        _ => "START",
    };

    /// <summary>A line beside a stop: words, after a hand of cards if it deals one. A crewmate's vote never fades.</summary>
    private readonly record struct Detail(string Text, Color Color, IReadOnlyList<HandSlot>? Hand = null, bool Vote = false);

    /// <summary>What's written beside a stop: its name, how hard it is, and the hand it deals.</summary>
    private static IEnumerable<Detail> Details(ChartNode node)
    {
        if (node.Name.Length > 0)
            yield return new Detail(node.Name, Title);
        switch (node.Kind)
        {
            case NodeKind.Fortress:
                yield return new Detail($"{SeaChart.Name(node.Difficulty)}  LV {node.Level}", Subtitle);
                yield return new Detail(CardRewards.FreeRerollsFor(OfferSource.Fortress, node.Level) > 0 ? "+ REROLL" : "", Muted,
                    CardRewards.Hand(OfferSource.Fortress, node.Level));
                break;
            case NodeKind.Port:
                yield return new Detail("REPAIRS + SHOP", Subtitle);
                break;
            case NodeKind.Boss:
                yield return new Detail($"FLAGSHIP  LV {node.Level}", Subtitle);
                yield return node.Act >= SeaChart.Acts
                    ? new Detail("SINK IT TO WIN", Subtitle)
                    : new Detail("", Subtitle, CardRewards.Hand(OfferSource.Boss, node.Level));
                break;
        }
    }

    /// <summary>
    /// The key to the hands beside the fortresses, along the bottom: a card per card, its color its tier, split if it
    /// may come better.
    /// </summary>
    private void DrawLegend(float middle, float width)
    {
        const float scale = 1.5f;
        var parts = new (HandSlot? Pip, string Text)[]
        {
            (null, "CARDS:"), (new HandSlot(CardTier.Silver), "SILVER"), (new HandSlot(CardTier.Gold), "GOLD"),
            (new HandSlot(CardTier.Prismatic), "PRISMATIC"), (new HandSlot(CardTier.Gold, 1f), "A CHANCE TO COME BETTER"),
        };
        var pip = HandPips.PipWidth(scale) + 6f;
        var total = parts.Sum(p => (p.Pip is null ? 0f : pip) + PixelFont.Measure(p.Text, scale) + 16f) - 16f;
        var x = (width - total) / 2f;
        foreach (var (slot, text) in parts)
        {
            if (slot is { } shown)
                x += HandPips.DrawPip(_batch, shown, 0, new Vector2(x, middle), scale) + 6f;
            PixelFont.Draw(_batch, text, new Vector2(x, middle - PixelFont.Height(scale) / 2f), scale, Muted);
            x += PixelFont.Measure(text, scale) + 16f;
        }
    }

    private static Vector2 ToVector(Point point) => new(point.X, point.Y);

    private void DrawCentered(string text, float top, float scale, Color color, float width) =>
        PixelFont.Draw(_batch, text, new Vector2((width - PixelFont.Measure(text, scale)) / 2f, top), scale, color);

    private void Ring(Vector2 center, float radius, Color color, float width)
    {
        Span<Vector2> points = stackalloc Vector2[40];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = MathF.Tau * i / points.Length;
            points[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
        for (var i = 0; i < points.Length; i++)
            _batch.Stroke(points[i], points[(i + 1) % points.Length], width, color);
    }

    private void FillRect(Rectangle rect, Color color)
    {
        Span<Vector2> corners = stackalloc Vector2[] { new(rect.Left, rect.Top), new(rect.Right, rect.Top), new(rect.Right, rect.Bottom), new(rect.Left, rect.Bottom) };
        _batch.FillConvex(corners, color);
    }
}
