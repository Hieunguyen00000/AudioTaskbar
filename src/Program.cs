namespace AudioTaskbar;

internal static class Program
{
    private const string MutexName = "Local\\Hieunv1.AudioTaskbar.Singleton";
    /// <summary>v5.3: set by a second launch so the running instance brings its portal forward.</summary>
    public const string ShowPortalEventName = "Local\\AudioTaskbar.Cmd.ShowPortal";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--render")
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            RenderPreview.Run(args);
            return;
        }
        using var mutex = new Mutex(true, MutexName, out bool created);
        bool minimizedArg = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) || a.Equals("/minimized", StringComparison.OrdinalIgnoreCase));
        bool updatedArg = args.Any(a => a.Equals("--updated", StringComparison.OrdinalIgnoreCase));
        if (!created)
        {
            // Already running (UniKey-style): a manual launch brings the portal to the front;
            // autostart / updater relaunches (--minimized) stay silent.
            if (!minimizedArg)
            {
                try
                {
                    using var ev = EventWaitHandle.OpenExisting(ShowPortalEventName);
                    ev.Set();
                }
                catch { /* older instance without a portal */ }
            }
            return;
        }

        // Per-Monitor V2 before any window exists: UIA rects, GetWindowRect and our
        // layered windows then all speak the same physical pixels at 100/125/150 %.
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
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
            bool startMinimized = minimizedArg;
            if (!startMinimized)
            {
                try { startMinimized = ConfigStore.Load(ConfigStore.LocalConfigPath()).StartMinimized && File.Exists(ConfigStore.LocalConfigPath()); }
                catch { }
            }
            Application.Run(new TrayApp(startMinimized, updatedArg));
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

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
