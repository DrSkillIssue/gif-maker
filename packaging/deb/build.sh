#!/bin/bash
set -euo pipefail

# Build .deb package for GifMaker
# Usage: ./packaging/deb/build.sh
# Output: gifmaker_VERSION_amd64.deb

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
VERSION="${VERSION:-1.0.0}"
ARCH="amd64"
PKG_NAME="gifmaker"
PKG_DIR="$SCRIPT_DIR/build/${PKG_NAME}_${VERSION}_${ARCH}"

echo "Building $PKG_NAME $VERSION for $ARCH"

# Clean previous build
rm -rf "$SCRIPT_DIR/build"
mkdir -p "$PKG_DIR"

# Build the application
echo "Building application..."
cd "$REPO_ROOT"
dotnet publish -c Release -r linux-x64 --self-contained true -o "$SCRIPT_DIR/build/publish"

# Create directory structure
mkdir -p "$PKG_DIR/usr/bin"
mkdir -p "$PKG_DIR/usr/share/applications"
mkdir -p "$PKG_DIR/usr/share/metainfo"
mkdir -p "$PKG_DIR/usr/share/doc/$PKG_NAME"
mkdir -p "$PKG_DIR/DEBIAN"

# Install binary (rename to lowercase)
cp "$SCRIPT_DIR/build/publish/GifMaker" "$PKG_DIR/usr/bin/gifmaker"
chmod 755 "$PKG_DIR/usr/bin/gifmaker"

# Install desktop file
cp "$REPO_ROOT/gifmaker.desktop.in" "$PKG_DIR/usr/share/applications/com.gifmaker.app.desktop"

# Install metainfo
cp "$REPO_ROOT/com.gifmaker.app.metainfo.xml" "$PKG_DIR/usr/share/metainfo/"

# Install docs
cp "$REPO_ROOT/LICENSE" "$PKG_DIR/usr/share/doc/$PKG_NAME/copyright"
cp "$REPO_ROOT/README.md" "$PKG_DIR/usr/share/doc/$PKG_NAME/"

# Calculate installed size (in KB)
INSTALLED_SIZE=$(du -sk "$PKG_DIR" | cut -f1)

# Create control file
cat > "$PKG_DIR/DEBIAN/control" << EOF
Package: $PKG_NAME
Version: $VERSION
Section: video
Priority: optional
Architecture: $ARCH
Depends: ffmpeg, slop, libgtk-4-1, libx11-6
Installed-Size: $INSTALLED_SIZE
Maintainer: GifMaker Developers <gifmaker@example.com>
Homepage: https://github.com/OWNER/gif-maker
Description: Lightweight screen recorder for Linux
 Record any screen region as GIF, MP4, or WebM.
 Features area selection, configurable FPS, global hotkeys,
 and clipboard support.
EOF

# Build the package
echo "Building .deb package..."
cd "$SCRIPT_DIR/build"
fakeroot dpkg-deb --build "${PKG_NAME}_${VERSION}_${ARCH}"

# Move to repo root
mv "${PKG_NAME}_${VERSION}_${ARCH}.deb" "$REPO_ROOT/"

echo "Done: $REPO_ROOT/${PKG_NAME}_${VERSION}_${ARCH}.deb"
echo ""
echo "Install with: sudo apt install ./${PKG_NAME}_${VERSION}_${ARCH}.deb"
