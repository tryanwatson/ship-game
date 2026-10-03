#!/usr/bin/env bash
# Updates the dedicated server to a release tag and restarts it. Runs on the server.
#
#   deploy-server.sh v0.3.0
#
# The release workflow calls it over SSH with a deploy key that the server pins to this script (a forced command;
# see docs/hosting.md), so the tag arrives in SSH_ORIGINAL_COMMAND and that key can do nothing else.
#
# Install it outside the checkout, so a deploy never rewrites the script while it's running:
#   install -m 755 /opt/ship-game/scripts/deploy-server.sh /usr/local/bin/shipgame-deploy
set -euo pipefail

REPO=${SHIPGAME_REPO:-/opt/ship-game}
tag=${1:-${SSH_ORIGINAL_COMMAND:-}}

# Only release tags: this is all that ever reaches git, so nothing else can be smuggled in.
if [[ ! $tag =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "usage: deploy-server.sh vX.Y.Z (got '$tag')" >&2
    exit 2
fi

cd "$REPO"
git fetch --quiet --force origin tag "$tag"
git checkout --quiet --detach "$tag"
echo "Checked out $(git log --oneline -1)"

# Builds the new image, then replaces the container: the old one finishes its tick and disconnects everyone.
docker compose up -d --build --remove-orphans
docker image prune -f >/dev/null

sleep 3
docker compose logs --tail 5
