#!/bin/bash
set -euo pipefail

# GifMaker installer
# Usage: ./install.sh [--system] [--prefix=PATH]

PREFIX="$HOME/.local"

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

echo "Done. Make sure $BINDIR is in your PATH."
