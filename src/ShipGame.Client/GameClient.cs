using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ShipGame.Client.Input;
using ShipGame.Client.Rendering;
using ShipGame.Client.Session;
using ShipGame.Net;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Maps;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Upgrades;
using NVector2 = System.Numerics.Vector2;

namespace ShipGame.Client;

public sealed class GameClient : Game
{
    // Player id in single-player; online, the server assigns one.
    private const int SoloPlayerId = 1;

    private readonly string? _connectHost;
    private readonly int _connectPort;
    private readonly bool _hosting;
    private HostedServer? _hostedServer;
    private string? _hostError;

    private const float CameraPanSpeed = 900f;

    private static readonly (Keys Key, AbilitySlot Slot)[] AbilityKeys =
    {
        (Keys.D1, AbilitySlot.One),
        (Keys.D2, AbilitySlot.Two),
        (Keys.D3, AbilitySlot.Three),
        (Keys.D4, AbilitySlot.Four),
    };

    // While right mouse is held, re-issue the move order once the cursor drifts this far (world units). Kept small:
    // with the camera following the ship the target rides along ahead of it, and coarse jumps in the target made
    // the glide-in speed limit sawtooth. If this gets chatty over the network, rate-limit it there.
    private const float DragReissueDistance = 0.1f;

    private readonly GraphicsDeviceManager _graphics;
    private readonly InputState _input = new();
    private readonly Camera _camera = new();

    private IGameSession _session = null!;
    private PrimitiveBatch _primitives = null!;
    private WorldRenderer _worldRenderer = null!;
    private AbilityBar _abilityBar = null!;
    private CompassRose _compass = null!;
    private HudCounters _hudCounters = null!;
    private OffscreenMarkers _offscreenMarkers = null!;
    private IslandOverlays _islandOverlays = null!;
    private ShipyardPanel _shipyardPanel = null!;
    private StatusBanner _statusBanner = null!;

    private bool _cameraLocked = true;
    private NVector2 _lastMoveOrder;
    private int _sentRudder;
    private double _titleTimer;
    private int _framesSinceTitle;

    /// <param name="connectHost">Server to join; null for single-player.</param>
    /// <param name="host">Run a server in this process on <paramref name="connectPort"/> and join it.</param>
    public GameClient(string? connectHost = null, int connectPort = Protocol.DefaultPort, bool host = false)
    {
        _connectHost = connectHost;
        _connectPort = connectPort;
        _hosting = host;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            SynchronizeWithVerticalRetrace = true,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        IsFixedTimeStep = false; // The session runs its own fixed tick; render as fast as vsync allows.
        Window.AllowUserResizing = true;
    }

    private int LocalPlayerId => _session.LocalPlayerId;

    private NetworkGameSession? Online => _session as NetworkGameSession;

    protected override void Initialize()
    {
        if (_hosting)
        {
            try
            {
                _hostedServer = new HostedServer(_connectPort);
            }
            catch (InvalidOperationException)
            {
                _hostError = $"PORT {_connectPort} IS IN USE";
            }
        }

        if (_hostError is not null)
            _session = new LocalGameSession(EmptySea(), SoloPlayerId); // just the banner over open water: don't join anyone else's game
        else if (_connectHost is not null)
            _session = new NetworkGameSession(_connectHost, _connectPort);
        else
            StartRun();
        base.Initialize();
    }

    private static World EmptySea()
    {
        var world = new World(Archipelago.Size);
        foreach (var island in Archipelago.CreateIslands())
            world.AddIsland(island);
        return world;
    }

    /// <summary>A fresh run: the player's ship at the center, pirates arriving in waves.</summary>
    private void StartRun()
    {
        var world = new World(Archipelago.Size) { Waves = new WaveDirector(seed: Environment.TickCount) };
        foreach (var island in Archipelago.CreateIslands())
            world.AddIsland(island);
        world.SpawnShip(Archipelago.Size / 2f, 0f, ShipStats.Sloop, SoloPlayerId, Loadouts.Sloop);

        _session = new LocalGameSession(world, SoloPlayerId);
        _sentRudder = 0;
        _cameraLocked = true;
    }

    protected override void LoadContent()
    {
        _primitives = new PrimitiveBatch(GraphicsDevice);
        _worldRenderer = new WorldRenderer(_primitives);
        _abilityBar = new AbilityBar(_primitives);
        _compass = new CompassRose(_primitives);
        _hudCounters = new HudCounters(_primitives);
        _offscreenMarkers = new OffscreenMarkers(_primitives);
        _islandOverlays = new IslandOverlays(_primitives);
        _shipyardPanel = new ShipyardPanel(_primitives);
        _statusBanner = new StatusBanner(_primitives);
    }

    protected override void UnloadContent()
    {
        Online?.Dispose();
        _hostedServer?.Dispose();
        _primitives.Dispose();
    }

