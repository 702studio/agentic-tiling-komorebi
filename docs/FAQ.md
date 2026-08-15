# ❓ Frequently Asked Questions (FAQ) & Troubleshooting

Comprehensive guide for users and autonomous coding agents covering common scenarios, edge cases, root causes, and verified solutions.

---

## Table of Contents
1. [Window Management & Capturing](#1-window-management--capturing)
   - [Why did a window not tile or get ignored when opened?](#why-did-a-window-not-tile-or-get-ignored-when-opened)
   - [Why are Administrator / Elevated windows not managed? (UIPI)](#why-are-administrator--elevated-windows-not-managed-uipi)
   - [How do I bring an unmanaged or floating window into the grid?](#how-do-i-bring-an-unmanaged-or-floating-window-into-the-grid)
2. [Keyboard Navigation & Shortcuts](#2-keyboard-navigation--shortcuts)
   - [What is the complete list of default shortcuts?](#what-is-the-complete-list-of-default-shortcuts)
   - [What is the difference between Monocle (Alt+F) and Maximize?](#what-is-the-difference-between-monocle-altf-and-maximize)
   - [Why are hotkeys ultra-fast (2ms) in this baseline?](#why-are-hotkeys-ultra-fast-2ms-in-this-baseline)
3. [Mouse & Focus Behavior](#3-mouse--focus-behavior)
   - [How does automatic mouse hover (`mouse_follows_focus`) work?](#how-does-automatic-mouse-hover-mouse_follows_focus-work)
   - [How does `masir` handle pointer movement?](#how-does-masir-handle-pointer-movement)
4. [Tray Hub & Watchdog Self-Healing](#4-tray-hub--watchdog-self-healing)
   - [What do the system tray icon colors mean?](#what-do-the-system-tray-icon-colors-mean)
   - [How does the full-stack self-healing watchdog work?](#how-does-the-full-stack-self-healing-watchdog-work)
5. [Updates, Diagnostics & Rollback](#5-updates-diagnostics--rollback)
   - [How do I update to the latest release?](#how-do-i-update-to-the-latest-release)
   - [How do I run diagnostic health checks?](#how-do-i-run-diagnostic-health-checks)
   - [How do I completely uninstall or restore the pre-install desktop?](#how-do-i-completely-uninstall-or-restore-the-pre-install-desktop)

---

## 1. Window Management & Capturing

### Why did a window not tile or get ignored when opened?
**Root Cause:**
1. **Window Rule Matching:** Komorebi evaluates application matching rules in `applications.local.json` and bundled `applications.json`. If a window class or title matches an `ignore` or `floating` rule (e.g. transient dialogs, tooltips, or splash screens), Komorebi intentionally leaves it unmanaged.
2. **Floating Mode Active:** The window was opened as a floating dialog or explicitly toggled to floating mode.

**Solutions:**
* **`Alt + M` (Force Manage):** Focus the window and press `Alt + M` to immediately force Komorebi to manage it inside the active workspace tiling grid (`komorebic manage`).
* **`Alt + Space` or `Alt + T` (Toggle Float):** Toggles the window between floating mode and tiled grid mode (`komorebic toggle-float`).
* **`Alt + Shift + W` (Retile Workspace):** Recalculates all window coordinates and retiles the entire active workspace (`komorebic retile`).

---

### Why are Administrator / Elevated windows not managed? (UIPI)
**Root Cause:**
Windows enforces **User Interface Privilege Isolation (UIPI)**. When an application (such as PowerShell, Windows Terminal, or CMD) is launched **"Run as Administrator" (High Integrity Level)**, the Windows kernel strictly prevents standard user processes (Medium Integrity Level) from:
* Sending Win32 positioning messages (`SetWindowPos`, `MoveWindow`, `ShowWindow`)
* Hooking window events (`SetWinEventHook`)
* Forcing foreground activation (`SetForegroundWindow`)

If Komorebi was started under standard user privileges (`RunLevel: Limited`), Windows blocks it from modifying any elevated Administrator window.

**Solutions:**
1. **Standard Execution (Recommended):** Open your terminal or development tools as a standard user. Komorebi will tile and manage them automatically.
2. **Elevated WM Execution:** If you frequently use Administrator tools, configure the `StartKomorebi` scheduled task to run with `RunLevel: HighestAvailable` so Komorebi has elevation to manage both standard and elevated windows.

---

### How do I bring an unmanaged or floating window into the grid?
Use the dedicated window management hotkeys:

| Action | Hotkey | Command |
| :--- | :--- | :--- |
| **Force Manage into Grid** | `Alt + M` | `komorebic manage` |
| **Unmanage from Grid** | `Alt + Shift + M` | `komorebic unmanage` |
| **Toggle Floating / Tiled** | `Alt + Space` or `Alt + T` | `komorebic toggle-float` |
| **Retile Entire Screen** | `Alt + Shift + W` | `komorebic retile` |

---

## 2. Keyboard Navigation & Shortcuts

### What is the complete list of default shortcuts?

#### Window Focus & Navigation
* `Alt + Left / Right / Up / Down`: Focus adjacent window in that direction (instant 2ms socket call)
* `Alt + J` / `Alt + K`: Previous / next workspace
* `Ctrl + Alt + Left / Right`: Previous / next workspace (alternate)
* `Alt + A` / `Alt + S`: Previous / next active workspace
* `Alt + D`: Switch to last focused workspace
* `Alt + 1-9`: Switch directly to named workspace 1-9

#### Window Layout & State
* `Alt + M`: Force manage focused window into tiling grid
* `Alt + Shift + M`: Unmanage focused window
* `Alt + Space` / `Alt + T`: Toggle float
* `Alt + F`: Toggle Monocle mode (fullscreen within workspace boundary)
* `Alt + N`: Minimize window
* `Alt + Q`: Close focused window
* `Alt + Shift + Space`: Toggle container behaviour (stacking / tiling)
* `Ctrl + Alt + Space` / `Ctrl + Alt + Shift + Space`: Next / previous layout preset

#### Window Moving
* `Alt + Shift + Up / Down / Left / Right`: Move focused window in that direction
* `Alt + Shift + J` / `Alt + Shift + K`: Send window to previous / next workspace
* `Alt + Shift + 1-9`: Send window to workspace 1-9 (without following)
* `Ctrl + Alt + 1-9`: Move window to workspace 1-9 and follow
* `Alt + Shift + A / S / D / F`: Move active workspace across monitors (Left / Down / Up / Right)

#### Resizing & Presets
* `Alt + H` / `Alt + L`: Decrease / increase width by 5%
* `Alt + U` / `Alt + I`: Increase / decrease height by 5%
* `Alt + Y`: Modal interactive resize mode
* `Alt + P`: Agentic Pair-Programming preset (BSP layout)
* `Alt + G`: Grid layout preset

#### Lifecycle & Launchers
* `Alt + Return`: Launch Windows Terminal
* `Alt + B`: Launch Firefox
* `Alt + E`: Launch File Explorer
* `Alt + O`: Launch Obsidian
* `Alt + R`: Launch Flow Launcher
* `Alt + C`: Launch Cursor IDE
* `Alt + Shift + R`: Controlled reload (restarts daemon cleanly)
* `Alt + Shift + Backspace` or `Alt + Shift + X`: Quick restart
* `Alt + Shift + E`: Stop window manager

---

### What is the difference between Monocle (Alt+F) and Maximize?
* **`Alt + F` (Monocle Mode - `toggle-monocle`):**
  Expands the active tiled window so it occupies the entire workspace tiling area while **preserving the Komorebi taskbar, window borders, and workspace margins**. Pressing `Alt + F` again immediately restores the window to its exact previous grid spot without recalculating layout dimensions.
* **Windows Maximize (`toggle-maximize`):**
  Sends a native Win32 `SW_MAXIMIZE` message. This covers the taskbar, strips border highlights, and treats the window outside of tiling geometry.

---

### Why are hotkeys ultra-fast (2ms) in this baseline?
Core window management hotkeys in `config/whkdrc` use direct binary IPC calls to `komorebic.exe` over local named pipes / Unix domain sockets rather than spawning an intermediate `powershell.exe` process. This reduces keystroke dispatch latency from **~400ms down to 1-2ms**, giving instantaneous 60 FPS window response.

---

## 3. Mouse & Focus Behavior

### How does automatic mouse hover (`mouse_follows_focus`) work?
In `komorebi.json`, `"mouse_follows_focus": true` is enabled. Whenever focus changes via keyboard shortcut (`Alt + Left/Right/Up/Down`, `Alt + 1-9`, `Alt + J/K`), Komorebi automatically warps the mouse cursor to the center of the newly focused window. This triggers hover states in web browsers, code editors, and communication apps seamlessly.

### How does `masir` handle pointer movement?
`masir` provides relative-motion focus-follows-mouse. It only shifts window focus when the physical mouse pointer moves across a window boundary. A stationary mouse cursor never steals focus from keyboard-driven workflows.

---

## 4. Tray Hub & Watchdog Self-Healing

### What do the system tray icon colors mean?

| Badge Indicator | State | Description |
| :---: | :--- | :--- |
| 🟢 **Green** | **Healthy (4/4)** | All subsystems (`komorebi`, `komorebi-bar`, `whkd`, `masir`) are fully active and monitored. |
| 🟡 **Yellow** | **Recovering / Degraded** | The Watchdog has detected a missing subsystem (e.g. bar or hotkey daemon) and is actively auto-resurrecting it. |
| ⏸️ **Cyan** | **Paused** | Tiling is temporarily paused (`Alt + Shift + P`). Windows can be dragged freely without automatic tiling. |
| 🔴 **Red** | **Offline** | The window manager is stopped. |

---

### How does the full-stack self-healing watchdog work?
The C# .NET 8 Tray Hub (`KomorebiTray.exe`) runs a high-frequency supervisor loop:
1. **Bar Crash / GPU TDR Recovery:** If a graphics driver reset or sleep event crashes `komorebi-bar`, the Watchdog restarts the bar in **<200ms** via `-RestartBar` without flickering or disturbing active application layouts.
2. **Hotkey Daemon (`whkd`) Auto-Resurrection:** If the keyboard daemon exits or encounters an OS hook drop, the Watchdog detects it and restarts `whkd` within 5 seconds.
3. **Core Engine Resurrection:** If `komorebi.exe` halts unexpectedly, the Watchdog performs a clean mutex-guarded restart of the entire stack.
4. **GDI Saturation Guard:** Win32 GDI handle counts are tracked continuously. A balloon notification is issued if GDI allocation exceeds 7,500 handles (warning before the 10,000 system limit).

---

## 5. Updates, Diagnostics & Rollback

### How do I update to the latest release?
To check for updates:
```powershell
komorebi-update -CheckOnly
```
To apply the latest release in-place:
```powershell
komorebi-update
# Or force upgrade:
komorebi-update -Force
```
You can also right-click the **Komorebi Tray Icon** and click **🚀 Check for Updates / Upgrade**.

---

### How do I run diagnostic health checks?
Run the built-in doctor script for a complete health report:
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\KomorebiStarter\doctor.ps1" -Json
```
Or right-click the tray icon and select **🩺 Run Diagnostics (Doctor)**.

---

### How do I completely uninstall or restore the pre-install desktop?

**Restore previous desktop (preserving GlazeWM/backups):**
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\KomorebiStarter\restore.ps1"
```

**Clean uninstallation (preserves configuration files):**
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\KomorebiStarter\uninstall.ps1"
```

**Complete uninstallation (removes all configuration and runtime state):**
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\KomorebiStarter\uninstall.ps1" -RemoveConfig -Force
```
