// TaskbarTap.cpp
// Clean-room helper for Audio Taskbar (v2 protocol since 5.1.1: persistent, reusable).
// Uses the documented XAML Diagnostics API (xamlom.h / InitializeXamlDiagnosticsEx)
// to make the Windows 11 taskbar background fill transparent.
//
// Written from public Microsoft documentation and the Windows SDK header
// xamlom.h. Not derived from TranslucentTB (GPLv3) or windhawk-mods (GPLv3).
//
// The host process (AudioTaskbar.exe) calls InitializeXamlDiagnosticsEx,
// which loads this DLL into explorer.exe and creates CLSID_TaskbarTap
// (IObjectWithSite). We walk visual-tree mutations, match elements by
// x:Name (BackgroundFill / BackgroundStroke) so we keep working when
// the 26H2/26300 tree wraps TaskbarFrame, replace Fill / Visibility,
// and restore the originals on request.

#define INITGUID
#include <windows.h>
#include <initguid.h>
#include <objbase.h>
#include <ocidl.h>
#include <inspectable.h>
#include <xamlom.h>
#include <shlwapi.h>
#include <shlobj.h>
#include <strsafe.h>
#include <new>

// {E8A31B2C-7D4F-4C19-B6A1-9F3E2C8D4A10}
static const CLSID CLSID_TaskbarTap =
{ 0xe8a31b2c, 0x7d4f, 0x4c19, { 0xb6, 0xa1, 0x9f, 0x3e, 0x2c, 0x8d, 0x4a, 0x10 } };

static const wchar_t kReadyEvent[]   = L"Local\\AudioTaskbar.Tap.Ready";
static const wchar_t kRestoreEvent[] = L"Local\\AudioTaskbar.Tap.Restore";
static const wchar_t kStopEvent[]    = L"Local\\AudioTaskbar.Tap.Stop";
static const wchar_t kStoppedEvent[] = L"Local\\AudioTaskbar.Tap.Stopped";
static const wchar_t kAppliedEvent[] = L"Local\\AudioTaskbar.Tap.Applied";
static const wchar_t kApplyEvent[]    = L"Local\\AudioTaskbar.Tap.Apply";
// v2 (AudioTaskbar 5.1.1): the TAP is a persistent in-Explorer service. While this event
// exists the watcher is alive and can be re-enabled with Apply — the host must NOT call
// InitializeXamlDiagnosticsEx again for this Explorer.
// Protocol v3 (5.1.1): all XAML work happens on the XAML UI thread (posted to a message-only
// window created there); the worker thread only waits on events and never touches XAML.
static const wchar_t kAliveEvent[]    = L"Local\\AudioTaskbar.Tap.Alive3";
// Created once at DLL load and never closed: proves a v3 (teardown-safe) copy is in this
// Explorer, so a fresh injection after its site was released is safe.
static const wchar_t kLoadedEvent[]   = L"Local\\AudioTaskbar.Tap.Loaded3";
static const wchar_t kRestoredEvent[] = L"Local\\AudioTaskbar.Tap.Restored";
static HANDLE g_alive = nullptr;
static HANDLE g_loaded = nullptr;
static HWND g_msgWnd = nullptr;
static const UINT WM_TAP_RESTORE = WM_APP + 0x41;
static const UINT WM_TAP_APPLY   = WM_APP + 0x42;
class TapSite;
static TapSite* g_site = nullptr;          // only touched on the XAML UI thread

static LONG g_locks = 0;
static HINSTANCE g_hinst = nullptr;

