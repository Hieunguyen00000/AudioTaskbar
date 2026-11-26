namespace AudioTaskbar;

internal static class Program
{
    private const string MutexName = "Local\\Hieunv1.AudioTaskbar.Singleton";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out bool created);
        if (!created)
        {
            // Already running — nothing to do.
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => LogCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) LogCrash(ex);
        };

        try
        {
            Application.Run(new TrayApp());
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            try
            {
                MessageBox.Show(
                    "Audio Taskbar gặp lỗi và đã dừng.\n\n" + ex.Message +
                    "\n\nXem log: %LOCALAPPDATA%\\AudioTaskbar\\app.log",
                    "Audio Taskbar",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { /* headless */ }
        }
        finally
        {
            GC.KeepAlive(mutex);
        }
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioTaskbar");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "app.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  CRASH: " + ex + Environment.NewLine);
        }
        catch { }
    }
}
