#!/bin/bash
set -euo pipefail

# GifMaker installer
# Usage: ./install.sh [--system] [--prefix=PATH]

PREFIX="$HOME/.local"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

for arg in "$@"; do
    case $arg in
        --prefix=*) PREFIX="${arg#*=}" ;;
        --system) PREFIX="/usr/local" ;;
        --help|-h)
            echo "Usage: $0 [--system] [--prefix=PATH]"
            echo "  --system       Install to /usr/local (requires sudo)"
            echo "  --prefix=PATH  Install to PATH"
            echo "  Default: ~/.local"
            exit 0
            ;;
        *) echo "Unknown option: $arg"; exit 1 ;;
    esac
done

BINDIR="$PREFIX/bin"
APPDIR="$PREFIX/share/applications"
PUBLISH_DIR="$(mktemp -d -t gifmaker-publish.XXXXXX)"
trap 'rm -rf "$PUBLISH_DIR"' EXIT

echo "Installing GifMaker to $PREFIX"

echo "Building..."
dotnet publish "$SCRIPT_DIR/GifMaker.csproj" -c Release -r linux-x64 --self-contained true -o "$PUBLISH_DIR"

# Install binary
echo "Installing binary to $BINDIR"
mkdir -p "$BINDIR"
cp "$PUBLISH_DIR/GifMaker" "$BINDIR/gifmaker"
chmod +x "$BINDIR/gifmaker"

# Install desktop file
echo "Installing desktop file to $APPDIR"
mkdir -p "$APPDIR"
cp "$SCRIPT_DIR/gifmaker.desktop.in" "$APPDIR/com.gifmaker.app.desktop"

echo "Done. Make sure $BINDIR is in your PATH."
