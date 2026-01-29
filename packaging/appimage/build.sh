#!/bin/bash
set -euo pipefail

# Build AppImage for GifMaker
# Usage: ./packaging/appimage/build.sh
# Output: GifMaker-VERSION-x86_64.AppImage

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
VERSION="${VERSION:-1.0.0}"
ARCH="x86_64"

echo "Building GifMaker $VERSION AppImage for $ARCH"

# Clean previous build
rm -rf "$SCRIPT_DIR/build"
mkdir -p "$SCRIPT_DIR/build"
cd "$SCRIPT_DIR/build"

# Build the application
echo "Building application..."
dotnet publish "$REPO_ROOT" -c Release -r linux-x64 --self-contained true -o publish

# Create AppDir structure
APPDIR="$SCRIPT_DIR/build/AppDir"
mkdir -p "$APPDIR/usr/bin"
mkdir -p "$APPDIR/usr/share/applications"
mkdir -p "$APPDIR/usr/share/metainfo"

# Install binary
cp publish/GifMaker "$APPDIR/usr/bin/gifmaker"
chmod 755 "$APPDIR/usr/bin/gifmaker"

# Install desktop file
cp "$REPO_ROOT/gifmaker.desktop.in" "$APPDIR/usr/share/applications/com.gifmaker.app.desktop"
# Also copy to AppDir root (required by AppImage)
cp "$REPO_ROOT/gifmaker.desktop.in" "$APPDIR/com.gifmaker.app.desktop"

# Install metainfo
cp "$REPO_ROOT/com.gifmaker.app.metainfo.xml" "$APPDIR/usr/share/metainfo/"

# Create AppRun script
cat > "$APPDIR/AppRun" << 'EOF'
#!/bin/bash
SELF=$(readlink -f "$0")
HERE=${SELF%/*}
export PATH="${HERE}/usr/bin:${PATH}"
exec "${HERE}/usr/bin/gifmaker" "$@"
EOF
chmod 755 "$APPDIR/AppRun"

# Download linuxdeploy if not present
if [[ ! -f linuxdeploy-x86_64.AppImage ]]; then
    echo "Downloading linuxdeploy..."
    wget -q "https://github.com/linuxdeploy/linuxdeploy/releases/download/continuous/linuxdeploy-x86_64.AppImage"
    chmod +x linuxdeploy-x86_64.AppImage
fi

# Download appimagetool if not present
if [[ ! -f appimagetool-x86_64.AppImage ]]; then
    echo "Downloading appimagetool..."
    wget -q "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
    chmod +x appimagetool-x86_64.AppImage
fi

# Build AppImage
# Note: We skip linuxdeploy's library bundling since .NET self-contained already includes everything
# and the native libs (GTK4, X11) must come from the host system
echo "Building AppImage..."
export VERSION
ARCH=$ARCH ./appimagetool-x86_64.AppImage "$APPDIR" "GifMaker-${VERSION}-${ARCH}.AppImage"

# Move to repo root
mv "GifMaker-${VERSION}-${ARCH}.AppImage" "$REPO_ROOT/"

echo "Done: $REPO_ROOT/GifMaker-${VERSION}-${ARCH}.AppImage"
echo ""
echo "Run with: ./GifMaker-${VERSION}-${ARCH}.AppImage"
echo ""
echo "NOTE: This AppImage requires the host system to have:"
echo "  - GTK4 (libgtk-4)"
echo "  - X11 (libX11)"
echo "  - ffmpeg"
echo "  - slop"
