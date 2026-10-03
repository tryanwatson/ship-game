using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using ShipGame.Client.Input;
using ShipGame.Net;

namespace ShipGame.Client.Rendering;

public enum MenuAction
{
    None,
    PlaySolo,
    Host,
    Join,
    Quit,
}

/// <summary>
/// The title menu: play solo, host, or join a server by address. Arrow keys and Enter or the mouse; Esc backs out
/// (and quits from the top page). Typing goes through <see cref="OnTextInput"/> so keyboard layouts and key repeat
/// behave. <see cref="Message"/> shows why we're back here (refused, dropped, couldn't host).
/// </summary>
public sealed class MainMenu
{
    private enum Page
    {
        Main,
        Join,
    }

    private enum Item
    {
        Solo,
        Host,
        Join,
        FriendlyFire,
        Quit,
        Connect,
        Back,
    }

    private sealed record Button(Rectangle Bounds, string Label, Item Item);

    private const int MinPanelWidth = 520;
    private const int Padding = 24;
    private const int ButtonHeight = 40;
    private const int ButtonGap = 12;
    private const int MaxAddressLength = 64;
    private const float TitleScale = 6f;
    private const float LabelScale = 2f;
    private const float SmallScale = 1.5f;

    private static readonly Color PanelBack = new Color(14, 18, 28) * 0.92f;
    private static readonly Color PanelBorder = new(110, 95, 70);
    private static readonly Color Title = new(240, 215, 150);
    private static readonly Color Text = new(225, 228, 235);
    private static readonly Color Muted = new(150, 155, 170);
    private static readonly Color Error = new(235, 110, 90);
    private static readonly Color ButtonBack = new(60, 70, 92);
    private static readonly Color ButtonHover = new(84, 98, 128);
    private static readonly Color ButtonBorder = new(130, 140, 165);
    private static readonly Color FieldBack = new(8, 10, 16);

    private static readonly Item[] MainItems = { Item.Solo, Item.Host, Item.Join, Item.FriendlyFire, Item.Quit };
    private static readonly Item[] JoinItems = { Item.Connect, Item.Back };

    private readonly PrimitiveBatch _batch;
    private Page _page;
    private int _selected;
    private double _blink;

