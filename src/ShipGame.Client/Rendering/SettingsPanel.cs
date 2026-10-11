using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using ShipGame.Client.Audio;
using ShipGame.Client.Input;

namespace ShipGame.Client.Rendering;

public enum SettingsAction
{
    None,
    /// <summary>A slider is being dragged: it's already taken effect, but isn't settled yet.</summary>
    Changed,
    /// <summary>A slider has settled on a new value (stepped, scrolled, or let go): save it, and play a sample.</summary>
    Committed,
    Closed,
}

public readonly record struct SettingsResult(SettingsAction Action, Channel? Channel = null);

/// <summary>
/// The settings, from the title menu or the game menu: a slider for each audio channel. Drag a slider, click
/// anywhere along it, or scroll over it; or Up/Down to choose and Left/Right to step. Esc or Back closes. The
/// sliders write straight into the shared <see cref="AudioLevels"/>, so the sound follows as they move.
/// </summary>
public sealed class SettingsPanel
{
    private const int PanelWidth = 600;
    private const int Padding = 24;
    private const int RowHeight = 40;
    private const int RowGap = 10;
    private const int LabelWidth = 150;
    private const int PercentWidth = 70;
    private const int TrackHeight = 6;
    private const int HandleWidth = 12;
    private const int HandleHeight = 24;
    private const float Step = 0.05f;
    private const float TitleScale = 4f;
    private const float LabelScale = 2f;
    private const float SmallScale = 1.5f;

    private static readonly Color Dim = new Color(0, 0, 0) * 0.45f;
    private static readonly Color PanelBack = new Color(14, 18, 28) * 0.95f;
    private static readonly Color PanelBorder = new(110, 95, 70);
    private static readonly Color Title = new(240, 215, 150);
    private static readonly Color Text = new(225, 228, 235);
    private static readonly Color Muted = new(150, 155, 170);
    private static readonly Color RowHover = new(40, 48, 66);
    private static readonly Color TrackBack = new(8, 10, 16);
    private static readonly Color TrackFill = new(200, 170, 105);
    private static readonly Color ButtonBack = new(60, 70, 92);
    private static readonly Color ButtonHover = new(84, 98, 128);
    private static readonly Color ButtonBorder = new(130, 140, 165);

    private static readonly Channel[] Channels = Enum.GetValues<Channel>();

    private readonly PrimitiveBatch _batch;
    private int _selected;
    private Channel? _dragging;

