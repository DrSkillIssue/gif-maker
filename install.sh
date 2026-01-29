#!/bin/bash
set -euo pipefail

# GifMaker installer
# Usage: ./install.sh [--prefix=/usr/local] [--user]

PREFIX="/usr/local"
USER_INSTALL=false

for arg in "$@"; do
    case $arg in
        --prefix=*) PREFIX="${arg#*=}" ;;
        --user) USER_INSTALL=true ;;
        --help|-h)
            echo "Usage: $0 [--prefix=/usr/local] [--user]"
            echo "  --prefix=PATH  Install to PATH (default: /usr/local)"
            echo "  --user         Install to ~/.local (sets prefix automatically)"
            exit 0
            ;;
        *) echo "Unknown option: $arg"; exit 1 ;;
    esac
done

if $USER_INSTALL; then
    PREFIX="$HOME/.local"
fi

BINDIR="$PREFIX/bin"
APPDIR="$PREFIX/share/applications"
ICONDIR="$PREFIX/share/icons/hicolor/scalable/apps"

echo "Installing GifMaker to $PREFIX"

# Build if not already built
if [[ ! -f "publish/gifmaker" ]]; then
    echo "Building..."
    dotnet publish -c Release -o publish
    # Rename to lowercase
    mv publish/GifMaker publish/gifmaker 2>/dev/null || true
fi

# Install binary
echo "Installing binary to $BINDIR"
mkdir -p "$BINDIR"
cp publish/gifmaker "$BINDIR/"
chmod +x "$BINDIR/gifmaker"

# Install desktop file
echo "Installing desktop file to $APPDIR"
mkdir -p "$APPDIR"
cp gifmaker.desktop.in "$APPDIR/com.gifmaker.app.desktop"

# Install icon
echo "Installing icon to $ICONDIR"
mkdir -p "$ICONDIR"
cp data/icons/hicolor/scalable/apps/com.gifmaker.app.svg "$ICONDIR/"

echo "Done. Make sure $BINDIR is in your PATH."
