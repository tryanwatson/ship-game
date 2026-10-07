using System;
using Microsoft.Xna.Framework;

namespace ShipGame.Client.Rendering;

/// <summary>What a card's emblem shows: the kind of thing it changes.</summary>
public enum CardIcon
{
    Speed,
    Shield,
    Reload,
    Damage,
    Range,
    Repair,
    Helm,
    Cannonballs,
    Pierce,
    Blast,
    Cluster,
    Salvo,
    Coin,
    Fire,
    Echo,
    Anchor,
}

/// <summary>Simple line-and-shape icons for card emblems, drawn to fit a circle of the given radius.</summary>
public static class CardIcons
{
    public static void Draw(PrimitiveBatch batch, CardIcon icon, Vector2 c, float r, Color color)
    {
        const float w = 3f;
        switch (icon)
        {
            case CardIcon.Speed:
                // Two chevrons, racing right.
                foreach (var dx in new[] { -0.45f, 0.15f })
                {
                    batch.Stroke(c + new Vector2(dx - 0.25f, -0.6f) * r, c + new Vector2(dx + 0.3f, 0f) * r, w + 1f, color);
                    batch.Stroke(c + new Vector2(dx + 0.3f, 0f) * r, c + new Vector2(dx - 0.25f, 0.6f) * r, w + 1f, color);
                }
                break;
            case CardIcon.Shield:
            {
                Span<Vector2> shield = stackalloc Vector2[]
                {
                    c + new Vector2(-0.7f, -0.7f) * r, c + new Vector2(0.7f, -0.7f) * r, c + new Vector2(0.65f, 0.1f) * r,
                    c + new Vector2(0f, 0.85f) * r, c + new Vector2(-0.65f, 0.1f) * r,
                };
                batch.FillConvex(shield, color * 0.35f);
                Polyline(batch, shield, w, color, closed: true);
                batch.Stroke(c + new Vector2(0f, -0.5f) * r, c + new Vector2(0f, 0.5f) * r, w, color);
                break;
            }
            case CardIcon.Reload:
            {
                // An hourglass.
                Span<Vector2> top = stackalloc Vector2[] { c + new Vector2(-0.55f, -0.8f) * r, c + new Vector2(0.55f, -0.8f) * r, c };
                Span<Vector2> bottom = stackalloc Vector2[] { c, c + new Vector2(0.55f, 0.8f) * r, c + new Vector2(-0.55f, 0.8f) * r };
                batch.FillConvex(top, color * 0.35f);
                batch.FillConvex(bottom, color);
                Polyline(batch, top, w, color, closed: true);
                Polyline(batch, bottom, w, color, closed: true);
                break;
            }
            case CardIcon.Damage:
            {
                // A starburst.
                Span<Vector2> star = stackalloc Vector2[16];
                for (var i = 0; i < star.Length; i++)
                {
                    var angle = MathF.Tau * i / star.Length - MathF.PI / 2f;
                    var radius = (i % 2 == 0 ? 0.9f : 0.42f) * r;
                    star[i] = c + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                }
                Span<Vector2> spike = stackalloc Vector2[4];
                for (var i = 0; i < star.Length; i += 2)
                {
                    spike[0] = c;
                    spike[1] = star[(i + star.Length - 1) % star.Length];
                    spike[2] = star[i];
                    spike[3] = star[i + 1];
                    batch.FillConvex(spike, color);
                }
                break;
            }
            case CardIcon.Range:
                // Crosshairs.
                Circle(batch, c, 0.7f * r, w, color);
                batch.Stroke(c + new Vector2(-0.95f * r, 0f), c + new Vector2(-0.3f * r, 0f), w, color);
                batch.Stroke(c + new Vector2(0.3f * r, 0f), c + new Vector2(0.95f * r, 0f), w, color);
                batch.Stroke(c + new Vector2(0f, -0.95f * r), c + new Vector2(0f, -0.3f * r), w, color);
                batch.Stroke(c + new Vector2(0f, 0.3f * r), c + new Vector2(0f, 0.95f * r), w, color);
                batch.FillEllipse(c, new Vector2(0.12f * r), color);
                break;
            case CardIcon.Repair:
                // A plus.
                batch.Stroke(c + new Vector2(0f, -0.75f) * r, c + new Vector2(0f, 0.75f) * r, w * 3f, color);
                batch.Stroke(c + new Vector2(-0.75f, 0f) * r, c + new Vector2(0.75f, 0f) * r, w * 3f, color);
                break;
            case CardIcon.Anchor:
            {
                // Ring, stock, shank, and the arms curving up to the flukes.
                Circle(batch, c + new Vector2(0f, -0.78f) * r, 0.16f * r, w, color);
                batch.Stroke(c + new Vector2(0f, -0.62f) * r, c + new Vector2(0f, 0.8f) * r, w, color);
                batch.Stroke(c + new Vector2(-0.4f, -0.4f) * r, c + new Vector2(0.4f, -0.4f) * r, w, color);
                var previous = c + new Vector2(-0.75f, 0.25f) * r;
                for (var i = 1; i <= 8; i++)
                {
                    var angle = MathF.PI * i / 8f; // a half circle under the shank
                    var next = c + new Vector2(-MathF.Cos(angle) * 0.75f, 0.25f + MathF.Sin(angle) * 0.55f) * r;
                    batch.Stroke(previous, next, w, color);
                    previous = next;
                }
                Span<Vector2> fluke = stackalloc Vector2[3];
                foreach (var side in new[] { -1f, 1f })
                {
                    fluke[0] = c + new Vector2(side * 0.75f, 0.05f) * r;
                    fluke[1] = c + new Vector2(side * 0.95f, 0.35f) * r;
                    fluke[2] = c + new Vector2(side * 0.6f, 0.3f) * r;
                    batch.FillConvex(fluke, color);
                }
                break;
            }
            case CardIcon.Helm:
                // A ship's wheel.
                Circle(batch, c, 0.6f * r, w, color);
                for (var i = 0; i < 8; i++)
                {
                    var angle = MathF.Tau * i / 8f;
                    var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    batch.Stroke(c + direction * 0.15f * r, c + direction * 0.95f * r, w, color);
                    batch.FillEllipse(c + direction * 0.95f * r, new Vector2(w), color);
                }
                batch.FillEllipse(c, new Vector2(0.18f * r), color);
                break;
            case CardIcon.Cannonballs:
                foreach (var offset in new[] { new Vector2(-0.45f, 0.3f), new Vector2(0.45f, 0.3f), new Vector2(0f, -0.4f) })
                {
                    batch.FillEllipse(c + offset * r, new Vector2(0.36f * r), color);
                    batch.FillEllipse(c + offset * r + new Vector2(-0.12f, -0.12f) * r, new Vector2(0.1f * r), Color.White * 0.5f);
                }
                break;
            case CardIcon.Pierce:
                // A shot through two hulls.
                batch.Stroke(c + new Vector2(-0.3f, -0.7f) * r, c + new Vector2(-0.3f, 0.7f) * r, w + 1f, color * 0.6f);
                batch.Stroke(c + new Vector2(0.2f, -0.7f) * r, c + new Vector2(0.2f, 0.7f) * r, w + 1f, color * 0.6f);
                batch.Stroke(c + new Vector2(-0.9f, 0f) * r, c + new Vector2(0.65f, 0f) * r, w + 1f, color);
                Span<Vector2> head = stackalloc Vector2[] { c + new Vector2(0.95f, 0f) * r, c + new Vector2(0.55f, -0.25f) * r, c + new Vector2(0.55f, 0.25f) * r };
                batch.FillConvex(head, color);
                break;
            case CardIcon.Blast:
                Circle(batch, c, 0.85f * r, w, color * 0.5f);
                Circle(batch, c, 0.55f * r, w, color * 0.8f);
                batch.FillEllipse(c, new Vector2(0.28f * r), color);
                break;
            case CardIcon.Cluster:
                batch.FillEllipse(c, new Vector2(0.3f * r), color);
                for (var i = 0; i < 6; i++)
                {
                    var angle = MathF.Tau * i / 6f;
                    batch.FillEllipse(c + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 0.72f * r, new Vector2(0.14f * r), color);
                }
                break;
            case CardIcon.Coin:
                Coin(batch, c + new Vector2(-0.25f, 0.2f) * r, 0.75f * r, color * 0.7f);
                Coin(batch, c + new Vector2(0.2f, -0.15f) * r, 0.75f * r, color);
                break;
            case CardIcon.Fire:
            {
                // Three tongues of flame.
                Span<Vector2> flame = stackalloc Vector2[4];
                foreach (var (dx, height) in new[] { (-0.45f, 0.75f), (0.45f, 0.75f), (0f, 1f) })
                {
                    flame[0] = c + new Vector2(dx, 0.85f - height * 1.6f) * r;
                    flame[1] = c + new Vector2(dx + 0.3f, 0.5f) * r;
                    flame[2] = c + new Vector2(dx, 0.85f) * r;
                    flame[3] = c + new Vector2(dx - 0.3f, 0.5f) * r;
                    batch.FillConvex(flame, dx == 0f ? color : color * 0.7f);
                }
                break;
            }
            case CardIcon.Echo:
                // A shot, and its fainter echo behind it.
                batch.FillEllipse(c + new Vector2(0.35f, 0f) * r, new Vector2(0.32f * r), color);
                batch.FillEllipse(c + new Vector2(-0.35f, 0f) * r, new Vector2(0.32f * r), color * 0.45f);
                batch.Stroke(c + new Vector2(-0.95f, -0.55f) * r, c + new Vector2(0.95f, -0.55f) * r, w - 1f, color * 0.5f);
                batch.Stroke(c + new Vector2(-0.95f, 0.55f) * r, c + new Vector2(0.95f, 0.55f) * r, w - 1f, color * 0.5f);
                break;
            case CardIcon.Salvo:
                // Three shells arcing in.
                foreach (var dx in new[] { -0.55f, 0f, 0.55f })
                {
                    batch.FillEllipse(c + new Vector2(dx, 0.35f) * r, new Vector2(0.22f * r), color);
                    batch.Stroke(c + new Vector2(dx - 0.25f, -0.55f) * r, c + new Vector2(dx, 0.15f) * r, w - 1f, color * 0.6f);
                }
                break;
        }
    }

    private static void Coin(PrimitiveBatch batch, Vector2 c, float r, Color color)
    {
        batch.FillEllipse(c, new Vector2(0.8f * r), color);
        batch.FillEllipse(c, new Vector2(0.58f * r), color * 0.55f);
        batch.Stroke(c + new Vector2(0f, -0.4f) * r, c + new Vector2(0f, 0.4f) * r, 3f, color);
    }

    private static void Circle(PrimitiveBatch batch, Vector2 center, float radius, float width, Color color)
    {
        const int segments = 32;
        var previous = center + new Vector2(radius, 0f);
        for (var i = 1; i <= segments; i++)
        {
            var angle = MathF.Tau * i / segments;
            var point = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            batch.Stroke(previous, point, width, color);
            previous = point;
        }
    }

    private static void Polyline(PrimitiveBatch batch, ReadOnlySpan<Vector2> points, float width, Color color, bool closed)
    {
        for (var i = 0; i < points.Length - (closed ? 0 : 1); i++)
            batch.Stroke(points[i], points[(i + 1) % points.Length], width, color);
    }
}