static void Log(const wchar_t* fmt, ...)
{
    wchar_t line[1024];
    va_list ap;
    va_start(ap, fmt);
    StringCchVPrintfW(line, 1024, fmt, ap);
    va_end(ap);

    wchar_t path[MAX_PATH] = {};
    if (FAILED(SHGetFolderPathW(nullptr, 0x001c /*CSIDL_LOCAL_APPDATA*/, nullptr, 0, path)))
        return;
    StringCchCatW(path, MAX_PATH, L"\\AudioTaskbar");
    CreateDirectoryW(path, nullptr);
    StringCchCatW(path, MAX_PATH, L"\\app.log");

    SYSTEMTIME st;
    GetLocalTime(&st);
    wchar_t stamped[1200];
    StringCchPrintfW(stamped, 1200, L"%04u-%02u-%02u %02u:%02u:%02u  [TAP] %s\r\n",
        st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, line);

    HANDLE h = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                           nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;
    int bytes = WideCharToMultiByte(CP_UTF8, 0, stamped, -1, nullptr, 0, nullptr, nullptr);
    if (bytes > 1)
    {
        char* utf8 = (char*)LocalAlloc(LPTR, bytes);
        if (utf8)
        {
            WideCharToMultiByte(CP_UTF8, 0, stamped, -1, utf8, bytes, nullptr, nullptr);
            DWORD wr = 0;
            WriteFile(h, utf8, (DWORD)bytes - 1, &wr, nullptr);
            LocalFree(utf8);
        }
    }
    CloseHandle(h);
}

static void WriteStatus(const wchar_t* state, int patched, const wchar_t* error)
{
    wchar_t path[MAX_PATH] = {};
    if (FAILED(SHGetFolderPathW(nullptr, 0x001c, nullptr, 0, path)))
        return;
    StringCchCatW(path, MAX_PATH, L"\\AudioTaskbar\\tap-status.txt");
    wchar_t body[512];
    StringCchPrintfW(body, 512, L"state=%s\r\npatched=%d\r\nerror=%s\r\n",
        state ? state : L"unknown", patched, error ? error : L"");
    HANDLE h = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS,
                           FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;
    int bytes = WideCharToMultiByte(CP_UTF8, 0, body, -1, nullptr, 0, nullptr, nullptr);
    if (bytes > 1)
    {
        char* utf8 = (char*)LocalAlloc(LPTR, bytes);
        if (utf8)
        {
            WideCharToMultiByte(CP_UTF8, 0, body, -1, utf8, bytes, nullptr, nullptr);
            DWORD wr = 0;
            WriteFile(h, utf8, (DWORD)bytes - 1, &wr, nullptr);
            LocalFree(utf8);
        }
    }
    CloseHandle(h);
}


// ---------- patched element record ----------
struct PatchRecord
{
    InstanceHandle element;
    unsigned int propIndex;
    InstanceHandle originalValue;
    bool originalWasNull;
    bool isFill;          // else Visibility
    bool active;
    bool removed;         // element left the tree: never touch its handle again
};

static const int kMaxPatches = 32;
static const int kMaxLogNames = 48;

class TapSite;

class TreeWatcher : public IVisualTreeServiceCallback2
{
public:
    TreeWatcher() : m_ref(1), m_diag(nullptr), m_svc(nullptr),
                    m_patchCount(0), m_loggedNames(0), m_advised(false), m_enabled(true) {}

    void Init(IXamlDiagnostics* diag, IVisualTreeService* svc)
    {
        if (m_diag) m_diag->Release();
        if (m_svc) m_svc->Release();
        m_diag = diag;
        m_svc = svc;
        if (m_diag) m_diag->AddRef();
        if (m_svc) m_svc->AddRef();
    }

    HRESULT Advise()
    {
        if (!m_svc) return E_POINTER;
        HRESULT hr = m_svc->AdviseVisualTreeChange(this);
        m_advised = SUCCEEDED(hr);
        return hr;
    }

    bool Enabled() const { return m_enabled; }

