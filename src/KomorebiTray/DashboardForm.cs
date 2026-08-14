using System.Drawing;
using System.Windows.Forms;

namespace KomorebiTray;

public class DashboardForm : Form
{
    private readonly KomorebiManager _manager;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    private Label _lblStatusTitle = null!;
    private Label _lblStatusDetail = null!;
    private Label _lblGdiMeter = null!;
    private Label _lblWatchdogStats = null!;
    private ListView _lvProcesses = null!;
    private TextBox _txtLogs = null!;
    private Button _btnRestartAll = null!;
    private Button _btnRestartBar = null!;
    private Button _btnTogglePause = null!;
    private Button _btnDoctor = null!;
    private Button _btnOpenConfig = null!;

    public DashboardForm(KomorebiManager manager)
    {
        _manager = manager;

        InitializeComponentsCustom();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += (s, e) => RefreshDashboard();

        Load += (s, e) =>
        {
            RefreshDashboard();
            _refreshTimer.Start();
        };

        FormClosing += (s, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                _refreshTimer.Stop();
            }
        };
    }

    private void InitializeComponentsCustom()
    {
        Text = "Komorebi Hub — Process & Health Monitor";
        Size = new Size(720, 560);
        MinimumSize = new Size(680, 500);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(24, 24, 26);
        ForeColor = Color.FromArgb(235, 235, 235);
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        DoubleBuffered = true;

        // Top Status Panel
        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 110,
            BackColor = Color.FromArgb(32, 32, 35),
            Padding = new Padding(16, 12, 16, 12)
        };

        _lblStatusTitle = new Label
        {
            Text = "● Checking System Status...",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 215, 255),
            AutoSize = true,
            Location = new Point(16, 12)
        };

        _lblStatusDetail = new Label
        {
            Text = "Hardware: NVIDIA GeForce GTX 980 Ti + Parsec Virtual Display",
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(160, 160, 165),
            AutoSize = true,
            Location = new Point(18, 42)
        };

        _lblGdiMeter = new Label
        {
            Text = "GDI Handles: 0 / 10000",
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(220, 220, 220),
            AutoSize = true,
            Location = new Point(18, 68)
        };

        _lblWatchdogStats = new Label
        {
            Text = "Watchdog: Active (0 recoveries)",
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 210, 140),
            AutoSize = true,
            Location = new Point(280, 68)
        };

        topPanel.Controls.Add(_lblStatusTitle);
        topPanel.Controls.Add(_lblStatusDetail);
        topPanel.Controls.Add(_lblGdiMeter);
        topPanel.Controls.Add(_lblWatchdogStats);

        // Action Buttons Bar
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            BackColor = Color.FromArgb(28, 28, 30),
            Padding = new Padding(12, 10, 12, 10)
        };

        _btnRestartAll = CreateDarkButton("⚡ Quick Restart", 125, Color.FromArgb(0, 130, 200));
        _btnRestartAll.Click += (s, e) => _manager.RestartAll();

        _btnRestartBar = CreateDarkButton("🔄 Restart Bar", 115, Color.FromArgb(50, 120, 180));
        _btnRestartBar.Click += (s, e) => _manager.RestartBarOnly();

        _btnTogglePause = CreateDarkButton("⏸️ Pause/Resume", 125, Color.FromArgb(70, 70, 80));
        _btnTogglePause.Click += (s, e) => _manager.TogglePause();

        _btnDoctor = CreateDarkButton("🩺 Run Doctor", 115, Color.FromArgb(50, 140, 90));
        _btnDoctor.Click += (s, e) => ShowDoctorResults();

        _btnOpenConfig = CreateDarkButton("⚙️ Configs", 95, Color.FromArgb(60, 60, 65));
        _btnOpenConfig.Click += (s, e) => _manager.OpenConfigFile("komorebi.json");

        var flowButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        flowButtons.Controls.AddRange(new Control[] { _btnRestartAll, _btnRestartBar, _btnTogglePause, _btnDoctor, _btnOpenConfig });
        buttonPanel.Controls.Add(flowButtons);

        // Center Content: Process List + Logs
        var centerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 160,
            BackColor = Color.FromArgb(40, 40, 44),
            Panel1MinSize = 120,
            Panel2MinSize = 100
        };

        // Processes ListView
        _lvProcesses = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = Color.FromArgb(22, 22, 24),
            ForeColor = Color.FromArgb(230, 230, 230),
            BorderStyle = BorderStyle.None,
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        _lvProcesses.Columns.Add("Process", 140);
        _lvProcesses.Columns.Add("Status", 100);
        _lvProcesses.Columns.Add("PID", 75);
        _lvProcesses.Columns.Add("RAM (MB)", 90);
        _lvProcesses.Columns.Add("GDI Handles", 95);
        _lvProcesses.Columns.Add("Details", 180);

        centerSplit.Panel1.Controls.Add(_lvProcesses);

        // Logs Panel
        var logContainer = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 18, 20), Padding = new Padding(8) };
        var lblLogHeader = new Label
        {
            Text = "Watchdog & Lifecycle Event History:",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = Color.FromArgb(150, 150, 155),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
        };
        _txtLogs = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(14, 14, 16),
            ForeColor = Color.FromArgb(180, 215, 255),
            Font = new Font("Cascadia Code", 8.5f),
            BorderStyle = BorderStyle.None
        };
        logContainer.Controls.Add(_txtLogs);
        logContainer.Controls.Add(lblLogHeader);

        centerSplit.Panel2.Controls.Add(logContainer);

        Controls.Add(centerSplit);
        Controls.Add(buttonPanel);
        Controls.Add(topPanel);
    }

    private static Button CreateDarkButton(string text, int width, Color bg)
    {
        var btn = new Button
        {
            Text = text,
            Width = width,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 8, 0)
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    public void RefreshDashboard()
    {
        if (!Visible)
            return;

        var snapshot = _manager.GetHealthSnapshot();

        // Update Top Banner
        switch (snapshot.Status)
        {
            case HealthStatus.Healthy:
                _lblStatusTitle.Text = "🟢 Komorebi: Healthy (All Systems Active)";
                _lblStatusTitle.ForeColor = Color.FromArgb(80, 230, 140);
                break;
            case HealthStatus.Recovering:
                _lblStatusTitle.Text = "🟡 Komorebi: Recovering / Bar Missing";
                _lblStatusTitle.ForeColor = Color.FromArgb(255, 205, 80);
                break;
            case HealthStatus.Paused:
                _lblStatusTitle.Text = "⏸️ Komorebi: Paused (Tiling Disabled)";
                _lblStatusTitle.ForeColor = Color.FromArgb(0, 215, 255);
                break;
            case HealthStatus.Stopped:
                _lblStatusTitle.Text = "🔴 Komorebi: Offline / Stopped";
                _lblStatusTitle.ForeColor = Color.FromArgb(255, 95, 95);
                break;
        }

        _lblGdiMeter.Text = $"GDI Handles: {snapshot.KomorebiGdiHandles} / 10000";
        _lblGdiMeter.ForeColor = snapshot.KomorebiGdiHandles > 7000 ? Color.FromArgb(255, 120, 100) : Color.FromArgb(220, 220, 220);

        _lblWatchdogStats.Text = $"Watchdog: {(_manager.AutoRecoverBar ? "Active" : "Disabled")} ({_manager.RecoveryCount} auto-recoveries)";

        // Update Process Grid
        _lvProcesses.BeginUpdate();
        _lvProcesses.Items.Clear();

        foreach (var p in snapshot.Processes)
        {
            var item = new ListViewItem(p.Name);
            item.SubItems.Add(p.IsAlive ? "● Running" : "○ Stopped");
            item.SubItems.Add(p.IsAlive ? p.Pid.ToString() : "-");
            item.SubItems.Add(p.IsAlive ? $"{p.WorkingSetMb} MB" : "-");
            item.SubItems.Add(p.IsAlive ? p.GdiHandles.ToString() : "-");

            var detail = p.Name switch
            {
                "komorebi" => "Core Tiling WM (Win32 Hooks)",
                "komorebi-bar" => "DX12 / WGPU Custom Status Bar",
                "whkd" => "Hotkeys Daemon (whkdrc)",
                "masir" => "Window Switcher Helper",
                _ => ""
            };
            item.SubItems.Add(detail);

            item.ForeColor = p.IsAlive ? Color.FromArgb(220, 220, 220) : Color.FromArgb(140, 140, 140);
            _lvProcesses.Items.Add(item);
        }
        _lvProcesses.EndUpdate();

        // Update Logs
        var logs = _manager.GetRecentLogs();
        var logLines = logs.Select(l => $"[{l.Time:HH:mm:ss}] [{l.Level}] {l.Message}").ToList();
        _txtLogs.Text = string.Join(Environment.NewLine, logLines);
        _txtLogs.SelectionStart = _txtLogs.Text.Length;
        _txtLogs.ScrollToCaret();
    }

    private void ShowDoctorResults()
    {
        _btnDoctor.Enabled = false;
        _btnDoctor.Text = "Running...";
        
        Task.Run(() =>
        {
            var output = _manager.RunDoctor();
            Invoke(() =>
            {
                _btnDoctor.Enabled = true;
                _btnDoctor.Text = "🩺 Run Doctor";
                MessageBox.Show(this, output, "Komorebi Doctor Results", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        });
    }
}
