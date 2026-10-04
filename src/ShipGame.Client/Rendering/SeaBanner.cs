using System;
using Microsoft.Xna.Framework;
using ShipGame.Shared.Maps;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Big news across the top of the screen for a few seconds, then fading: sailing into new waters (their name and
/// level, also shown for the first waters at the start of a run), a fortress taken, a boss on its way.
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
    private static readonly Color AlarmColor = new(245, 110, 85);

    private readonly PrimitiveBatch _batch;
    private Sea? _sea;
    private string? _title;
    private string _subtitle = "";
    private Color _titleColor = NameColor;
    private long _shownAt;

    public SeaBanner(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    /// <summary>Shows <paramref name="title"/> over <paramref name="subtitle"/> now, in place of whatever was showing.</summary>
    public void Announce(string title, string subtitle, bool alarm = false)
    {
        _title = title;
        _subtitle = subtitle;
        _titleColor = alarm ? AlarmColor : NameColor;
        _shownAt = Environment.TickCount64;
    }

    /// <param name="sea">The waters our ship is in; null while we have no ship (which keeps the last, so respawning doesn't re-announce them).</param>
    public void Draw(Sea? sea, HudView hud)
    {
        var now = Environment.TickCount64;
        if (sea is not null && sea != _sea)
        {
            _sea = sea;
            Announce(sea.Name, $"LEVEL {sea.Level} WATERS");
        }
        if (_title is null)
            return;

        var age = (now - _shownAt) / 1000.0;
        if (age >= ShowSeconds + FadeSeconds)
            return;
        var opacity = age <= ShowSeconds ? 1f : 1f - (float)((age - ShowSeconds) / FadeSeconds);

        var viewport = hud.Viewport;
        var nameWidth = PixelFont.Measure(_title, NameScale);
        var levelWidth = PixelFont.Measure(_subtitle, LevelScale);
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
        PixelFont.Draw(_batch, _title, new Vector2((viewport.Width - nameWidth) / 2f, top), NameScale, _titleColor * opacity);
        PixelFont.Draw(_batch, _subtitle, new Vector2((viewport.Width - levelWidth) / 2f, top + PixelFont.Height(NameScale) + Gap),
            LevelScale, LevelColor * opacity);
        _batch.Flush();
    }
}
