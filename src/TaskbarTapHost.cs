using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AudioTaskbar;

/// <summary>
/// Host-side launcher for TaskbarTap.dll. Calls the documented
/// InitializeXamlDiagnosticsEx API so Windows loads our TAP into explorer
/// (no CreateRemoteThread). Signals named events for restore / stop.
/// </summary>
internal sealed class TaskbarTapHost : IDisposable
{
    public static readonly Guid TapClsid = new("E8A31B2C-7D4F-4C19-B6A1-9F3E2C8D4A10");

    private const string ReadyName = "Local\\AudioTaskbar.Tap.Ready";
    private const string RestoreName = "Local\\AudioTaskbar.Tap.Restore";
    private const string StopName = "Local\\AudioTaskbar.Tap.Stop";
    private const string StoppedName = "Local\\AudioTaskbar.Tap.Stopped";
    private const string AppliedName = "Local\\AudioTaskbar.Tap.Applied";
    private const string ApplyName = "Local\\AudioTaskbar.Tap.Apply";

    private EventWaitHandle? _ready;
    private EventWaitHandle? _restore;
    private EventWaitHandle? _stop;
    private EventWaitHandle? _stopped;
    private EventWaitHandle? _applied;
    private EventWaitHandle? _apply;
    private int _explorerPid;
    private bool _attached;
    private string _status = "idle";
    private string _dllPath = "";
    private Thread? _diagThread;
    private ManualResetEventSlim? _diagKeepAlive;

    public bool IsAttached => _attached;
    public string Status => _status;
    public int LastExplorerPid => _explorerPid;

    private delegate int InitXamlDiagEx(
        [MarshalAs(UnmanagedType.LPWStr)] string endPointName,
        uint pid,
        [MarshalAs(UnmanagedType.LPWStr)] string? xamlDiagDll,
        [MarshalAs(UnmanagedType.LPWStr)] string tapDll,
        ref Guid tapClsid,
        [MarshalAs(UnmanagedType.LPWStr)] string? initData);

