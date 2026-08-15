using System.Diagnostics;

namespace KomorebiTray;

public class KomorebiManager
{
    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string BinPath = Path.Combine(UserProfile, "bin");
    private static readonly string ConfigHome = Path.Combine(UserProfile, ".config", "komorebi");
    private static readonly string WgpuBarPath = Path.Combine(BinPath, "komorebi-wgpu-v0.1.41", "komorebi-bar.exe");
    private static readonly string SwitzerBarConfig = Path.Combine(ConfigHome, "komorebi.bar.json");
    private static readonly string JetBrainsBarConfig = Path.Combine(ConfigHome, "komorebi.bar.jetbrains.json");

    public static string ResolveStartScriptPath()
    {
        var localProg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "KomorebiStarter", "start.ps1");
        if (File.Exists(localProg)) return localProg;

        var binStart = Path.Combine(BinPath, "start.ps1");
        if (File.Exists(binStart)) return binStart;

        var binStartKomorebi = Path.Combine(BinPath, "start-komorebi.ps1");
        if (File.Exists(binStartKomorebi)) return binStartKomorebi;

        return Path.Combine(BinPath, "start.ps1");
    }

    public static string ResolveDoctorScriptPath()
    {
        var localProg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "KomorebiStarter", "doctor.ps1");
        if (File.Exists(localProg)) return localProg;

        var binDoctor = Path.Combine(BinPath, "doctor.ps1");
        if (File.Exists(binDoctor)) return binDoctor;

        var binValidate = Path.Combine(BinPath, "validate-komorebi.ps1");
        if (File.Exists(binValidate)) return binValidate;

        return Path.Combine(BinPath, "doctor.ps1");
    }

    public static string ResolveWmScriptPath()
    {
        var localProg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "KomorebiStarter", "wm.ps1");
        if (File.Exists(localProg)) return localProg;

        var binWm = Path.Combine(BinPath, "wm.ps1");
        if (File.Exists(binWm)) return binWm;

        return Path.Combine(BinPath, "wm.ps1");
    }

    public bool AutoRecoverBar { get; set; } = true;
    public bool AutoRecoverStack { get; set; } = true;
    public bool AutoRecoverWhkd { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool IsManuallyStopped { get; set; } = false;
    public int RecoveryCount { get; private set; }
    public DateTime? LastRecoveryTime { get; private set; }

    public event Action<string>? OnBarRecovered;
    public event Action<string>? OnStackRecovered;
    public event Action<uint>? OnHighGdiWarning;
    public event Action<WatchdogLogEntry>? OnLogEntry;

    private readonly List<WatchdogLogEntry> _logs = new();
    private readonly object _logLock = new();
    private DateTime _lastRecoveryAttempt = DateTime.MinValue;
    private DateTime _lastStackRecoveryAttempt = DateTime.MinValue;
    private DateTime _lastWhkdRecoveryAttempt = DateTime.MinValue;
    private bool _isStarting = false;
    private DateTime _startInitiated = DateTime.MinValue;

    public IReadOnlyList<WatchdogLogEntry> GetRecentLogs()
    {
        lock (_logLock)
        {
            return _logs.TakeLast(100).ToList();
        }
    }

    public void AddLog(string level, string message)
    {
        var entry = new WatchdogLogEntry
        {
            Time = DateTime.Now,
            Level = level,
            Message = message
        };
        lock (_logLock)
        {
            _logs.Add(entry);
            if (_logs.Count > 300)
            {
                _logs.RemoveAt(0);
            }
        }
        OnLogEntry?.Invoke(entry);
    }

    public string GetBarConfigPath()
    {
        if (File.Exists(SwitzerBarConfig))
            return SwitzerBarConfig;
        if (File.Exists(JetBrainsBarConfig))
            return JetBrainsBarConfig;
        return SwitzerBarConfig;
    }

    public WmHealthSnapshot GetHealthSnapshot()
    {
        var snapshot = new WmHealthSnapshot
        {
            RecoveryCount = RecoveryCount,
            LastRecoveryTime = LastRecoveryTime,
            Timestamp = DateTime.Now
        };

        var targetNames = new[] { "komorebi", "komorebi-bar", "whkd", "masir" };
        var processMap = new Dictionary<string, Process?>();

        foreach (var name in targetNames)
        {
            var procs = Process.GetProcessesByName(name);
            var proc = procs.FirstOrDefault();
            processMap[name] = proc;

            var pInfo = new WmProcessInfo
            {
                Name = name,
                IsAlive = proc != null && !proc.HasExited
            };

            if (proc != null && !proc.HasExited)
            {
                try
                {
                    pInfo.Pid = proc.Id;
                    pInfo.WorkingSetMb = Math.Round(proc.WorkingSet64 / (1024.0 * 1024.0), 1);
                    pInfo.GdiHandles = NativeMethods.GetGuiResources(proc.Handle, NativeMethods.GR_GDIOBJECTS);
                }
                catch
                {
                    // Ignore transient access errors
                }
            }

            snapshot.Processes.Add(pInfo);
        }

        var komorebiProc = processMap["komorebi"];
        var barProc = processMap["komorebi-bar"];
        var whkdProc = processMap["whkd"];

        if (komorebiProc != null && !komorebiProc.HasExited)
        {
            try
            {
                snapshot.KomorebiGdiHandles = NativeMethods.GetGuiResources(komorebiProc.Handle, NativeMethods.GR_GDIOBJECTS);
            }
            catch { }

            // Check if paused
            snapshot.IsPaused = CheckIfKomorebiPaused();

            if (snapshot.IsPaused)
            {
                snapshot.Status = HealthStatus.Paused;
                snapshot.StatusText = "Paused (Tiling Disabled)";
            }
            else if (barProc != null && !barProc.HasExited && whkdProc != null && !whkdProc.HasExited)
            {
                snapshot.Status = HealthStatus.Healthy;
                snapshot.StatusText = "Healthy (All Systems Active)";
            }
            else if (barProc == null || barProc.HasExited)
            {
                snapshot.Status = HealthStatus.Recovering;
                snapshot.StatusText = "Bar Missing (Watchdog Active)";
            }
            else
            {
                snapshot.Status = HealthStatus.Recovering;
                snapshot.StatusText = "Degraded (Helper Missing)";
            }
        }
        else
        {
            snapshot.Status = HealthStatus.Stopped;
            snapshot.StatusText = "Stopped (Komorebi Offline)";
        }

        return snapshot;
    }

    private bool CheckIfKomorebiPaused()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "komorebic.exe",
                Arguments = "state",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null && proc.WaitForExit(600))
            {
                var output = proc.StandardOutput.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(output))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(output);
                    if (doc.RootElement.TryGetProperty("is_paused", out var prop))
                    {
                        return prop.GetBoolean();
                    }
                }
            }
        }
        catch { }
        return false;
    }

    public void WatchdogTick()
    {
        var snapshot = GetHealthSnapshot();

        // GDI Handle Warning
        if (snapshot.KomorebiGdiHandles > 7500)
        {
            AddLog("WARN", $"Komorebi GDI handle count high: {snapshot.KomorebiGdiHandles}/10000");
            OnHighGdiWarning?.Invoke(snapshot.KomorebiGdiHandles);
        }

        if (IsManuallyStopped)
            return;

        var komorebiAlive = snapshot.Processes.Any(p => p.Name == "komorebi" && p.IsAlive);
        var whkdAlive = snapshot.Processes.Any(p => p.Name == "whkd" && p.IsAlive);
        var barAlive = snapshot.Processes.Any(p => p.Name == "komorebi-bar" && p.IsAlive);

        if (komorebiAlive && whkdAlive && barAlive)
        {
            _isStarting = false;
        }

        if (_isStarting)
        {
            if ((DateTime.Now - _startInitiated).TotalSeconds > 15)
            {
                _isStarting = false;
            }
            else
            {
                return;
            }
        }

        // Case 1: Komorebi engine is offline -> Auto-resurrect entire stack
        if (!komorebiAlive && AutoRecoverStack)
        {
            if ((DateTime.Now - _lastStackRecoveryAttempt).TotalSeconds < 10)
                return;

            _lastStackRecoveryAttempt = DateTime.Now;
            AddLog("WARN", "Watchdog: komorebi process is down! Auto-restarting entire WM stack...");
            StartAll();
            RecoveryCount++;
            LastRecoveryTime = DateTime.Now;
            OnStackRecovered?.Invoke($"Komorebi stack automatically resurrected by Watchdog. Total recoveries: {RecoveryCount}");
            return;
        }

        // Case 2: Komorebi is alive but bar is missing -> Auto-resurrect bar
        if (komorebiAlive && !barAlive && AutoRecoverBar)
        {
            if ((DateTime.Now - _lastRecoveryAttempt).TotalSeconds < 5)
                return;

            _lastRecoveryAttempt = DateTime.Now;
            AddLog("WARN", "Watchdog: komorebi-bar is missing while komorebi is active. Initiating instant recovery...");
            
            bool recovered = RecoverBarInternal();
            if (recovered)
            {
                RecoveryCount++;
                LastRecoveryTime = DateTime.Now;
                AddLog("INFO", $"Watchdog: komorebi-bar successfully resurrected (Recovery #{RecoveryCount}).");
                OnBarRecovered?.Invoke($"Komorebi Bar recovered automatically (GPU reset/crash detected). Total recoveries: {RecoveryCount}");
            }
            else
            {
                AddLog("ERROR", "Watchdog: Failed to resurrect komorebi-bar.");
            }
        }

        // Case 3: Komorebi is alive but whkd (hotkeys) is missing -> Auto-resurrect whkd
        if (komorebiAlive && !whkdAlive && AutoRecoverWhkd)
        {
            if ((DateTime.Now - _lastWhkdRecoveryAttempt).TotalSeconds < 5)
                return;

            _lastWhkdRecoveryAttempt = DateTime.Now;
            AddLog("WARN", "Watchdog: whkd process is missing while komorebi is active. Restarting whkd...");
            bool recovered = RecoverWhkdInternal();
            if (recovered)
            {
                RecoveryCount++;
                LastRecoveryTime = DateTime.Now;
                AddLog("INFO", $"Watchdog: whkd successfully resurrected (Recovery #{RecoveryCount}).");
                OnStackRecovered?.Invoke($"Komorebi Hotkey Daemon (whkd) recovered automatically. Total recoveries: {RecoveryCount}");
            }
            else
            {
                AddLog("ERROR", "Watchdog: Failed to resurrect whkd.");
            }
        }
    }

    public void EnsureEnvironmentStarted()
    {
        var snapshot = GetHealthSnapshot();
        var komorebiAlive = snapshot.Processes.Any(p => p.Name == "komorebi" && p.IsAlive);
        var whkdAlive = snapshot.Processes.Any(p => p.Name == "whkd" && p.IsAlive);
        var barAlive = snapshot.Processes.Any(p => p.Name == "komorebi-bar" && p.IsAlive);

        if (!komorebiAlive || !whkdAlive || !barAlive)
        {
            AddLog("INFO", "WM environment not fully running on startup. Auto-launching stack...");
            StartAll();
        }
    }

    public void StartAll()
    {
        IsManuallyStopped = false;
        _isStarting = true;
        _startInitiated = DateTime.Now;
        AddLog("INFO", "Starting Komorebi Window Manager environment...");
        RunDetachedScript(ResolveStartScriptPath(), "-Restart -DelayMilliseconds 100");
    }

    private bool RecoverBarInternal()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{ResolveStartScriptPath()}\" -RestartBar",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit(4000);
                return proc.ExitCode == 0;
            }
            return false;
        }
        catch (Exception ex)
        {
            AddLog("ERROR", $"Recovery exception: {ex.Message}");
            return false;
        }
    }

    private bool RecoverWhkdInternal()
    {
        try
        {
            var configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "komorebi");
            var whkdrc = Path.Combine(configHome, "whkdrc");
            var whkdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "whkd", "bin", "whkd.exe");
            if (!File.Exists(whkdExe))
            {
                whkdExe = "whkd.exe";
            }

            var psi = new ProcessStartInfo
            {
                FileName = whkdExe,
                Arguments = File.Exists(whkdrc) ? $"--config \"{whkdrc}\"" : string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.Environment["WHKD_CONFIG_HOME"] = configHome;
            var proc = Process.Start(psi);
            return proc != null && !proc.HasExited;
        }
        catch (Exception ex)
        {
            AddLog("ERROR", $"Failed to restart whkd: {ex.Message}");
            return false;
        }
    }

    public void RestartAll()
    {
        IsManuallyStopped = false;
        _isStarting = true;
        _startInitiated = DateTime.Now;
        AddLog("INFO", "User requested Full Restart (Nuke & Clean).");
        RunDetachedScript(ResolveStartScriptPath(), "-Restart -DelayMilliseconds 100");
    }

    public void RestartBarOnly()
    {
        AddLog("INFO", "User requested Bar Restart.");
        RecoverBarInternal();
    }

    public void StopAll()
    {
        IsManuallyStopped = true;
        AddLog("INFO", "User requested Stop All WM Processes.");
        RunDetachedScript(ResolveStartScriptPath(), "-StopOnly -DelayMilliseconds 100");
    }

    public void TogglePause()
    {
        AddLog("INFO", "User toggled WM Pause state.");
        RunPowershellCommand("& 'komorebic.exe' toggle-pause");
    }

    public void ApplyPreset(string presetName)
    {
        AddLog("INFO", $"Applying workspace preset: {presetName}");
        RunPowershellCommand($"& '{ResolveWmScriptPath()}' preset {presetName}");
    }

    public void LaunchUpdate()
    {
        AddLog("INFO", "Launching agentic-tiling-komorebi update...");
        var updateScript = Path.Combine(BinPath, "komorebi-update.ps1");
        if (!File.Exists(updateScript))
        {
            updateScript = Path.Combine(UserProfile, ".config", "komorebi", "komorebi-update.ps1");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"& '{updateScript}'; Write-Host 'Press Enter to close...'; [void][Console]::ReadLine()\"",
            UseShellExecute = true,
            CreateNoWindow = false
        };
        Process.Start(psi);
    }

    public string RunDoctor()
    {
        AddLog("INFO", "Running system doctor check...");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{ResolveDoctorScriptPath()}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null && proc.WaitForExit(10000))
            {
                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                return string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stdout}\nErrors:\n{stderr}";
            }
            return "Validation timed out after 10 seconds.";
        }
        catch (Exception ex)
        {
            return $"Error running doctor: {ex.Message}";
        }
    }

    public void OpenConfigFile(string relativeOrAbsolute)
    {
        try
        {
            var fullPath = Path.IsPathRooted(relativeOrAbsolute) ? relativeOrAbsolute : Path.Combine(ConfigHome, relativeOrAbsolute);
            if (File.Exists(fullPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            AddLog("ERROR", $"Cannot open config: {ex.Message}");
        }
    }

    private static void RunDetachedScript(string scriptPath, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"RunDetachedScript failed: {ex.Message}");
        }
    }

    private static void RunPowershellCommand(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command \"{command}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"RunPowershellCommand failed: {ex.Message}");
        }
    }
}