    public SettingsPanel(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public bool IsOpen { get; private set; }

    /// <summary>The sliders' values: shared with the audio, so moving one is heard at once.</summary>
    public AudioLevels Levels { get; set; } = new();

    // Rows: one per channel, then Back.
    private int BackRow => Channels.Length;

    public void Open()
    {
        IsOpen = true;
        _selected = 0;
        _dragging = null;
    }

    public void Close()
    {
        IsOpen = false;
        _dragging = null;
    }

    public SettingsResult Update(InputState input, HudView hud)
    {
        if (input.WasKeyPressed(Keys.Escape))
        {
            Close();
            return new SettingsResult(SettingsAction.Closed);
        }

        Layout(hud, out _, out var rows, out var tracks, out var back);
        var mouse = hud.FromScreen(input.MousePosition);

        // A drag holds its slider until the button comes up, wherever the mouse wanders.
        if (_dragging is { } held)
        {
            if (input.IsLeftMouseDown)
            {
                return Set(held, FromMouse(tracks[(int)held], mouse.X)) ? new SettingsResult(SettingsAction.Changed, held)
                    : new SettingsResult(SettingsAction.None);
            }
            _dragging = null;
            return new SettingsResult(SettingsAction.Committed, held);
        }

        if (input.WasKeyPressed(Keys.Up))
            _selected = (_selected + BackRow) % (BackRow + 1);
        if (input.WasKeyPressed(Keys.Down))
            _selected = (_selected + 1) % (BackRow + 1);
        if (_selected < BackRow && (input.WasKeyPressed(Keys.Left) || input.WasKeyPressed(Keys.Right)))
        {
            var channel = Channels[_selected];
            var step = input.WasKeyPressed(Keys.Left) ? -Step : Step;
            if (Set(channel, Levels[channel] + step))
                return new SettingsResult(SettingsAction.Committed, channel);
        }

        var moved = input.Mouse.Position != input.PreviousMouse.Position;
        for (var i = 0; i < rows.Length; i++)
        {
            if (!rows[i].Contains(mouse))
                continue;
            var channel = Channels[i];
            if (moved)
                _selected = i;
            if (input.WasLeftMousePressed)
            {
                _selected = i;
                _dragging = channel;
                Set(channel, FromMouse(tracks[i], mouse.X));
                return new SettingsResult(SettingsAction.Changed, channel);
            }
            if (input.ScrollDelta != 0 && Set(channel, Levels[channel] + Math.Sign(input.ScrollDelta) * Step))
                return new SettingsResult(SettingsAction.Committed, channel);
        }
        if (back.Contains(mouse))
        {
            if (moved)
                _selected = BackRow;
            if (input.WasLeftMousePressed)
            {
                Close();
                return new SettingsResult(SettingsAction.Closed);
            }
        }
        if (_selected == BackRow && input.WasKeyPressed(Keys.Enter))
        {
            Close();
            return new SettingsResult(SettingsAction.Closed);
        }
        return new SettingsResult(SettingsAction.None);
    }

    /// <summary>Sets a slider (snapped to whole percents); whether it moved.</summary>
    private bool Set(Channel channel, float value)
    {
        var snapped = MathF.Round(Math.Clamp(value, 0f, 1f) * 100f) / 100f;
        if (MathF.Abs(snapped - Levels[channel]) < 0.001f)
            return false;
        Levels[channel] = snapped;
        return true;
    }

    private static float FromMouse(Rectangle track, float x) => (x - track.Left) / track.Width;

    public void Draw(HudView hud)
    {
        _batch.Begin(hud.Transform);
        var viewport = hud.Viewport;
        Fill(new Rectangle(0, 0, viewport.Width, viewport.Height), Dim);
        Layout(hud, out var panel, out var rows, out var tracks, out var back);
        Fill(panel, PanelBack);
        Outline(panel, PanelBorder);

        var y = panel.Y + Padding;
        DrawCentered("SETTINGS", panel, y, TitleScale, Title);
        y += (int)PixelFont.Height(TitleScale) + Padding;
        PixelFont.Draw(_batch, "AUDIO", new Vector2(panel.X + Padding, y), LabelScale, Muted);

        for (var i = 0; i < rows.Length; i++)
        {
            var channel = Channels[i];
            var selected = i == _selected || _dragging == channel;
            if (selected)
            {
                Fill(rows[i], RowHover);
                Outline(rows[i], Title);
            }
            var textY = rows[i].Center.Y - PixelFont.Height(LabelScale) / 2f;
            PixelFont.Draw(_batch, AudioLevels.Name(channel), new Vector2(rows[i].X + 12, textY), LabelScale, selected ? Title : Text);

            var level = Levels[channel];
            var track = tracks[i];
            var bar = new Rectangle(track.X, track.Center.Y - TrackHeight / 2, track.Width, TrackHeight);
            Fill(bar, TrackBack);
            Fill(new Rectangle(bar.X, bar.Y, (int)(bar.Width * level), bar.Height), TrackFill);
            var handleX = track.X + (int)(track.Width * level) - HandleWidth / 2;
            var handle = new Rectangle(handleX, track.Center.Y - HandleHeight / 2, HandleWidth, HandleHeight);
            Fill(handle, selected ? Title : Text);
            Outline(handle, ButtonBorder);

            var percent = level <= 0f ? "OFF" : $"{MathF.Round(level * 100f)}%";
            PixelFont.Draw(_batch, percent, new Vector2(rows[i].Right - 12 - PixelFont.Measure(percent, LabelScale), textY), LabelScale,
                level <= 0f ? Muted : Text);
        }

        var backSelected = _selected == BackRow;
        Fill(back, backSelected ? ButtonHover : ButtonBack);
        Outline(back, backSelected ? Title : ButtonBorder);
        DrawCentered("BACK", back, back.Center.Y - PixelFont.Height(LabelScale) / 2f, LabelScale, Text);

        const string hint = "DRAG, CLICK OR SCROLL  -  ARROWS TO STEP  -  ESC: BACK";
        DrawCentered(hint, panel, panel.Bottom - Padding - PixelFont.Height(SmallScale), SmallScale, Muted);
        _batch.Flush();
    }

    /// <summary>The panel, each channel's row and the slider track in it, and the Back button; shared by drawing and input.</summary>
    private static void Layout(HudView hud, out Rectangle panel, out Rectangle[] rows, out Rectangle[] tracks, out Rectangle back)
    {
        var header = Padding + (int)PixelFont.Height(TitleScale) + Padding + (int)PixelFont.Height(LabelScale) + RowGap;
        var body = Channels.Length * (RowHeight + RowGap) + Padding;
        var footer = RowHeight + Padding + (int)PixelFont.Height(SmallScale) + Padding;
        var height = header + body + footer;
        panel = new Rectangle((hud.Viewport.Width - PanelWidth) / 2, (hud.Viewport.Height - height) / 2, PanelWidth, height);
        var inner = PanelWidth - Padding * 2;
        rows = new Rectangle[Channels.Length];
        tracks = new Rectangle[Channels.Length];
        for (var i = 0; i < Channels.Length; i++)
        {
            rows[i] = new Rectangle(panel.X + Padding, panel.Y + header + i * (RowHeight + RowGap), inner, RowHeight);
            var trackLeft = rows[i].X + LabelWidth;
            tracks[i] = new Rectangle(trackLeft, rows[i].Y, rows[i].Right - PercentWidth - 12 - trackLeft, RowHeight);
        }
        back = new Rectangle(panel.X + Padding, panel.Y + header + body, inner, RowHeight);
    }

    private void DrawCentered(string text, Rectangle area, float y, float scale, Color color) =>
        PixelFont.Draw(_batch, text, new Vector2(area.Center.X - PixelFont.Measure(text, scale) / 2f, y), scale, color);

    private void Fill(Rectangle rect, Color color)
    {
        Span<Vector2> corners = stackalloc Vector2[] { new(rect.Left, rect.Top), new(rect.Right, rect.Top), new(rect.Right, rect.Bottom), new(rect.Left, rect.Bottom) };
        _batch.FillConvex(corners, color);
    }

    private void Outline(Rectangle rect, Color color)
    {
        Span<Vector2> corners = stackalloc Vector2[] { new(rect.Left, rect.Top), new(rect.Right, rect.Top), new(rect.Right, rect.Bottom), new(rect.Left, rect.Bottom) };
        _batch.Outline(corners, color);
    }
}