    public string EnsureDllDeployed()
    {
        var destDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioTaskbar");
        Directory.CreateDirectory(destDir);

        string? src = null;
        var asm = Assembly.GetExecutingAssembly();
        using (var s = asm.GetManifestResourceStream("AudioTaskbar.TaskbarTap.dll"))
        {
            if (s is not null)
            {
                var extracted = Path.Combine(destDir, "TaskbarTap-embedded.dll");
                try
                {
                    using var fs = new FileStream(extracted, FileMode.Create, FileAccess.Write, FileShare.Read);
                    s.CopyTo(fs);
                    src = extracted;
                }
                catch (Exception)
                {
                    var alt = Path.Combine(destDir, "TaskbarTap-embedded-" + Guid.NewGuid().ToString("N")[..8] + ".dll");
                    using var fs = new FileStream(alt, FileMode.Create, FileAccess.Write, FileShare.Read);
                    s.CopyTo(fs);
                    src = alt;
                }
            }
        }
        src ??= new[]
        {
            Path.Combine(AppContext.BaseDirectory, "TaskbarTap.dll"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "native", "out", "TaskbarTap.dll")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "native", "TaskbarTap.dll")),
        }.FirstOrDefault(File.Exists);

        if (src is null)
            throw new FileNotFoundException("Không tìm thấy TaskbarTap.dll (đã nhúng trong exe hoặc cạnh ứng dụng).");

        var dest = Path.Combine(destDir, "TaskbarTap.dll");
        if (!TryCopyDll(src, dest))
        {
            dest = Path.Combine(destDir, "TaskbarTap-" + Guid.NewGuid().ToString("N")[..8] + ".dll");
            if (!TryCopyDll(src, dest))
                throw new IOException("Không ghi được TaskbarTap.dll vào " + destDir);
        }
        _dllPath = dest;
        return dest;
    }

    private static bool TryCopyDll(string src, string dest)
    {
        try
        {
            if (File.Exists(dest))
            {
                try
                {
                    if (new FileInfo(dest).Length == new FileInfo(src).Length &&
                        File.GetLastWriteTimeUtc(dest) >= File.GetLastWriteTimeUtc(src))
                        return true;
                }
                catch { /* compare failed, try copy */ }
            }
            File.Copy(src, dest, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static int? FindTaskbarExplorerPid()
    {
        var hwnd = Native.FindWindow("Shell_TrayWnd", null);
        if (hwnd == IntPtr.Zero) return null;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == 0 ? null : (int)pid;
    }

    /// <summary>
    /// Smart App Control (Windows 11 Home/Pro clean installs) enforces a code
    /// integrity policy that blocks unsigned DLLs from loading into explorer.exe.
    /// InitializeXamlDiagnosticsEx still returns S_OK, but our TAP never loads
    /// (CodeIntegrity events 3033/3077) — so we must not wait for it.
    /// </summary>
    public static string? DetectTapBlocker()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy");
            if (k?.GetValue("VerifiedAndReputablePolicyState") is int state && state == 1)
                return "smart-app-control";
        }
        catch { }
        return null;
    }

    /// <summary>Checks the CodeIntegrity log for a recent block of our DLL in explorer.</summary>
    public static bool CodeIntegrityBlockedRecently(TimeSpan window)
    {
        try
        {
            var q = new System.Diagnostics.Eventing.Reader.EventLogQuery(
                "Microsoft-Windows-CodeIntegrity/Operational",
                System.Diagnostics.Eventing.Reader.PathType.LogName,
                $"*[System[(EventID=3033 or EventID=3077) and TimeCreated[timediff(@SystemTime) <= {(long)window.TotalMilliseconds}]]]")
            { ReverseDirection = true };
            using var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(q);
            for (int i = 0; i < 20; i++)
            {
                using var ev = reader.ReadEvent();
                if (ev is null) break;
                string? msg = null;
                try { msg = ev.FormatDescription(); } catch { }
                if (msg is not null && msg.Contains("TaskbarTap", StringComparison.OrdinalIgnoreCase)) return true;
                if (msg is null && ev.Properties.Any(p => (p.Value?.ToString() ?? "").Contains("TaskbarTap", StringComparison.OrdinalIgnoreCase))) return true;
            }
        }
        catch { }
        return false;
    }

    public float ReadyTimeoutSeconds { get; set; } = 3.5f;

    /// <summary>
    /// True when Explorer already holds a TaskbarTap copy (from an earlier app run or an
    /// earlier attach). Loading a second copy into the same Explorer can crash it, so the
    /// host must not inject again until Explorer restarts.
    /// </summary>
    public static bool ExplorerHasTapModule(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            foreach (System.Diagnostics.ProcessModule m in p.Modules)
                if (m.ModuleName.StartsWith("TaskbarTap", StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        catch { }
        return false;
    }

    public bool Attach(Action<string> log)
    {
        DetachInternal(restoreFirst: false, log, fast: true);
        var blocker = DetectTapBlocker();
        if (blocker is not null)
        {
            _status = "blocked-" + blocker;
            log("TAP: skipped — Smart App Control is ON (unsigned TaskbarTap.dll cannot load into explorer). Using UIA icon-safe mode.");
            return false;
        }
        try
        {
            var dll = EnsureDllDeployed();
            log("TAP DLL: " + dll);
            var pid = FindTaskbarExplorerPid();
            if (pid is null)
            {
                _status = "no-taskbar";
                log("TAP: Shell_TrayWnd not found.");
                return false;
            }
            _explorerPid = pid.Value;
            log("TAP: explorer PID " + _explorerPid);

            _ready = CreateOrOpen(ReadyName, out _);
            _restore = CreateOrOpen(RestoreName, out _);
            _stop = CreateOrOpen(StopName, out _);
            _stopped = CreateOrOpen(StoppedName, out _);
            _applied = CreateOrOpen(AppliedName, out _);
            _apply = CreateOrOpen(ApplyName, out _);
            _ready.Reset();
            _restore.Reset();
            _stop.Reset();
            _stopped.Reset();
            _applied.Reset();
            _apply.Reset();

            var wux = NativeMethods.LoadLibraryExW("Windows.UI.Xaml.dll", IntPtr.Zero, 0x00000800 /* SYSTEM32 */);
            if (wux == IntPtr.Zero)
            {
                _status = "no-wux";
                log("TAP: LoadLibrary Windows.UI.Xaml.dll failed " + Marshal.GetLastWin32Error());
                return false;
            }
            var fn = NativeMethods.GetProcAddress(wux, "InitializeXamlDiagnosticsEx");
            if (fn == IntPtr.Zero)
            {
                _status = "no-ixde";
                log("TAP: InitializeXamlDiagnosticsEx not found.");
                return false;
            }
            var init = Marshal.GetDelegateForFunctionPointer<InitXamlDiagEx>(fn);
            _diagKeepAlive?.Dispose();
            _diagKeepAlive = new ManualResetEventSlim(false);
            int hr = unchecked((int)0x80004005);
            string used = "";
            var started = new ManualResetEventSlim(false);
            _diagThread = new Thread(() =>
            {
                Guid clsid = TapClsid;
                for (int n = 1; n <= 12; n++)
                {
                    string name = "VisualDiagConnection" + n;
                    var c = clsid;
                    hr = init(name, (uint)_explorerPid, null, dll, ref c, null);
                    used = name;
                    if (hr >= 0) break;
                }
                started.Set();
                // Keep this STA thread alive for the diagnostics session.
                _diagKeepAlive?.Wait();
            });
            _diagThread.IsBackground = true;
            _diagThread.SetApartmentState(ApartmentState.STA);
            _diagThread.Start();
            try { File.Delete(Path.Combine(Path.GetDirectoryName(dll)!, "tap-status.txt")); } catch { }
            if (!started.Wait(5000))
            {
                _status = "ixde-thread-timeout";
                log("TAP: diagnostics thread did not finish InitializeXamlDiagnosticsEx in time.");
                _diagKeepAlive.Set();
                return false;
            }
            log($"TAP: {used} hr=0x{hr:X8}");

            if (hr < 0)
            {
                _status = $"ixde-failed-0x{hr:X8}";
                log("TAP: InitializeXamlDiagnosticsEx failed, falling back.");
                _diagKeepAlive.Set();
                return false;
            }

            var sw = Stopwatch.StartNew();
            bool ready = _ready.WaitOne(TimeSpan.FromSeconds(Math.Clamp(ReadyTimeoutSeconds, 1.5f, 8f)));
            log(ready ? $"TAP: Ready event signaled ({used}) in {sw.ElapsedMilliseconds} ms." : $"TAP: no Ready after {sw.ElapsedMilliseconds} ms.");
            if (!ready)
            {
                bool loaded = File.Exists(Path.Combine(Path.GetDirectoryName(dll)!, "tap-status.txt"));
                if (CodeIntegrityBlockedRecently(TimeSpan.FromMinutes(2)))
                {
                    _status = "blocked-code-integrity";
                    log("TAP: Windows Code Integrity blocked TaskbarTap.dll in explorer (event 3033/3077) — permanent for this session.");
                }
                else _status = loaded ? "ready-timeout" : "dll-not-loaded";
                // If the DLL never loaded there is nothing to restore: tear down instantly.
                DetachInternal(restoreFirst: loaded, log, fast: !loaded);
                return false;
            }

            // Give the visual tree walk a moment to actually patch fills.
            _applied.WaitOne(TimeSpan.FromSeconds(2));
            Thread.Sleep(250);
            var (state, patched, error) = ReadStatus();
            log($"TAP status: state={state} patched={patched} error={error}");
            if (patched <= 0)
            {
                // Advise succeeded but no BackgroundFill seen yet — retry a short wait.
                _applied.WaitOne(TimeSpan.FromSeconds(2));
                (state, patched, error) = ReadStatus();
                log($"TAP status (retry): state={state} patched={patched} error={error}");
            }

            if (patched <= 0)
            {
                _status = "advised-but-unpatched";
                log("TAP: no BackgroundFill/Stroke patched; treating as unsuccessful.");
                DetachInternal(restoreFirst: true, log);
                return false;
            }

            _attached = true;
            _status = "attached-patched-" + patched;
            return true;
        }
        catch (Exception ex)
        {
            _status = "exception";
            log("TAP attach exception: " + ex.Message);
            return false;
        }
    }

    public void RequestRestore(Action<string> log)
    {
        try
        {
            _restore?.Set();
            Thread.Sleep(300);
            log("TAP: restore signaled.");
        }
        catch (Exception ex) { log("TAP restore: " + ex.Message); }
    }

    public void RequestApply(Action<string> log)
    {
        try
        {
            _apply?.Set();
            Thread.Sleep(250);
            log("TAP: reapply signaled.");
        }
        catch (Exception ex) { log("TAP reapply: " + ex.Message); }
    }

    public void Detach(Action<string> log) => DetachInternal(restoreFirst: true, log);

    private void DetachInternal(bool restoreFirst, Action<string> log, bool fast = false)
    {
        try
        {
            if (fast)
            {
                _stop?.Set();
                _diagKeepAlive?.Set();
            }
            else
            {
                if (restoreFirst) RequestRestore(log);
                Thread.Sleep(400); // let Explorer paint the restored fill before we unadvise
                _stop?.Set();
                _stopped?.WaitOne(TimeSpan.FromSeconds(2.5));
                Thread.Sleep(150);
                _diagKeepAlive?.Set();
            }
        }
        catch (Exception ex) { log("TAP detach: " + ex.Message); }
        finally
        {
            _attached = false;
            _ready?.Dispose(); _ready = null;
            _restore?.Dispose(); _restore = null;
            _stop?.Dispose(); _stop = null;
            _stopped?.Dispose(); _stopped = null;
            _applied?.Dispose(); _applied = null;
            _apply?.Dispose(); _apply = null;
        }
    }

    public bool ExplorerStillAlive()
    {
        // A visible Shell_TrayWnd hosted by the PID we attached to is definitive.
        int? trayPid = FindTaskbarExplorerPid();
        if (trayPid == _explorerPid && _explorerPid != 0)
            return true;
        if (_explorerPid == 0)
            return trayPid is not null;

        try
        {
            var p = Process.GetProcessById(_explorerPid);
            try { return !p.HasExited; }
            catch (System.ComponentModel.Win32Exception)
            {
                // HasExited can throw "Access is denied" on a still-running
                // explorer. That is NOT proof of death — only kill/restart
                // if the taskbar window is also gone.
                return trayPid is not null;
            }
        }
        catch (ArgumentException)
        {
            // That PID no longer exists.
            return false;
        }
        catch
        {
            return trayPid is not null;
        }
    }

    public static void RestartExplorer(Action<string> log)
    {
        log("Restarting explorer.exe…");
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); } catch { }
            }
            Thread.Sleep(800);
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            Thread.Sleep(1500);
            log("explorer.exe relaunched.");
        }
        catch (Exception ex) { log("Restart explorer failed: " + ex.Message); }
    }

    public static (string state, int patched, string error) ReadStatus()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AudioTaskbar", "tap-status.txt");
            if (!File.Exists(path)) return ("missing", 0, "");
            string state = "", error = "";
            int patched = 0;
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('=', 2);
                if (parts.Length != 2) continue;
                if (parts[0] == "state") state = parts[1].Trim();
                if (parts[0] == "patched") int.TryParse(parts[1].Trim(), out patched);
                if (parts[0] == "error") error = parts[1].Trim();
            }
            return (state, patched, error);
        }
        catch { return ("unread", 0, ""); }
    }

    private static EventWaitHandle CreateOrOpen(string name, out bool created)
    {
        if (EventWaitHandle.TryOpenExisting(name, out var ev))
        {
            created = false;
            return ev;
        }
        created = true;
        return new EventWaitHandle(false, EventResetMode.ManualReset, name);
    }

    public void Dispose() => DetachInternal(restoreFirst: true, _ => { });

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibraryExW(string lpFileName, IntPtr hFile, uint dwFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
    }
}
