using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>A deterministic kit of tropical scenery. Raised objects share the ships' depth order.</summary>
public sealed class IslandScenery
{
    public enum Kind { Palm, Bush, Rock, Boathouse, Fort, Beacon, DockPost, Crate }
    public sealed record Item(Kind Kind, NVector2 Position, float Scale, int Variant);
    private readonly PrimitiveBatch _batch;
    private readonly List<Item> _items = new();
    private readonly Dictionary<int, List<(NVector2 Position, float Size, Color Color)>> _patches = new();
    private World? _world;
    private int _region;
    public IReadOnlyList<Item> Items => _items;

    public IslandScenery(PrimitiveBatch batch) => _batch = batch;
    public static float MarkerHeight(Island island) => island.HasShipyard ? 58f : island.IsFortress || HasBeacon(island) ? 78f : 56f;

    // Plain islands' looks, picked from their ids so every client agrees.
    private static bool HasBeacon(Island island) => !island.HasShipyard && !island.IsFortress && island.Id % 6 == 4;
    private static bool IsRocky(Island island) => !island.HasShipyard && island.Id % 3 == 0;
    private static bool IsDense(Island island) => !IsRocky(island) && island.Id % 5 == 1;

    public void EnsureWorld(World world)
    {
        // Built afresh for each world, and each region it sails into: the islands are all new.
        if (ReferenceEquals(_world, world) && _region == world.RegionsEntered) return;
        _world = world;
        _region = world.RegionsEntered;
        _items.Clear(); _patches.Clear();
        foreach (var island in world.Islands)
            Build(island);
    }

