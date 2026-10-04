using System;
using Microsoft.Xna.Framework;
using ShipGame.Shared.Maps;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Sailing into a new sea announces it: its name and level, across the top of the screen for a few seconds, then
/// fading. Also shown for the first sea at the start of a run.
/// </summary>
public sealed class SeaBanner
{
    private const double ShowSeconds = 3.5;
    private const double FadeSeconds = 1.0;
    private const float NameScale = 4f;
    private const float LevelScale = 2f;
    private const float Gap = 10f;

    private static readonly Color Backdrop = new Color(8, 10, 16) * 0.6f;
    private static readonly Color NameColor = new(240, 220, 160);
    private static readonly Color LevelColor = new(230, 230, 236);

    private readonly PrimitiveBatch _batch;
    private Sea? _sea;
    private long _shownAt;

    public SeaBanner(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    /// <param name="sea">The sea our ship is in; null while we have no ship (which keeps the last one, so respawning doesn't re-announce it).</param>
    public void Draw(Sea? sea, HudView hud)
    {
        var now = Environment.TickCount64;
        if (sea is not null && sea != _sea)
        {
            _sea = sea;
            _shownAt = now;
        }
        if (_sea is null)
            return;

        var age = (now - _shownAt) / 1000.0;
        if (age >= ShowSeconds + FadeSeconds)
            return;
        var opacity = age <= ShowSeconds ? 1f : 1f - (float)((age - ShowSeconds) / FadeSeconds);

        var viewport = hud.Viewport;
        var name = _sea.Name;
        var level = $"LEVEL {_sea.Level} WATERS";
        var nameWidth = PixelFont.Measure(name, NameScale);
        var levelWidth = PixelFont.Measure(level, LevelScale);
        var width = MathF.Max(nameWidth, levelWidth) + 48f;
        var height = PixelFont.Height(NameScale) + Gap + PixelFont.Height(LevelScale);
        var top = viewport.Height * 0.14f;
        var left = (viewport.Width - width) / 2f;

        _batch.Begin(hud.Transform);
        Span<Vector2> backdrop = stackalloc Vector2[]
        {
            new(left, top - 14f), new(left + width, top - 14f), new(left + width, top + height + 14f), new(left, top + height + 14f),
        };
        _batch.FillConvex(backdrop, Backdrop * opacity);
        PixelFont.Draw(_batch, name, new Vector2((viewport.Width - nameWidth) / 2f, top), NameScale, NameColor * opacity);
        PixelFont.Draw(_batch, level, new Vector2((viewport.Width - levelWidth) / 2f, top + PixelFont.Height(NameScale) + Gap),
            LevelScale, LevelColor * opacity);
        _batch.Flush();
    }
}
