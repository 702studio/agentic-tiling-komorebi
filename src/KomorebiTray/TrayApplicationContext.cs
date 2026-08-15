using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.IO;

namespace KomorebiTray;

public class TrayApplicationContext : ApplicationContext
{
    private static readonly string[] PossibleIconNames = new[] { "k-optical-floating.ico", "k-matrix-diagonal.ico" };
    private readonly NotifyIcon _notifyIcon;
    private readonly KomorebiManager _manager;
    private readonly DashboardForm _dashboard;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Icon? _baseIcon;

    private ToolStripMenuItem _menuHeader = null!;
    private ToolStripMenuItem _menuAutoRecover = null!;
    private ToolStripMenuItem _menuNotifications = null!;
    private Icon? _activeIcon;

    public TrayApplicationContext()
    {
        _manager = new KomorebiManager();
        _dashboard = new DashboardForm(_manager);

        _baseIcon = ResolveBaseIcon();

        if (_baseIcon != null)
        {
            _dashboard.Icon = _baseIcon;
        }

        _notifyIcon = new NotifyIcon
        {
            Icon = _baseIcon ?? SystemIcons.Application,
            Visible = false,
            Text = "Komorebi Hub (Initializing...)"
        };

        _notifyIcon.DoubleClick += (s, e) => ShowDashboard();

        BuildContextMenu();

        _manager.OnBarRecovered += msg =>
        {
            if (_manager.ShowNotifications)
            {
                _notifyIcon.ShowBalloonTip(3000, "Komorebi Watchdog", msg, ToolTipIcon.Info);
            }
            UpdateTrayVisuals();
        };

        _manager.OnStackRecovered += msg =>
        {
            if (_manager.ShowNotifications)
            {
                _notifyIcon.ShowBalloonTip(3500, "Komorebi Watchdog", msg, ToolTipIcon.Info);
            }
            UpdateTrayVisuals();
        };

        _manager.OnHighGdiWarning += gdiCount =>
        {
            if (_manager.ShowNotifications)
            {
                _notifyIcon.ShowBalloonTip(4000, "Komorebi Memory Warning", $"Komorebi GDI handle count is high ({gdiCount}/10000). A quick restart is recommended.", ToolTipIcon.Warning);
            }
        };

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (s, e) =>
        {
            _manager.WatchdogTick();
            UpdateTrayVisuals();
        };

        UpdateTrayVisuals();
        _notifyIcon.Visible = true;
        _timer.Start();

        _manager.EnsureEnvironmentStarted();
        StartActivationListener();

        _manager.AddLog("INFO", "Komorebi Tray Hub initialized with Optical Floating branding & Watchdog supervisor.");
    }

