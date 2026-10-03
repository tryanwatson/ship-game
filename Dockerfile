# Dedicated ShipGame server.
#   docker build -t shipgame-server .
#   docker run -d --name shipgame --restart unless-stopped -p 7777:7777/udp shipgame-server
# Settings: SHIPGAME_PORT, SHIPGAME_FRIENDLY_FIRE (see src/ShipGame.Server/ServerOptions.cs). See docs/hosting.md.

# Build on the machine's own platform and cross-publish for the target (e.g. amd64 from an Apple Silicon Mac).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY src/ShipGame.Shared/ShipGame.Shared.csproj src/ShipGame.Shared/
COPY src/ShipGame.Net/ShipGame.Net.csproj src/ShipGame.Net/
COPY src/ShipGame.Server/ShipGame.Server.csproj src/ShipGame.Server/
RUN dotnet restore src/ShipGame.Server/ShipGame.Server.csproj -a $TARGETARCH

COPY src/ShipGame.Shared/ src/ShipGame.Shared/
COPY src/ShipGame.Net/ src/ShipGame.Net/
COPY src/ShipGame.Server/ src/ShipGame.Server/
RUN dotnet publish src/ShipGame.Server/ShipGame.Server.csproj -c Release -a $TARGETARCH --no-restore -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV SHIPGAME_PORT=7777
EXPOSE 7777/udp
ENTRYPOINT ["dotnet", "ShipGame.Server.dll"]