    void RestoreAll()
    {
        m_enabled = false; // new BackgroundFill elements are only remembered until Apply
        if (!m_svc) return;
        for (int i = 0; i < m_patchCount; ++i)
        {
            PatchRecord& r = m_patches[i];
            if (!r.active || r.removed) continue;
            if (r.originalWasNull)
                m_svc->ClearProperty(r.element, r.propIndex);
            else
                m_svc->SetProperty(r.element, r.originalValue, r.propIndex);
            r.active = false;
        }
        Log(L"Restored %d patched element(s).", m_patchCount);
        WriteStatus(L"restored", 0, L"");
        HANDLE ev = OpenEventW(EVENT_MODIFY_STATE, FALSE, kRestoredEvent);
        if (ev) { SetEvent(ev); CloseHandle(ev); }
    }

    void ReapplyAll()
    {
        m_enabled = true;
        if (!m_svc) return;
        int n = 0;
        for (int i = 0; i < m_patchCount; ++i)
        {
            PatchRecord& r = m_patches[i];
            if (r.removed) continue;
            InstanceHandle val = 0;
            HRESULT hr = r.isFill
                ? CreateTransparentBrush(L"Windows.UI.Xaml.Shapes.Rectangle", &val)
                : CreateCollapsed(&val);
            if (FAILED(hr)) continue;
            hr = m_svc->SetProperty(r.element, val, r.propIndex);
            if (SUCCEEDED(hr))
            {
                r.active = true;
                ++n;
            }
        }
        Log(L"Reapplied %d element(s).", n);
        WriteStatus(n > 0 ? L"patched" : L"reapply-empty", n, L"");
        HANDLE ev = OpenEventW(EVENT_MODIFY_STATE, FALSE, kAppliedEvent);
        if (ev) { SetEvent(ev); CloseHandle(ev); }
    }

    void Shutdown()
    {
        RestoreAll();
        if (m_advised && m_svc)
        {
            m_svc->UnadviseVisualTreeChange(this);
            m_advised = false;
        }
        if (m_diag) { m_diag->Release(); m_diag = nullptr; }
        if (m_svc) { m_svc->Release(); m_svc = nullptr; }
    }

    int ActivePatches() const
    {
        int n = 0;
        for (int i = 0; i < m_patchCount; ++i)
            if (m_patches[i].active) ++n;
        return n;
    }

