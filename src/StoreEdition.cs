using System.Runtime.InteropServices;
using System.Text;

namespace AudioTaskbar;

/// <summary>
/// Detects the Microsoft Store / MSIX edition and isolates WinRT calls
/// (StartupTask, ApplicationData, activation kind) behind <c>STORE_BUILD</c>
/// so the portable/NSIS build stays on <c>net8.0-windows</c> with identical output.
/// A runtime package-identity check still disables GitHub self-update if a
/// non-Store build is ever launched with package identity.
/// </summary>
internal static class StoreEdition
{
    public const string StartupTaskId = "AudioTaskbarStartup";
    public const string StoreUpdateMessage = "Cập nhật tự động qua Microsoft Store";

    /// <summary>Compile-time Store binary, or a process that has MSIX package identity.</summary>
    public static bool IsStorePackage
    {
        get
        {
#if STORE_BUILD
            return true;
#else
            return HasPackageIdentity();
#endif
        }
    }

    /// <summary>Store installs must not check or download GitHub Releases.</summary>
    public static bool UsesStoreUpdates => IsStorePackage;

    public static bool HasPackageIdentity()
    {
        try
        {
            int length = 0;
            int rc = GetCurrentPackageFullName(ref length, null);
            if (rc == AppModelErrorNoPackage) return false;
            if (rc == ErrorInsufficientBuffer && length > 1)
            {
                var sb = new StringBuilder(length);
                rc = GetCurrentPackageFullName(ref length, sb);
                return rc == 0 && sb.Length > 0;
            }
            return rc == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool LaunchedByStartupTask()
    {
#if STORE_BUILD
        try
        {
            var args = Windows.ApplicationModel.AppInstance.GetActivatedEventArgs();
            return args is not null &&
                   args.Kind == Windows.ApplicationModel.Activation.ActivationKind.StartupTask;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>
    /// Folder used to stage a bit-identical copy of TaskbarTap.dll for Explorer.
    /// Prefers the package TempState folder (Explorer cannot load from WindowsApps).
    /// </summary>
    public static string TapRuntimeFolder()
    {
#if STORE_BUILD
        try
        {
            string path = Windows.Storage.ApplicationData.Current.TemporaryFolder.Path;
            if (!string.IsNullOrEmpty(path))
            {
                Directory.CreateDirectory(path);
                return path;
            }
        }
        catch { /* fall back */ }
#endif
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AudioTaskbar", "tap-runtime");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    public static bool IsStartupEnabled()
    {
#if STORE_BUILD
        try
        {
            var task = Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId)
                .AsTask().GetAwaiter().GetResult();
            return task.State is Windows.ApplicationModel.StartupTaskState.Enabled
                or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    public static void SetStartup(bool enabled)
    {
#if STORE_BUILD
        var task = Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId)
            .AsTask().GetAwaiter().GetResult();
        if (enabled)
        {
            switch (task.State)
            {
                case Windows.ApplicationModel.StartupTaskState.Enabled:
                case Windows.ApplicationModel.StartupTaskState.EnabledByPolicy:
                    return;
                case Windows.ApplicationModel.StartupTaskState.DisabledByUser:
                    throw new InvalidOperationException(
                        "Windows đã tắt 'Khởi động cùng Windows' cho Audio Taskbar. " +
                        "Hãy bật lại trong Cài đặt → Ứng dụng → Khởi động (hoặc Trình quản lý tác vụ → Khởi động) rồi thử lần nữa.");
                default:
                {
                    var state = task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
                    if (state is not Windows.ApplicationModel.StartupTaskState.Enabled
                        and not Windows.ApplicationModel.StartupTaskState.EnabledByPolicy)
                    {
                        throw new InvalidOperationException(
                            "Không bật được khởi động cùng Windows (trạng thái: " + state + ").");
                    }
                    break;
                }
            }
        }
        else if (task.State is Windows.ApplicationModel.StartupTaskState.Enabled
                 or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy)
        {
            task.Disable();
        }
#else
        throw new InvalidOperationException("StartupTask API is only available in the Microsoft Store build.");
#endif
    }

    private const int AppModelErrorNoPackage = 15700;
    private const int ErrorInsufficientBuffer = 122;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);
}
