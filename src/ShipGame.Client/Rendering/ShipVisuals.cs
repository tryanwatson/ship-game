using System;
using Microsoft.Xna.Framework;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>Small wooden sloops made from shaded geometry; the waterline keeps the simulation's exact hull shape.</summary>
public sealed class ShipVisuals
{
    public const float MastHeight = 52f;
    public const float DeckHeight = 8f;
    public const float HealthHeight = MastHeight + DeckHeight + 16f;
    private static readonly Color Timber = new(61, 36, 27);
    private static readonly Color Rail = new(225, 177, 105);
    private static readonly Color Canvas = new(248, 235, 193);
    private static readonly Color CanvasShade = new(207, 184, 138);
    private static readonly Color Rope = new(117, 94, 66);
    private static readonly Color Iron = new(42, 49, 52);
    private readonly PrimitiveBatch _batch;

    public ShipVisuals(PrimitiveBatch batch) => _batch = batch;

    public void Draw(Ship ship, NVector2 position, float heading, bool isLocal, bool targeted, float time, float hitFlash = 0f)
    {
        var forward = new NVector2(MathF.Cos(heading), MathF.Sin(heading));
        var side = new NVector2(-forward.Y, forward.X);
        Vector2 Deck(float along, float across, float height = DeckHeight) =>
            IsoProjection.WorldToIso(position + forward * along + side * across) - new Vector2(0, height);

        var pirate = ship.Team == Team.Pirates;
        var hullColor = pirate ? new Color(108, 69, 53) : new Color(154, 96, 53);
        var deckColor = pirate ? new Color(174, 142, 95) : new Color(212, 177, 116);
        hullColor = Color.Lerp(hullColor, new Color(255, 226, 173), hitFlash * 0.75f);
        deckColor = Color.Lerp(deckColor, new Color(255, 247, 209), hitFlash);
        var flagColor = pirate ? new Color(213, 85, 64)
            : isLocal ? new Color(242, 195, 78) : new Color(77, 208, 192);
        Span<NVector2> outline = stackalloc NVector2[HullShape.PointCount];
        HullShape.GetWorldOutline(position, heading, ship.Stats, outline);
        Span<Vector2> waterline = stackalloc Vector2[HullShape.PointCount];
        Span<Vector2> deck = stackalloc Vector2[HullShape.PointCount];
        var center = IsoProjection.WorldToIso(position);
        for (var i = 0; i < deck.Length; i++)
        {
            waterline[i] = IsoProjection.WorldToIso(outline[i]);
            deck[i] = waterline[i] - new Vector2(0, DeckHeight);
        }

        // Only the sides facing the viewer are visible beneath the raised deck.
        Span<Vector2> face = stackalloc Vector2[4];
        for (var i = 0; i < deck.Length; i++)
        {
            var next = (i + 1) % deck.Length;
            if (waterline[next].X >= waterline[i].X)
                continue;
            face[0] = waterline[i]; face[1] = waterline[next];
            face[2] = deck[next]; face[3] = deck[i];
            _batch.FillConvex(face, Color.Lerp(hullColor, Timber, waterline[next].Y > waterline[i].Y ? 0.18f : 0.45f));
            _batch.Stroke(waterline[i], waterline[next], 1.5f, Timber);
            _batch.Stroke(Vector2.Lerp(waterline[i], deck[i], 0.65f),
                Vector2.Lerp(waterline[next], deck[next], 0.65f), 1.5f, Rail * 0.7f);
        }
        _batch.FillConvex(deck, hullColor);
        Span<Vector2> inset = stackalloc Vector2[HullShape.PointCount];
        for (var i = 0; i < deck.Length; i++)
            inset[i] = center + (waterline[i] - center) * 0.82f - new Vector2(0, DeckHeight);
        _batch.FillConvex(inset, deckColor);
        for (var i = 0; i < deck.Length; i++)
            _batch.Stroke(deck[i], deck[(i + 1) % deck.Length], 1.8f, targeted ? new Color(250, 111, 79) : Rail);

        var length = ship.Stats.Length;
        var beam = ship.Stats.Beam;
        for (var i = -1; i <= 1; i++)
            _batch.Stroke(Deck(-length * 0.37f, i * beam * 0.18f),
                Deck(length * 0.19f, i * beam * 0.18f), 0.7f, Timber * 0.23f);
        for (var i = 0; i < 4; i++)
        {
            var along = length * (-0.32f + i * 0.13f);
            _batch.Stroke(Deck(along, -beam * 0.28f), Deck(along, beam * 0.28f), 0.6f, Timber * 0.2f);
        }
        // A hatch at the stern and three small iron guns on each beam.
        face[0] = Deck(-length * 0.35f, -beam * 0.2f);
        face[1] = Deck(-length * 0.17f, -beam * 0.2f);
        face[2] = Deck(-length * 0.17f, beam * 0.2f);
        face[3] = Deck(-length * 0.35f, beam * 0.2f);
        _batch.FillConvex(face, hullColor);
        for (var i = 0; i < face.Length; i++)
            _batch.Stroke(face[i], face[(i + 1) % face.Length], 1f, Timber);
        foreach (var sign in new[] { -1f, 1f })
        {
            for (var i = 0; i < 3; i++)
            {
                var along = length * (-0.28f + i * 0.2f);
                _batch.Stroke(Deck(along, sign * beam * 0.25f, DeckHeight + 2f),
                    Deck(along, sign * beam * 0.52f, DeckHeight + 2f), 3.4f, Iron);
            }
        }
        _batch.Stroke(Deck(length * 0.42f, 0), Deck(length * 0.62f, 0, DeckHeight + 3f), 2f, Rail);

        var mastBase = Deck(0, 0);
        var mastTop = mastBase - new Vector2(0, MastHeight);
        _batch.Stroke(Deck(-length * 0.38f, 0), mastTop + new Vector2(0, 5), 0.9f, Rope * 0.6f);
        _batch.Stroke(Deck(length * 0.44f, 0), mastTop + new Vector2(0, 5), 0.9f, Rope * 0.6f);
        _batch.Stroke(mastBase, mastTop, 3f, Timber);
        _batch.Stroke(mastBase + new Vector2(-0.6f, 0), mastTop + new Vector2(-0.6f, 0), 1f, Rail);

        DrawSail(ship, side, mastTop, time);
        var flutter = MathF.Sin(time * 3.2f + ship.Id) * 2f;
        Span<Vector2> pennant = stackalloc Vector2[]
        {
            mastTop + new Vector2(1, -1), mastTop + new Vector2(17, 3 + flutter), mastTop + new Vector2(1, 8),
        };
        _batch.FillConvex(pennant, flagColor);
        if (pirate)
            _batch.FillEllipse(mastTop + new Vector2(6, 3), new Vector2(1.8f, 1.5f), Canvas);
        else if (!isLocal)
            _batch.Stroke(mastTop + new Vector2(4, 3), mastTop + new Vector2(10, 3), 1.5f, Canvas);
    }

