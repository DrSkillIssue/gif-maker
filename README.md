# GIF Maker

> NOTE! This was vibe-coded for my own personal use. 

Lightweight Ubuntu 24.04 screen recorder. Capture any screen region as GIF, MP4, or WebM.

## Features

- **Area selection**: Click and drag to select any screen region
- **Multiple formats**: GIF, MP4, WebM output
- **Configurable FPS**: 15, 24, 30, or 60 fps
- **Global hotkey**: Ctrl+Alt+S to start capture
- **Visual feedback**: Red border overlay shows recording region
- **Clipboard support**: Copy output file directly to clipboard

## Requirements

- Linux with X11 (Wayland: limited functionality)
- .NET 10 runtime
- FFmpeg
- slop (for area selection)

### Install dependencies (Debian/Ubuntu)

```bash
sudo apt install ffmpeg slop
```

## Build

```bash
dotnet build                    # Debug
dotnet build -c Release         # Release
dotnet publish -c Release       # Self-contained binary
```

Output: `publish/gifmaker`

## Run

```bash
dotnet run                      # Development
./publish/gifmaker              # Published binary
```

## Usage

1. Launch the application
2. Press **Ctrl+Alt+S** or click **Select Area**
3. Click and drag to select the region to record
4. Configure format, FPS, and output directory
5. Click **Record** to start, **Stop** to finish
6. **Open** to view the file or **Copy** to clipboard

## Install

### Debian/Ubuntu (.deb)

```bash
sudo apt install ./gifmaker_1.0.0_amd64.deb
```

### AppImage

```bash
chmod +x GifMaker-1.0.0-x86_64.AppImage
./GifMaker-1.0.0-x86_64.AppImage
```

Requires host system to have: GTK4, X11, ffmpeg, slop.

### Flatpak (from Flathub)

```bash
flatpak install flathub com.gifmaker.app
```

### From source

```bash
# User install (~/.local/bin)
./install.sh --user

# System install (/usr/local/bin)
sudo ./install.sh
```

## Building Packages

### .deb package

```bash
# Requires: dotnet-sdk-10.0, fakeroot
VERSION=1.0.0 ./packaging/deb/build.sh
```

Output: `gifmaker_1.0.0_amd64.deb`

### AppImage

```bash
# Requires: dotnet-sdk-10.0, wget
VERSION=1.0.0 ./packaging/appimage/build.sh
```

Output: `GifMaker-1.0.0-x86_64.AppImage`

### Flatpak

Submit to [Flathub](https://github.com/flathub/flathub) - they build and host it.

## Architecture

```
src/
  App/        # GTK4 UI, main window, record window
  Core/       # Result<T>, Rectangle, interfaces
  Recording/  # FFmpeg x11grab screen capture
  Conversion/ # FFmpeg format conversion
  X11/        # P/Invoke for X11, hotkeys, area selection
```

## Technical Notes

- **GTK4** via GirCore bindings
- **X11** for global hotkeys and precise window positioning
- **FFmpeg** x11grab for capture, libx264 for encoding
- **slop** for interactive area selection
- State machines modeled as sealed record hierarchies (discriminated unions)
- `Result<T>` for explicit error handling without exceptions

## License

MIT
