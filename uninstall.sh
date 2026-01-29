#!/bin/bash
set -euo pipefail

# GifMaker uninstaller
# Removes from both user and system locations

echo "Uninstalling GifMaker..."

# User install
rm -f "$HOME/.local/bin/gifmaker"
rm -f "$HOME/.local/share/applications/com.gifmaker.app.desktop"

# System install (may fail without sudo, that's fine)
rm -f /usr/local/bin/gifmaker 2>/dev/null || true
rm -f /usr/local/share/applications/com.gifmaker.app.desktop 2>/dev/null || true

echo "Done."
