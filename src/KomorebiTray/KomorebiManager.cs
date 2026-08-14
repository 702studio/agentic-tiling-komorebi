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
    private static readonly string StartScriptPath = Path.Combine(BinPath, "start-komorebi.ps1");
    private static readonly string WmScriptPath = Path.Combine(BinPath, "wm.ps1");
    private static readonly string ValidateScriptPath = Path.Combine(BinPath, "validate-komorebi.ps1");

    public bool AutoRecoverBar { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public int RecoveryCount { get; private set; }
    public DateTime? LastRecoveryTime { get; private set; }

    public event Action<string>? OnBarRecovered;
    public event Action<uint>? OnHighGdiWarning;
    public event Action<WatchdogLogEntry>? OnLogEntry;

    private readonly List<WatchdogLogEntry> _logs = new();
    private readonly object _logLock = new();
    private DateTime _lastRecoveryAttempt = DateTime.MinValue;

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
                Arguments = "query is-paused",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null && proc.WaitForExit(500))
            {
                var output = proc.StandardOutput.ReadToEnd().Trim().ToLowerInvariant();
                return output == "true";
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

        if (!AutoRecoverBar)
            return;

        // Check if Komorebi is alive but bar is missing
        var komorebiAlive = snapshot.Processes.Any(p => p.Name == "komorebi" && p.IsAlive);
        var barAlive = snapshot.Processes.Any(p => p.Name == "komorebi-bar" && p.IsAlive);

        if (komorebiAlive && !barAlive)
        {
            // Debounce recovery attempts to avoid rapid loops if bar config is broken
            if ((DateTime.Now - _lastRecoveryAttempt).TotalSeconds < 3)
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
    }

    private bool RecoverBarInternal()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{StartScriptPath}\" -RestartBar",
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

    public void RestartAll()
    {
        AddLog("INFO", "User requested Full Restart (Nuke & Clean).");
        RunDetachedScript(StartScriptPath, "-Restart -DelayMilliseconds 100");
    }

    public void RestartBarOnly()
    {
        AddLog("INFO", "User requested Bar Restart.");
        RecoverBarInternal();
    }

    public void StopAll()
    {
        AddLog("INFO", "User requested Stop All WM Processes.");
        RunDetachedScript(StartScriptPath, "-StopOnly -DelayMilliseconds 100");
    }

    public void TogglePause()
    {
        AddLog("INFO", "User toggled WM Pause state.");
        RunPowershellCommand("& 'komorebic.exe' toggle-pause");
    }

    public string RunDoctor()
    {
        AddLog("INFO", "Running validate-komorebi.ps1 system doctor...");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{ValidateScriptPath}\"",
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
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        Process.Start(psi);
    }

    private static void RunPowershellCommand(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command \"{command}\"",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        Process.Start(psi);
    }
}
