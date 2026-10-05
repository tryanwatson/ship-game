# ShipGame

An isometric pirate-ship roguelike for up to 12 players. Start in the middle of a big square sea and fight your way
out: the further from the middle, the higher the pirates' levels and the richer the loot. Fortress islands are held by
guns on their shores and ships at sea; take one (sink every gun) and every player chooses from a hand of cards, big
permanent boosts to their ship or weapons: silver (changes how you play), gold (big boosts) and prismatic (breaks a
rule). The harder the fortress, the better the tiers and the bigger the numbers; a level-8 fortress deals four
prismatics. Sinking a pirate flagship deals a hand of prismatics too. Every two fortresses taken, a pirate flagship comes hunting the crew; sink
the third to win. Pirates also roam each ring of sea, alone or in packs that fight together. Plunder islands, upgrade
your ship at shipyards and taken fortresses (outer ones stock more), and run cargo contracts between islands (sunk cargo floats free for
anyone to salvage). Friendly fire is on unless the host turns it off.

## Playing

Download the launcher for your system once. Each time it starts, it installs the latest release if there is a new
one, then starts the game (if it can't reach GitHub, it starts the version you already have).

- **Windows**: [ShipGame-Launcher-win-x64.exe](https://github.com/tryanwatson/ship-game/releases/latest/download/ShipGame-Launcher-win-x64.exe).
  Put it anywhere and run it. If SmartScreen says "Windows protected your PC", click **More info → Run anyway** (the
  build isn't code-signed).
- **macOS (Apple Silicon)**: [ShipGame-Launcher-osx-arm64.zip](https://github.com/tryanwatson/ship-game/releases/latest/download/ShipGame-Launcher-osx-arm64.zip).
  Unzip it by double-clicking it in Finder (other unzip tools drop the app's signature and it won't open), move
  `ShipGame.app` to Applications, then run this once in Terminal. The app isn't notarized, so without it macOS says
  the app "is damaged":
  ```sh
  xattr -dr com.apple.quarantine /Applications/ShipGame.app
  ```
- **Linux**: [ShipGame-Launcher-linux-x64.zip](https://github.com/tryanwatson/ship-game/releases/latest/download/ShipGame-Launcher-linux-x64.zip).
  Unzip it and run `./ShipGame.Launcher`.

Games are installed in `%LocalAppData%\ShipGame`, `~/Library/Application Support/ShipGame` or
`~/.local/share/ShipGame`. Any arguments to the launcher are passed on to the game. Each release on the
[Releases](https://github.com/tryanwatson/ship-game/releases) page also has the game itself as a plain zip
(`ShipGame-<version>-<system>.zip`) if you'd rather not use the launcher.

From the menu: **Play Solo**, **Host Game** (others join you on UDP port 7777, which your router has to forward
for internet play), or **Join Game** and type the server's address (`host` or `host:port`) and its password, if it has one. In the lobby, type your
name (it's shown over your ship and on the map) and press **Enter** to ready up; the run starts when the whole crew is
ready. Every run opens with each player choosing a free starting card (any card, rerollable with starting gold) and
then the weapon to set sail with, so a good weapon card can decide the weapon. Players can't join a run already in
progress.

### Controls

| | |
|---|---|
| Right-click (hold to steer) | Sail to a point |
| W / S | More / less sail; S again with the sails furled rows slowly astern |
| A / D | Helm to port / starboard |
| 1-4 | Your weapons, in the order you got them. Broadside fires the side the cursor is on, aimed toward it within 15° either way of the beam; all three: tap to fire at the cursor, hold to aim |
| X | Hold to drop anchor, press to raise it |
| Click (card screen) | Choose a card after a fortress falls. The game pauses until every player has chosen |
| R / click (card screen) | Reroll the three cards for gold: 50, doubling with every reroll you make that run |
| M | Map |
| Mouse wheel | Zoom |
| Y / C / arrow keys | Toggle camera lock / center on ship / pan |
| Click (start of a run) | Choose your starting card, then your weapon: broadside, long gun, or mortar. Buy the others, and each weapon's skills, at ports |
| Type (lobby) | Your name, shown over your ship and on the map |
| - / = | Starting gold: in the lobby, or on the main menu for solo (a playtesting option) |
| Enter | Ready up (lobby), new run (solo, after sinking) |
| Esc | Game menu: resume or leave the game (solo pauses; online carries on) |

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
SHIPGAME_INSTALL_DIR=/tmp/shipgame dotnet run --project src/ShipGame.Launcher # try the launcher without touching your install
```

| Project | |
|---|---|
| `src/ShipGame.Shared` | Game rules: `World`, stepped 30 times a second. No MonoGame. |
| `src/ShipGame.Net` | Wire protocol, `ClientConnection`, `ClientReplica` (the client's interpolated copy of the server world). |
| `src/ShipGame.Server` | Dedicated server. Also runs inside the client for Host Game. |
| `src/ShipGame.Client` | MonoGame DesktopGL client. |
| `src/ShipGame.Launcher` | Installs the latest GitHub release of the client and starts it. |

Bump `Protocol.Version` in `src/ShipGame.Net/Protocol.cs` on any wire change. Clients and servers on different
versions refuse each other.

- **Dedicated server**: see [docs/hosting.md](docs/hosting.md) (Docker on a VPS).
- **Releases**: push a tag such as `v0.1.0`. The Release workflow builds Windows, macOS and Linux zips of the game,
  and the launcher for each, and attaches them to a GitHub release. Players' launchers pick the new release up the next
  time they start. To build a client by hand:
  `dotnet publish src/ShipGame.Client -c Release -r osx-arm64 --self-contained -o publish/ShipGame`, then
  `scripts/make-macos-app.sh publish/ShipGame publish` for a macOS app.
