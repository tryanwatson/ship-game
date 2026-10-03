using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ShipGame.Client.Rendering;

/// <summary>Big centered message with an optional smaller line beneath it (e.g. "SUNK" / "RESPAWNING IN 7").</summary>
public sealed class StatusBanner
{
    private const float TitleScale = 5f;
    private const float SubtitleScale = 2f;
    private const float Gap = 14f;

    private static readonly Color Backdrop = new Color(8, 10, 16) * 0.7f;
    private static readonly Color TitleColor = new(235, 90, 70);
    private static readonly Color SubtitleColor = new(230, 230, 236);

    private readonly PrimitiveBatch _batch;

    public StatusBanner(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public void Draw(string title, string subtitle, HudView hud)
    {
        var viewport = hud.Viewport;
        _batch.Begin(hud.Transform);

        var titleWidth = PixelFont.Measure(title, TitleScale);
        var subtitleWidth = PixelFont.Measure(subtitle, SubtitleScale);
        var height = PixelFont.Height(TitleScale) + Gap + PixelFont.Height(SubtitleScale);
        var top = viewport.Height * 0.3f;
        var width = MathF.Max(titleWidth, subtitleWidth) + 48f;
        var left = (viewport.Width - width) / 2f;

        Span<Vector2> backdrop = stackalloc Vector2[]
        {
            new(left, top - 20f), new(left + width, top - 20f), new(left + width, top + height + 20f), new(left, top + height + 20f),
        };
        _batch.FillConvex(backdrop, Backdrop);

        PixelFont.Draw(_batch, title, new Vector2((viewport.Width - titleWidth) / 2f, top), TitleScale, TitleColor);
        PixelFont.Draw(_batch, subtitle,
            new Vector2((viewport.Width - subtitleWidth) / 2f, top + PixelFont.Height(TitleScale) + Gap), SubtitleScale, SubtitleColor);

        _batch.Flush();
    }
}
