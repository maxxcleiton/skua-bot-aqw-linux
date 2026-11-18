using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Skua.Linux.Flash;

public class X11FlashHost : NativeControlHost
{
    private IntPtr _display;
    private IntPtr _containerXid;
    private IntPtr _embeddedFlashXid;
    private int _targetPid;
    private DispatcherTimer? _searchTimer;

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
    private static extern int XReparentWindow(IntPtr display, IntPtr window, IntPtr parent, int x, int y);

    [DllImport("libX11.so.6")]
    private static extern int XMoveResizeWindow(IntPtr display, IntPtr window, int x, int y, uint width, uint height);

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

    [DllImport("libX11.so.6")]
    private static extern int XGetWindowAttributes(IntPtr display, IntPtr window, out XWindowAttributes windowAttributesReturn);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XErrorHandlerDelegate(IntPtr display, IntPtr errorEvent);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XSetErrorHandler(XErrorHandlerDelegate handler);

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
                _containerXid = XCreateSimpleWindow(
                    _display, parent.Handle, 0, 0,
                    (uint)Math.Max(100, Bounds.Width),
                    (uint)Math.Max(100, Bounds.Height),
                    0, 0, 0x000000);
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
        _embeddedFlashXid = IntPtr.Zero;

        _searchTimer?.Stop();
        int attempts = 0;
        _searchTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _searchTimer.Tick += (_, _) =>
        {
            attempts++;
            if (attempts > 40 || _display == IntPtr.Zero || _containerXid == IntPtr.Zero)
            {
                _searchTimer?.Stop();
                return;
            }

            IntPtr root = XDefaultRootWindow(_display);
            IntPtr pidAtom = XInternAtom(_display, "_NET_WM_PID", true);
            if (pidAtom == IntPtr.Zero)
                return;

            IntPtr found = FindWindowByPid(_display, root, pidAtom, (ulong)_targetPid, 0);
            if (found != IntPtr.Zero)
            {
                _embeddedFlashXid = found;
                _searchTimer?.Stop();

                uint w = (uint)Math.Max(100, Bounds.Width);
                uint h = (uint)Math.Max(100, Bounds.Height);
                XReparentWindow(_display, _embeddedFlashXid, _containerXid, 0, 0);
                XMapWindow(_display, _embeddedFlashXid);
                XMoveResizeWindow(_display, _embeddedFlashXid, 0, 0, w, h);
                XSync(_display, false);
            }
        };
        _searchTimer.Start();
    }

    public void UndockFlashWindow()
    {
        if (_display == IntPtr.Zero || _embeddedFlashXid == IntPtr.Zero)
            return;

        try
        {
            IntPtr root = XDefaultRootWindow(_display);
            XReparentWindow(_display, _embeddedFlashXid, root, 100, 100);
            XMapWindow(_display, _embeddedFlashXid);
            XMoveResizeWindow(_display, _embeddedFlashXid, 100, 100, 960, 550);
            XSync(_display, false);
            _embeddedFlashXid = IntPtr.Zero;
        }
        catch
        {
        }
    }

    public void RedockFlashWindow()
    {
        if (_targetPid > 0)
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
            XMoveResizeWindow(_display, _containerXid, 0, 0, w, h);
            if (_embeddedFlashXid != IntPtr.Zero)
            {
                XMoveResizeWindow(_display, _embeddedFlashXid, 0, 0, w, h);
            }
            XFlush(_display);
        }
        catch
        {
        }
    }

    [DllImport("libX11.so.6")]
    private static extern int XFree(IntPtr data);

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
}
