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
echo "[4/4] Ajustando permissões de execução..."
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

echo ""
echo "=================================================="
echo "  Instalação concluída com sucesso!               "
echo "  Para iniciar o bot, execute:                    "
echo "    cd \"$DIR\" && ./run-linux.sh                 "
echo "=================================================="
