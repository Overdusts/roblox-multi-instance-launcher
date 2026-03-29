# Roblox Multi-Instance Launcher

A lightweight multi-instance Roblox launcher built in C# with aggressive performance optimization, continuous mutex bypass, and anti-AFK support.

![.NET 9](https://img.shields.io/badge/.NET-9.0-blue)
![Windows](https://img.shields.io/badge/Platform-Windows-lightgrey)
![License](https://img.shields.io/badge/License-MIT-green)

## Features

- **Multi-Instance** — Launch multiple Roblox clients simultaneously using Fishtrap-style continuous mutex monitoring
- **FFlag Optimization** — 4 quality presets (Potato, Low, Medium, Default) that dramatically reduce GPU/CPU/RAM usage
- **Anti-AFK** — Background input simulation prevents idle kicks across all instances
- **Instance Monitor** — Real-time PID, RAM usage, and uptime tracking per instance
- **Modern UI** — Custom-drawn dark theme with borderless window, rounded panels, and hover effects

## Quality Presets

| Preset | FPS | Textures | Shadows | Particles | Draw Distance | Best For |
|--------|-----|----------|---------|-----------|---------------|----------|
| **Potato** | 1 | Off | Off | Off | 50 | Max instances / AFK farming |
| **Low** | 15 | Lowest | Off | Minimal | 300 | Playable on weak hardware |
| **Medium** | 30 | Low | Reduced | Normal | Default | Balanced |
| **Default** | — | — | — | — | — | No changes |

### Potato Preset Details

Designed for running as many instances as possible (~250-350MB RAM each):

- 1 FPS (1 FPS when unfocused)
- Voxel lighting (cheapest rendering mode)
- All shadows, particles, grass, wind, water disabled
- Texture quality 0, draw distance 50
- Audio disabled, physics throttled
- All telemetry disabled

## Requirements

- Windows 10/11
- [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- Roblox installed (standard installation)

## Building from Source

```bash
# Install .NET 9 SDK
# https://dotnet.microsoft.com/download/dotnet/9.0

# Clone and build
git clone https://github.com/Overdusts/roblox-multi-instance-launcher.git
cd roblox-multi-instance-launcher/RobloxLauncher
dotnet build --configuration Release

# Run
./bin/Release/net9.0-windows/RobloxLauncher.exe
```

## Usage

1. Launch `RobloxLauncher.exe`
2. Set number of instances and quality preset
3. Click **Launch Instances**
4. Log into each Roblox window and join your game
5. Anti-AFK keeps all instances alive automatically

## Project Structure

```
RobloxLauncher/
├── Core/
│   ├── MutexBypass.cs       # Continuous singleton mutex monitor & killer
│   ├── QualityOptimizer.cs  # FFlag presets & ClientAppSettings.json manager
│   ├── RobloxLauncher.cs    # Instance spawning & lifecycle management
│   ├── AntiAFK.cs           # Background input simulation (F13 + mouse jiggle)
│   └── AccountManager.cs    # Cookie-based account storage (optional)
├── UI/
│   ├── Theme.cs             # Color palette & font definitions
│   ├── TitleBar.cs          # Custom borderless title bar with drag support
│   ├── ModernButton.cs      # Rounded buttons with hover/press animations
│   ├── ModernTextBox.cs     # Styled text input with focus glow
│   ├── ModernComboBox.cs    # Owner-drawn dropdown with chevron
│   ├── ModernListView.cs    # Custom-drawn list with styled checkboxes
│   ├── ModernLog.cs         # Color-coded activity log
│   ├── RoundedPanel.cs      # Card containers with rounded corners
│   ├── ProgressIndicator.cs # Gradient progress bar
│   └── LoginDialog.cs       # WebView2 Roblox login (optional)
├── Form1.cs                 # Main window
└── Program.cs               # Entry point
```

## How It Works

### Mutex Bypass
Roblox uses a named mutex (`ROBLOX_singletonMutex`) to prevent multiple clients. The launcher runs a background thread that continuously scans all Roblox processes using `NtQuerySystemInformation`, finds mutex handles by name, and closes them via `DuplicateHandle` with `DUPLICATE_CLOSE_SOURCE`.

### FFlag Optimization
Writes a `ClientAppSettings.json` file to Roblox's `ClientSettings` directory with fast flags that override rendering, audio, physics, and telemetry settings. This is a supported Roblox feature — the same mechanism Roblox uses internally for A/B testing.

### Anti-AFK
Uses `PostMessage` to send invisible F13 key presses and tiny mouse movements to each Roblox window at configurable intervals. This counts as user input without affecting gameplay.

## Disclaimer

This tool does not inject into or modify Roblox processes. It only:
- Launches the official Roblox client
- Manages OS-level mutex handles
- Writes supported configuration files (FFlags)
- Sends standard Windows input messages

Use at your own discretion.
