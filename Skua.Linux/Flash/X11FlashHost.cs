using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Skua.Linux.Flash;

/// <summary>
/// Embeds the Adobe Flash Player GTK2 window inside an Avalonia NativeControlHost on X11 / XWayland.
/// Uses an ICCCM-compliant two-phase XWithdrawWindow -> wait-for-root -> XReparentWindow sequence
/// plus a self-healing parent watchdog so Wayland WMs (Mutter / COSMIC Smithay) never detach the surface.
/// </summary>
public class X11FlashHost : NativeControlHost
{
    private IntPtr _display;
    private IntPtr _parentXid;
    private IntPtr _containerXid;
    private IntPtr _flashTopLevelXid;
    private int _targetPid;
    private bool _wantEmbedded;
    private bool _isEmbedded;
    private int _chromeHeight = 0;
    private DispatcherTimer? _embedTimer;
    private DispatcherTimer? _watchdogTimer;

    #region X11 P/Invoke

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XDefaultScreen(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XCreateSimpleWindow(
        IntPtr display, IntPtr parent, int x, int y, uint width, uint height,
        uint borderWidth, ulong border, ulong background);

    [DllImport("libX11.so.6")]
    private static extern int XDestroyWindow(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern int XMapWindow(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern int XMapRaised(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern int XUnmapWindow(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern int XWithdrawWindow(IntPtr display, IntPtr window, int screenNumber);

    [DllImport("libX11.so.6")]
    private static extern int XReparentWindow(IntPtr display, IntPtr window, IntPtr parent, int x, int y);

    [DllImport("libX11.so.6")]
    private static extern int XMoveResizeWindow(IntPtr display, IntPtr window, int x, int y, uint width, uint height);

    [DllImport("libX11.so.6")]
    private static extern int XResizeWindow(IntPtr display, IntPtr window, uint width, uint height);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XSync(IntPtr display, bool discard);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

    [DllImport("libX11.so.6")]
    private static extern int XGetWindowProperty(
        IntPtr display, IntPtr window, IntPtr property, IntPtr longOffset, IntPtr longLength,
        bool delete, IntPtr reqType, out IntPtr actualTypeReturn, out int actualFormatReturn,
        out IntPtr nitemsReturn, out IntPtr bytesAfterReturn, out IntPtr propReturn);

    [DllImport("libX11.so.6")]
    private static extern int XQueryTree(
        IntPtr display, IntPtr window, out IntPtr rootReturn, out IntPtr parentReturn,
        out IntPtr childrenReturn, out uint nchildrenReturn);

    [DllImport("libX11.so.6")]
    private static extern int XFree(IntPtr data);

    [DllImport("libX11.so.6")]
    private static extern int XGetWindowAttributes(IntPtr display, IntPtr window, out XWindowAttributes windowAttributesReturn);

    [DllImport("libX11.so.6")]
    private static extern int XChangeWindowAttributes(
        IntPtr display, IntPtr window, ulong valuemask, ref XSetWindowAttributes attributes);

    [DllImport("libX11.so.6")]
    private static extern int XSelectInput(IntPtr display, IntPtr window, long eventMask);

    [DllImport("libX11.so.6")]
    private static extern int XChangeProperty(
        IntPtr display, IntPtr window, IntPtr property, IntPtr type,
        int format, int mode, IntPtr[] data, int nelements);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XErrorHandlerDelegate(IntPtr display, IntPtr errorEvent);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XSetErrorHandler(XErrorHandlerDelegate handler);

    [StructLayout(LayoutKind.Sequential)]
    private struct XWindowAttributes
    {
        public int x, y;
        public int width, height;
        public int border_width;
        public int depth;
        public IntPtr visual;
        public IntPtr root;
        public int c_class;
        public int bit_gravity;
        public int win_gravity;
        public int backing_store;
        public ulong backing_planes;
        public ulong backing_pixel;
        public int save_under;
        public IntPtr colormap;
        public int map_installed;
        public int map_state;
        public long all_event_masks;
        public long your_event_mask;
        public long do_not_propagate_mask;
        public int override_redirect;
        public IntPtr screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XSetWindowAttributes
    {
        public IntPtr background_pixmap;
        public ulong background_pixel;
        public IntPtr border_pixmap;
        public ulong border_pixel;
        public int bit_gravity;
        public int win_gravity;
        public int backing_store;
        public ulong backing_planes;
        public ulong backing_pixel;
        public int save_under;
        public long event_mask;
        public long do_not_propagate_mask;
        public int override_redirect;
        public IntPtr colormap;
        public IntPtr cursor;
    }

    private const ulong CWOverrideRedirect = 1UL << 9;
    private const long SubstructureNotifyMask = 1L << 19;
    private const long StructureNotifyMask = 1L << 17;

    #endregion

    private static readonly XErrorHandlerDelegate IgnoreXError = (_, _) => 0;
    private static bool _errorHandlerInstalled;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        try
        {
            if (!_errorHandlerInstalled)
            {
                XSetErrorHandler(IgnoreXError);
                _errorHandlerInstalled = true;
            }

            _display = XOpenDisplay(IntPtr.Zero);
            _parentXid = parent.Handle;
            if (_display != IntPtr.Zero && _parentXid != IntPtr.Zero)
            {
                (uint w, uint h) = GetTargetContainerSize();

                // 0xFF000000 ensures alpha=255 opaque black even when Avalonia uses a 32-bit ARGB visual
                _containerXid = XCreateSimpleWindow(
                    _display, _parentXid, 0, 0, w, h, 0, 0, 0xFF000000UL);

                XSelectInput(_display, _containerXid, SubstructureNotifyMask | StructureNotifyMask);
                XMapWindow(_display, _containerXid);
                XFlush(_display);

                if (_wantEmbedded && _targetPid > 0)
                {
                    StartEmbedSequence();
                }

                return new PlatformHandle(_containerXid, "XID");
            }
        }
        catch
        {
        }

        return base.CreateNativeControlCore(parent);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _embedTimer?.Stop();
        _embedTimer = null;
        _watchdogTimer?.Stop();
        _watchdogTimer = null;
        _isEmbedded = false;

        try
        {
            if (_display != IntPtr.Zero)
            {
                if (_containerXid != IntPtr.Zero)
                {
                    XDestroyWindow(_display, _containerXid);
                    _containerXid = IntPtr.Zero;
                }
                XCloseDisplay(_display);
                _display = IntPtr.Zero;
            }
        }
        catch
        {
        }

        base.DestroyNativeControlCore(control);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        SyncEmbeddedGeometry();
    }

    private (uint w, uint h) GetTargetContainerSize()
    {
        double scaling = VisualRoot?.RenderScaling ?? 1.0;
        uint w = (uint)Math.Max(100, Math.Round(Bounds.Width * scaling));
        uint h = (uint)Math.Max(100, Math.Round(Bounds.Height * scaling));
        return (w, h);
    }

    public void AttachFlashProcess(int pid)
    {
        if (_targetPid != pid)
        {
            _targetPid = pid;
            _flashTopLevelXid = IntPtr.Zero;
            _isEmbedded = false;
        }

        _wantEmbedded = true;
        if (_isEmbedded && _flashTopLevelXid != IntPtr.Zero &&
            _display != IntPtr.Zero && _containerXid != IntPtr.Zero &&
            GetParentWindow(_display, _flashTopLevelXid, out _) == _containerXid)
        {
            SyncEmbeddedGeometry();
            return;
        }

        StartEmbedSequence();
    }

    private void StartEmbedSequence()
    {
        if (_display == IntPtr.Zero || _containerXid == IntPtr.Zero || _targetPid <= 0)
            return;

        _watchdogTimer?.Stop();
        _embedTimer?.Stop();
        _isEmbedded = false;

        int searchTicks = 0;
        int withdrawTicks = 0;

        _embedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(90)
        };
        _embedTimer.Tick += (_, _) =>
        {
            if (!_wantEmbedded || _display == IntPtr.Zero || _containerXid == IntPtr.Zero)
            {
                _embedTimer?.Stop();
                return;
            }

            searchTicks++;
            if (searchTicks > 90)
            {
                _embedTimer?.Stop();
                return;
            }

            IntPtr root = XDefaultRootWindow(_display);

            // Step 1: Locate the GTK2 top-level window for the Flash process
            if (_flashTopLevelXid == IntPtr.Zero || XGetWindowAttributes(_display, _flashTopLevelXid, out _) == 0)
            {
                IntPtr pidAtom = XInternAtom(_display, "_NET_WM_PID", true);
                if (pidAtom == IntPtr.Zero)
                    return;

                _flashTopLevelXid = FindWindowByPid(_display, root, pidAtom, (ulong)_targetPid, 0);
                if (_flashTopLevelXid == IntPtr.Zero)
                    return;
            }

            // Measure GTK menu/URL bar height dynamically from the Flash stage child window
            int detectedChrome = DetectCanvasOffsetY(_display, _flashTopLevelXid);
            if (detectedChrome >= 20 && detectedChrome <= 110)
            {
                _chromeHeight = detectedChrome;
            }
            else
            {
                _chromeHeight = 0;
            }

            IntPtr currentParent = GetParentWindow(_display, _flashTopLevelXid, out IntPtr actualRoot);
            if (currentParent == IntPtr.Zero)
            {
                _flashTopLevelXid = IntPtr.Zero;
                return;
            }

            // Already inside our container
            if (currentParent == _containerXid)
            {
                FinalizeEmbed();
                return;
            }

            // Step 2: ICCCM Withdraw — if a Window Manager (e.g. Smithay / Mutter) currently holds
            // _flashTopLevelXid inside a WM frame (currentParent != actualRoot) or it is still mapped,
            // withdraw it and WAIT until the WM finishes handling UnmapNotify and reparents it to root.
            XGetWindowAttributes(_display, _flashTopLevelXid, out XWindowAttributes attrs);
            if ((currentParent != actualRoot || attrs.map_state != 0) && withdrawTicks < 14)
            {
                if (withdrawTicks % 3 == 0)
                {
                    XSetWindowAttributes swaNormal = new() { override_redirect = 0 };
                    XChangeWindowAttributes(_display, _flashTopLevelXid, CWOverrideRedirect, ref swaNormal);
                    XWithdrawWindow(_display, _flashTopLevelXid, XDefaultScreen(_display));
                    XUnmapWindow(_display, _flashTopLevelXid);
                    XSync(_display, false);
                }
                withdrawTicks++;
                return;
            }

            // Step 3: WM has completely released _flashTopLevelXid back to root while unmapped.
            // Set override_redirect = 1, reparent into _containerXid, map, and force WM_STATE = NormalState (1)
            // so GTK2/Flash never pauses rendering due to WithdrawnState.
            (uint w, uint h) = GetTargetContainerSize();
            XResizeWindow(_display, _containerXid, w, h);

            XSetWindowAttributes swaEmbed = new() { override_redirect = 1 };
            XChangeWindowAttributes(_display, _flashTopLevelXid, CWOverrideRedirect, ref swaEmbed);

            XReparentWindow(_display, _flashTopLevelXid, _containerXid, 0, -_chromeHeight);
            XMoveResizeWindow(_display, _flashTopLevelXid, 0, -_chromeHeight, w, h + (uint)_chromeHeight);
            XMapRaised(_display, _flashTopLevelXid);
            EnsureWmStateNormal(_display, _flashTopLevelXid);
            XSync(_display, false);

            FinalizeEmbed();
        };
        _embedTimer.Start();
    }

    private void FinalizeEmbed()
    {
        _embedTimer?.Stop();
        _isEmbedded = true;
        SyncEmbeddedGeometry();
        StartWatchdog();
    }

    private void StartWatchdog()
    {
        _watchdogTimer?.Stop();
        _watchdogTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _watchdogTimer.Tick += (_, _) =>
        {
            if (!_wantEmbedded || !_isEmbedded || _display == IntPtr.Zero ||
                _containerXid == IntPtr.Zero || _flashTopLevelXid == IntPtr.Zero)
            {
                _watchdogTimer?.Stop();
                return;
            }

            IntPtr currentParent = GetParentWindow(_display, _flashTopLevelXid, out _);
            if (currentParent == IntPtr.Zero)
            {
                // Window destroyed (process restarted)
                _isEmbedded = false;
                _flashTopLevelXid = IntPtr.Zero;
                _watchdogTimer?.Stop();
                return;
            }

            if (currentParent != _containerXid)
            {
                // Self-heal: WM or GTK detached the window; re-embed cleanly without ever moving on root
                _isEmbedded = false;
                _watchdogTimer?.Stop();
                StartEmbedSequence();
                return;
            }

            int detectedChrome = DetectCanvasOffsetY(_display, _flashTopLevelXid);
            if (detectedChrome >= 20 && detectedChrome <= 110)
            {
                _chromeHeight = detectedChrome;
            }
            else
            {
                _chromeHeight = 0;
            }

            (uint w, uint h) = GetTargetContainerSize();
            int targetH = (int)h + _chromeHeight;

            if (XGetWindowAttributes(_display, _flashTopLevelXid, out XWindowAttributes attrs) != 0)
            {
                if (attrs.x != 0 || attrs.y != -_chromeHeight || attrs.width != (int)w || attrs.height != targetH)
                {
                    XResizeWindow(_display, _containerXid, w, h);
                    XMoveResizeWindow(_display, _flashTopLevelXid, 0, -_chromeHeight, w, (uint)targetH);
                }
                if (attrs.map_state != 2)
                {
                    XMapRaised(_display, _flashTopLevelXid);
                }
                XFlush(_display);
            }
        };
        _watchdogTimer.Start();
    }

    public void UndockFlashWindow()
    {
        _wantEmbedded = false;
        _embedTimer?.Stop();
        _watchdogTimer?.Stop();

        if (_display == IntPtr.Zero || _flashTopLevelXid == IntPtr.Zero)
            return;

        try
        {
            IntPtr root = XDefaultRootWindow(_display);

            XUnmapWindow(_display, _flashTopLevelXid);
            XSync(_display, false);

            XSetWindowAttributes swa = new() { override_redirect = 0 };
            XChangeWindowAttributes(_display, _flashTopLevelXid, CWOverrideRedirect, ref swa);

            XReparentWindow(_display, _flashTopLevelXid, root, 120, 120);
            XMoveResizeWindow(_display, _flashTopLevelXid, 120, 120, 958, 550 + (uint)_chromeHeight);
            XMapRaised(_display, _flashTopLevelXid);
            XSync(_display, false);

            _isEmbedded = false;
        }
        catch
        {
        }
    }

    public void RedockFlashWindow()
    {
        _wantEmbedded = true;
        if (_targetPid > 0)
        {
            StartEmbedSequence();
        }
    }

    private void SyncEmbeddedGeometry()
    {
        if (_display == IntPtr.Zero || _containerXid == IntPtr.Zero)
            return;

        (uint w, uint h) = GetTargetContainerSize();

        try
        {
            XResizeWindow(_display, _containerXid, w, h);

            // CRITICAL: Only move/resize _flashTopLevelXid when it is confirmed inside _containerXid
            if (_isEmbedded && _flashTopLevelXid != IntPtr.Zero &&
                GetParentWindow(_display, _flashTopLevelXid, out _) == _containerXid)
            {
                XMoveResizeWindow(_display, _flashTopLevelXid, 0, -_chromeHeight, w, h + (uint)_chromeHeight);
            }
            XFlush(_display);
        }
        catch
        {
        }
    }

    #region Window Tree Helpers

    private static void EnsureWmStateNormal(IntPtr display, IntPtr window)
    {
        IntPtr wmStateAtom = XInternAtom(display, "WM_STATE", false);
        if (wmStateAtom != IntPtr.Zero)
        {
            // PropModeReplace = 0, format = 32 (long[2] on LP64: { NormalState = 1, icon = 0 })
            IntPtr[] stateData = { new IntPtr(1), IntPtr.Zero };
            XChangeProperty(display, window, wmStateAtom, wmStateAtom, 32, 0, stateData, 2);
        }

        IntPtr allowCommitsAtom = XInternAtom(display, "_XWAYLAND_ALLOW_COMMITS", false);
        if (allowCommitsAtom != IntPtr.Zero)
        {
            IntPtr cardinalAtom = XInternAtom(display, "CARDINAL", false);
            IntPtr[] commitData = { new IntPtr(1) };
            XChangeProperty(display, window, allowCommitsAtom, cardinalAtom, 32, 0, commitData, 1);
        }
    }

    private static IntPtr GetParentWindow(IntPtr display, IntPtr window, out IntPtr rootReturn)
    {
        rootReturn = IntPtr.Zero;
        if (XQueryTree(display, window, out rootReturn, out IntPtr parentReturn, out IntPtr childrenPtr, out _) == 0)
            return IntPtr.Zero;

        if (childrenPtr != IntPtr.Zero)
            XFree(childrenPtr);

        return parentReturn;
    }

    private static int DetectCanvasOffsetY(IntPtr display, IntPtr topLevel)
    {
        if (XQueryTree(display, topLevel, out _, out _, out IntPtr childrenPtr, out uint nChildren) == 0)
            return -1;

        if (childrenPtr == IntPtr.Zero || nChildren == 0)
            return -1;

        try
        {
            for (int i = 0; i < nChildren; i++)
            {
                IntPtr child = Marshal.ReadIntPtr(childrenPtr, i * IntPtr.Size);
                if (XGetWindowAttributes(display, child, out XWindowAttributes attrs) != 0 &&
                    attrs.width >= 200 && attrs.height >= 200)
                {
                    return attrs.y;
                }
            }
        }
        finally
        {
            XFree(childrenPtr);
        }

        return -1;
    }

    private static IntPtr FindWindowByPid(IntPtr display, IntPtr window, IntPtr pidAtom, ulong targetPid, int depth)
    {
        if (depth > 6)
            return IntPtr.Zero;

        if (GetWindowPid(display, window, pidAtom) == targetPid)
        {
            if (XGetWindowAttributes(display, window, out XWindowAttributes attrs) != 0 &&
                attrs.width >= 200 && attrs.height >= 200)
            {
                return window;
            }
        }

        if (XQueryTree(display, window, out _, out _, out IntPtr childrenPtr, out uint nChildren) == 0)
            return IntPtr.Zero;

        if (childrenPtr == IntPtr.Zero || nChildren == 0)
            return IntPtr.Zero;

        try
        {
            for (int i = 0; i < nChildren; i++)
            {
                IntPtr child = Marshal.ReadIntPtr(childrenPtr, i * IntPtr.Size);
                IntPtr match = FindWindowByPid(display, child, pidAtom, targetPid, depth + 1);
                if (match != IntPtr.Zero)
                    return match;
            }
        }
        finally
        {
            XFree(childrenPtr);
        }

        return IntPtr.Zero;
    }

    private static ulong GetWindowPid(IntPtr display, IntPtr window, IntPtr pidAtom)
    {
        int status = XGetWindowProperty(
            display, window, pidAtom, IntPtr.Zero, new IntPtr(1), false, IntPtr.Zero,
            out _, out int actualFormat, out IntPtr nItems, out _, out IntPtr prop);

        if (status != 0 || prop == IntPtr.Zero)
            return 0;

        try
        {
            if (nItems.ToInt64() > 0 && actualFormat == 32)
            {
                return (ulong)Marshal.ReadInt32(prop);
            }
        }
        finally
        {
            XFree(prop);
        }

        return 0;
    }

    #endregion
}