    // IUnknown
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) ||
            IsEqualIID(riid, IID_IVisualTreeServiceCallback) ||
            IsEqualIID(riid, IID_IVisualTreeServiceCallback2))
        {
            *ppv = static_cast<IVisualTreeServiceCallback2*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return (ULONG)InterlockedIncrement(&m_ref); }
    ULONG STDMETHODCALLTYPE Release() override
    {
        LONG c = InterlockedDecrement(&m_ref);
        if (c == 0) delete this;
        return (ULONG)c;
    }

    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(
        ParentChildRelation relation, VisualElement element, VisualMutationType mutation) override
    {
        // Never throw, never abort — an unhandled error here can take down Explorer.
        if (!m_svc) return S_OK;

        if (mutation == Remove)
        {
            for (int i = 0; i < m_patchCount; ++i)
            {
                if (m_patches[i].element == element.Handle)
                {
                    m_patches[i].active = false;
                    m_patches[i].removed = true;
                }
            }
            return S_OK;
        }

        const wchar_t* name = element.Name ? element.Name : L"";
        const wchar_t* type = element.Type ? element.Type : L"";

        if (m_loggedNames < kMaxLogNames)
        {
            if (lstrcmpiW(name, L"BackgroundFill") == 0 ||
                lstrcmpiW(name, L"BackgroundStroke") == 0 ||
                lstrcmpiW(name, L"TaskbarFrame") == 0 ||
                lstrcmpiW(name, L"BackgroundControl") == 0)
            {
                Log(L"tree add name='%s' type='%s' children=%u", name, type, element.NumChildren);
                ++m_loggedNames;
            }
        }

        bool isFill = (lstrcmpiW(name, L"BackgroundFill") == 0);
        bool isStroke = (lstrcmpiW(name, L"BackgroundStroke") == 0);
        if (!isFill && !isStroke) return S_OK;

        if (!m_enabled)
        {
            // Host is not running (or asked for restore): remember the element so a later
            // Apply can patch it, but leave the stock taskbar background untouched.
            unsigned int idx = 0;
            HRESULT fh = FindProperty(element.Handle, isFill ? L"Fill" : L"Visibility", &idx);
            if (SUCCEEDED(fh)) RememberOnly(element.Handle, idx, isFill);
            Log(L"Remembered %s while disabled.", name);
            return S_OK;
        }

        HRESULT hr = isFill ? PatchFill(element, type) : PatchStroke(element);
        if (SUCCEEDED(hr))
        {
            int n = ActivePatches();
            WriteStatus(L"patched", n, L"");
            HANDLE ev = OpenEventW(EVENT_MODIFY_STATE, FALSE, kAppliedEvent);
            if (ev) { SetEvent(ev); CloseHandle(ev); }
            Log(L"Patched %s (active=%d) type='%s'", name, n, type);
        }
        else
        {
            Log(L"Failed to patch %s hr=0x%08X type='%s'", name, hr, type);
        }
        (void)relation;
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE OnElementStateChanged(
        InstanceHandle, VisualElementState, LPCWSTR) noexcept override
    {
        return S_OK;
    }

private:
    HRESULT FindProperty(InstanceHandle handle, const wchar_t* propName, unsigned int* index)
    {
        unsigned int sourceCount = 0, propCount = 0;
        PropertyChainSource* sources = nullptr;
        PropertyChainValue* props = nullptr;
        HRESULT hr = m_svc->GetPropertyValuesChain(handle, &sourceCount, &sources, &propCount, &props);
        if (FAILED(hr)) return hr;
        hr = E_NOTFOUND;
        for (unsigned int i = 0; i < propCount; ++i)
        {
            if (props[i].PropertyName && lstrcmpiW(props[i].PropertyName, propName) == 0)
            {
                *index = props[i].Index;
                hr = S_OK;
                break;
            }
        }
        if (sources) CoTaskMemFree(sources);
        if (props) CoTaskMemFree(props);
        return hr;
    }

    const wchar_t* BrushTypeFromElementType(const wchar_t* type)
    {
        if (type && wcsstr(type, L"Microsoft.UI.Xaml"))
            return L"Microsoft.UI.Xaml.Media.SolidColorBrush";
        return L"Windows.UI.Xaml.Media.SolidColorBrush";
    }

    HRESULT CreateTransparentBrush(const wchar_t* elementType, InstanceHandle* outHandle)
    {
        const wchar_t* typ = BrushTypeFromElementType(elementType);
        BSTR t = SysAllocString(typ);
        BSTR v = SysAllocString(L"#00000000");
        HRESULT hr = E_FAIL;
        if (t && v)
            hr = m_svc->CreateInstance(t, v, outHandle);
        if (FAILED(hr) && t)
        {
            SysFreeString(v);
            v = SysAllocString(L"Transparent");
            if (v) hr = m_svc->CreateInstance(t, v, outHandle);
        }
        if (t) SysFreeString(t);
        if (v) SysFreeString(v);
        return hr;
    }

    HRESULT CreateCollapsed(InstanceHandle* outHandle)
    {
        // Try both XAML stacks.
        const wchar_t* types[] = {
            L"Windows.UI.Xaml.Visibility",
            L"Microsoft.UI.Xaml.Visibility"
        };
        HRESULT hr = E_FAIL;
        for (int i = 0; i < 2 && FAILED(hr); ++i)
        {
            BSTR t = SysAllocString(types[i]);
            BSTR v = SysAllocString(L"Collapsed");
            if (t && v) hr = m_svc->CreateInstance(t, v, outHandle);
            if (t) SysFreeString(t);
            if (v) SysFreeString(v);
        }
        return hr;
    }

    void Compact()
    {
        int w = 0;
        for (int i = 0; i < m_patchCount; ++i)
            if (!m_patches[i].removed) m_patches[w++] = m_patches[i];
        m_patchCount = w;
    }

    void RememberOnly(InstanceHandle element, unsigned int index, bool isFill)
    {
        for (int i = 0; i < m_patchCount; ++i)
            if (m_patches[i].element == element && m_patches[i].propIndex == index && !m_patches[i].removed)
                return;
        if (m_patchCount >= kMaxPatches) Compact();
        if (m_patchCount >= kMaxPatches) return;
        PatchRecord rec{};
        rec.element = element;
        rec.propIndex = index;
        rec.originalWasNull = true;
        rec.isFill = isFill;
        rec.active = false;
        rec.removed = false;
        m_patches[m_patchCount++] = rec;
    }

    HRESULT RememberAndSet(InstanceHandle element, unsigned int index, InstanceHandle newValue, bool isFill)
    {
        if (m_patchCount >= kMaxPatches) Compact();
        if (m_patchCount >= kMaxPatches) return E_OUTOFMEMORY;
        // Avoid double-patching the same element.
        for (int i = 0; i < m_patchCount; ++i)
        {
            if (m_patches[i].element == element && m_patches[i].propIndex == index && !m_patches[i].removed)
            {
                if (!m_patches[i].active)
                {
                    HRESULT hr = m_svc->SetProperty(element, newValue, index);
                    if (SUCCEEDED(hr)) m_patches[i].active = true;
                    return hr;
                }
                return S_FALSE;
            }
        }

        // Read current value handle if present, via GetPropertyValuesChain.
        unsigned int sourceCount = 0, propCount = 0;
        PropertyChainSource* sources = nullptr;
        PropertyChainValue* props = nullptr;
        InstanceHandle original = 0;
        bool wasNull = true;
        HRESULT hr = m_svc->GetPropertyValuesChain(element, &sourceCount, &sources, &propCount, &props);
        if (SUCCEEDED(hr) && props)
        {
            for (unsigned int i = 0; i < propCount; ++i)
            {
                if (props[i].Index == index)
                {
                    wasNull = (props[i].MetadataBits & IsValueNull) != 0;
                    if (!wasNull && (props[i].MetadataBits & IsValueHandle) && props[i].Value)
                    {
                        // Value is not a handle integer in the string reliably;
                        // Register the live inspectable instead.
                    }
                    break;
                }
            }
        }
        if (sources) CoTaskMemFree(sources);
        if (props) CoTaskMemFree(props);

        if (m_diag)
        {
            IInspectable* insp = nullptr;
            if (SUCCEEDED(m_diag->GetIInspectableFromHandle(element, &insp)) && insp)
            {
                // Keep a handle to the original by registering after we read it
                // through SetProperty later on restore via Clear+reapply of saved handle.
                insp->Release();
            }
        }

        hr = m_svc->SetProperty(element, newValue, index);
        if (FAILED(hr)) return hr;

        PatchRecord rec{};
        rec.element = element;
        rec.propIndex = index;
        rec.originalValue = original;
        rec.originalWasNull = true; // safest restore: ClearProperty (revert to style/default)
        rec.isFill = isFill;
        rec.active = true;
        m_patches[m_patchCount++] = rec;
        return S_OK;
    }

    HRESULT PatchFill(const VisualElement& element, const wchar_t* type)
    {
        unsigned int index = 0;
        HRESULT hr = FindProperty(element.Handle, L"Fill", &index);
        if (FAILED(hr)) return hr;
        InstanceHandle brush = 0;
        hr = CreateTransparentBrush(type, &brush);
        if (FAILED(hr)) return hr;
        return RememberAndSet(element.Handle, index, brush, true);
    }

    HRESULT PatchStroke(const VisualElement& element)
    {
        unsigned int index = 0;
        HRESULT hr = FindProperty(element.Handle, L"Visibility", &index);
        if (FAILED(hr))
            hr = FindProperty(element.Handle, L"Fill", &index);
        if (FAILED(hr)) return hr;

        InstanceHandle val = 0;
        // Prefer Visibility=Collapsed; fall back to transparent fill.
        hr = CreateCollapsed(&val);
        if (FAILED(hr))
            hr = CreateTransparentBrush(element.Type, &val);
        if (FAILED(hr)) return hr;
        return RememberAndSet(element.Handle, index, val, false);
    }

    LONG m_ref;
    IXamlDiagnostics* m_diag;
    IVisualTreeService* m_svc;
    PatchRecord m_patches[kMaxPatches];
    int m_patchCount;
    int m_loggedNames;
    bool m_advised;
    volatile bool m_enabled;
};

