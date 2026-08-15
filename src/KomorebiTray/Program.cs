using System.Threading;
using System.Windows.Forms;
using System.IO;

namespace KomorebiTray;

internal static class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    private static void Main(string[] args)
    {
        string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "komorebi", "tray.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        }
        catch { }

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:O}] UnhandledException: {e.ExceptionObject}\n"); } catch { }
        };

        Application.ThreadException += (s, e) =>
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:O}] ThreadException: {e.Exception}\n"); } catch { }
        };

        bool createdNew = false;
        try
        {
            _singleInstanceMutex = new Mutex(true, "KomorebiStarter.TrayHub.Singleton", out createdNew);
        }
        catch (AbandonedMutexException)
        {
            createdNew = true;
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:O}] Mutex exception: {ex.Message}\n"); } catch { }
            createdNew = true;
        }

        if (!createdNew)
        {
            try
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:O}] Another instance is already running; activating existing instance.\n");
                using var showEvent = EventWaitHandle.OpenExisting("KomorebiStarter.TrayHub.ShowEvent");
                showEvent.Set();
            }
            catch { }
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        try
        {
            Application.Run(new TrayApplicationContext());
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:O}] Application.Run crashed: {ex}\n"); } catch { }
        }
        finally
        {
            if (_singleInstanceMutex != null && createdNew)
            {
                try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                _singleInstanceMutex.Dispose();
            }
        }
    }
}