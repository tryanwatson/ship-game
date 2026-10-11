using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using ShipGame.Client.Input;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Client.Rendering;

/// <summary>
/// The pause after a fortress falls (or a boss sinks), laid out like League of Legends Arena's augment choice: the frozen
/// game dimmed behind, what was won across the top, and the cards side by side (three, or four from a level-8
/// fortress), each framed in its tier's color (silver, gold, or a shifting prismatic) with an emblem, its name, what it
/// does at the level it was dealt, and what it's for (the ship, or a weapon, in that weapon's color). A prismatic
/// already held shows as an upgrade, with the numbers it would improve to. They rise in one after another and lift under the
/// mouse; click one to take it (no number keys, which are the weapons, so a fight's last shots don't pick by accident). Under them, a reroll button (or R) pays gold for a fresh three; it doubles in price
/// every time. There's no time limit: the game waits for everyone. Having chosen, you see your card while the rest of
/// the crew make up their minds. A testing run's hands (a late game's worth, before setting sail) reroll for free, as
/// often as you like, into whichever tier is picked on the chips beside the button.
/// </summary>
public sealed class CardSelectScreen
{
    /// <summary>What the player did on the screen this frame: picked a card, asked for a reroll, or picked their starting weapon.</summary>
    /// <param name="Tier">For a testing hand's reroll: the tier to deal it in.</param>
    public readonly record struct Action(string? CardId, bool Reroll, string? WeaponId = null, CardTier? Tier = null);

    /// <summary>What a card shows, whether it's a card or (at the start of a run) a weapon.</summary>
    /// <param name="Label">Top left: its tier, or that it's an upgrade.</param>
    /// <param name="Tag">The category pill's color (the weapon's, or the frame's).</param>
    /// <param name="Bonus">Under the description, in the frame's color: a starting weapon's bonus.</param>
    private sealed record Face(string Name, string Description, CardIcon Icon, Color Frame, string Category, string Label = "",
        Color? Tag = null, bool Prismatic = false, string Bonus = "");

    // Stands for the weapon choice while it's on screen, so it rises in like an offer.
    private static readonly object WeaponChoice = new();

    private const float RerollWidth = 330f;
    private const float RerollHeight = 40f;
    private const float ChipWidth = 112f;
    private const float ChipGap = 6f;
    private static readonly CardTier[] Tiers = { CardTier.Silver, CardTier.Gold, CardTier.Prismatic };
    private static readonly Color Gold = new(250, 215, 110);
    private static readonly Color Disabled = new(110, 112, 122);
    private const float CardWidth = 250f;
    private const float CardHeight = 370f;
    private const float Gap = 36f;
    private const float CardsTop = 190f;
    private const float EmblemRadius = 46f;
    private const float Padding = 18f;
    private const float HoverLift = 12f;
    private const float RiseSeconds = 0.35f;
    private const float RiseStagger = 0.12f;
    private const float RiseDistance = 60f;

    private static readonly Color Dim = new Color(4, 6, 12) * 0.72f;
    private static readonly Color Title = new(245, 220, 150);
    private static readonly Color Subtitle = new(225, 228, 235);
    private static readonly Color Muted = new(150, 155, 170);
    private static readonly Color Body = new(16, 20, 34);
    private static readonly Color BodyHover = new(26, 32, 52);
    private static readonly Color EmblemBack = new(8, 10, 20);
    private static readonly Color Text = new(220, 224, 232);


    private readonly PrimitiveBatch _batch;
    private object? _shown;
    private long _shownAt;
    private long _lastDraw;
    private readonly float[] _lift = new float[CardRewards.TopOfferSize];

    // What a testing hand's reroll deals; it stays picked from one hand to the next.
    private CardTier _testingTier = CardRewards.DefaultTestingTier;

