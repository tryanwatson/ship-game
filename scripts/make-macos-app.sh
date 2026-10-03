#!/usr/bin/env bash
# Wraps a self-contained macOS publish of the client in ShipGame.app and ad-hoc signs it.
#   scripts/make-macos-app.sh <publish-dir> <output-dir> [version]
# The app isn't notarized: players clear the download quarantine once with
#   xattr -dr com.apple.quarantine ShipGame.app
set -euo pipefail

publish_dir=$1
out_dir=$2
version=${3:-0.0.0}

app="$out_dir/ShipGame.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish_dir"/. "$app/Contents/MacOS/"

cat > "$app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key><string>ShipGame.Client</string>
  <key>CFBundleIdentifier</key><string>com.tryanwatson.shipgame</string>
  <key>CFBundleName</key><string>ShipGame</string>
  <key>CFBundleDisplayName</key><string>ShipGame</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF

# Apple Silicon refuses to run unsigned code; an ad-hoc signature is enough once quarantine is cleared.
codesign --force --deep --sign - "$app"
echo "Built $app"
