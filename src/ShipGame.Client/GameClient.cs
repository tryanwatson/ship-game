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
using ShipGame.Shared.Trading;
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
    private readonly bool _hostFriendlyFire;
    private readonly NetworkConditions _conditions;
    private readonly string? _connectPassword;
    private HostedServer? _hostedServer;
    private ClientSettings _settings = new();

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
    // the glide-in speed limit sawtooth. At most once per simulation tick, though: the world only acts on the last
    // order each tick, and the server drops players who send faster than GameServer.MessagesPerSecond.
    private const float DragReissueDistance = 0.1f;
    private double _sinceMoveOrder;

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
    private MapView _mapView = null!;
    private MainMenu _menu = null!;
    private WeaponPicker _weaponPicker = null!;
    private GameMenu _gameMenu = null!;
    private RunForecast _runForecast = null!;
    private SeaBanner _seaBanner = null!;
    private bool _mapOpen;

    // Solo: choosing the starting weapon, before the run starts (online, the lobby does this).
    private bool _pickingSoloWeapon;

    // Starting gold, a playtesting option: - and = step through these on the weapon choice (solo) or in the lobby.
    private static readonly int[] StartingGoldSteps = { 0, 50, 100, 250, 500, 1000, 2500, 10000 };

    private bool _cameraLocked = true;
    private NVector2 _lastMoveOrder;

    // Quick-cast for aimed abilities: a tap (released within AimHoldSeconds) fires at the cursor on release with no
    // indicator; holding longer brings up the targeting indicator and releasing fires; any click while the key is
    // down cancels.
    private const double AimHoldSeconds = 0.15;
    private AbilitySlot? _aimKeyDown;
    private double _aimHeldSeconds;
    private NVector2 _aimCursor;

    // X goes to the simulation as key-down/key-up (it times the hold that lets the anchor go). Locally we time the
    // same hold just to draw its progress straight away, without waiting on the server.
    private bool _anchorKeySent;
    private bool _lettingGo;
    private double _anchorHeldSeconds;

    /// <summary>0..1 while X is being held to drop anchor; 0 otherwise.</summary>
    private float AnchorDropProgress =>
        _lettingGo ? (float)Math.Clamp(_anchorHeldSeconds / Anchoring.DropSeconds, 0, 1) : 0f;

    // A right-click that cancelled targeting doesn't turn into drag-to-move while the button stays down.
    private bool _ignoreRightDrag;

    /// <summary>The ability whose targeting indicator is showing (held past the tap threshold).</summary>
    private AbilitySlot? ShowingAim => _aimKeyDown is { } slot && _aimHeldSeconds >= AimHoldSeconds ? slot : null;
    private int _sentRudder;
    private double _titleTimer;
    private int _framesSinceTitle;

    /// <param name="connectHost">Server to join straight away; null to start at the menu.</param>
    /// <param name="host">Run a server in this process on <paramref name="connectPort"/> and join it.</param>
    /// <param name="friendlyFire">When hosting: whether players' shots hurt each other.</param>
    /// <param name="conditions">Simulated lag and loss for online games, for testing.</param>
    /// <param name="password">Password for <paramref name="connectHost"/>, if it has one.</param>
    public GameClient(string? connectHost = null, int connectPort = Protocol.DefaultPort, bool host = false, bool friendlyFire = true,
        NetworkConditions? conditions = null, string? password = null)
    {
        _connectPassword = password;
        _conditions = conditions ?? new NetworkConditions();
        _hostFriendlyFire = friendlyFire;
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
        Window.TextInput += (_, e) => _menu?.OnTextInput(e.Character, e.Key);
    }

    private int LocalPlayerId => _session.LocalPlayerId;

    /// <summary>HUD layout space for the current window size (see <see cref="HudView"/>).</summary>
    private HudView Hud => new(GraphicsDevice.Viewport);

    private NetworkGameSession? Online => _session as NetworkGameSession;

    protected override void Initialize()
    {
        base.Initialize(); // loads content, including the menu

        _settings = ClientSettings.Load();
        _menu.Address = _settings.LastAddress;
        _menu.Password = _settings.LastPassword;
        _menu.FriendlyFire = _settings.HostFriendlyFire;

        if (_hosting)
            StartHosting(_hostFriendlyFire);
        else if (_connectHost is not null)
            Join(new ServerAddress(_connectHost, _connectPort), _connectPassword);
        else
            OpenMenu();
    }

    /// <summary>Leaves whatever game is going (stopping a hosted server) and shows the menu over open water.</summary>
    private void OpenMenu(string? message = null, bool onJoinPage = false)
    {
        LeaveSession();
        _pickingSoloWeapon = false;
        _session = new LocalGameSession(EmptySea(), SoloPlayerId);
        _camera.Position = IsoProjection.WorldToIso(Archipelago.Start);
        _menu.Open(message, onJoinPage);
    }

    private void LeaveSession()
    {
        Online?.Dispose();
        _hostedServer?.Dispose();
        _hostedServer = null;
        ResetControls();
    }

    /// <summary>Runs a server in this process on <see cref="_connectPort"/> and joins it.</summary>
    private void StartHosting(bool friendlyFire)
    {
        LeaveSession();
        try
        {
            _hostedServer = new HostedServer(_connectPort, friendlyFire);
        }
        catch (InvalidOperationException)
        {
            OpenMenu($"PORT {_connectPort} IS IN USE");
            return;
        }
        Join(new ServerAddress("127.0.0.1", _hostedServer.Port));
    }

    private void Join(ServerAddress address, string? password = null)
    {
        _menu.Close();
        _session = new NetworkGameSession(address.Host, address.Port, _conditions, password);
        ResetControls();
    }

    private static World EmptySea() => Runs.CreateMap();

    /// <summary>A fresh run: the player's ship at the southern edge carrying <paramref name="weapon"/>, the seas ahead.</summary>
    private void StartRun(WeaponOffer weapon)
    {
        var world = Runs.Create(Environment.TickCount, new[] { (SoloPlayerId, weapon.Ability) }, startingGold: _settings.SoloStartingGold);

        _session = new LocalGameSession(world, SoloPlayerId);
        ResetControls();
    }

    /// <summary>Forget held keys and toggles from the last game.</summary>
    private void ResetControls()
    {
        _sentRudder = 0;
        _cameraLocked = true;
        _mapOpen = false;
        _aimKeyDown = null;
        _anchorKeySent = false;
        _lettingGo = false;
        _anchorHeldSeconds = 0;
        _ignoreRightDrag = false;
        _gameMenu?.Close();
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
        _mapView = new MapView(_primitives);
        _menu = new MainMenu(_primitives);
        _weaponPicker = new WeaponPicker(_primitives);
        _gameMenu = new GameMenu(_primitives);
        _runForecast = new RunForecast(_primitives);
        _seaBanner = new SeaBanner(_primitives);
    }

    protected override void UnloadContent()
    {
        LeaveSession();
        _primitives.Dispose();
    }

    protected override void Update(GameTime gameTime)
    {
        var dt = gameTime.ElapsedGameTime.TotalSeconds;
        _input.Update();

        if (_menu.IsOpen)
        {
            UpdateMenu(dt);
            UpdateTitle(dt);
            base.Update(gameTime);
            return;
        }

        // Refused or dropped: back to the menu with the reason, on the join page so trying again is one key.
        if (Online is { Connection.Status: ConnectionStatus.Disconnected } dropped)
        {
            var wasHosting = _hostedServer is not null;
            OpenMenu(dropped.Connection.DisconnectReason, onJoinPage: !wasHosting);
            base.Update(gameTime);
            return;
        }

        // Esc: the game menu (resume or leave). Still connecting, or choosing a weapon before a solo run, there's
        // nothing to leave yet, so it goes straight back to the title menu.
        var menuJustOpened = false;
        if (IsActive && _input.WasKeyPressed(Keys.Escape) && !_gameMenu.IsOpen)
        {
            if (_pickingSoloWeapon || Online is { Connection.Status: ConnectionStatus.Connecting })
            {
                OpenMenu();
                base.Update(gameTime);
                return;
            }
            _gameMenu.Open();
            menuJustOpened = true;
            _aimKeyDown = null;
            ReleaseAnchorKey();
        }

        // With the game menu up, nothing reaches the game. Solo it's paused; online it carries on underneath. A
        // choice takes effect from the next frame, so the Enter or click that resumes doesn't also act on the game.
        if (_gameMenu.IsOpen)
        {
            if (IsActive && !menuJustOpened)
            {
                switch (_gameMenu.Update(_input, Hud, GameMenuNote))
                {
                    case GameMenuAction.Resume:
                        _gameMenu.Close();
                        break;
                    case GameMenuAction.Leave:
                        OpenMenu();
                        base.Update(gameTime);
                        return;
                }
            }
            UpdateRudder();
            if (Online is not null)
                StepSession(dt);
            UpdateTitle(dt);
            base.Update(gameTime);
            return;
        }

        if (_pickingSoloWeapon)
        {
            _session.Update(dt); // the sea (or the last run's wreckage) behind the choice
            if (IsActive && StartingGoldStep() is { } step)
            {
                _settings.SoloStartingGold = StepStartingGold(_settings.SoloStartingGold, step);
                _settings.Save();
            }
            if (IsActive && _weaponPicker.Update(_input, Hud) is { } weapon)
            {
                _pickingSoloWeapon = false;
                StartRun(weapon);
            }
            UpdateTitle(dt);
            base.Update(gameTime);
            return;
        }

        // In the lobby: choose a starting weapon (clicks or 1-3), then Enter to ready up.
        if (IsActive && Online is { Connection.Status: ConnectionStatus.Lobby } inLobby && _weaponPicker.Update(_input, Hud) is { } choice)
            inLobby.Connection.ChooseStartingWeapon(choice.Id);
        if (IsActive && Online is { Connection.Status: ConnectionStatus.Lobby } goldLobby && StartingGoldStep() is { } goldStep)
            goldLobby.Connection.SetStartingGold(StepStartingGold(goldLobby.Connection.Lobby?.StartingGold ?? 0, goldStep));

        // Enter: online, ready up in the lobby (once a weapon is chosen); offline, choose a weapon for a new run once
        // this one is over.
        if (IsActive && _input.WasKeyPressed(Keys.Enter))
        {
            if (Online is { Connection.Status: ConnectionStatus.Lobby } online && LocalStartingWeapon(online) is not null)
                online.Connection.SetReady(!IsLocallyReady(online));
            else if (Online is null && _session.World.IsRunOver)
                _pickingSoloWeapon = true;
        }

        if (!IsActive)
        {
            _aimKeyDown = null; // keys released while unfocused are never seen: don't leave an indicator stuck on
            ReleaseAnchorKey();  // nor the anchor key, or the server would let go on its own
        }

        if (IsActive)
        {
            // While an aimed key is down, clicks belong to targeting (they cancel it), not to the shipyard panel.
            if (_aimKeyDown is null)
                _shipyardPanel.Update(_session.World, _session.World.GetPlayerShip(LocalPlayerId), _input, Hud, _session.Send);
            HandleOrders(dt);
        }
        UpdateRudder();
        StepSession(dt);

        if (IsActive)
            HandleCamera((float)dt);

        UpdateTitle(dt);
        base.Update(gameTime);
    }

    /// <summary>Advances the game (and its effects) by a frame.</summary>
    private void StepSession(double dt)
    {
        _worldRenderer.CaptureEffects(_session.World, _session.InterpolationAlpha);
        _worldRenderer.UpdateEffects((float)dt);
        _session.Update(dt);
        _worldRenderer.ProcessEffects(_session.World, _session.TakeEvents());
    }

    /// <summary>The game menu's title: solo pauses, online doesn't.</summary>
    private string GameMenuTitle => Online is null ? "PAUSED" : "MENU";

    /// <summary>Under the game menu's title: a reminder, mid-run online, that the game doesn't stop.</summary>
    private string? GameMenuNote => Online is { Connection.Status: ConnectionStatus.InRun } ? "THE GAME CARRIES ON" : null;

    private void UpdateMenu(double dt)
    {
        _session.Update(dt); // the sea behind the menu
        if (!IsActive)
            return;

        switch (_menu.Update(_input, Hud, dt))
        {
            case MenuAction.PlaySolo:
                _menu.Close();
                _pickingSoloWeapon = true;
                break;
            case MenuAction.Host:
                SaveSettings();
                StartHosting(_menu.FriendlyFire);
                break;
            case MenuAction.Join when _menu.ParsedAddress is { } address:
                SaveSettings();
                Join(address, _menu.Password);
                break;
            case MenuAction.Quit:
                SaveSettings();
                Exit();
                break;
        }
    }

    private void SaveSettings()
    {
        _settings.LastAddress = _menu.Address.Trim();
        _settings.LastPassword = _menu.Password;
        _settings.HostFriendlyFire = _menu.FriendlyFire;
        _settings.Save();
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(10, 22, 40));
        var view = _camera.GetView(GraphicsDevice.Viewport);
        if (_menu.IsOpen)
        {
            _worldRenderer.Draw(_session.World, _session.InterpolationAlpha, LocalPlayerId, view);
            _menu.Draw(Hud);
            base.Draw(gameTime);
            return;
        }

        if (_pickingSoloWeapon)
        {
            _worldRenderer.Draw(_session.World, _session.InterpolationAlpha, LocalPlayerId, view);
            _statusBanner.Draw("CHOOSE YOUR WEAPON",
                $"THE OTHERS CAN BE BOUGHT AT SHIPYARDS  -  {StartingGoldLabel(_settings.SoloStartingGold)}  -  ESC FOR THE MENU", Hud);
            _weaponPicker.Draw(_input, Hud, null);
            base.Draw(gameTime);
            return;
        }

        var aim = ShowingAim is { } slot ? new AimPreview(slot, _aimCursor) : (AimPreview?)null;
        _worldRenderer.Draw(_session.World, _session.InterpolationAlpha, LocalPlayerId, view, aim);
        _islandOverlays.Draw(_session.World, _session.World.GetPlayerShip(LocalPlayerId), _session.InterpolationAlpha, view, Hud, AnchorDropProgress);
        _offscreenMarkers.Draw(_session.World, _session.World.GetPlayerShip(LocalPlayerId), _session.InterpolationAlpha, view, Hud);
        var localShip = _session.World.GetPlayerShip(LocalPlayerId);
        var plunderReady = localShip is not null && Plundering.PlunderableFrom(_session.World, localShip.Position) is not null;
        var shipyardReady = localShip is not null && Shipyards.ShipyardFrom(_session.World, localShip.Position) is not null;
        _abilityBar.Draw(localShip, plunderReady, shipyardReady, Hud, AnchorDropProgress);
        _compass.Draw(_session.World.Wind, Hud);
        var gold = _session.World.Players.TryGetValue(LocalPlayerId, out var player) ? player.Gold : 0;
        var inRun = !_session.World.IsRunOver && Online is null or { Connection.Status: ConnectionStatus.InRun };
        var here = localShip?.Position ?? _session.World.GetPlayerShip(LocalPlayerId)?.Position ?? IsoProjection.IsoToWorld(_camera.Position);
        _hudCounters.Draw(gold, Archipelago.LevelAt(here), Hud);
        // Where we are and what's coming, while there's a run to come to (not in the lobby or after it ends).
        if (_session.World.Director is { } director && inRun)
        {
            _runForecast.Draw(director.Status, Archipelago.SeaAt(here), here, Hud);
            _seaBanner.Draw(localShip is null ? null : Archipelago.SeaAt(localShip.Position), Hud);
        }
        // Choosing a contract charts each route beside the panel; otherwise M shows the full map.
        if (_shipyardPanel.CurrentRoutes(_session.World, localShip, _input, Hud) is { } routes)
            _mapView.Draw(_session.World, LocalPlayerId, Hud, ShipyardPanel.RouteMapArea(Hud), routes);
        else if (_mapOpen)
            _mapView.Draw(_session.World, LocalPlayerId, Hud);
        _shipyardPanel.Draw(_session.World, localShip, _input, Hud);
        DrawStatusBanner();
        if (_gameMenu.IsOpen)
            _gameMenu.Draw(Hud, GameMenuTitle, GameMenuNote);
        base.Draw(gameTime);
    }

    private void HandleOrders(double dt)
    {
        _sinceMoveOrder += dt;
        UpdateAnchorKey(dt);

        if (_input.WasKeyPressed(Keys.M))
            _mapOpen = !_mapOpen; // the game carries on underneath

        if (_input.WasKeyPressed(Keys.W) || _input.WasKeyPressed(Keys.Space))
            _session.Send(new AdjustThrottleCommand(LocalPlayerId, +1));
        if (_input.WasKeyPressed(Keys.S) || _input.WasKeyPressed(Keys.LeftControl) || _input.WasKeyPressed(Keys.RightControl))
            _session.Send(new AdjustThrottleCommand(LocalPlayerId, -1));

        var mouseWorld = IsoProjection.IsoToWorld(_camera.ScreenToIso(_input.MousePosition, GraphicsDevice.Viewport));

        // Abilities. Broadsides fire on key-down. Aimed ones are quick-cast: tap fires at the cursor (on release,
        // no indicator), hold shows the targeting indicator and releasing fires, any click while down cancels.
        var ship = _session.World.GetPlayerShip(LocalPlayerId);
        _aimCursor = mouseWorld;
        if (_aimKeyDown is not null)
            _aimHeldSeconds += dt;

        foreach (var (key, slot) in AbilityKeys)
        {
            var aimed = ship?.GetAbility(slot)?.Definition.IsAimed == true;
            if (!aimed)
            {
                if (_input.WasKeyPressed(key))
                    _session.Send(new CastAbilityCommand(LocalPlayerId, slot, mouseWorld));
            }
            else if (_input.WasKeyPressed(key))
            {
                _aimKeyDown = slot; // pressing another aimed key switches to it
                _aimHeldSeconds = 0;
            }
            else if (_aimKeyDown == slot && _input.WasKeyReleased(key))
            {
                _session.Send(new CastAbilityCommand(LocalPlayerId, slot, mouseWorld));
                _aimKeyDown = null;
            }
        }

        if (_aimKeyDown is not null && (_input.WasLeftMousePressed || _input.WasRightMousePressed))
        {
            _aimKeyDown = null; // cancelled: this click doesn't also move the ship, and releasing the key won't fire
            _ignoreRightDrag = _input.WasRightMousePressed;
            return;
        }

        if (!_input.IsRightMouseDown)
            _ignoreRightDrag = false;
        if (_ignoreRightDrag)
            return;

        if (!GraphicsDevice.Viewport.Bounds.Contains(_input.Mouse.Position))
            return;

        var dragged = _input.IsRightMouseDown && _sinceMoveOrder >= SimConstants.TickDelta
                      && NVector2.Distance(mouseWorld, _lastMoveOrder) > DragReissueDistance;
        if (_input.WasRightMousePressed || dragged)
        {
            _session.Send(new MoveCommand(LocalPlayerId, mouseWorld));
            _lastMoveOrder = mouseWorld;
            _sinceMoveOrder = 0;
        }
    }

    /// <summary>
    /// A/D hold the helm to port/starboard (relative to the ship, not the screen). Sent only on change; released
    /// when the window loses focus so a key held while alt-tabbing doesn't leave the helm hard over.
    /// </summary>
    private void UpdateRudder()
    {
        var rudder = 0;
        if (IsActive && !_gameMenu.IsOpen)
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
        // Pan at a steady speed across the screen, whatever the zoom or window size.
        _camera.Position += pan * (CameraPanSpeed / _camera.EffectiveScale(GraphicsDevice.Viewport) * dt);
    }

    /// <summary>
    /// X: with the anchor up, hold it for <see cref="Anchoring.DropSeconds"/> to let go; with it down, a press starts
    /// hauling it in. Mid-haul it does nothing. The simulation does the timing; this reports the key.
    /// </summary>
    private void UpdateAnchorKey(double dt)
    {
        var anchor = _session.World.GetPlayerShip(LocalPlayerId)?.Anchor;
        if (_input.WasKeyPressed(Keys.X) && anchor is AnchorState.Weighed or AnchorState.Down)
        {
            _session.Send(new AnchorKeyCommand(LocalPlayerId, true));
            _anchorKeySent = true;
            _lettingGo = anchor == AnchorState.Weighed;
            _anchorHeldSeconds = 0;
        }
        else if (!_input.IsKeyDown(Keys.X))
        {
            ReleaseAnchorKey();
        }

        if (_lettingGo && anchor == AnchorState.Weighed)
            _anchorHeldSeconds += dt;
        else
            _lettingGo = false; // it's down (or the ship's gone)
    }

    private void ReleaseAnchorKey()
    {
        if (_anchorKeySent)
            _session.Send(new AnchorKeyCommand(LocalPlayerId, false));
        _anchorKeySent = false;
        _lettingGo = false;
        _anchorHeldSeconds = 0;
    }

    private bool IsLocallyReady(NetworkGameSession online) =>
        online.Connection.Lobby?.Players.Any(p => p.PlayerId == online.LocalPlayerId && p.Ready) == true;

    /// <summary>The starting weapon the server has us down for, if we've chosen one.</summary>
    /// <summary>-1 or +1 if a starting gold key went down this frame (- and =, or the keypad's - and +).</summary>
    private int? StartingGoldStep() =>
        _input.WasKeyPressed(Keys.OemMinus) || _input.WasKeyPressed(Keys.Subtract) ? -1
        : _input.WasKeyPressed(Keys.OemPlus) || _input.WasKeyPressed(Keys.Add) ? 1
        : null;

    /// <summary>The next of <see cref="StartingGoldSteps"/> above (+1) or below (-1) <paramref name="gold"/>, or the end it's at.</summary>
    private static int StepStartingGold(int gold, int step) => step > 0
        ? StartingGoldSteps.FirstOrDefault(g => g > gold, StartingGoldSteps[^1])
        : StartingGoldSteps.LastOrDefault(g => g < gold, StartingGoldSteps[0]);

    private static string StartingGoldLabel(int gold) => $"START GOLD {gold} [- +]";

    private static string? LocalStartingWeapon(NetworkGameSession online) =>
        online.Connection.Lobby?.Players.FirstOrDefault(p => p.PlayerId == online.LocalPlayerId)?.StartingWeaponId;

    private void DrawStatusBanner()
    {
        var world = _session.World;
        if (Online is { } online && DrawConnectionBanner(online))
            return;

        if (world.IsRunOver)
            _statusBanner.Draw(RunOverTitle(world), $"{RunOverSummary(world)}  -  PRESS ENTER FOR A NEW RUN", Hud);
        else if (world.Players.TryGetValue(LocalPlayerId, out var player) && player.IsAwaitingRespawn)
        {
            var seconds = (int)Math.Ceiling(player.RespawnTicksRemaining / (double)SimConstants.TickRate);
            _statusBanner.Draw("SUNK", $"RESPAWNING IN {seconds}", Hud);
        }
    }

    private static string RunOverTitle(World world) => world.IsVictory ? "VICTORY" : "RUN OVER";

    /// <summary>How the run ended: the flagship sunk, or how far north the crew got.</summary>
    private string RunOverSummary(World world)
    {
        return world.IsVictory ? "THE PIRATE FLAGSHIP IS SUNK" : $"REACHED {Archipelago.SeaAt(FurthestNorth(world)).Name}";
    }

    /// <summary>Roughly where the crew's furthest-north ship got to: a sight's depth behind the northernmost charted water.</summary>
    private static NVector2 FurthestNorth(World world)
    {
        var discovery = world.Discovery;
        for (var index = 0; index < discovery.CellCount; index++)
        {
            // Cells run row by row from the north, so the first charted one is the furthest north.
            if (discovery.IsDiscovered(Team.Players, index))
                return discovery.CellCenter(index) + new NVector2(0f, Discovery.SightHalfUpDown);
        }
        return Archipelago.Start;
    }

    /// <summary>Online-only banners: connecting, refused or dropped, and the lobby. True if one was drawn.</summary>
    private bool DrawConnectionBanner(NetworkGameSession online)
    {
        var connection = online.Connection;
        switch (connection.Status)
        {
            case ConnectionStatus.Connecting:
                _statusBanner.Draw("CONNECTING", $"{new ServerAddress(online.Host, online.Port)}  -  ESC TO CANCEL", Hud);
                return true;
            case ConnectionStatus.Lobby:
            {
                var players = connection.Lobby?.Players ?? Array.Empty<LobbyPlayer>();
                var ready = players.Count(p => p.Ready);
                var weapon = LocalStartingWeapon(online);
                var prompt = weapon is null ? "CHOOSE YOUR STARTING WEAPON"
                    : IsLocallyReady(online) ? "READY - WAITING FOR THE CREW"
                    : "PRESS ENTER WHEN READY";
                var title = _session.World.IsRunOver ? RunOverTitle(_session.World) : "LOBBY";
                var mode = connection.Lobby?.FriendlyFire == true ? "FRIENDLY FIRE ON" : "CO-OP";
                var gold = StartingGoldLabel(connection.Lobby?.StartingGold ?? 0);
                _statusBanner.Draw(title, $"{players.Count} SAILORS  {ready} READY  -  {mode}  -  {gold}  -  {prompt}", Hud);
                _weaponPicker.Draw(_input, Hud, weapon);
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
        if (_menu.IsOpen)
        {
            Window.Title = "ShipGame";
            _titleTimer = 0;
            _framesSinceTitle = 0;
            return;
        }

        var world = _session.World;
        var ship = world.GetPlayerShip(LocalPlayerId);
        var fps = _framesSinceTitle / _titleTimer;
        var gold = world.Players.TryGetValue(LocalPlayerId, out var player) ? player.Gold : 0;

        // The window title doubles as a status line.
        string status;
        if (world.IsRunOver)
            status = $"{(world.IsVictory ? "VICTORY" : "RUN OVER")} with {gold} gold - press Enter for a new run";
        else if (ship is null)
            status = "sunk - respawning";
        else
        {
            var sea = Archipelago.SeaAt(ship.Position);
            status = $"{sea.Name.ToLowerInvariant()} (level {sea.Level})";
        }

        if (ship?.Anchor == AnchorState.Down)
            status += ship.PlunderIslandId is null ? " | at anchor" : " | at anchor, plundering";
        else if (ship?.Anchor == AnchorState.Raising)
            status += $" | weighing anchor {Math.Ceiling(ship.AnchorRaiseTicksRemaining / (double)SimConstants.TickRate):0}s";
        if (Online is { } online)
        {
            var role = _hostedServer is not null ? $"hosting on {_hostedServer.Port}" : "online";
            var simulated = _conditions.IsPerfect ? "" : $", simulating {_conditions}";
            status = $"{role} as player {online.LocalPlayerId} ({online.Connection.RoundTripMs} ms{simulated}) | {status}";
        }
        Window.Title = $"ShipGame | {status} | speed {ship?.Speed:0.0} (sail {ship?.Throttle}/{ShipMovement.ThrottleLevels}) | {fps:0} fps";
        _titleTimer = 0;
        _framesSinceTitle = 0;
    }
}