class TapSite : public IObjectWithSite
{
public:
    TapSite() : m_ref(1), m_site(nullptr), m_watcher(nullptr), m_worker(nullptr), m_stop(FALSE) {}

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IObjectWithSite))
        {
            *ppv = static_cast<IObjectWithSite*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return (ULONG)InterlockedIncrement(&m_ref); }
    ULONG STDMETHODCALLTYPE Release() override
    {
        LONG c = InterlockedDecrement(&m_ref);
        if (c == 0) delete this;
        return (ULONG)c;
    }

    // Called on the XAML UI thread via the message window.
    void OnRestore() { if (m_watcher) m_watcher->RestoreAll(); }
    void OnApply()   { if (m_watcher) m_watcher->ReapplyAll(); }

    static LRESULT CALLBACK MsgWndProc(HWND h, UINT msg, WPARAM wp, LPARAM lp)
    {
        if (msg == WM_TAP_RESTORE || msg == WM_TAP_APPLY)
        {
            TapSite* site = g_site;
            if (!site) { Log(L"%s ignored: no active site.", msg == WM_TAP_RESTORE ? L"Restore" : L"Apply"); return 0; }
            site->AddRef();
            if (msg == WM_TAP_RESTORE) site->OnRestore(); else site->OnApply();
            site->Release();
            return 0;
        }
        return DefWindowProcW(h, msg, wp, lp);
    }

    static void EnsureMsgWindow()
    {
        if (g_msgWnd && IsWindow(g_msgWnd)) return;
        WNDCLASSW wc{};
        wc.lpfnWndProc = MsgWndProc;
        wc.hInstance = g_hinst;
        wc.lpszClassName = L"AudioTaskbarTapMsg3";
        RegisterClassW(&wc); // may already exist: fine
        g_msgWnd = CreateWindowExW(0, L"AudioTaskbarTapMsg3", L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, g_hinst, nullptr);
        Log(L"UI-thread message window %s (tid %u).", g_msgWnd ? L"ready" : L"FAILED", GetCurrentThreadId());
    }

    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* pUnkSite) override
    {
        if (m_site) { m_site->Release(); m_site = nullptr; }

        if (!pUnkSite)
        {
            Log(L"SetSite(null) — tearing down (tid %u).", GetCurrentThreadId());
            Teardown();
            return S_OK;
        }

        m_site = pUnkSite;
        m_site->AddRef();
        // SetSite runs on the XAML UI thread: host the message window here so Restore/Apply
        // are executed on this thread (IVisualTreeService is not thread-safe).
        EnsureMsgWindow();
        g_site = this;

        IXamlDiagnostics* diag = nullptr;
        IVisualTreeService* svc = nullptr;
        HRESULT hr = pUnkSite->QueryInterface(IID_IXamlDiagnostics, (void**)&diag);
        if (FAILED(hr))
        {
            Log(L"QI IXamlDiagnostics failed 0x%08X", hr);
            WriteStatus(L"error", 0, L"QI IXamlDiagnostics failed");
            return hr;
        }
        hr = pUnkSite->QueryInterface(IID_IVisualTreeService, (void**)&svc);
        if (FAILED(hr))
        {
            Log(L"QI IVisualTreeService failed 0x%08X", hr);
            WriteStatus(L"error", 0, L"QI IVisualTreeService failed");
            diag->Release();
            return hr;
        }

        if (m_watcher)
        {
            m_watcher->Shutdown();
            m_watcher->Release();
            m_watcher = nullptr;
        }
        m_watcher = new (std::nothrow) TreeWatcher();
        if (!m_watcher)
        {
            diag->Release();
            svc->Release();
            return E_OUTOFMEMORY;
        }
        m_watcher->Init(diag, svc);
        diag->Release();
        svc->Release();

        // Advise on a fresh thread — calling it on the SetSite thread can hang.
        InterlockedIncrement(&g_locks);
        HANDLE th = CreateThread(nullptr, 0, AdviseProc, this, 0, nullptr);
        if (th) CloseHandle(th);
        else InterlockedDecrement(&g_locks);

        if (!m_worker)
        {
            m_stop = FALSE;
            InterlockedIncrement(&g_locks);
            AddRef(); // the worker holds a reference until it exits
            m_worker = CreateThread(nullptr, 0, WorkerProc, this, 0, nullptr);
            if (!m_worker) { Release(); InterlockedDecrement(&g_locks); }
        }
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE GetSite(REFIID riid, void** ppvSite) override
    {
        if (!ppvSite) return E_POINTER;
        *ppvSite = nullptr;
        if (!m_site) return E_FAIL;
        return m_site->QueryInterface(riid, ppvSite);
    }

private:
    static DWORD WINAPI AdviseProc(LPVOID param)
    {
        TapSite* self = static_cast<TapSite*>(param);
        CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        if (self->m_watcher)
        {
            HRESULT hr = self->m_watcher->Advise();
            Log(L"AdviseVisualTreeChange hr=0x%08X", hr);
            int n = self->m_watcher->ActivePatches();
            if (SUCCEEDED(hr) || n > 0)
            {
                if (FAILED(hr))
                    Log(L"Advise returned 0x%08X but %d element(s) were already patched — continuing.", hr, n);
                WriteStatus(n > 0 ? L"patched" : L"advised", n, L"");
                HANDLE ev = OpenEventW(EVENT_MODIFY_STATE, FALSE, kReadyEvent);
                if (ev) { SetEvent(ev); CloseHandle(ev); }
                else Log(L"OpenEvent Ready failed %u", GetLastError());
            }
            else
            {
                wchar_t err[64];
                StringCchPrintfW(err, 64, L"Advise failed 0x%08X", hr);
                WriteStatus(L"error", 0, err);
            }
        }
        CoUninitialize();
        InterlockedDecrement(&g_locks);
        return 0;
    }

    static DWORD WINAPI WorkerProc(LPVOID param)
    {
        TapSite* self = static_cast<TapSite*>(param);
        HANDLE evRestore = CreateEventW(nullptr, TRUE, FALSE, kRestoreEvent);
        HANDLE evApply = CreateEventW(nullptr, TRUE, FALSE, kApplyEvent);
        HANDLE evStop = CreateEventW(nullptr, TRUE, FALSE, kStopEvent);
        HANDLE handles[3] = { evRestore, evApply, evStop };
        // Persistent-service marker: exists while this worker (and its XAML site) is alive.
        if (!g_alive) g_alive = CreateEventW(nullptr, TRUE, FALSE, kAliveEvent);
        Log(L"TAP service alive (v3: reusable, UI-thread dispatch).");
        for (;;)
        {
            DWORD w = WaitForMultipleObjects(3, handles, FALSE, 500);
            if (self->m_stop) break;
            if (w == WAIT_OBJECT_0)
            {
                ResetEvent(evRestore);
                Log(L"Restore requested.");
                if (g_msgWnd) PostMessageW(g_msgWnd, WM_TAP_RESTORE, 0, 0);
            }
            else if (w == WAIT_OBJECT_0 + 1)
            {
                ResetEvent(evApply);
                Log(L"Reapply requested.");
                if (g_msgWnd) PostMessageW(g_msgWnd, WM_TAP_APPLY, 0, 0);
            }
            else if (w == WAIT_OBJECT_0 + 2)
            {
                // v3 hosts never send Stop; ignore stray signals (e.g. from an old host build).
                ResetEvent(evStop);
                Log(L"Stop ignored (v3 TAP is persistent; restore instead).");
                if (g_msgWnd) PostMessageW(g_msgWnd, WM_TAP_RESTORE, 0, 0);
            }
        }
        // Only reached after Teardown (SetSite(null)) on the UI thread; never touch XAML here.
        if (g_alive) { CloseHandle(g_alive); g_alive = nullptr; }
        Log(L"Worker exit (site released).");
        if (evRestore) CloseHandle(evRestore);
        if (evApply) CloseHandle(evApply);
        if (evStop) CloseHandle(evStop);
        HANDLE evStopped = OpenEventW(EVENT_MODIFY_STATE, FALSE, kStoppedEvent);
        if (evStopped) { SetEvent(evStopped); CloseHandle(evStopped); }
        self->Release();
        InterlockedDecrement(&g_locks);
        return 0;
    }

    void Teardown()
    {
        // UI thread. The worker only polls m_stop, it never calls into the watcher.
        m_stop = TRUE;
        if (g_site == this) g_site = nullptr;
        if (g_alive) { CloseHandle(g_alive); g_alive = nullptr; } // host will inject a fresh copy
        if (m_worker) { CloseHandle(m_worker); m_worker = nullptr; }
        if (m_watcher)
        {
            m_watcher->Shutdown();
            m_watcher->Release();
            m_watcher = nullptr;
        }
    }

    LONG m_ref;
    IUnknown* m_site;
    TreeWatcher* m_watcher;
    HANDLE m_worker;
    volatile BOOL m_stop;
};

