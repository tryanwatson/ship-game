using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using ShipGame.Client.Input;

namespace ShipGame.Client.Rendering;

public enum GameMenuAction
{
    None,
    Resume,
    Leave,
    /// <summary><see cref="GameMenu.SoundPercent"/> changed.</summary>
    Sound,
    /// <summary><see cref="GameMenu.MusicPercent"/> changed.</summary>
    Music,
}

/// <summary>
/// The Esc menu during a game or in the lobby: resume, or leave for the title menu. Arrow keys and Enter or the
/// mouse; Esc again resumes. The caller titles it (solo pauses the game behind it; online it carries on, which the
/// note can say). The sound row steps the volume: click or Enter to go up a notch (wrapping to off), Left/Right too.
/// </summary>
public sealed class GameMenu
{
    private const int PanelWidth = 420;
    private const int Padding = 24;
    private const int ButtonHeight = 40;
    private const int ButtonGap = 12;
    private const float TitleScale = 4f;
    private const float LabelScale = 2f;
    private const float SmallScale = 1.5f;
    private const int SoundStep = 20;

    private static readonly Color Dim = new Color(0, 0, 0) * 0.45f;
    private static readonly Color PanelBack = new Color(14, 18, 28) * 0.95f;
    private static readonly Color PanelBorder = new(110, 95, 70);
    private static readonly Color Title = new(240, 215, 150);
    private static readonly Color Text = new(225, 228, 235);
    private static readonly Color Muted = new(150, 155, 170);
    private static readonly Color ButtonBack = new(60, 70, 92);
    private static readonly Color ButtonHover = new(84, 98, 128);
    private static readonly Color ButtonBorder = new(130, 140, 165);

    private static readonly (string Label, GameMenuAction Action)[] Items =
    {
        ("RESUME", GameMenuAction.Resume),
        ("SOUND", GameMenuAction.Sound),
        ("MUSIC", GameMenuAction.Music),
        ("LEAVE GAME", GameMenuAction.Leave),
    };

    private readonly PrimitiveBatch _batch;
    private int _selected;

    public GameMenu(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public bool IsOpen { get; private set; }

    /// <summary>The volume shown on (and set by) the sound row, 0..100.</summary>
    public int SoundPercent { get; set; } = 80;

    /// <summary>The volume shown on (and set by) the music row, 0..100.</summary>
    public int MusicPercent { get; set; } = 30;

    public void Open()
    {
        IsOpen = true;
        _selected = 0;
    }

    public void Close() => IsOpen = false;

    public GameMenuAction Update(InputState input, HudView hud, string? note)
    {
        if (input.WasKeyPressed(Keys.Escape))
            return GameMenuAction.Resume;
        if (input.WasKeyPressed(Keys.Up))
            _selected = (_selected + Items.Length - 1) % Items.Length;
        if (input.WasKeyPressed(Keys.Down))
            _selected = (_selected + 1) % Items.Length;
        if (Items[_selected].Action is GameMenuAction.Sound or GameMenuAction.Music
            && (input.WasKeyPressed(Keys.Left) || input.WasKeyPressed(Keys.Right)))
        {
            var step = input.WasKeyPressed(Keys.Left) ? -SoundStep : SoundStep;
            if (Items[_selected].Action == GameMenuAction.Sound)
                SoundPercent = Math.Clamp(SoundPercent + step, 0, 100);
            else
                MusicPercent = Math.Clamp(MusicPercent + step, 0, 100);
            return Items[_selected].Action;
        }

        var mouse = hud.FromScreen(input.Mouse.Position);
        var moved = input.Mouse.Position != input.PreviousMouse.Position;
        Layout(hud, note, out _, out var buttons);
        for (var i = 0; i < buttons.Length; i++)
        {
            if (!buttons[i].Contains(mouse))
                continue;
            if (moved)
                _selected = i;
            if (input.WasLeftMousePressed)
                return Choose(i);
        }

        return input.WasKeyPressed(Keys.Enter) ? Choose(_selected) : GameMenuAction.None;
    }

    private GameMenuAction Choose(int item)
    {
        if (Items[item].Action == GameMenuAction.Sound)
            SoundPercent = NextStep(SoundPercent);
        else if (Items[item].Action == GameMenuAction.Music)
            MusicPercent = NextStep(MusicPercent);
        return Items[item].Action;
    }

    private static int NextStep(int percent) => percent >= 100 ? 0 : Math.Min(100, percent + SoundStep);

    private string Label(int item) => Items[item].Action switch
    {
        GameMenuAction.Sound => VolumeLabel("SOUND", SoundPercent),
        GameMenuAction.Music => VolumeLabel("MUSIC", MusicPercent),
        _ => Items[item].Label,
    };

    private static string VolumeLabel(string name, int percent) => percent == 0 ? $"{name}  OFF" : $"{name}  {percent}%";

    /// <param name="note">A line under the title, or null.</param>
    public void Draw(HudView hud, string title, string? note)
    {
        _batch.Begin(hud.Transform);
        var viewport = hud.Viewport;
        Fill(new Rectangle(0, 0, viewport.Width, viewport.Height), Dim);

        Layout(hud, note, out var panel, out var buttons);
        Fill(panel, PanelBack);
        Outline(panel, PanelBorder);

        var y = panel.Y + Padding;
        DrawCentered(title, panel, y, TitleScale, Title);
        if (note is not null)
            DrawCentered(note, panel, y + PixelFont.Height(TitleScale) + 10, SmallScale, Muted);

        for (var i = 0; i < buttons.Length; i++)
        {
            var selected = i == _selected;
            Fill(buttons[i], selected ? ButtonHover : ButtonBack);
            Outline(buttons[i], selected ? Title : ButtonBorder);
            var label = Label(i);
            PixelFont.Draw(_batch, label, new Vector2(buttons[i].Center.X - PixelFont.Measure(label, LabelScale) / 2f,
                buttons[i].Center.Y - PixelFont.Height(LabelScale) / 2f), LabelScale, Text);
        }
        _batch.Flush();
    }

    private static void Layout(HudView hud, string? note, out Rectangle panel, out Rectangle[] buttons)
    {
        var header = (int)PixelFont.Height(TitleScale) + Padding + (note is not null ? (int)PixelFont.Height(SmallScale) + 10 : 0);
        var height = Padding + header + Items.Length * (ButtonHeight + ButtonGap) - ButtonGap + Padding;
        panel = new Rectangle((hud.Viewport.Width - PanelWidth) / 2, (hud.Viewport.Height - height) / 2, PanelWidth, height);
        buttons = new Rectangle[Items.Length];
        for (var i = 0; i < Items.Length; i++)
        {
            buttons[i] = new Rectangle(panel.X + Padding, panel.Y + Padding + header + i * (ButtonHeight + ButtonGap),
                PanelWidth - Padding * 2, ButtonHeight);
        }
    }

    private void DrawCentered(string text, Rectangle panel, float y, float scale, Color color) =>
        PixelFont.Draw(_batch, text, new Vector2(panel.Center.X - PixelFont.Measure(text, scale) / 2f, y), scale, color);

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
