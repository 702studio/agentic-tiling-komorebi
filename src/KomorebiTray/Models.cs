namespace KomorebiTray;

public enum HealthStatus
{
    Healthy,     // Green
    Recovering,  // Amber/Yellow
    Paused,      // Cyan/Blue
    Stopped      // Red
}

public class WmProcessInfo
{
    public string Name { get; set; } = string.Empty;
    public int Pid { get; set; }
    public double WorkingSetMb { get; set; }
    public uint GdiHandles { get; set; }
    public bool IsAlive { get; set; }
    public string Path { get; set; } = string.Empty;
}

public class WmHealthSnapshot
{
    public HealthStatus Status { get; set; } = HealthStatus.Stopped;
    public string StatusText { get; set; } = "Stopped";
    public List<WmProcessInfo> Processes { get; set; } = new();
    public uint KomorebiGdiHandles { get; set; }
    public bool IsPaused { get; set; }
    public string FocusedWorkspace { get; set; } = "1";
    public string Layout { get; set; } = "Grid";
    public int RecoveryCount { get; set; }
    public DateTime? LastRecoveryTime { get; set; }
    public string GpuName { get; set; } = "NVIDIA GeForce GTX 980 Ti";
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class WatchdogLogEntry
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string Level { get; set; } = "INFO";
    public string Message { get; set; } = string.Empty;
}
