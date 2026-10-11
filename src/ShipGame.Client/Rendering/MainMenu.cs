using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using ShipGame.Client.Input;
using ShipGame.Net;
using ShipGame.Shared.Progression;

namespace ShipGame.Client.Rendering;

public enum MenuAction
{
    None,
    PlaySolo,
    Host,
    Join,
    Settings,
    Quit,
}

/// <summary>
/// The title menu: play solo, host, or join a server by address (and password, if it has one); or the settings. Arrow keys and
/// Enter or the mouse; Tab or Up/Down move between the join page's fields; Esc backs out of the join page (it never
/// quits; that's the Quit button). Typing goes through <see cref="OnTextInput"/> so keyboard layouts and key repeat
/// behave; Ctrl/Cmd+V pastes into the focused field and Ctrl/Cmd+C copies the address.
/// <see cref="Message"/> shows why we're back here (refused, dropped, couldn't host).
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
        Testing,
        Settings,
        Quit,
        Connect,
        Back,
    }

    private enum Field
    {
        Address,
        Password,
    }

    private sealed record Button(Rectangle Bounds, string Label, Item Item);

    private const int MinPanelWidth = 520;
    private const int Padding = 24;
    private const int ButtonHeight = 40;
    private const int ButtonGap = 12;
    private const int LabelGap = 10;
    private const int MaxFieldLength = 64;
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

    private static readonly Item[] MainItems = { Item.Solo, Item.Host, Item.Join, Item.FriendlyFire, Item.Testing, Item.Settings, Item.Quit };
    private static readonly Item[] JoinItems = { Item.Connect, Item.Back };

    private readonly PrimitiveBatch _batch;
    private Page _page;
    private int _selected;
    private Field _focus;
    private double _blink;

    public MainMenu(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    public bool IsOpen { get; private set; }

    /// <summary>Open on its first page, where nothing is being typed (so other keys are free).</summary>
    public bool OnMainPage => IsOpen && _page == Page.Main;

    /// <summary>The server address being typed on the join page.</summary>
    public string Address { get; set; } = "";

    /// <summary>The server's password, if it has one; blank otherwise.</summary>
    public string Password { get; set; } = "";

    /// <summary>Whether a game hosted from here has friendly fire on.</summary>
    public bool FriendlyFire { get; set; } = true;

    /// <summary>Whether a solo or hosted run opens with a late game's worth of cards to choose (for playtesting).</summary>
    public bool Testing { get; set; }

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
        _focus = onJoinPage && message == "WRONG PASSWORD" ? Field.Password : Field.Address;
    }

    public void Close() => IsOpen = false;

    public void OnTextInput(char character, Keys key)
    {
        if (!IsOpen || _page != Page.Join)
            return;

        var text = FocusedText;
        if (key == Keys.Back)
        {
            if (text.Length > 0)
                SetFocusedText(text[..^1]);
        }
        else if (!IsShortcutLetter(character))
        {
            Insert(character.ToString());
        }
    }

    /// <summary>
    /// Adds <paramref name="text"/> to the focused field, keeping only what it accepts; pasted text is cut at its
    /// first line break and trimmed, since copied addresses and passwords tend to bring a stray newline or space.
    /// </summary>
    private void Insert(string text)
    {
        var current = FocusedText;
        var added = new StringBuilder();
        foreach (var character in text)
        {
            if (current.Length + added.Length >= MaxFieldLength)
                break;
            if (Accepts(_focus, character))
                added.Append(character);
        }
        if (added.Length > 0)
            SetFocusedText(current + added);
    }

    private void Paste()
    {
        var text = Clipboard.GetText();
        var lineBreak = text.IndexOfAny(new[] { '\r', '\n' });
        Insert((lineBreak >= 0 ? text[..lineBreak] : text).Trim());
    }

    private string FocusedText => _focus == Field.Address ? Address : Password;

    private void SetFocusedText(string text)
    {
        if (_focus == Field.Address)
            Address = text;
        else
            Password = text;
        _blink = 0;
        _selected = 0; // Enter joins
        Message = null;
    }

    // The letter of a Ctrl/Cmd+C or +V shortcut, in case the platform also sends it as typed text. AltGr reads as
    // Ctrl+Alt, so letters typed with Alt held still go through.
    private static bool IsShortcutLetter(char character)
    {
        if (character is not ('c' or 'C' or 'v' or 'V'))
            return false;
        var keyboard = Keyboard.GetState();
        var alt = keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);
        var control = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        var command = OperatingSystem.IsMacOS() && (keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows));
        return command || (control && !alt);
    }

    public MenuAction Update(InputState input, HudView hud, double dt)
    {
        _blink += dt;
        var items = _page == Page.Main ? MainItems : JoinItems;

        // Esc backs out of the join page; on the top page it does nothing (quitting takes the Quit button), so a
        // stray second press after leaving a game doesn't close it.
        if (input.WasKeyPressed(Keys.Escape))
        {
            if (_page != Page.Main)
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
        else
        {
            if (input.WasKeyPressed(Keys.Tab) || input.WasKeyPressed(Keys.Up) || input.WasKeyPressed(Keys.Down))
                Focus(_focus == Field.Address ? Field.Password : Field.Address);
            if (input.WasKeyPressed(Keys.Left) || input.WasKeyPressed(Keys.Right))
                _selected = 1 - _selected;
            if (input.WasShortcutPressed(Keys.V))
                Paste();
            if (input.WasShortcutPressed(Keys.C) && _focus == Field.Address)
                Clipboard.SetText(Address.Trim()); // never the password: it's drawn masked
        }

        var mouse = hud.FromScreen(input.Mouse.Position);
        var moved = input.Mouse.Position != input.PreviousMouse.Position;
        var buttons = Layout(hud, out _, out var addressField, out var passwordField);
        foreach (var button in buttons)
        {
            if (!button.Bounds.Contains(mouse))
                continue;
            if (moved)
                _selected = Array.IndexOf(items, button.Item);
            if (input.WasLeftMousePressed)
                return Activate(button.Item);
        }
        if (_page == Page.Join && input.WasLeftMousePressed)
        {
            if (addressField.Contains(mouse))
                Focus(Field.Address);
            else if (passwordField.Contains(mouse))
                Focus(Field.Password);
        }

        if (input.WasKeyPressed(Keys.Enter))
            return Activate(items[_selected]);
        return MenuAction.None;
    }

    public void Draw(HudView hud)
    {
        _batch.Begin(hud.Transform);
        var buttons = Layout(hud, out var panel, out var addressField, out var passwordField);

        FillRect(panel, PanelBack);
        OutlineRect(panel, PanelBorder);

        var y = (float)panel.Y + Padding;
        DrawCentered("SHIPGAME", panel, y, TitleScale, Title);
        y += PixelFont.Height(TitleScale) + Padding;

        if (Message is not null)
            DrawCentered(Message, panel, y, LabelScale, Error);

        if (_page == Page.Join)
        {
            DrawField("SERVER ADDRESS", Address, addressField, _focus == Field.Address);
            DrawField("PASSWORD - BLANK IF NONE", new string('*', Password.Length), passwordField, _focus == Field.Password);
        }

        for (var i = 0; i < buttons.Count; i++)
            DrawButton(buttons[i], i == _selected);

        var paste = OperatingSystem.IsMacOS() ? "CMD+V" : "CTRL+V";
        var hint = _page == Page.Main ? "ARROWS AND ENTER OR CLICK" : $"TAB: NEXT FIELD  -  {paste}: PASTE  -  ESC: BACK";
        DrawCentered(hint, panel, panel.Bottom - Padding - PixelFont.Height(SmallScale), SmallScale, Muted);

        _batch.Flush();
    }

    private static bool Accepts(Field field, char character) => field == Field.Address
        ? char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or ':' or '[' or ']'
        : character is >= ' ' and <= '~';

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
            case Item.Testing:
                Testing = !Testing;
                return MenuAction.None;
            case Item.Settings:
                return MenuAction.Settings;
            case Item.Quit:
                return MenuAction.Quit;
            case Item.Connect:
                if (ParsedAddress is not null)
                    return MenuAction.Join;
                Message = Address.Trim().Length == 0 ? "TYPE A SERVER ADDRESS" : "NOT A VALID ADDRESS";
                Focus(Field.Address);
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
        _focus = Field.Address;
        _blink = 0;
    }

    private void Focus(Field field)
    {
        _focus = field;
        _blink = 0;
    }

    private string LabelFor(Item item) => item switch
    {
        Item.Solo => "PLAY SOLO",
        Item.Host => "HOST GAME",
        Item.Join => "JOIN GAME",
        Item.FriendlyFire => FriendlyFire ? "HOSTING: FRIENDLY FIRE ON" : "HOSTING: CO-OP ONLY",
        Item.Testing => Testing ? $"TESTING: START WITH {CardRewards.TestingHands} CARDS" : "TESTING: OFF",
        Item.Settings => "SETTINGS",
        Item.Quit => "QUIT",
        Item.Connect => "JOIN",
        _ => "BACK",
    };

    /// <summary>
    /// The panel, the join page's two text fields (empty on the main page), and the buttons. Shared by drawing and
    /// input. Each field's label sits just above it.
    /// </summary>
    private List<Button> Layout(HudView hud, out Rectangle panel, out Rectangle addressField, out Rectangle passwordField)
    {
        var viewport = hud.Viewport;
        var width = (int)MathF.Max(MinPanelWidth, PixelFont.Measure(Message ?? "", LabelScale) + Padding * 2);
        var inner = width - Padding * 2;
        var labelHeight = (int)PixelFont.Height(LabelScale);
        var header = Padding + (int)PixelFont.Height(TitleScale) + Padding + labelHeight + Padding;
        var footer = Padding + (int)PixelFont.Height(SmallScale) + Padding;
        var fieldBlock = labelHeight + LabelGap + ButtonHeight;

        var body = _page == Page.Main
            ? MainItems.Length * (ButtonHeight + ButtonGap) - ButtonGap
            : fieldBlock + Padding + fieldBlock + Padding + ButtonHeight;

        var height = header + body + footer;
        panel = new Rectangle((viewport.Width - width) / 2, (viewport.Height - height) / 2, width, height);
        var left = panel.X + Padding;
        var top = panel.Y + header;

        var buttons = new List<Button>();
        if (_page == Page.Main)
        {
            addressField = passwordField = Rectangle.Empty;
            for (var i = 0; i < MainItems.Length; i++)
                buttons.Add(new Button(new Rectangle(left, top + i * (ButtonHeight + ButtonGap), inner, ButtonHeight), LabelFor(MainItems[i]), MainItems[i]));
            return buttons;
        }

        addressField = new Rectangle(left, top + labelHeight + LabelGap, inner, ButtonHeight);
        passwordField = new Rectangle(left, addressField.Bottom + Padding + labelHeight + LabelGap, inner, ButtonHeight);
        var buttonTop = passwordField.Bottom + Padding;
        var half = (inner - ButtonGap) / 2;
        buttons.Add(new Button(new Rectangle(left, buttonTop, half, ButtonHeight), LabelFor(Item.Connect), Item.Connect));
        buttons.Add(new Button(new Rectangle(left + half + ButtonGap, buttonTop, half, ButtonHeight), LabelFor(Item.Back), Item.Back));
        return buttons;
    }

    private void DrawField(string label, string shown, Rectangle field, bool focused)
    {
        PixelFont.Draw(_batch, label, new Vector2(field.X, field.Y - LabelGap - PixelFont.Height(LabelScale)), LabelScale, Muted);
        FillRect(field, FieldBack);
        OutlineRect(field, focused ? Title : ButtonBorder);

        // Long text scrolls: show the end, where the typing is.
        var visible = shown;
        while (visible.Length > 0 && PixelFont.Measure(visible, LabelScale) > field.Width - 28)
            visible = visible[1..];
        var textTop = field.Center.Y - PixelFont.Height(LabelScale) / 2f;
        PixelFont.Draw(_batch, visible, new Vector2(field.X + 10, textTop), LabelScale, Text);

        if (focused && _blink % 1.0 < 0.6)
        {
            var caretX = field.X + 10 + PixelFont.Measure(visible, LabelScale) + (visible.Length > 0 ? 4 : 0);
            FillRect(new Rectangle((int)caretX, (int)textTop, 3, (int)PixelFont.Height(LabelScale)), Text);
        }
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