    protected override void Update(GameTime gameTime)
    {
        var dt = gameTime.ElapsedGameTime.TotalSeconds;
        _input.Update();

        if (_input.IsKeyDown(Keys.Escape))
            Exit();

        // Enter: online, ready up in the lobby; offline, start a new run once this one is over.
        if (IsActive && _input.WasKeyPressed(Keys.Enter))
        {
            if (Online is { Connection.Status: ConnectionStatus.Lobby } online)
                online.Connection.SetReady(!IsLocallyReady(online));
            else if (Online is null && _session.World.IsRunOver)
                StartRun();
        }

        if (IsActive)
        {
            _shipyardPanel.Update(_session.World, _session.World.GetPlayerShip(LocalPlayerId), _input, GraphicsDevice.Viewport, _session.Send);
            HandleOrders();
        }
        UpdateRudder();

        _session.Update(dt);
        _session.TakeEvents(); // nothing listens yet; drained so they don't pile up (effects and sounds will)

        if (IsActive)
            HandleCamera((float)dt);

        UpdateTitle(dt);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(10, 22, 40));
        var view = _camera.GetView(GraphicsDevice.Viewport);
        _worldRenderer.Draw(_session.World, _session.InterpolationAlpha, LocalPlayerId, view);
        _islandOverlays.Draw(_session.World, _session.World.GetPlayerShip(LocalPlayerId), _session.InterpolationAlpha, view, GraphicsDevice.Viewport);
        _offscreenMarkers.Draw(_session.World, _session.InterpolationAlpha, view, GraphicsDevice.Viewport);
        var localShip = _session.World.GetPlayerShip(LocalPlayerId);
        var plunderReady = localShip is not null && Plundering.PlunderableFrom(_session.World, localShip.Position) is not null;
        var shipyardReady = localShip is not null && Shipyards.ShipyardFrom(_session.World, localShip.Position) is not null;
        _abilityBar.Draw(localShip, plunderReady, shipyardReady, GraphicsDevice.Viewport);
        _compass.Draw(_session.World.Wind, GraphicsDevice.Viewport);
        var gold = _session.World.Players.TryGetValue(LocalPlayerId, out var player) ? player.Gold : 0;
        _hudCounters.Draw(gold, _session.World.Waves?.Wave ?? 0, GraphicsDevice.Viewport);
        _shipyardPanel.Draw(_session.World, localShip, _input, GraphicsDevice.Viewport);
        DrawStatusBanner();
        base.Draw(gameTime);
    }

    private void HandleOrders()
    {
        if (_input.WasKeyPressed(Keys.X))
            _session.Send(new ToggleAnchorCommand(LocalPlayerId));

        if (_input.WasKeyPressed(Keys.W) || _input.WasKeyPressed(Keys.Space))
            _session.Send(new AdjustThrottleCommand(LocalPlayerId, +1));
        if (_input.WasKeyPressed(Keys.S) || _input.WasKeyPressed(Keys.LeftControl) || _input.WasKeyPressed(Keys.RightControl))
            _session.Send(new AdjustThrottleCommand(LocalPlayerId, -1));

        var mouseWorld = IsoProjection.IsoToWorld(_camera.ScreenToIso(_input.MousePosition, GraphicsDevice.Viewport));

        // Quick-cast: abilities fire on key press, sending the cursor position for any that need a target.
        foreach (var (key, slot) in AbilityKeys)
        {
            if (_input.WasKeyPressed(key))
                _session.Send(new CastAbilityCommand(LocalPlayerId, slot, mouseWorld));
        }

        if (!GraphicsDevice.Viewport.Bounds.Contains(_input.Mouse.Position))
            return;

        var dragged = _input.IsRightMouseDown && NVector2.Distance(mouseWorld, _lastMoveOrder) > DragReissueDistance;
        if (_input.WasRightMousePressed || dragged)
        {
            _session.Send(new MoveCommand(LocalPlayerId, mouseWorld));
            _lastMoveOrder = mouseWorld;
        }
    }

    /// <summary>
    /// A/D hold the helm to port/starboard (relative to the ship, not the screen). Sent only on change; released
    /// when the window loses focus so a key held while alt-tabbing doesn't leave the helm hard over.
    /// </summary>
    private void UpdateRudder()
    {
        var rudder = 0;
        if (IsActive)
        {
            if (_input.IsKeyDown(Keys.A)) rudder -= 1;
            if (_input.IsKeyDown(Keys.D)) rudder += 1;
        }

        if (rudder == _sentRudder)
            return;
        _session.Send(new SetRudderCommand(LocalPlayerId, rudder));
        _sentRudder = rudder;
    }

    private void HandleCamera(float dt)
    {
        if (_input.ScrollDelta != 0)
            _camera.Zoom *= 1f + _input.ScrollDelta / 1200f;

        if (_input.WasKeyPressed(Keys.Y))
            _cameraLocked = !_cameraLocked;

        var ship = _session.World.GetPlayerShip(LocalPlayerId);
        if (ship is not null && (_cameraLocked || _input.IsKeyDown(Keys.C)))
        {
            var pos = NVector2.Lerp(ship.PreviousPosition, ship.Position, _session.InterpolationAlpha);
            _camera.Position = IsoProjection.WorldToIso(pos);
            return;
        }

        var pan = Vector2.Zero;
        if (_input.IsKeyDown(Keys.Left)) pan.X -= 1;
        if (_input.IsKeyDown(Keys.Right)) pan.X += 1;
        if (_input.IsKeyDown(Keys.Up)) pan.Y -= 1;
        if (_input.IsKeyDown(Keys.Down)) pan.Y += 1;
        _camera.Position += pan * (CameraPanSpeed / _camera.Zoom * dt);
    }

    private bool IsLocallyReady(NetworkGameSession online) =>
        online.Connection.Lobby?.Players.Any(p => p.PlayerId == online.LocalPlayerId && p.Ready) == true;

    private void DrawStatusBanner()
    {
        var world = _session.World;
        if (_hostError is not null)
        {
            _statusBanner.Draw("COULD NOT HOST", _hostError, GraphicsDevice.Viewport);
            return;
        }
        if (Online is { } online && DrawConnectionBanner(online))
            return;

        if (world.IsRunOver)
        {
            var wave = world.Waves?.Wave ?? 0;
            _statusBanner.Draw("RUN OVER", $"SUNK ON WAVE {wave}  -  PRESS ENTER FOR A NEW RUN", GraphicsDevice.Viewport);
        }
        else if (world.Players.TryGetValue(LocalPlayerId, out var player) && player.IsAwaitingRespawn)
        {
            var seconds = (int)Math.Ceiling(player.RespawnTicksRemaining / (double)SimConstants.TickRate);
            _statusBanner.Draw("SUNK", $"RESPAWNING IN {seconds}", GraphicsDevice.Viewport);
        }
    }

    /// <summary>Online-only banners: connecting, refused or dropped, and the lobby. True if one was drawn.</summary>
    private bool DrawConnectionBanner(NetworkGameSession online)
    {
        var connection = online.Connection;
        switch (connection.Status)
        {
            case ConnectionStatus.Connecting:
                _statusBanner.Draw("CONNECTING", $"{online.Host}:{online.Port}", GraphicsDevice.Viewport);
                return true;
            case ConnectionStatus.Disconnected:
                _statusBanner.Draw("DISCONNECTED", connection.DisconnectReason ?? "", GraphicsDevice.Viewport);
                return true;
            case ConnectionStatus.Lobby:
            {
                var players = connection.Lobby?.Players ?? Array.Empty<LobbyPlayer>();
                var ready = players.Count(p => p.Ready);
                var prompt = IsLocallyReady(online) ? "READY - WAITING FOR THE CREW" : "PRESS ENTER WHEN READY";
                var title = _session.World.IsRunOver ? $"RUN OVER - WAVE {_session.World.Waves?.Wave ?? 0}" : "LOBBY";
                _statusBanner.Draw(title, $"{players.Count} SAILORS  {ready} READY  -  {prompt}", GraphicsDevice.Viewport);
                return true;
            }
            default:
                return false;
        }
    }

    private void UpdateTitle(double dt)
    {
        _framesSinceTitle++;
        _titleTimer += dt;
        if (_titleTimer < 0.5)
            return;

        var world = _session.World;
        var ship = world.GetPlayerShip(LocalPlayerId);
        var fps = _framesSinceTitle / _titleTimer;
        var wave = world.Waves?.Wave ?? 0;
        var pirates = world.Ships.Count(s => s.Team == Team.Pirates);
        var gold = world.Players.TryGetValue(LocalPlayerId, out var player) ? player.Gold : 0;

        // The window title doubles as a status line until the game has text rendering.
        string status;
        if (world.IsRunOver)
            status = $"RUN OVER on wave {wave} with {gold} gold - press Enter for a new run";
        else if (ship is null)
            status = "sunk - respawning";
        else if (world.Waves is { } waves && pirates == 0)
            status = $"wave {wave + 1} in {Math.Ceiling(waves.TicksUntilNextWave / (double)SimConstants.TickRate):0}s";
        else
            status = $"wave {wave}: {pirates} pirates";

        if (ship?.Anchor == AnchorState.Down)
            status += ship.PlunderIslandId is null ? " | at anchor" : " | at anchor, plundering";
        else if (ship?.Anchor == AnchorState.Raising)
            status += $" | weighing anchor {Math.Ceiling(ship.AnchorRaiseTicksRemaining / (double)SimConstants.TickRate):0}s";
        if (Online is { } online)
        {
            var role = _hostedServer is not null ? $"hosting on {_hostedServer.Port}" : "online";
            status = $"{role} as player {online.LocalPlayerId} ({online.Connection.RoundTripMs} ms) | {status}";
        }
        Window.Title = $"ShipGame | {status} | speed {ship?.Speed:0.0} (sail {ship?.Throttle}/{ShipMovement.ThrottleLevels}) | {fps:0} fps";
        _titleTimer = 0;
        _framesSinceTitle = 0;
    }
}
