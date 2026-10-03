# Hosting a dedicated server

The server is a small console app (`src/ShipGame.Server`) that listens on one UDP port (7777 by default). It runs one
game at a time for up to 12 players: a lobby, then a run, then back to the lobby. It has no state on disk, so
restarting it only ends the current run.

Players join with `ShipGame.Client -- --connect <server-ip>[:port]`. The client and server must be built from the
same protocol version (`Protocol.Version` in `src/ShipGame.Net/Protocol.cs`). When it changes, update the server and
hand out new client builds together. An outdated client is refused with "VERSION MISMATCH".

## Settings

| Argument             | Environment variable        | Default |
|----------------------|-----------------------------|---------|
| `--port N`           | `SHIPGAME_PORT`             | `7777`  |
| `--no-friendly-fire` | `SHIPGAME_FRIENDLY_FIRE=false` | on   |
| `--password P`       | `SHIPGAME_PASSWORD`         | none    |

Arguments override environment variables. A bad value stops the server with exit code 2 and a usage message.

**Set a password** on any server with a public address. Without one, anyone who finds the IP can join. Players
enter it on the Join Game screen, or pass `--password P` with `--connect`. It travels unencrypted and only keeps
strangers out; it doesn't make the server secure. Each player is also limited to 60 commands a second (bursts of
120). Normal play stays far below that, and anything over it is dropped and logged.

## A VPS with Docker (recommended)

Any small Linux VPS will do: 1 vCPU and 1 GB of RAM is plenty, and Hetzner's x86 or ARM servers or a basic
DigitalOcean droplet both work. The image builds for whichever architecture it's built on. Bandwidth is the main
cost; expect up to about 45 KB/s outbound per connected player during a busy run.

1. **Install Docker** (Ubuntu/Debian):
   ```sh
   curl -fsSL https://get.docker.com | sh
   ```
2. **Open UDP 7777.** Do this in the provider's firewall (Hetzner Cloud Firewall, DigitalOcean Cloud Firewall):
   add an inbound rule for **UDP** 7777 from anywhere. Keep SSH (TCP 22) open too.
   > Docker publishes ports by editing iptables directly, which bypasses `ufw`. The provider's firewall is the one
   > that counts. Don't rely on `ufw` to block or allow the game port.
3. **Get the code and start it:**
   ```sh
   git clone https://github.com/tryanwatson/ship-game.git
   cd ship-game
   docker compose up -d --build
   ```
   `compose.yaml` restarts the server if it crashes or the machine reboots (`restart: unless-stopped`), and caps
   log size. Edit its `environment:` block to change settings.
4. **Check it:**
   ```sh
   docker compose logs -f     # "ShipGame server listening on UDP 7777 (protocol vN)..."
   ```
   Then from your own machine: `ShipGame.Client -- --connect <server-ip>`.

### Updating

```sh
cd ship-game
git pull
docker compose up -d --build
```

The old container gets SIGTERM, finishes its current tick, disconnects everyone cleanly, and exits. Any run in
progress ends. Rebuild when nobody's mid-run.

### Without Compose

```sh
docker build -t shipgame-server .
docker run -d --name shipgame --restart unless-stopped -p 7777:7777/udp \
  -e SHIPGAME_FRIENDLY_FIRE=true shipgame-server
```

To use another port, change both sides of `-p` and `SHIPGAME_PORT` (for example `-p 9000:9000/udp -e SHIPGAME_PORT=9000`),
or keep the container on 7777 and map only the outside: `-p 9000:7777/udp`.

## Without Docker (systemd)

```sh
# On any machine with the .NET 10 SDK; use linux-arm64 for an ARM server.
dotnet publish src/ShipGame.Server -c Release -r linux-x64 --self-contained -o publish/server
scp -r publish/server you@server:/opt/shipgame
```

`/etc/systemd/system/shipgame.service`:

```ini
[Unit]
Description=ShipGame server
After=network-online.target

[Service]
ExecStart=/opt/shipgame/ShipGame.Server
Environment=SHIPGAME_PORT=7777
DynamicUser=yes
Restart=always
RestartSec=2

[Install]
WantedBy=multi-user.target
```

```sh
sudo systemctl enable --now shipgame
journalctl -u shipgame -f
```

Open UDP 7777 in the provider's firewall and in `ufw allow 7777/udp` if ufw is on.

## Troubleshooting

- **"COULD NOT REACH SERVER" / "CONNECTION TIMED OUT"**: UDP 7777 isn't getting through. Check the provider
  firewall rule is UDP, not TCP, and that `docker compose ps` shows `0.0.0.0:7777->7777/udp`.
- **"VERSION MISMATCH"**: the client and server were built from different protocol versions.
- **"WRONG PASSWORD"**: the server has `SHIPGAME_PASSWORD` set and the player gave a different one, or none.
- **"RUN IN PROGRESS"**: players can't join mid-run. Wait for the run to end and the server to return to the lobby.
