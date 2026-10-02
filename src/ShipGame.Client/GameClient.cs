using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ShipGame.Client.Input;
using ShipGame.Client.Rendering;
using ShipGame.Client.Session;
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
    private const int LocalPlayerId = 1;

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

    private bool _cameraLocked = true;
    private NVector2 _lastMoveOrder;
    private int _sentRudder;
    private double _titleTimer;
    private int _framesSinceTitle;

    public GameClient()
    {
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

    protected override void Initialize()
    {
        StartRun();
        base.Initialize();
    }

    /// <summary>A fresh run: the player's ship at the center, pirates arriving in waves.</summary>
    private void StartRun()
    {
        var world = new World(Archipelago.Size) { Waves = new WaveDirector(seed: Environment.TickCount) };
        foreach (var island in Archipelago.CreateIslands())
            world.AddIsland(island);
        world.SpawnShip(Archipelago.Size / 2f, 0f, ShipStats.Sloop, LocalPlayerId, Loadouts.Sloop);

        _session = new LocalGameSession(world, LocalPlayerId);
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
    }

    protected override void UnloadContent()
    {
        _primitives.Dispose();
    }

    protected override void Update(GameTime gameTime)
    {
        var dt = gameTime.ElapsedGameTime.TotalSeconds;
        _input.Update();

        if (_input.IsKeyDown(Keys.Escape))
            Exit();

        // Sunk: the run is over. Enter starts a new one.
        if (IsActive && _session.World.GetPlayerShip(LocalPlayerId) is null && _input.WasKeyPressed(Keys.Enter))
            StartRun();

        if (IsActive)
        {
            _shipyardPanel.Update(_session.World, _session.World.GetPlayerShip(LocalPlayerId), _input, GraphicsDevice.Viewport, _session.Send);
            HandleOrders();
        }
        UpdateRudder();

        _session.Update(dt);

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
        if (ship is null)
            status = $"SUNK on wave {wave} with {gold} gold - press Enter for a new run";
        else if (world.Waves is { } waves && pirates == 0)
            status = $"wave {wave + 1} in {Math.Ceiling(waves.TicksUntilNextWave / (double)SimConstants.TickRate):0}s";
        else
            status = $"wave {wave}: {pirates} pirates";

        if (ship?.Anchor == AnchorState.Down)
            status += ship.PlunderIslandId is null ? " | at anchor" : " | at anchor, plundering";
        else if (ship?.Anchor == AnchorState.Raising)
            status += $" | weighing anchor {Math.Ceiling(ship.AnchorRaiseTicksRemaining / (double)SimConstants.TickRate):0}s";
        Window.Title = $"ShipGame | {status} | speed {ship?.Speed:0.0} (sail {ship?.Throttle}/{ShipMovement.ThrottleLevels}) | {fps:0} fps";
        _titleTimer = 0;
        _framesSinceTitle = 0;
    }
}