    public MainMenu(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public bool IsOpen { get; private set; }

    /// <summary>The server address being typed on the join page.</summary>
    public string Address { get; set; } = "";

    /// <summary>Whether a game hosted from here has friendly fire on.</summary>
    public bool FriendlyFire { get; set; } = true;

    /// <summary>Shown under the title until the player does something; for why the last attempt failed.</summary>
    public string? Message { get; set; }

    /// <summary>The address on the join page, if it's valid.</summary>
    public ServerAddress? ParsedAddress => ServerAddress.TryParse(Address, out var address) ? address : null;

    public void Open(string? message = null, bool onJoinPage = false)
    {
        IsOpen = true;
        Message = message;
        _page = onJoinPage ? Page.Join : Page.Main;
        _selected = 0;
    }

    public void Close() => IsOpen = false;

    public void OnTextInput(char character, Keys key)
    {
        if (!IsOpen || _page != Page.Join)
            return;
        if (key == Keys.Back)
        {
            if (Address.Length > 0)
                Address = Address[..^1];
        }
        else if (Address.Length < MaxAddressLength && (char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or ':' or '[' or ']'))
        {
            Address += character;
        }
        else
        {
            return;
        }
        _blink = 0;
        _selected = 0; // Enter joins
        Message = null;
    }

    public MenuAction Update(InputState input, HudView hud, double dt)
    {
        _blink += dt;
        var items = _page == Page.Main ? MainItems : JoinItems;

        if (input.WasKeyPressed(Keys.Escape))
        {
            if (_page == Page.Main)
                return MenuAction.Quit;
            GoTo(Page.Main, Array.IndexOf(MainItems, Item.Join));
            return MenuAction.None;
        }

        if (_page == Page.Main)
        {
            if (input.WasKeyPressed(Keys.Up))
                _selected = (_selected + items.Length - 1) % items.Length;
            if (input.WasKeyPressed(Keys.Down))
                _selected = (_selected + 1) % items.Length;
        }
        else if (input.WasKeyPressed(Keys.Left) || input.WasKeyPressed(Keys.Right))
        {
            _selected = 1 - _selected;
        }

        var mouse = hud.FromScreen(input.Mouse.Position);
        var moved = input.Mouse.Position != input.PreviousMouse.Position;
        foreach (var button in Layout(hud, out _, out _))
        {
            if (!button.Bounds.Contains(mouse))
                continue;
            if (moved)
                _selected = Array.IndexOf(items, button.Item);
            if (input.WasLeftMousePressed)
                return Activate(button.Item);
        }

        if (input.WasKeyPressed(Keys.Enter))
            return Activate(items[_selected]);
        return MenuAction.None;
    }

    public void Draw(HudView hud)
    {
        _batch.Begin(hud.Transform);
        var buttons = Layout(hud, out var panel, out var field);

        FillRect(panel, PanelBack);
        OutlineRect(panel, PanelBorder);

        var y = (float)panel.Y + Padding;
        DrawCentered("SHIPGAME", panel, y, TitleScale, Title);
        y += PixelFont.Height(TitleScale) + Padding;

        if (Message is not null)
            DrawCentered(Message, panel, y, LabelScale, Error);
        y += PixelFont.Height(LabelScale) + Padding;

        if (_page == Page.Join)
        {
            PixelFont.Draw(_batch, "SERVER ADDRESS", new Vector2(field.X, y), LabelScale, Muted);
            FillRect(field, FieldBack);
            OutlineRect(field, ButtonBorder);
            // Long addresses scroll: show the end, where the typing is.
            var visible = Address;
            while (visible.Length > 0 && PixelFont.Measure(visible, LabelScale) > field.Width - 28)
                visible = visible[1..];
            var textTop = field.Center.Y - PixelFont.Height(LabelScale) / 2f;
            PixelFont.Draw(_batch, visible, new Vector2(field.X + 10, textTop), LabelScale, Text);
            if (_blink % 1.0 < 0.6)
            {
                var caretX = field.X + 10 + PixelFont.Measure(visible, LabelScale) + (visible.Length > 0 ? 4 : 0);
                FillRect(new Rectangle((int)caretX, (int)textTop, 3, (int)PixelFont.Height(LabelScale)), Text);
            }
        }

        for (var i = 0; i < buttons.Count; i++)
            DrawButton(buttons[i], i == _selected);

        var hint = _page == Page.Main ? "ARROWS AND ENTER OR CLICK" : "HOST OR HOST:PORT  -  ESC TO GO BACK";
        DrawCentered(hint, panel, panel.Bottom - Padding - PixelFont.Height(SmallScale), SmallScale, Muted);

        _batch.Flush();
    }

    private MenuAction Activate(Item item)
    {
        Message = null;
        switch (item)
        {
            case Item.Solo:
                return MenuAction.PlaySolo;
            case Item.Host:
                return MenuAction.Host;
            case Item.Join:
                GoTo(Page.Join, 0);
                return MenuAction.None;
            case Item.FriendlyFire:
                FriendlyFire = !FriendlyFire;
                return MenuAction.None;
            case Item.Quit:
                return MenuAction.Quit;
            case Item.Connect:
                if (ParsedAddress is not null)
                    return MenuAction.Join;
                Message = Address.Trim().Length == 0 ? "TYPE A SERVER ADDRESS" : "NOT A VALID ADDRESS";
                return MenuAction.None;
            default:
                GoTo(Page.Main, Array.IndexOf(MainItems, Item.Join));
                return MenuAction.None;
        }
    }

    private void GoTo(Page page, int selected)
    {
        _page = page;
        _selected = selected;
        _blink = 0;
    }

    private string LabelFor(Item item) => item switch
    {
        Item.Solo => "PLAY SOLO",
        Item.Host => "HOST GAME",
        Item.Join => "JOIN GAME",
        Item.FriendlyFire => FriendlyFire ? "HOSTING: FRIENDLY FIRE ON" : "HOSTING: CO-OP ONLY",
        Item.Quit => "QUIT",
        Item.Connect => "JOIN",
        _ => "BACK",
    };

    /// <summary>The panel, the address field (join page only), and the buttons. Shared by drawing and input.</summary>
    private List<Button> Layout(HudView hud, out Rectangle panel, out Rectangle field)
    {
        var viewport = hud.Viewport;
        var width = (int)MathF.Max(MinPanelWidth, PixelFont.Measure(Message ?? "", LabelScale) + Padding * 2);
        var inner = width - Padding * 2;
        var header = Padding + (int)PixelFont.Height(TitleScale) + Padding + (int)PixelFont.Height(LabelScale) + Padding;
        var footer = Padding + (int)PixelFont.Height(SmallScale) + Padding;

        int body;
        if (_page == Page.Main)
            body = MainItems.Length * (ButtonHeight + ButtonGap) - ButtonGap;
        else
            body = (int)PixelFont.Height(LabelScale) + 10 + ButtonHeight + Padding + ButtonHeight;

        var height = header + body + footer;
        panel = new Rectangle((viewport.Width - width) / 2, (viewport.Height - height) / 2, width, height);
        var left = panel.X + Padding;
        var top = panel.Y + header;

        var buttons = new List<Button>();
        if (_page == Page.Main)
        {
            field = Rectangle.Empty;
            for (var i = 0; i < MainItems.Length; i++)
                buttons.Add(new Button(new Rectangle(left, top + i * (ButtonHeight + ButtonGap), inner, ButtonHeight), LabelFor(MainItems[i]), MainItems[i]));
            return buttons;
        }

        field = new Rectangle(left, top + (int)PixelFont.Height(LabelScale) + 10, inner, ButtonHeight);
        var buttonTop = field.Bottom + Padding;
        var half = (inner - ButtonGap) / 2;
        buttons.Add(new Button(new Rectangle(left, buttonTop, half, ButtonHeight), LabelFor(Item.Connect), Item.Connect));
        buttons.Add(new Button(new Rectangle(left + half + ButtonGap, buttonTop, half, ButtonHeight), LabelFor(Item.Back), Item.Back));
        return buttons;
    }

    private void DrawCentered(string text, Rectangle panel, float y, float scale, Color color) =>
        PixelFont.Draw(_batch, text, new Vector2(panel.Center.X - PixelFont.Measure(text, scale) / 2f, y), scale, color);

    private void DrawButton(Button button, bool highlighted)
    {
        FillRect(button.Bounds, highlighted ? ButtonHover : ButtonBack);
        OutlineRect(button.Bounds, highlighted ? Title : ButtonBorder);
        var width = PixelFont.Measure(button.Label, LabelScale);
        var position = new Vector2(button.Bounds.Center.X - width / 2f, button.Bounds.Center.Y - PixelFont.Height(LabelScale) / 2f);
        PixelFont.Draw(_batch, button.Label, position, LabelScale, Text);
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
