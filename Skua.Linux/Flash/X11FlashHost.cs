using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Skua.Linux.Flash;

/// <summary>
/// Embeds the Adobe Flash Player window inside an Avalonia NativeControlHost.
/// 
/// Strategy: Reparent the entire GTK2 top-level window (with override_redirect=1
/// to prevent WM interference) into our X11 container. Position it with a negative
/// Y offset to crop the GTK menu bar. This preserves GTK's event handling so
/// mouse clicks work normally.
/// </summary>
public class X11FlashHost : NativeControlHost
{
    private IntPtr _display;
    private IntPtr _containerXid;
    private IntPtr _flashTopLevelXid;
    private int _targetPid;
    private bool _isEmbedded;
    private DispatcherTimer? _searchTimer;
    private DispatcherTimer? _resizeTimer;

    // The GTK2 chrome height (menu bar + URL bar) of flashplayer
    private const int GtkChromeHeight = 56;

    #region X11 P/Invoke

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XCreateSimpleWindow(
        IntPtr display, IntPtr parent, int x, int y, uint width, uint height,
        uint borderWidth, ulong border, ulong background);

    [DllImport("libX11.so.6")]
    private static extern int XDestroyWindow(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern int XMapWindow(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern int XUnmapWindow(IntPtr display, IntPtr window);

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
    private static extern int XMapRaised(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern int XGetWindowAttributes(IntPtr display, IntPtr window, out XWindowAttributes windowAttributesReturn);

    [DllImport("libX11.so.6")]
    private static extern int XChangeWindowAttributes(
        IntPtr display, IntPtr window, ulong valuemask, ref XSetWindowAttributes attributes);

    [DllImport("libX11.so.6")]
    private static extern int XConfigureWindow(IntPtr display, IntPtr window, uint valueMask, ref XWindowChanges changes);

    [DllImport("libX11.so.6")]
    private static extern int XSelectInput(IntPtr display, IntPtr window, long eventMask);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct XWindowChanges
    {
        public int x, y;
        public int width, height;
        public int border_width;
        public IntPtr sibling;
        public int stack_mode;
    }

    private const ulong CWOverrideRedirect = 1UL << 9;
    private const uint CWX = 1 << 0;
    private const uint CWY = 1 << 1;
    private const uint CWWidth = 1 << 2;
    private const uint CWHeight = 1 << 3;

    // Event masks
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
            if (_display != IntPtr.Zero && parent.Handle != IntPtr.Zero)
            {
                uint w = (uint)Math.Max(100, Bounds.Width);
                uint h = (uint)Math.Max(100, Bounds.Height);

                // Create a clipping container window inside the Avalonia-provided parent
                _containerXid = XCreateSimpleWindow(
                    _display, parent.Handle, 0, 0, w, h, 0, 0, 0x000000);

                // Enable substructure notify so we get ConfigureNotify from children
                XSelectInput(_display, _containerXid, SubstructureNotifyMask | StructureNotifyMask);
                XMapWindow(_display, _containerXid);
                XFlush(_display);

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
        _searchTimer?.Stop();
        _searchTimer = null;
        _resizeTimer?.Stop();
        _resizeTimer = null;

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
        ResizeEmbeddedWindow();
    }

    public void AttachFlashProcess(int pid)
    {
        _targetPid = pid;
        _flashTopLevelXid = IntPtr.Zero;
        _isEmbedded = false;

        _searchTimer?.Stop();
        int attempts = 0;
        _searchTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _searchTimer.Tick += (_, _) =>
        {
            attempts++;
            if (attempts > 50 || _display == IntPtr.Zero || _containerXid == IntPtr.Zero)
            {
                _searchTimer?.Stop();
                return;
            }

            IntPtr root = XDefaultRootWindow(_display);
            IntPtr pidAtom = XInternAtom(_display, "_NET_WM_PID", true);
            if (pidAtom == IntPtr.Zero)
                return;

            IntPtr topLevel = FindWindowByPid(_display, root, pidAtom, (ulong)_targetPid, 0);
            if (topLevel == IntPtr.Zero)
                return;

            // Verify the window is big enough (the actual flash player window, not the leader)
            if (XGetWindowAttributes(_display, topLevel, out XWindowAttributes attrs) == 0 ||
                attrs.width < 200 || attrs.height < 200)
                return;

            _flashTopLevelXid = topLevel;
            _searchTimer?.Stop();

            EmbedWindow();
        };
        _searchTimer.Start();
    }

    private void EmbedWindow()
    {
        if (_display == IntPtr.Zero || _containerXid == IntPtr.Zero || _flashTopLevelXid == IntPtr.Zero)
            return;

        uint containerW = (uint)Math.Max(100, Bounds.Width);
        uint containerH = (uint)Math.Max(100, Bounds.Height);

        // Step 1: Set override_redirect=1 on the flash top-level BEFORE reparenting.
        // This prevents the window manager from intercepting the reparent and
        // setting WM_STATE to Withdrawn (which freezes Flash rendering).
        XSetWindowAttributes swa = new() { override_redirect = 1 };
        XChangeWindowAttributes(_display, _flashTopLevelXid, CWOverrideRedirect, ref swa);
        XSync(_display, false);

        // Step 2: Unmap briefly to ensure clean reparent
        XUnmapWindow(_display, _flashTopLevelXid);
        XSync(_display, false);

        // Step 3: Reparent the entire GTK2 top-level window into our container.
        // Position with Y = -GtkChromeHeight to crop the menu/URL bar.
        // The window must be sized to (containerW, containerH + GtkChromeHeight) so that
        // GTK2 allocates (containerW, containerH) to the Flash stage child.
        XReparentWindow(_display, _flashTopLevelXid, _containerXid, 0, -GtkChromeHeight);
        XSync(_display, false);

        // Step 4: Resize the top-level to fill our container + extra for the hidden chrome
        XMoveResizeWindow(_display, _flashTopLevelXid, 0, -GtkChromeHeight,
            containerW, containerH + (uint)GtkChromeHeight);
        XSync(_display, false);

        // Step 5: Map the window back
        XMapWindow(_display, _flashTopLevelXid);
        XSync(_display, false);

        _isEmbedded = true;

        // Step 6: Start a brief resize enforcement timer.
        // GTK2 may try to resize itself after being reparented; we override that.
        int enforceCount = 0;
        _resizeTimer?.Stop();
        _resizeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _resizeTimer.Tick += (_, _) =>
        {
            enforceCount++;
            if (enforceCount > 20)
            {
                _resizeTimer?.Stop();
                return;
            }
            ResizeEmbeddedWindow();
        };
        _resizeTimer.Start();
    }

    public void UndockFlashWindow()
    {
        if (_display == IntPtr.Zero || _flashTopLevelXid == IntPtr.Zero || !_isEmbedded)
            return;

        try
        {
            IntPtr root = XDefaultRootWindow(_display);

            // Unmap, reparent back to root, remove override_redirect, map
            XUnmapWindow(_display, _flashTopLevelXid);
            XSync(_display, false);

            XReparentWindow(_display, _flashTopLevelXid, root, 100, 100);
            XSync(_display, false);

            XSetWindowAttributes swa = new() { override_redirect = 0 };
            XChangeWindowAttributes(_display, _flashTopLevelXid, CWOverrideRedirect, ref swa);

            XMoveResizeWindow(_display, _flashTopLevelXid, 100, 100, 960, 600);
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
        if (_flashTopLevelXid != IntPtr.Zero && !_isEmbedded)
        {
            EmbedWindow();
        }
        else if (_targetPid > 0)
        {
            AttachFlashProcess(_targetPid);
        }
    }

    private void ResizeEmbeddedWindow()
    {
        if (_display == IntPtr.Zero || _containerXid == IntPtr.Zero)
            return;

        uint w = (uint)Math.Max(100, Bounds.Width);
        uint h = (uint)Math.Max(100, Bounds.Height);

        try
        {
            XResizeWindow(_display, _containerXid, w, h);

            if (_flashTopLevelXid != IntPtr.Zero && _isEmbedded)
            {
                // Position the top-level at (0, -GtkChromeHeight) so the menu bar
                // is cropped above the container's visible area.
                // Size it to (w, h + GtkChromeHeight) so GTK allocates (w, h) to Flash.
                XMoveResizeWindow(_display, _flashTopLevelXid, 0, -GtkChromeHeight,
                    w, h + (uint)GtkChromeHeight);
            }
            XFlush(_display);
        }
        catch
        {
        }
    }

    #region Window Search

    private static IntPtr FindWindowByPid(IntPtr display, IntPtr window, IntPtr pidAtom, ulong targetPid, int depth)
    {
        if (depth > 6)
            return IntPtr.Zero;

        if (GetWindowPid(display, window, pidAtom) == targetPid)
        {
            if (XGetWindowAttributes(display, window, out XWindowAttributes attrs) != 0 &&
                attrs.width >= 200 && attrs.height >= 200 && attrs.map_state == 2)
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
            out IntPtr actualType, out int actualFormat, out IntPtr nItems, out _, out IntPtr prop);

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