    private void StartActivationListener()
    {
        try
        {
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "TolgaOzisik.KomorebiTrayHub.ShowEvent");
            var thread = new Thread(() =>
            {
                while (true)
                {
                    if (showEvent.WaitOne())
                    {
                        try
                        {
                            if (_dashboard.IsHandleCreated)
                            {
                                _dashboard.BeginInvoke(new Action(() => ShowDashboard()));
                            }
                        }
                        catch { }
                    }
                }
            })
            {
                IsBackground = true
            };
            thread.Start();
        }
        catch { }
    }

    private static Icon? ResolveBaseIcon()
    {
        var searchDirs = new[]
        {
            AppDomain.CurrentDomain.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "bin")
        };

        foreach (var dir in searchDirs)
        {
            foreach (var name in PossibleIconNames)
            {
                var full = Path.Combine(dir, name);
                if (File.Exists(full))
                {
                    try { return new Icon(full); } catch { }
                }
            }
        }
        return null;
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(28, 28, 30),
            ForeColor = Color.FromArgb(235, 235, 235),
            Font = new Font("Segoe UI", 9.5f),
            ShowImageMargin = false
        };

        _menuHeader = new ToolStripMenuItem("● Komorebi Hub: Checking...") { Enabled = false, Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold) };
        menu.Items.Add(_menuHeader);
        menu.Items.Add(new ToolStripSeparator());

        var itemStart = new ToolStripMenuItem("▶ Start / Resume Window Manager", null, (s, e) =>
        {
            if (_manager.ShowNotifications)
            {
                _notifyIcon.ShowBalloonTip(2000, "Komorebi Hub", "Starting Komorebi Window Manager...", ToolTipIcon.Info);
            }
            _manager.StartAll();
        });
        var itemRestartAll = new ToolStripMenuItem("⚡ Quick Restart (Nuke & Clean)", null, (s, e) =>
        {
            if (_manager.ShowNotifications)
            {
                _notifyIcon.ShowBalloonTip(2000, "Komorebi Hub", "Restarting Komorebi Window Manager...", ToolTipIcon.Info);
            }
            _manager.RestartAll();
        });
        var itemRestartBar = new ToolStripMenuItem("🔄 Restart Bar Only (150ms)", null, (s, e) => _manager.RestartBarOnly());
        var itemPause = new ToolStripMenuItem("⏸️ Toggle Pause / Tiling", null, (s, e) => _manager.TogglePause());

        menu.Items.Add(itemStart);
        menu.Items.Add(itemRestartAll);
        menu.Items.Add(itemRestartBar);
        menu.Items.Add(itemPause);
        menu.Items.Add(new ToolStripSeparator());

        var itemDashboard = new ToolStripMenuItem("📊 Open Dashboard...", null, (s, e) => ShowDashboard());
        var itemDoctor = new ToolStripMenuItem("🩺 Run Diagnostics (Doctor)", null, (s, e) =>
        {
            ShowDashboard();
            var output = _manager.RunDoctor();
            MessageBox.Show(_dashboard, output, "Komorebi Doctor Results", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });

        menu.Items.Add(itemDashboard);
        menu.Items.Add(itemDoctor);

        var itemConfigs = new ToolStripMenuItem("⚙️ Configuration Files");
        itemConfigs.DropDownItems.Add("komorebi.json (WM Settings)", null, (s, e) => _manager.OpenConfigFile("komorebi.json"));
        itemConfigs.DropDownItems.Add("komorebi.bar.json (Bar Layout)", null, (s, e) => _manager.OpenConfigFile("komorebi.bar.json"));
        itemConfigs.DropDownItems.Add("whkdrc (Hotkeys)", null, (s, e) => _manager.OpenConfigFile("whkdrc"));
        itemConfigs.DropDownItems.Add("applications.local.json (App Rules)", null, (s, e) => _manager.OpenConfigFile("applications.local.json"));
        menu.Items.Add(itemConfigs);

        var itemPresets = new ToolStripMenuItem("🤖 Agentic Presets");
        itemPresets.DropDownItems.Add("Pair-Programming Layout (BSP)", null, (s, e) => _manager.ApplyPreset("pair"));
        itemPresets.DropDownItems.Add("Grid Layout", null, (s, e) => _manager.ApplyPreset("grid"));
        itemPresets.DropDownItems.Add("Focus / Monocle Mode", null, (s, e) => _manager.ApplyPreset("focus"));
        menu.Items.Add(itemPresets);

        var itemUpdate = new ToolStripMenuItem("🚀 Check for Updates / Upgrade", null, (s, e) => _manager.LaunchUpdate());
        menu.Items.Add(itemUpdate);

        menu.Items.Add(new ToolStripSeparator());

        _menuAutoRecover = new ToolStripMenuItem("🛡️ Auto-Recover Bar (Watchdog)", null, (s, e) =>
        {
            _manager.AutoRecoverBar = !_manager.AutoRecoverBar;
            _menuAutoRecover.Checked = _manager.AutoRecoverBar;
        }) { Checked = _manager.AutoRecoverBar };

        _menuNotifications = new ToolStripMenuItem("🔔 Show Notifications", null, (s, e) =>
        {
            _manager.ShowNotifications = !_manager.ShowNotifications;
            _menuNotifications.Checked = _manager.ShowNotifications;
        }) { Checked = _manager.ShowNotifications };

        menu.Items.Add(_menuAutoRecover);
        menu.Items.Add(_menuNotifications);

        menu.Items.Add(new ToolStripSeparator());

        var itemExitAll = new ToolStripMenuItem("❌ Stop All WM Processes & Exit", null, (s, e) =>
        {
            _manager.StopAll();
            ExitThread();
        });

        var itemExitTrayOnly = new ToolStripMenuItem("🚪 Close Tray Only (Keep WM Running)", null, (s, e) => ExitThread());

        menu.Items.Add(itemExitAll);
        menu.Items.Add(itemExitTrayOnly);

        _notifyIcon.ContextMenuStrip = menu;
    }

    private void UpdateTrayVisuals()
    {
        var snapshot = _manager.GetHealthSnapshot();

        Color statusColor;
        string statusShort;

        switch (snapshot.Status)
        {
            case HealthStatus.Healthy:
                statusColor = Color.FromArgb(0, 230, 118); // Emerald Green
                statusShort = "Healthy (4/4)";
                _menuHeader.ForeColor = statusColor;
                _menuHeader.Text = $"🟢 Komorebi Hub: {statusShort}";
                break;
            case HealthStatus.Recovering:
                statusColor = Color.FromArgb(255, 193, 7); // Amber/Yellow
                statusShort = "Bar Recovering";
                _menuHeader.ForeColor = statusColor;
                _menuHeader.Text = $"🟡 Komorebi Hub: {statusShort}";
                break;
            case HealthStatus.Paused:
                statusColor = Color.FromArgb(0, 215, 255); // Cyan
                statusShort = "Paused";
                _menuHeader.ForeColor = statusColor;
                _menuHeader.Text = $"⏸️ Komorebi Hub: {statusShort}";
                break;
            default:
                statusColor = Color.FromArgb(255, 82, 82); // Red
                statusShort = "Offline";
                _menuHeader.ForeColor = statusColor;
                _menuHeader.Text = $"🔴 Komorebi Hub: {statusShort}";
                break;
        }

        SetDynamicTrayIcon(statusColor, snapshot.Status);

        var tip = $"Komorebi: {statusShort}\nGDI: {snapshot.KomorebiGdiHandles}/10k | Recv: {_manager.RecoveryCount}";
        if (tip.Length > 63)
            tip = tip.Substring(0, 63);
        _notifyIcon.Text = tip;
    }

    private void SetDynamicTrayIcon(Color statusColor, HealthStatus status)
    {
        try
        {
            var oldIcon = _activeIcon;
            _activeIcon = CreateBadgeIcon(statusColor);
            _notifyIcon.Icon = _activeIcon;
            oldIcon?.Dispose();
        }
        catch
        {
            // Ignore transient icon render faults
        }
    }

    private Icon CreateBadgeIcon(Color statusColor)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            // Draw custom Optical Floating logo
            if (_baseIcon != null)
            {
                g.DrawIcon(_baseIcon, new Rectangle(0, 0, size, size));
            }
            else
            {
                using var bgBrush = new SolidBrush(Color.FromArgb(20, 20, 24));
                g.FillEllipse(bgBrush, 2, 2, size - 4, size - 4);
            }

            // Draw high-contrast status badge indicator in bottom-right corner
            const int dotSize = 10;
            int dotX = size - dotSize - 1;
            int dotY = size - dotSize - 1;

            using (var ringBrush = new SolidBrush(Color.FromArgb(20, 20, 24)))
            {
                g.FillEllipse(ringBrush, dotX - 1, dotY - 1, dotSize + 2, dotSize + 2);
            }

            using (var dotBrush = new SolidBrush(statusColor))
            {
                g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
            }
        }

        var hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        NativeMethods.DestroyIcon(hIcon);
        return icon;
    }

    private void ShowDashboard()
    {
        _dashboard.Show();
        _dashboard.WindowState = FormWindowState.Normal;
        _dashboard.BringToFront();
        NativeMethods.SetForegroundWindow(_dashboard.Handle);
        _dashboard.RefreshDashboard();
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _dashboard.Dispose();
        _activeIcon?.Dispose();
        _baseIcon?.Dispose();
        base.ExitThreadCore();
    }
}
