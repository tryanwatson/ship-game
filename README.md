# ShipGame

An isometric pirate-ship roguelike for up to 12 players. Sail, sink pirate waves, plunder islands, upgrade your
ship at shipyards, and run cargo contracts between islands (sunk cargo floats free for anyone to salvage). Friendly
fire is on unless the host turns it off.

## Playing

Download the zip for your system from [Releases](https://github.com/tryanwatson/ship-game/releases) and unzip it.

- **Windows**: run `ShipGame\ShipGame.Client.exe`. If SmartScreen says "Windows protected your PC", click
  **More info → Run anyway** (the build isn't code-signed).
- **macOS (Apple Silicon)**: unzip by double-clicking the zip in Finder (other unzip tools drop the app's
  signature and it won't open), move `ShipGame.app` to Applications, then run this once in Terminal. The app isn't
  notarized, so without it macOS says the app "is damaged":
  ```sh
  xattr -dr com.apple.quarantine /Applications/ShipGame.app
  ```
- **Linux**: `./ShipGame/ShipGame.Client`.

From the menu: **Play Solo**, **Host Game** (others join you on UDP port 7777, which your router has to forward
for internet play), or **Join Game** and type the server's address (`host` or `host:port`) and its password, if it has one. Everyone in the lobby
presses **Enter** to ready up, and the run starts when the whole crew is ready. Players can't join a run already in
progress.

### Controls

| | |
|---|---|
| Right-click (hold to steer) | Sail to a point |
| W / S | More / less sail |
| A / D | Helm to port / starboard |
| 1-4 | Your weapons, in the order you got them. Broadside fires the side the cursor is on; long gun and mortar: tap to fire at the cursor, hold to aim |
| X | Hold to drop anchor, press to raise it |
| M | Map |
| Mouse wheel | Zoom |
| Y / C / arrow keys | Toggle camera lock / center on ship / pan |
| 1-3 / click (lobby, new run) | Choose your starting weapon: broadside, long gun, or mortar. Buy the others, and each weapon's skills, at shipyards |
| Enter | Ready up (lobby), new run (solo, after sinking) |
| Esc | Back to the menu |

## Developing

Needs the .NET 10 SDK.

```sh
dotnet tool restore
dotnet test ShipGame.slnx
dotnet run --project src/ShipGame.Client                          # menu
dotnet run --project src/ShipGame.Client -- --host [port]         # host straight away
dotnet run --project src/ShipGame.Client -- --connect host[:port] [--password P] # join straight away
dotnet run --project src/ShipGame.Client -- --connect host --lag 150 --jitter 30 --loss 5  # test on a bad network
dotnet run --project src/ShipGame.Server -- [--port 7777] [--no-friendly-fire] [--password P]
```

| Project | |
|---|---|
| `src/ShipGame.Shared` | Game rules: `World`, stepped 30 times a second. No MonoGame. |
| `src/ShipGame.Net` | Wire protocol, `ClientConnection`, `ClientReplica` (the client's interpolated copy of the server world). |
| `src/ShipGame.Server` | Dedicated server. Also runs inside the client for Host Game. |
| `src/ShipGame.Client` | MonoGame DesktopGL client. |

Bump `Protocol.Version` in `src/ShipGame.Net/Protocol.cs` on any wire change. Clients and servers on different
versions refuse each other.

- **Dedicated server**: see [docs/hosting.md](docs/hosting.md) (Docker on a VPS).
- **Releases**: push a tag such as `v0.1.0`. The Release workflow builds Windows, macOS and Linux zips and attaches
  them to a GitHub release. To build a client by hand:
  `dotnet publish src/ShipGame.Client -c Release -r osx-arm64 --self-contained -o publish/ShipGame`, then
  `scripts/make-macos-app.sh publish/ShipGame publish` for a macOS app.
