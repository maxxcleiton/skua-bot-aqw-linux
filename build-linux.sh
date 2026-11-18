#!/usr/bin/env bash
set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.local/bin:$HOME/.dotnet:$DIR/.tools/jdk-17.0.21+9-jre/bin:$PATH"
export JAVA_HOME="$DIR/.tools/jdk-17.0.21+9-jre"

echo "[1/2] Compiling Skua.AS3 (skua.swf)..."
cd "$DIR/Skua.AS3"
mkdir -p skua/bin
mxmlc -source-path "skua/src" -default-size 958 550 -default-frame-rate 60 -output "skua/bin/skua.swf" "skua/src/skua/Main.as" -target-player 28.0 -optimize

echo "[2/2] Building Skua.Linux (.NET 10)..."
cd "$DIR"
dotnet build Skua.Linux/Skua.Linux.csproj -p:WarningLevel=0

OUT_DIR="$DIR/Skua.Linux/bin/Debug/net10.0"
cp -f "$DIR/Skua.AS3/skua/bin/skua.swf" "$OUT_DIR/skua.swf"
ln -sfn "$DIR/FFDec" "$OUT_DIR/FFDec"

echo "Build complete! Binary at: $OUT_DIR/Skua"