class ClassFactory : public IClassFactory
{
public:
    ClassFactory() : m_ref(1) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IClassFactory))
        {
            *ppv = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return (ULONG)InterlockedIncrement(&m_ref); }
    ULONG STDMETHODCALLTYPE Release() override
    {
        LONG c = InterlockedDecrement(&m_ref);
        if (c == 0) delete this;
        return (ULONG)c;
    }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override
    {
        if (outer) return CLASS_E_NOAGGREGATION;
        TapSite* obj = new (std::nothrow) TapSite();
        if (!obj) return E_OUTOFMEMORY;
        HRESULT hr = obj->QueryInterface(riid, ppv);
        obj->Release();
        return hr;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override
    {
        if (lock) InterlockedIncrement(&g_locks);
        else InterlockedDecrement(&g_locks);
        return S_OK;
    }
private:
    LONG m_ref;
};

extern "C" BOOL WINAPI DllMain(HINSTANCE h, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_hinst = h;
        DisableThreadLibraryCalls(h);
        if (!g_loaded) g_loaded = CreateEventW(nullptr, TRUE, FALSE, kLoadedEvent);
        Log(L"TaskbarTap loaded in PID %u", GetCurrentProcessId());
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        Log(L"TaskbarTap unloading from PID %u", GetCurrentProcessId());
    }
    return TRUE;
}

extern "C" HRESULT WINAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** ppv)
{
    if (!IsEqualCLSID(clsid, CLSID_TaskbarTap))
        return CLASS_E_CLASSNOTAVAILABLE;
    ClassFactory* f = new (std::nothrow) ClassFactory();
    if (!f) return E_OUTOFMEMORY;
    HRESULT hr = f->QueryInterface(riid, ppv);
    f->Release();
    return hr;
}

extern "C" HRESULT WINAPI DllCanUnloadNow()
{
    return g_locks == 0 ? S_OK : S_FALSE;
}