    private void Build(Island island)
    {
        var rng = new Random(island.Id * 7919);
        var rocky = IsRocky(island);
        var dense = IsDense(island);
        var dark = rocky && island.Level >= 5;
        if (island.HasShipyard)
        {
            _items.Add(new Item(Kind.Boathouse, island.Center, 1f, island.Id));
            _items.Add(new Item(Kind.Crate, island.Center + IsoProjection.Grid(-1.25f, 0.25f), 1f, 0));
            var (shore, outward) = DockPose(island);
            var side = new NVector2(-outward.Y, outward.X);
            foreach (var along in new[] { 0.4f, 1.9f })
                foreach (var sign in new[] { -1f, 1f })
                    _items.Add(new Item(Kind.DockPost, shore + outward * along + side * sign * 0.42f, 1f, 0));
        }
        else if (island.IsFortress)
            _items.Add(new Item(Kind.Fort, island.Center, 1.1f + 0.05f * island.Level, 0)); // the keep; its guns are on the shore
        else if (HasBeacon(island))
            _items.Add(new Item(Kind.Beacon, island.Center, 1f, 0));
        else if (rocky)
            _items.Add(new Item(Kind.Rock, island.Center, dark ? 2.2f : 1.6f, dark ? 1 : 0));

        var patches = new List<(NVector2 Position, float Size, Color Color)>();
        _patches.Add(island.Id, patches);
        // Big islands get more of everything, about as densely spread as on the small ones.
        var size = Math.Max(1f, island.Area / 60f);
        for (var i = 0; i < (int)(8 * MathF.Sqrt(size)); i++)
        {
            var point = InteriorPoint(island, rng, 0.2f, 0.62f);
            patches.Add((point, 10f + (float)rng.NextDouble() * 15f,
                i % 2 == 0 ? new Color(113, 155, 78) : new Color(75, 121, 68)));
        }
        var palms = (int)((rocky ? 2 : dense ? 8 : Math.Clamp((int)(island.Area / 17f), 3, 6)) * size / 2f) + 2;
        var rocks = (int)((rocky ? 7 : 3) * MathF.Sqrt(size));
        var bushes = (int)((dense ? 9 : 5) * MathF.Sqrt(size));
        for (var i = 0; i < palms + rocks + bushes; i++)
        {
            var kind = i < palms ? Kind.Palm : i < palms + rocks ? Kind.Rock : Kind.Bush;
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var point = InteriorPoint(island, rng, 0.48f, kind == Kind.Palm ? 0.74f : 0.72f);
                // Keep landmarks and the path to the dock clear; retry rather than losing most trees on small ports.
                if (NVector2.Distance(point, island.Center) < (island.HasShipyard ? 1.35f : 1.1f)) continue;
                if (_items.Exists(item => (item.Kind is Kind.Palm or Kind.Rock) && NVector2.DistanceSquared(item.Position, point) < 0.5f)) continue;
                if (island.HasShipyard)
                {
                    var (_, outward) = DockPose(island);
                    var across = new NVector2(-outward.Y, outward.X);
                    var offset = point - island.Center;
                    if (NVector2.Dot(offset, outward) > 0.7f && MathF.Abs(NVector2.Dot(offset, across)) < 0.6f) continue;
                }
                var scale = 0.72f + (float)rng.NextDouble() * 0.45f;
                _items.Add(new Item(kind, point, scale, rocky && dark ? 1 : rng.Next(3)));
                break;
            }
        }
    }

    private static NVector2 InteriorPoint(Island island, Random rng, float min, float max)
    {
        var outline = island.Outline;
        var i = rng.Next(outline.Length);
        var edge = NVector2.Lerp(outline[i], outline[(i + 1) % outline.Length], (float)rng.NextDouble());
        return NVector2.Lerp(island.Center, edge, min + (max - min) * (float)rng.NextDouble());
    }

    public void DrawGround(Island island)
    {
        if (_patches.TryGetValue(island.Id, out var patches))
            foreach (var patch in patches)
                _batch.FillEllipse(IsoProjection.WorldToIso(patch.Position), new Vector2(patch.Size, patch.Size * 0.45f), patch.Color * 0.32f);
        if (!island.HasShipyard) return;
        var (shore, outward) = DockPose(island);
        var side = new NVector2(-outward.Y, outward.X);
        Vector2 Point(float along, float across, float height = 4) =>
            IsoProjection.WorldToIso(shore + outward * along + side * across) - new Vector2(0, height);
        Span<Vector2> plank = stackalloc Vector2[4];
        plank[0] = Point(-0.28f, -0.4f); plank[1] = Point(2.1f, -0.4f);
        plank[2] = Point(2.1f, 0.4f); plank[3] = Point(-0.28f, 0.4f);
        _batch.FillConvex(plank, new Color(112, 74, 44));
        for (var i = 0; i < 13; i++)
        {
            var start = -0.24f + i * 0.18f;
            plank[0] = Point(start, -0.38f); plank[1] = Point(start + 0.15f, -0.38f);
            plank[2] = Point(start + 0.15f, 0.38f); plank[3] = Point(start, 0.38f);
            _batch.FillConvex(plank, i % 3 == 0 ? new Color(181, 139, 83) : new Color(202, 160, 101));
        }
    }

    private static (NVector2 Shore, NVector2 Outward) DockPose(Island island)
    {
        var outline = island.Outline;
        var shore = island.Center;
        var depth = float.MinValue;
        for (var i = 0; i < outline.Length; i++)
        {
            var middle = (outline[i] + outline[(i + 1) % outline.Length]) / 2f;
            if (middle.Y > depth) { depth = middle.Y; shore = middle; }
        }
        return (shore, NVector2.Normalize(shore - island.Center));
    }

    public void Draw(Item item, float time)
    {
        var at = IsoProjection.WorldToIso(item.Position);
        var scale = item.Scale;
        if (item.Kind != Kind.DockPost)
            _batch.FillEllipse(at + new Vector2(6, 3) * scale, new Vector2(12, 5) * scale, new Color(34, 63, 40) * 0.2f);
        switch (item.Kind)
        {
            case Kind.Palm: DrawPalm(at, scale, item.Variant, time); break;
            case Kind.Bush: DrawBush(at, scale, item.Variant); break;
            case Kind.Rock: DrawRock(at, scale, item.Variant); break;
            case Kind.Boathouse: DrawBoathouse(item.Position, item.Variant, time); break;
            case Kind.Fort: DrawFort(item.Position, scale, item.Variant); break;
            case Kind.Beacon: DrawBeacon(item.Position, time); break;
            case Kind.DockPost:
                _batch.Stroke(at + new Vector2(0, 3), at - new Vector2(0, 12), 4f, new Color(111, 77, 46));
                _batch.FillEllipse(at - new Vector2(0, 12), new Vector2(3, 1.5f), new Color(221, 184, 119));
                _batch.Stroke(at - new Vector2(2, 6), at + new Vector2(2, -6), 2f, new Color(211, 199, 163));
                break;
            case Kind.Crate:
                DrawBox(item.Position, 0.28f, 0.28f, 11f, new Color(178, 125, 62), new Color(132, 85, 45), new Color(231, 184, 94));
                _batch.Stroke(at + new Vector2(-5, -10), at + new Vector2(5, -10), 2, new Color(79, 62, 41));
                break;
        }
    }

    private void DrawPalm(Vector2 at, float scale, int variant, float time)
    {
        var lean = new Vector2((variant - 1) * 5f, -36f) * scale;
        var crown = at + lean + new Vector2(MathF.Sin(time * 0.8f + at.X * 0.01f) * 1.1f, 0);
        var bend = at + lean * 0.48f + new Vector2(2, 0) * scale;
        _batch.Stroke(at, bend, 5f * scale, new Color(134, 99, 56));
        _batch.Stroke(bend, crown, 3.8f * scale, new Color(177, 136, 76));
        for (var i = 1; i <= 5; i++)
        {
            var ring = Vector2.Lerp(at, crown, i / 7f);
            _batch.Stroke(ring - new Vector2(2, 0) * scale, ring + new Vector2(2, 1) * scale, 1f, new Color(104, 80, 44));
        }
        Span<Vector2> leaf = stackalloc Vector2[4];
        for (var i = 0; i < 7; i++)
        {
            var angle = i * MathF.Tau / 7 + variant * 0.25f;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle) * 0.45f);
            var tip = crown + direction * (22f * scale) + new Vector2(0, 6f * scale);
            var middle = crown + direction * (12f * scale);
            var across = new Vector2(-direction.Y, direction.X) * (4f * scale);
            leaf[0] = crown; leaf[1] = middle + across; leaf[2] = tip; leaf[3] = middle - across;
            _batch.FillConvex(leaf, i < 3 ? new Color(58, 111, 65) : new Color(95, 151, 72));
            _batch.Stroke(crown, middle, 0.9f, new Color(149, 176, 82) * 0.65f);
        }
        _batch.FillEllipse(crown + new Vector2(1, 3), new Vector2(2.4f, 2f) * scale, new Color(119, 85, 44));
    }

    private void DrawBush(Vector2 at, float scale, int variant)
    {
        _batch.FillEllipse(at + new Vector2(-5, -4) * scale, new Vector2(8, 6) * scale, new Color(52, 104, 61));
        _batch.FillEllipse(at + new Vector2(4, -7) * scale, new Vector2(9, 7) * scale, new Color(72, 128, 65));
        _batch.FillEllipse(at + new Vector2(-1, -10) * scale, new Vector2(6, 4) * scale, new Color(112, 157, 74));
        if (variant == 2)
            for (var i = 0; i < 3; i++)
                _batch.FillEllipse(at + new Vector2(-4 + i * 4, -9 - i % 2 * 3) * scale, new Vector2(1.5f) * scale, new Color(232, 181, 92));
    }

    private void DrawRock(Vector2 at, float scale, int variant)
    {
        var dark = variant == 1;
        var left = at + new Vector2(-13, -3) * scale;
        var right = at + new Vector2(14, -2) * scale;
        var front = at + new Vector2(2, 5) * scale;
        var peak = at + new Vector2(-3, -22) * scale;
        var back = at + new Vector2(6, -17) * scale;
        Span<Vector2> face = stackalloc Vector2[] { left, front, peak };
        _batch.FillConvex(face, dark ? new Color(98, 105, 100) : new Color(138, 149, 132));
        face[0] = front; face[1] = right; face[2] = back;
        _batch.FillConvex(face, dark ? new Color(65, 75, 76) : new Color(100, 119, 112));
        face[0] = front; face[1] = back; face[2] = peak;
        _batch.FillConvex(face, dark ? new Color(82, 92, 87) : new Color(123, 139, 122));
        face[0] = peak; face[1] = back; face[2] = right;
        _batch.FillConvex(face, dark ? new Color(125, 127, 111) : new Color(179, 181, 155));
        if (dark && scale > 1.5f)
        {
            _batch.Stroke(peak + new Vector2(0, 8) * scale, front - new Vector2(2, 4) * scale, 1.5f, new Color(213, 119, 61));
            _batch.Stroke(front - new Vector2(2, 4) * scale, right - new Vector2(5, 0) * scale, 1f, new Color(219, 143, 72));
        }
    }

    private void DrawBoathouse(NVector2 position, int variant, float time)
    {
        const float halfX = 0.8f, halfY = 0.62f, height = 22f;
        DrawBox(position, halfX, halfY, height, new Color(184, 137, 83), new Color(137, 94, 58), new Color(190, 144, 89));
        var up = new Vector2(0, -height);
        var a = IsoProjection.WorldToIso(position + IsoProjection.Grid(-halfX, -halfY)) + up;
        var b = IsoProjection.WorldToIso(position + IsoProjection.Grid(halfX, -halfY)) + up;
        var c = IsoProjection.WorldToIso(position + IsoProjection.Grid(halfX, halfY)) + up;
        var d = IsoProjection.WorldToIso(position + IsoProjection.Grid(-halfX, halfY)) + up;
        var ridgeRear = IsoProjection.WorldToIso(position + IsoProjection.Grid(-halfX, 0)) + up - new Vector2(0, 15);
        var ridgeFront = IsoProjection.WorldToIso(position + IsoProjection.Grid(halfX, 0)) + up - new Vector2(0, 15);
        Span<Vector2> roof = stackalloc Vector2[] { a, b, ridgeFront, ridgeRear };
        _batch.FillConvex(roof, new Color(125, 59, 46));
        Span<Vector2> gable = stackalloc Vector2[] { b, c, ridgeFront };
        _batch.FillConvex(gable, new Color(182, 128, 78));
        roof[0] = d; roof[1] = c; roof[2] = ridgeFront; roof[3] = ridgeRear;
        _batch.FillConvex(roof, new Color(187, 89, 56));
        for (var i = 1; i < 5; i++)
            _batch.Stroke(Vector2.Lerp(d, ridgeRear, i / 5f), Vector2.Lerp(c, ridgeFront, i / 5f), 0.8f, new Color(235, 147, 87) * 0.55f);
        _batch.Stroke(d, c, 2f, new Color(98, 61, 38));
        var baseLeft = d - up; var baseFront = c - up;
        DrawWallOpening(baseLeft, baseFront, 0.54f, 0.2f, 14f, 0f);
        DrawWallOpening(baseLeft, baseFront, 0.2f, 0.13f, 7f, 9f);
        var flagAt = ridgeRear - new Vector2(0, 2);
        _batch.Stroke(flagAt, flagAt - new Vector2(0, 15), 1.5f, new Color(78, 66, 45));
        Span<Vector2> flag = stackalloc Vector2[] { flagAt - new Vector2(0, 15), flagAt + new Vector2(12, -12 + MathF.Sin(time)), flagAt - new Vector2(0, 8) };
        _batch.FillConvex(flag, variant % 2 == 0 ? new Color(87, 197, 187) : new Color(240, 192, 82));
    }

    private void DrawFort(NVector2 position, float scale, int variant)
    {
        var half = 0.7f * scale;
        var height = 37f * scale;
        DrawBox(position, half, half, height, new Color(149, 151, 128), new Color(105, 121, 111), new Color(181, 179, 151));
        var left = IsoProjection.WorldToIso(position + IsoProjection.Grid(-half, half));
        var front = IsoProjection.WorldToIso(position + IsoProjection.Grid(half, half));
        var right = IsoProjection.WorldToIso(position + IsoProjection.Grid(half, -half));
        for (var i = 1; i < 5; i++)
        {
            var up = new Vector2(0, -height * i / 5);
            _batch.Stroke(left + up, front + up, 0.8f, new Color(78, 97, 88) * 0.5f);
            _batch.Stroke(front + up, right + up, 0.8f, new Color(71, 89, 82) * 0.55f);
        }
        DrawWallOpening(left, front, 0.55f, 0.2f, 17f * scale, 0);
        DrawWallOpening(front, right, 0.4f, 0.09f, 9f * scale, height * 0.55f);
        foreach (var offset in new[] { new NVector2(-half, -half), new NVector2(half, -half), new NVector2(half, half), new NVector2(-half, half) })
            DrawBox(position + IsoProjection.Grid(offset.X, offset.Y), 0.17f * scale, 0.17f * scale, (variant == 1 ? 3 : 8) * scale,
                new Color(155, 156, 131), new Color(106, 124, 111), new Color(193, 190, 157), height);
    }

    private void DrawBeacon(NVector2 position, float time)
    {
        DrawRock(IsoProjection.WorldToIso(position), 1f, 0);
        DrawBox(position, 0.34f, 0.34f, 42, new Color(221, 212, 164), new Color(158, 168, 145), new Color(237, 221, 165));
        var at = IsoProjection.WorldToIso(position) - new Vector2(0, 43);
        _batch.FillEllipse(at, new Vector2(11, 6), new Color(69, 92, 87));
        _batch.FillEllipse(at - new Vector2(0, 3), new Vector2(5, 4), new Color(251, 201, 99));
        _batch.Stroke(at, at - new Vector2(0, 20), 2, new Color(101, 78, 43));
        Span<Vector2> pennant = stackalloc Vector2[] { at - new Vector2(0, 20), at + new Vector2(17, -17 + MathF.Sin(time)), at - new Vector2(0, 11) };
        _batch.FillConvex(pennant, new Color(230, 147, 75));
    }

    private void DrawBox(NVector2 position, float halfX, float halfY, float height, Color light, Color shade, Color top, float baseHeight = 0f)
    {
        var raised = new Vector2(0, baseHeight);
        var a = IsoProjection.WorldToIso(position + IsoProjection.Grid(-halfX, -halfY)) - raised;
        var b = IsoProjection.WorldToIso(position + IsoProjection.Grid(halfX, -halfY)) - raised;
        var c = IsoProjection.WorldToIso(position + IsoProjection.Grid(halfX, halfY)) - raised;
        var d = IsoProjection.WorldToIso(position + IsoProjection.Grid(-halfX, halfY)) - raised;
        var up = new Vector2(0, -height);
        Span<Vector2> face = stackalloc Vector2[] { d, c, c + up, d + up };
        _batch.FillConvex(face, light);
        face[0] = c; face[1] = b; face[2] = b + up; face[3] = c + up;
        _batch.FillConvex(face, shade);
        face[0] = a + up; face[1] = b + up; face[2] = c + up; face[3] = d + up;
        _batch.FillConvex(face, top);
    }

    private void DrawWallOpening(Vector2 from, Vector2 to, float fraction, float width, float height, float bottom)
    {
        var a = Vector2.Lerp(from, to, fraction - width / 2f) - new Vector2(0, bottom);
        var b = Vector2.Lerp(from, to, fraction + width / 2f) - new Vector2(0, bottom);
        Span<Vector2> door = stackalloc Vector2[] { a, b, b - new Vector2(0, height), a - new Vector2(0, height) };
        _batch.FillConvex(door, new Color(44, 64, 56));
        _batch.Stroke(a, b, 1.2f, new Color(224, 187, 123));
    }
}
