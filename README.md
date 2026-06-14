# GIF Maker

Lightweight Linux screen recorder. Capture any region as GIF, MP4, or WebM.

> Vibe-coded for personal use. Tested on Ubuntu 24.04 + X11.

## Features

- Area selection with click-and-drag
- Output formats: GIF, MP4, WebM
- Frame rates: 15, 24, 30, 60 fps
- Global hotkey: `Ctrl+Alt+S`
- Red border overlay during recording
- Copy to clipboard

## Install

### From source

```bash
sudo apt install ffmpeg slop libgtk-4-1 libx11-6 libxext6 xdg-utils
./install.sh                # ~/.local/bin
sudo ./install.sh --system  # /usr/local/bin
```

## Uninstall

```bash
./uninstall.sh
```

## Dependencies

| Package (apt)   | Purpose              |
|-----------------|----------------------|
| `libgtk-4-1`    | UI toolkit           |
| `libx11-6`      | Display server       |
| `libxext6`      | X11 extensions       |
| `ffmpeg`        | Recording & encoding |
| `slop`          | Area selection       |
| `xdg-utils`     | File opening         |

## Usage

1. Launch GIF Maker
2. `Ctrl+Alt+S` or click **Select Area**
3. Drag to select region
4. Choose format, FPS, output directory
5. **Record** → **Stop**
6. **Open** or **Copy** to clipboard

## Building

```bash
dotnet build                    # Debug
dotnet build -c Release         # Release
dotnet run                      # Run
```

### Packaging (for distribution)

```bash
# Requires: fakeroot
VERSION=1.0.0 ./packaging/deb/build.sh       # → gifmaker_1.0.0_amd64.deb

# Requires: wget
VERSION=1.0.0 ./packaging/appimage/build.sh  # → GifMaker-1.0.0-x86_64.AppImage
```

Flatpak: submit manifest to [Flathub](https://github.com/flathub/flathub).

## Architecture

```
src/
  App/        # GTK4 UI
  Core/       # Result<T>, Rectangle, interfaces
  Recording/  # FFmpeg x11grab
  Conversion/ # Format conversion
  X11/        # P/Invoke, hotkeys, area selection
```

## Limitations

- X11 only (Wayland: no global hotkeys, no window positioning)
- Linux only

## License

MIT - DrSkillIssue