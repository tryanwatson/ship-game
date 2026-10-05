using System;
using System.Net.Http;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ShipGame.Client.Rendering;

namespace ShipGame.Launcher;

/// <summary>A small window that shows the update's progress, then starts the game and closes.</summary>
public sealed class LauncherGame : Game
{
    private static readonly Color Background = new(14, 18, 28);
    private static readonly Color Title = new(232, 196, 120);
    private static readonly Color Text = new(210, 220, 235);
    private static readonly Color Dim = new(130, 140, 160);
    private static readonly Color BarBack = new(40, 50, 70);
    private static readonly Color BarFill = new(80, 160, 220);

    private readonly string[] _gameArgs;
    private readonly GameInstall _install = new(GameInstall.DefaultRoot);
    private readonly HttpClient _http = ReleaseFeed.CreateClient();
    private readonly CancellationTokenSource _cancel = new();
    private PrimitiveBatch _batch = null!;
    private Updater _updater = null!;
    private double _readyFor;
    private KeyboardState _lastKeys;
    private MouseState _lastMouse;

    /// <param name="gameArgs">Passed through to the game, e.g. <c>--connect host</c>.</param>
    public LauncherGame(string[] gameArgs)
    {
        _gameArgs = gameArgs;
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 640,
            PreferredBackBufferHeight = 360,
            SynchronizeWithVerticalRetrace = true,
        };
        IsMouseVisible = true;
        Window.Title = "ShipGame";
    }

    protected override void LoadContent()
    {
        _batch = new PrimitiveBatch(GraphicsDevice);
        Start();
    }

    private void Start()
    {
        _readyFor = 0;
        _updater = new Updater(_install, _http, GameInstall.Rid);
        _ = _updater.RunAsync(_cancel.Token);
    }

    protected override void Update(GameTime gameTime)
    {
        var keys = Keyboard.GetState();
        var mouse = Mouse.GetState();
        var state = _updater.State;

        if (Pressed(keys, Keys.Escape))
        {
            Exit();
        }
        else if (state.Phase == LauncherPhase.Ready)
        {
            // Linger on a fallback note long enough to read it.
            _readyFor += gameTime.ElapsedGameTime.TotalSeconds;
            if (_readyFor >= (state.Note is null ? 0.3 : 3.0) && _updater.Launch(_gameArgs))
                Exit();
        }
        else if (state.Phase == LauncherPhase.Failed &&
                 (Pressed(keys, Keys.Enter) || (mouse.LeftButton == ButtonState.Pressed && _lastMouse.LeftButton == ButtonState.Released)))
        {
            Start();
        }

        _lastKeys = keys;
        _lastMouse = mouse;
        base.Update(gameTime);
    }

    private bool Pressed(KeyboardState keys, Keys key) => keys.IsKeyDown(key) && _lastKeys.IsKeyUp(key);

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Background);
        var viewport = GraphicsDevice.Viewport;
        var width = viewport.Width;
        var state = _updater.State;
        _batch.Begin(Matrix.Identity);

        var y = viewport.Height * 0.28f;
        DrawCentered("SHIPGAME", y, 6f, Title, width);
        y += PixelFont.Height(6f) + 40f;

        y = DrawWrapped(state.Message, y, 2f, state.Phase == LauncherPhase.Failed ? Title : Text, width);

        if (state.Progress is { } progress)
        {
            const float barHeight = 10f;
            var barWidth = Math.Min(440f, width - 40f);
            var left = (width - barWidth) / 2f;
            y += 14f;
            Rect(left, y, barWidth, barHeight, BarBack);
            Rect(left, y, barWidth * Math.Clamp(progress, 0f, 1f), barHeight, BarFill);
            y += barHeight;
        }

        if (state.Note is not null)
            DrawWrapped(state.Note, y + 16f, 1.5f, Dim, width);
        if (state.Phase == LauncherPhase.Failed)
            DrawWrapped("Press Enter to try again, Esc to quit", y + 24f, 1.5f, Dim, width);

        _batch.Flush();
        base.Draw(gameTime);
    }

    private float DrawWrapped(string text, float y, float scale, Color color, int width)
    {
        foreach (var line in PixelFont.Wrap(text, scale, width - 40f))
        {
            DrawCentered(line, y, scale, color, width);
            y += PixelFont.Height(scale) + 6f * scale;
        }
        return y;
    }

    private void DrawCentered(string text, float y, float scale, Color color, int width) =>
        PixelFont.Draw(_batch, text, new Vector2(MathF.Round((width - PixelFont.Measure(text, scale)) / 2f), y), scale, color);

    private void Rect(float x, float y, float w, float h, Color color)
    {
        Span<Vector2> corners = stackalloc Vector2[] { new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h) };
        _batch.FillConvex(corners, color);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancel.Cancel();
            _batch?.Dispose();
        }
        base.Dispose(disposing);
    }
}
