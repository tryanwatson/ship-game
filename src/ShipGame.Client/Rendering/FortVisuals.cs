using System;
using Microsoft.Xna.Framework;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Forts on a fortress's shore, from shaded boxes like the island scenery: a squat stone battery whose long gun swings
/// round to bear (the fort's heading), or a taller mortar tower. Both fly the pirates' flag.
/// </summary>
public sealed class FortVisuals
{
    /// <summary>How high over the fort its health bar floats.</summary>
    public const float HealthHeight = 58f;

    private const float BatteryHalf = 0.95f;
    private const float BatteryHeight = 18f;
    private const float TowerHalf = 0.75f;
    private const float TowerHeight = 32f;

    private static readonly Color StoneLit = new(150, 146, 128);
    private static readonly Color StoneShade = new(104, 106, 98);
    private static readonly Color StoneTop = new(178, 172, 150);
    private static readonly Color DarkStoneLit = new(126, 116, 104);
    private static readonly Color DarkStoneShade = new(86, 82, 78);
    private static readonly Color DarkStoneTop = new(152, 142, 124);
    private static readonly Color Mortar = new(66, 58, 50);
    private static readonly Color Iron = new(38, 40, 44);
    private static readonly Color Pole = new(78, 66, 45);
    private static readonly Color Flag = new(213, 85, 64);
    private static readonly Color Flash = new(255, 236, 190);

    private readonly PrimitiveBatch _batch;

    public FortVisuals(PrimitiveBatch batch) => _batch = batch;

    public void Draw(Ship fort, NVector2 position, float heading, float time, float hitFlash = 0f)
    {
        if (Fortresses.KindOf(fort) == FortKind.MortarTower)
            DrawTower(position, heading, time, hitFlash);
        else
            DrawBattery(position, heading, time, hitFlash);
    }

    private void DrawBattery(NVector2 position, float heading, float time, float hitFlash)
    {
        DrawBox(position, BatteryHalf, BatteryHalf, BatteryHeight, Tint(StoneLit, hitFlash), Tint(StoneShade, hitFlash), Tint(StoneTop, hitFlash));
        DrawMerlons(position, BatteryHalf, BatteryHeight, StoneLit, StoneShade, StoneTop, hitFlash);

        // The gun, over the parapet toward whatever it's tracking.
        var forward = new NVector2(MathF.Cos(heading), MathF.Sin(heading));
        var breech = IsoProjection.WorldToIso(position - forward * 0.25f) - new Vector2(0, BatteryHeight + 4f);
        var muzzle = IsoProjection.WorldToIso(position + forward * 1.35f) - new Vector2(0, BatteryHeight + 5f);
        _batch.Stroke(breech, muzzle, 5f, Iron);
        _batch.FillEllipse(muzzle, new Vector2(2.6f, 2f), Iron);
        _batch.FillEllipse(breech, new Vector2(4f, 3f), Iron);

        DrawFlag(position + IsoProjection.Grid(-BatteryHalf * 0.7f, -BatteryHalf * 0.7f), BatteryHeight, time);
    }

    private void DrawTower(NVector2 position, float heading, float time, float hitFlash)
    {
        DrawBox(position, TowerHalf, TowerHalf, TowerHeight, Tint(DarkStoneLit, hitFlash), Tint(DarkStoneShade, hitFlash), Tint(DarkStoneTop, hitFlash));
        DrawMerlons(position, TowerHalf, TowerHeight, DarkStoneLit, DarkStoneShade, DarkStoneTop, hitFlash);

        // A fat mortar squatting on the roof, tipped toward its target.
        var forward = new NVector2(MathF.Cos(heading), MathF.Sin(heading));
        var seat = IsoProjection.WorldToIso(position) - new Vector2(0, TowerHeight + 3f);
        var mouth = IsoProjection.WorldToIso(position + forward * 0.35f) - new Vector2(0, TowerHeight + 12f);
        _batch.FillEllipse(seat, new Vector2(8f, 4f), Mortar);
        _batch.Stroke(seat, mouth, 8f, Iron);
        _batch.FillEllipse(mouth, new Vector2(4.5f, 2.5f), new Color(18, 18, 20));

        DrawFlag(position + IsoProjection.Grid(-TowerHalf * 0.7f, -TowerHalf * 0.7f), TowerHeight, time);
    }

    private void DrawMerlons(NVector2 position, float half, float height, Color lit, Color shade, Color top, float hitFlash)
    {
        var merlon = half * 0.22f;
        foreach (var (x, y) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f), (0f, 1f), (1f, 0f) })
        {
            var at = position + IsoProjection.Grid(x * (half - merlon), y * (half - merlon));
            DrawBox(at, merlon, merlon, 5f, Tint(lit, hitFlash), Tint(shade, hitFlash), Tint(top, hitFlash), height);
        }
    }

    private void DrawFlag(NVector2 foot, float baseHeight, float time)
    {
        var bottom = IsoProjection.WorldToIso(foot) - new Vector2(0, baseHeight);
        var top = bottom - new Vector2(0, 20f);
        _batch.Stroke(bottom, top, 1.5f, Pole);
        Span<Vector2> flag = stackalloc Vector2[] { top, top + new Vector2(13f, 3f + MathF.Sin(time * 2f)), top + new Vector2(0, 7f) };
        _batch.FillConvex(flag, Flag);
    }

    private static Color Tint(Color color, float hitFlash) => Color.Lerp(color, Flash, hitFlash * 0.7f);

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
}
