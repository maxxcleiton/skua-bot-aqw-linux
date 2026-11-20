#!/usr/bin/env bash
set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.local/bin:$HOME/.dotnet:$DIR/.tools/jdk-17.0.21+9-jre/bin:$PATH"
export JAVA_HOME="$DIR/.tools/jdk-17.0.21+9-jre"

echo "[1/2] Compiling Skua.AS3 (skua.swf)..."
cd "$DIR/Skua.AS3"
mkdir -p skua/bin
mxmlc -source-path "skua/src" -default-size 958 550 -default-frame-rate 24 -output "skua/bin/skua.swf" "skua/src/skua/Main.as" -target-player 28.0 -optimize

echo "[2/2] Building Skua.Linux (.NET 10)..."
cd "$DIR"
dotnet build Skua.Linux/Skua.Linux.csproj -p:WarningLevel=0

OUT_DIR="$DIR/Skua.Linux/bin/Debug/net10.0"
cp -f "$DIR/Skua.AS3/skua/bin/skua.swf" "$OUT_DIR/skua.swf"
ln -sfn "$DIR/FFDec" "$OUT_DIR/FFDec"
if [ -f "$DIR/.tools/ruffle/ruffle_desktop" ]; then
    cp -f "$DIR/.tools/ruffle/ruffle_desktop" "$OUT_DIR/ruffle_desktop"
    chmod +x "$OUT_DIR/ruffle_desktop"
fi
mkdir -p "$OUT_DIR/Assets"
cp -f "$DIR/Assets/SkuaIcon.png" "$OUT_DIR/Assets/SkuaIcon.png"
cp -f "$DIR/Assets/SkuaIcon.png" "$OUT_DIR/SkuaIcon.png"

# Setup Desktop shortcut and icon integration for Linux (GNOME, KDE, etc.)
ICON_DIR="$HOME/.local/share/icons/hicolor/256x256/apps"
APP_DIR="$HOME/.local/share/applications"
mkdir -p "$ICON_DIR" "$APP_DIR" "$HOME/.local/share/icons"
cp -f "$DIR/Assets/SkuaIcon.png" "$HOME/.local/share/icons/skua.png"
cp -f "$DIR/Assets/SkuaIcon.png" "$ICON_DIR/skua.png"

cat << EOF > "$APP_DIR/skua.desktop"
[Desktop Entry]
Version=1.0
Type=Application
Name=Skua Linux
GenericName=AQW Bot & Client
Comment=AdventureQuest Worlds Bot & Client for Linux (Native .NET 10)
Exec=$DIR/run-linux.sh %u
Path=$DIR
Icon=skua
Terminal=false
Categories=Game;Utility;
Keywords=aqw;skua;bot;adventurequest;
StartupWMClass=Skua
StartupNotify=true
EOF
chmod +x "$APP_DIR/skua.desktop"

DESKTOP_DIRS=()
if command -v xdg-user-dir >/dev/null 2>&1; then
    XDG_DESK="$(xdg-user-dir DESKTOP 2>/dev/null || true)"
    if [ -n "$XDG_DESK" ] && [ -d "$XDG_DESK" ]; then
        DESKTOP_DIRS+=("$XDG_DESK")
    fi
fi
for fallback in "$HOME/Desktop" "$HOME/Área de trabalho"; do
    if [ -d "$fallback" ]; then
        DESKTOP_DIRS+=("$fallback")
    fi
done

declare -A SEEN_DESK=()
for desk in "${DESKTOP_DIRS[@]}"; do
    if [ -z "${SEEN_DESK["$desk"]:-}" ]; then
        SEEN_DESK["$desk"]=1
        cp -f "$APP_DIR/skua.desktop" "$desk/skua.desktop"
        chmod +x "$desk/skua.desktop"
        gio set "$desk/skua.desktop" metadata::trusted true 2>/dev/null || true
    fi
done

update-desktop-database "$APP_DIR" 2>/dev/null || true
gtk-update-icon-cache -f "$HOME/.local/share/icons/hicolor" 2>/dev/null || true

echo "Build complete! Binary at: $OUT_DIR/Skua"