    public CardSelectScreen(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    /// <summary>
    /// The card or starting weapon clicked or a reroll asked for (button or R) this
    /// frame, if any.
    /// </summary>
    public Action? Update(PlayerState? player, InputState input, HudView hud)
    {
        if (player is { CardOffers.Count: 0, NeedsStartingWeapon: true } && _shown == WeaponChoice)
            return PickWeapon(input, hud);
        if (player is null || player.CardOffers.Count == 0 || !ReferenceEquals(player.CardOffers[0], _shown))
            return null;
        var offer = player.CardOffers[0].Cards;
        var testing = player.CardOffers[0].Source == OfferSource.Testing;
        var reroll = new Action(null, true, Tier: testing ? _testingTier : null);
        var canReroll = testing || player.CardOffers[0].FreeRerolls > 0 || player.Gold >= CardRewards.RerollCost(player.Rerolls);
        if (canReroll && input.WasKeyPressed(Keys.R))
            return reroll;
        if (!input.WasLeftMousePressed)
            return null;
        var mouse = hud.FromScreen(input.Mouse.Position);
        for (var i = 0; i < offer.Count; i++)
        {
            if (CardRect(hud, i, offer.Count, 0f).Contains(mouse))
                return new Action(offer[i].Id, false);
        }
        for (var i = 0; testing && i < Tiers.Length; i++)
        {
            if (ChipRect(hud, i).Contains(mouse))
                _testingTier = Tiers[i];
        }
        return canReroll && RerollRect(hud).Contains(mouse) ? reroll : null;
    }

    private static Action? PickWeapon(InputState input, HudView hud)
    {
        var weapons = WeaponCatalog.All;
        if (!input.WasLeftMousePressed)
            return null;
        var mouse = hud.FromScreen(input.Mouse.Position);
        for (var i = 0; i < weapons.Count; i++)
        {
            if (CardRect(hud, i, weapons.Count, 0f).Contains(mouse))
                return new Action(null, false, weapons[i].Id);
        }
        return null;
    }

    /// <param name="fortress">The fortress whose fall this is, for the title; null at the start of a run.</param>
    public void Draw(World world, PlayerState? player, string? fortress, InputState input, HudView hud)
    {
        var now = Environment.TickCount64;
        var dt = _lastDraw == 0 ? 0f : MathF.Min(0.1f, (now - _lastDraw) / 1000f);
        _lastDraw = now;

        var offer = player?.CardOffers.Count > 0 ? player.CardOffers[0] : null;
        var choosingWeapon = offer is null && player?.NeedsStartingWeapon == true;
        var showing = (object?)offer ?? (choosingWeapon ? WeaponChoice : null);
        if (showing is not null && !ReferenceEquals(showing, _shown))
        {
            _shown = showing;
            _shownAt = now;
            Array.Clear(_lift);
        }

        var viewport = hud.Viewport;
        _batch.Begin(hud.Transform);
        FillRect(new Rectangle(0, 0, viewport.Width, viewport.Height), Dim);

        // At the start of a run everyone picks a card, then a weapon; after a fortress falls or a boss sinks, a card.
        var starting = player?.NeedsStartingWeapon == true;
        var testing = offer?.Source == OfferSource.Testing;
        var title = testing ? "LATE-GAME TEST"
            : starting || offer?.Source == OfferSource.Start ? "SET SAIL"
            : offer?.Source == OfferSource.Boss ? "PIRATE FLAGSHIP SUNK"
            : offer?.Source == OfferSource.Shop ? "BOUGHT AT THE YARD"
            : fortress is null ? "FORTRESS TAKEN" : $"{fortress} TAKEN";
        DrawCentered(title, 56f, 4f, Title, viewport.Width);
        var waiting = world.Players.Values.Count(p => (p.CardOffers.Count > 0 || p.NeedsStartingWeapon) && p.PlayerId != player?.PlayerId);
        var subtitle = choosingWeapon ? "CHOOSE YOUR STARTING WEAPON"
            : offer is null ? "WAITING FOR THE CREW TO CHOOSE"
            : testing ? $"CARD {CardRewards.TestingHands - player!.CardOffers.Count(o => o.Source == OfferSource.Testing) + 1} OF {CardRewards.TestingHands}  -  CHOOSE A CARD"
            : starting ? "CHOOSE A STARTING CARD"
            : player!.CardOffers.Count > 1 ? $"LEVEL {offer.Level} SPOILS  -  CHOOSE A CARD  -  {player.CardOffers.Count - 1} MORE TO COME"
            : $"LEVEL {offer.Level} SPOILS  -  CHOOSE A CARD";
        DrawCentered(subtitle, 56f + PixelFont.Height(4f) + 14f, 2f, Subtitle, viewport.Width);
        if (showing is not null && waiting > 0)
            DrawCentered(waiting == 1 ? "1 CREWMATE CHOOSING TOO" : $"{waiting} CREWMATES CHOOSING TOO", 130f, 1.5f, Muted, viewport.Width);

        var age = (now - _shownAt) / 1000f;
        if (offer is not null)
        {
            DrawFaces(offer.Cards.Where(c => CardCatalog.Find(c.Id) is not null).Select(c => FaceOf(c, player!.Cards, world.GetPlayerShip(player.PlayerId))).ToList(),
                age, dt, input, hud, null);
            DrawReroll(player!, offer, input, hud);
        }
        else if (choosingWeapon)
        {
            // The starting card may well decide this: say what it is, and which weapon it's for.
            var card = player!.Cards.Count > 0 ? player.Cards[^1].Definition : null;
            if (card is not null)
                DrawCentered($"YOUR CARD: {card.Name}", 150f, 2f, CardLook.TagColor(card), viewport.Width);
            DrawFaces(WeaponCatalog.All.Select(FaceOf).ToList(), age, dt, input, hud,
                card?.AbilityId is { } boosted ? WeaponCatalog.All.ToList().FindIndex(w => w.Id == boosted) : null);
        }
        else if (player?.Cards.Count > 0)
            DrawChosen(player.Cards[^1], hud);

        var footer = choosingWeapon ? "CLICK A WEAPON  -  THE OTHERS CAN BE BOUGHT AT PORTS"
            : offer is not null ? "CLICK A CARD  -  THE GAME IS PAUSED"
            : waiting == 1 ? "1 SAILOR STILL CHOOSING" : $"{waiting} SAILORS STILL CHOOSING";
        DrawCentered(footer, CardsTop + CardHeight + 84f, 1.5f, Muted, viewport.Width);
        _batch.Flush();
    }

    /// <param name="boosted">The one to mark as what the player's card improves, if any.</param>
    private void DrawFaces(IReadOnlyList<Face> faces, float age, float dt, InputState input, HudView hud, int? boosted)
    {
        var mouse = hud.FromScreen(input.Mouse.Position);
        for (var i = 0; i < faces.Count; i++)
        {
            // Each rises into place a beat after the last, then lifts while the mouse is over it.
            var rise = Math.Clamp((age - i * RiseStagger) / RiseSeconds, 0f, 1f);
            rise = 1f - (1f - rise) * (1f - rise);
            var hovered = rise >= 1f && CardRect(hud, i, faces.Count, 0f).Contains(mouse);
            if (i < _lift.Length)
                _lift[i] = MathHelper.Lerp(_lift[i], hovered ? 1f : 0f, Math.Clamp(dt * 14f, 0f, 1f));
            var lift = i < _lift.Length ? _lift[i] : 0f;
            var rect = CardRect(hud, i, faces.Count, (1f - rise) * RiseDistance - lift * HoverLift);
            DrawCard(faces[i], rect, rise, lift);
            if (i == boosted)
            {
                const string label = "YOUR CARD BOOSTS IT";
                PixelFont.Draw(_batch, label, new Vector2(rect.Center.X - PixelFont.Measure(label, 1.5f) / 2f, rect.Top - PixelFont.Height(1.5f) - 10f),
                    1.5f, faces[i].Frame * rise);
            }
        }
    }

    /// <summary>
    /// The reroll button: free while the offer has a free reroll (level 7 and 8 fortresses), otherwise its price and the
    /// gold to pay it with; greyed out when that's not enough. A testing hand's is always free, with the tier chips
    /// to its left.
    /// </summary>
    private void DrawReroll(PlayerState player, CardOffer offer, InputState input, HudView hud)
    {
        if (offer.Source == OfferSource.Testing)
        {
            DrawTestingReroll(input, hud);
            return;
        }
        var free = offer.FreeRerolls > 0;
        var cost = CardRewards.RerollCost(player.Rerolls);
        var affordable = free || player.Gold >= cost;
        var rect = RerollRect(hud);
        var hovered = affordable && rect.Contains(hud.FromScreen(input.Mouse.Position));
        FillRect(rect, (hovered ? BodyHover : Body) * 0.95f);
        OutlineRect(rect, affordable ? Gold * (hovered ? 1f : 0.7f) : Disabled * 0.6f);
        var label = free ? "REROLL  -  FREE  [R]" : affordable ? $"REROLL  -  {cost} GOLD  [R]" : $"REROLL  -  {cost} GOLD";
        DrawCentered(label, rect.Y + (rect.Height - PixelFont.Height(2f)) / 2f, 2f, affordable ? Gold : Disabled, rect.Center.X * 2f);
        var purse = free ? $"THEN {cost} GOLD" : affordable ? $"YOU HAVE {player.Gold} GOLD" : $"YOU HAVE {player.Gold} GOLD - NOT ENOUGH";
        PixelFont.Draw(_batch, purse, new Vector2(rect.Right + 14f, rect.Y + (rect.Height - PixelFont.Height(1.5f)) / 2f), 1.5f, Muted);
    }

    /// <summary>The tier chips (silver, gold, prismatic; the one picked filled in) and the free reroll into that tier.</summary>
    private void DrawTestingReroll(InputState input, HudView hud)
    {
        var mouse = hud.FromScreen(input.Mouse.Position);
        var time = Environment.TickCount64 / 1000f;
        for (var i = 0; i < Tiers.Length; i++)
        {
            var chip = ChipRect(hud, i);
            var picked = Tiers[i] == _testingTier;
            var color = Tiers[i] == CardTier.Prismatic ? CardLook.Hue(time * 0.15f) : CardLook.TierColor(Tiers[i]);
            FillRect(chip, picked ? color * 0.3f : (chip.Contains(mouse) ? BodyHover : Body) * 0.95f);
            OutlineRect(chip, color * (picked ? 1f : 0.5f));
            DrawCentered(Tiers[i].ToString().ToUpperInvariant(), chip.Y + (chip.Height - PixelFont.Height(1.5f)) / 2f, 1.5f,
                picked ? color : Muted, chip.Center.X * 2f);
        }

        var rect = RerollRect(hud);
        var hovered = rect.Contains(mouse);
        FillRect(rect, (hovered ? BodyHover : Body) * 0.95f);
        OutlineRect(rect, Gold * (hovered ? 1f : 0.7f));
        var label = $"REROLL  -  {CardRewards.TopOfferSize} {_testingTier.ToString().ToUpperInvariant()}S  [R]";
        DrawCentered(label, rect.Y + (rect.Height - PixelFont.Height(2f)) / 2f, 2f, Gold, rect.Center.X * 2f);
        PixelFont.Draw(_batch, "FREE  -  TESTING", new Vector2(rect.Right + 14f, rect.Y + (rect.Height - PixelFont.Height(1.5f)) / 2f), 1.5f, Muted);
    }

    /// <summary>Tier chip <paramref name="index"/> of <see cref="Tiers"/>, in a row ending just left of the reroll button.</summary>
    private static Rectangle ChipRect(HudView hud, int index)
    {
        var reroll = RerollRect(hud);
        var left = reroll.Left - 14f - Tiers.Length * ChipWidth - (Tiers.Length - 1) * ChipGap + index * (ChipWidth + ChipGap);
        return new Rectangle((int)left, reroll.Y, (int)ChipWidth, reroll.Height);
    }

    private static Rectangle RerollRect(HudView hud) =>
        new((int)((hud.Viewport.Width - RerollWidth) / 2f), (int)(CardsTop + CardHeight + 24f), (int)RerollWidth, (int)RerollHeight);

    private void DrawChosen(CardPick card, HudView hud)
    {
        var rect = CardRect(hud, 0, 1, 0f);
        DrawCard(FaceOf(card, Array.Empty<CardPick>()), rect, 1f, 1f);
        var label = "YOUR CHOICE";
        PixelFont.Draw(_batch, label, new Vector2(rect.Center.X - PixelFont.Measure(label, 2f) / 2f, rect.Top - PixelFont.Height(2f) - 10f), 2f, Title);
    }

    private void DrawCard(Face face, Rectangle rect, float opacity, float highlight)
    {
        // A prismatic's frame runs through the colors, slowly, and differently from one card to the next.
        var time = Environment.TickCount64 / 1000f;
        var frame = face.Prismatic ? CardLook.Hue(time * 0.15f + rect.X * 0.0007f) : face.Frame;

        // A soft glow round the frame when it's lifted.
        if (highlight > 0.01f)
        {
            for (var g = 1; g <= 3; g++)
            {
                var glow = rect;
                glow.Inflate(g * 3, g * 3);
                OutlineRect(glow, frame * (0.35f * highlight / g * opacity));
            }
        }
        FillRect(rect, Color.Lerp(Body, BodyHover, highlight) * (0.97f * opacity));
        // Frame: a bright outer edge, a dim inner one, and a band across the top in the frame's color.
        OutlineRect(rect, frame * opacity);
        var inner = rect;
        inner.Inflate(-4, -4);
        OutlineRect(inner, frame * (0.35f * opacity));
        if (face.Prismatic)
        {
            const int bands = 16;
            for (var b = 0; b < bands; b++)
            {
                var x0 = rect.X + rect.Width * b / bands;
                var x1 = rect.X + rect.Width * (b + 1) / bands;
                FillRect(new Rectangle(x0, rect.Y, x1 - x0, 6), CardLook.Hue(time * 0.15f + b / (float)bands) * opacity);
            }
        }
        else
            FillRect(new Rectangle(rect.X, rect.Y, rect.Width, 6), frame * opacity);
        if (face.Label.Length > 0)
            PixelFont.Draw(_batch, face.Label, new Vector2(rect.X + 12f, rect.Y + 14f), 1.5f, frame * (0.9f * opacity));

        var center = new Vector2(rect.Center.X, rect.Y + Padding + 18f + EmblemRadius);
        DrawEmblem(face.Icon, center, frame, opacity);

        var y = center.Y + EmblemRadius + 22f;
        foreach (var line in PixelFont.Wrap(face.Name, 2.5f, rect.Width - 2 * Padding))
        {
            DrawCentered(line, y, 2.5f, Title * opacity, rect.Center.X * 2f);
            y += PixelFont.Height(2.5f) + 6f;
        }
        y += 6f;
        _batch.Line(new Vector2(rect.X + 40f, y), new Vector2(rect.Right - 40f, y), frame * (0.5f * opacity));
        y += 14f;
        foreach (var line in PixelFont.Wrap(face.Description, 1.5f, rect.Width - 2 * Padding))
        {
            DrawCentered(line, y, 1.5f, Text * opacity, rect.Center.X * 2f);
            y += PixelFont.Height(1.5f) + 6f;
        }
        if (face.Bonus.Length > 0)
        {
            y += 10f;
            foreach (var line in PixelFont.Wrap(face.Bonus, 1.5f, rect.Width - 2 * Padding))
            {
                DrawCentered(line, y, 1.5f, frame * opacity, rect.Center.X * 2f);
                y += PixelFont.Height(1.5f) + 6f;
            }
        }

        // The category in a pill at the foot, in the weapon's color.
        var category = face.Category;
        var tag = face.Tag ?? frame;
        var pillWidth = PixelFont.Measure(category, 1.5f) + 20f;
        var pill = new Rectangle((int)(rect.Center.X - pillWidth / 2f), rect.Bottom - 44, (int)pillWidth, 22);
        FillRect(pill, tag * (0.22f * opacity));
        OutlineRect(pill, tag * (0.8f * opacity));
        DrawCentered(category, pill.Y + (pill.Height - PixelFont.Height(1.5f)) / 2f, 1.5f, tag * opacity, rect.Center.X * 2f);
    }

    /// <summary>A dark disc ringed in the frame color, with the card's icon.</summary>
    private void DrawEmblem(CardIcon icon, Vector2 center, Color frame, float opacity)
    {
        Circle(center, EmblemRadius + 4f, frame * (0.6f * opacity));
        Circle(center, EmblemRadius, EmblemBack * opacity);
        Ring(center, EmblemRadius - 6f, frame * (0.35f * opacity));
        CardIcons.Draw(_batch, icon, center, EmblemRadius * 0.6f, frame * opacity);
    }

    private static Rectangle CardRect(HudView hud, int index, int count, float raise)
    {
        var total = count * CardWidth + (count - 1) * Gap;
        var left = (hud.Viewport.Width - total) / 2f + index * (CardWidth + Gap);
        return new Rectangle((int)left, (int)(CardsTop + raise), (int)CardWidth, (int)CardHeight);
    }

    /// <summary>A card as dealt: a prismatic already in <paramref name="hand"/> shows as the upgrade it would be.</summary>
    private static Face FaceOf(CardPick pick, IReadOnlyList<CardPick> hand, Ship? ship = null)
    {
        var card = pick.Definition;
        var becomes = CardStacking.WouldBecome(hand, pick);
        var upgrade = becomes != pick;
        var label = upgrade ? "UPGRADE" : card.Tier.ToString().ToUpperInvariant();
        return new Face(card.Name, becomes.Description, CardLook.Icon(card, becomes.Values), CardLook.TierColor(card.Tier), CardLook.Category(card), label,
            CardLook.TagColor(card), card.Tier == CardTier.Prismatic, ship is null ? "" : becomes.DescriptionOn(ship) ?? "");
    }

    private static Face FaceOf(WeaponOffer weapon) => new(weapon.Ability.Name.ToUpperInvariant(), weapon.Ability.Description,
        weapon.Id switch
        {
            LongGun.AbilityId => CardIcon.Range,
            Mortar.AbilityId => CardIcon.Salvo,
            _ => CardIcon.Cannonballs,
        },
        CardLook.WeaponFrame(weapon.Id), "WEAPON", Bonus: weapon.StartingBonusText);

    private void DrawCentered(string text, float top, float scale, Color color, float width) =>
        PixelFont.Draw(_batch, text, new Vector2((width - PixelFont.Measure(text, scale)) / 2f, top), scale, color);

    private void Circle(Vector2 center, float radius, Color color) => _batch.FillEllipse(center, new Vector2(radius), color);

    private void Ring(Vector2 center, float radius, Color color)
    {
        Span<Vector2> points = stackalloc Vector2[40];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = MathF.Tau * i / points.Length;
            points[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
        _batch.Outline(points, color);
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
