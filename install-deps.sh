#!/usr/bin/env bash
set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"

echo "=================================================="
echo "    Instalador de Dependências do Skua Linux      "
echo "=================================================="

# 1. Configurar .NET 10
DOTNET_DIR="$HOME/.dotnet"
if [ ! -f "$DOTNET_DIR/dotnet" ]; then
    echo "[1/4] .NET 10 não encontrado. Baixando e instalando em $DOTNET_DIR..."
    curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_DIR"
    rm -f /tmp/dotnet-install.sh
else
    echo "[1/4] .NET 10 já está instalado em $DOTNET_DIR."
fi

# 2. Configurar PATH e DOTNET_ROOT no .bashrc
echo "[2/4] Verificando variáveis de ambiente no .bashrc..."
if ! grep -q 'DOTNET_ROOT' "$HOME/.bashrc" 2>/dev/null; then
    cat << 'EOF' >> "$HOME/.bashrc"

# .NET environment for Skua
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.local/bin:$PATH"
EOF
    echo "Variáveis adicionadas ao ~/.bashrc."
else
    echo "Variáveis de ambiente já configuradas."
fi

# 3. Pacotes do Sistema (X11, Vulkan, Fontes)
echo "[3/4] Verificando bibliotecas do sistema..."
PKGS=(libxtst6 libx11-6 libvulkan1 mesa-vulkan-drivers libfontconfig1)
MISSING=()
for p in "${PKGS[@]}"; do
    if ! dpkg -s "$p" &>/dev/null; then
        MISSING+=("$p")
    fi
done

if [ ${#MISSING[@]} -gt 0 ]; then
    echo "Instalando pacotes pendentes: ${MISSING[*]}..."
    sudo apt-get update && sudo apt-get install -y "${MISSING[@]}"
else
    echo "Todas as bibliotecas necessárias já estão presentes no sistema!"
fi

# 4. Ajustar permissões
echo "[4/5] Ajustando permissões de execução..."
chmod +x "$DIR/run-linux.sh" 2>/dev/null || true
chmod +x "$DIR/build-linux.sh" 2>/dev/null || true

OUT_DIR="$DIR/Skua.Linux/bin/Debug/net10.0"
if [ -f "$OUT_DIR/Skua" ]; then
    chmod +x "$OUT_DIR/Skua"
fi
if [ -f "$OUT_DIR/ruffle_desktop" ]; then
    chmod +x "$OUT_DIR/ruffle_desktop"
fi
if [ -f "$DIR/.tools/ruffle/ruffle_desktop" ]; then
    chmod +x "$DIR/.tools/ruffle/ruffle_desktop"
fi

# 5. Criar atalhos (.desktop) no Menu e na Área de Trabalho
echo "[5/5] Configurando atalhos na Área de Trabalho e Menu de Aplicativos..."
ICON_DIR="$HOME/.local/share/icons/hicolor/256x256/apps"
APP_DIR="$HOME/.local/share/applications"
mkdir -p "$ICON_DIR" "$APP_DIR" "$HOME/.local/share/icons"

if [ -f "$DIR/Assets/SkuaIcon.png" ]; then
    cp -f "$DIR/Assets/SkuaIcon.png" "$HOME/.local/share/icons/skua.png"
    cp -f "$DIR/Assets/SkuaIcon.png" "$ICON_DIR/skua.png"
fi

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

# Detectar todas as possíveis pastas de Área de Trabalho
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

# Copiar atalho e marcar como confiável/executável
declare -A SEEN_DESK=()
for desk in "${DESKTOP_DIRS[@]}"; do
    if [ -z "${SEEN_DESK["$desk"]:-}" ]; then
        SEEN_DESK["$desk"]=1
        cp -f "$APP_DIR/skua.desktop" "$desk/skua.desktop"
        chmod +x "$desk/skua.desktop"
        gio set "$desk/skua.desktop" metadata::trusted true 2>/dev/null || true
        echo "Atalho criado com sucesso em: $desk/skua.desktop"
    fi
done

update-desktop-database "$APP_DIR" 2>/dev/null || true
gtk-update-icon-cache -f "$HOME/.local/share/icons/hicolor" 2>/dev/null || true

echo ""
echo "=================================================="
echo "  Instalação concluída com sucesso!               "
echo "  Atalho criado na sua Área de Trabalho!          "
echo "  Para iniciar pelo terminal, execute:            "
echo "    cd \"$DIR\" && ./run-linux.sh                 "
echo "=================================================="