    private void DrawSail(Ship ship, NVector2 side, Vector2 mastTop, float time)
    {
        var sailAcross = IsoProjection.WorldToIso(side * 0.7f);
        // Prevent an end-on sail from vanishing at the isometric diagonal.
        if (MathF.Abs(sailAcross.X) < 13f)
            sailAcross.X = MathF.CopySign(13f, sailAcross.X == 0f ? 1f : sailAcross.X);
        sailAcross.Y *= 0.65f;
        var fullness = ship.IsAnchored ? 0f : Math.Clamp(ship.Throttle / (float)ShipMovement.ThrottleLevels, 0f, 1f);
        var top = mastTop + new Vector2(0, 11);
        var height = 5f + 25f * fullness;
        var billow = (2f + MathF.Sin(time * 2f + ship.Id) * 0.8f) * fullness;
        _batch.Stroke(top - sailAcross * 1.08f, top + sailAcross * 1.08f, 2.5f, Timber);
        Span<Vector2> panel = stackalloc Vector2[4];
        const int panels = 6;
        for (var i = 0; i < panels; i++)
        {
            var a = -1f + 2f * i / panels;
            var b = -1f + 2f * (i + 1) / panels;
            Vector2 Hem(float t) => top + sailAcross * (t * (0.95f + fullness * 0.12f))
                + new Vector2(billow * (1f - t * t), height + fullness * 3f * (1f - t * t));
            panel[0] = top + sailAcross * a; panel[1] = top + sailAcross * b;
            panel[2] = Hem(b); panel[3] = Hem(a);
            _batch.FillConvex(panel, Color.Lerp(Canvas, CanvasShade, 0.12f + 0.45f * i / (panels - 1f)));
            _batch.Stroke(panel[0], panel[3], 0.65f, CanvasShade * 0.55f);
            _batch.Stroke(panel[3], panel[2], 1.1f, CanvasShade);
        }
    }
}
