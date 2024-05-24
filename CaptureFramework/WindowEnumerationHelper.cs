using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public class WindowInfo {
    public IntPtr hWnd;
    public Process proc;
    public string Title;
    public WindowInfo(IntPtr hwmd, Process proc, string title) {
        this.hWnd = hwmd;
        this.proc = proc;
        this.Title = title;
    }
    public string Name {
        get {
            return Path.GetFileName(proc.MainModule.FileName) + ":" + Title;
        }
    }
}

public static class WindowEnumerationHelper {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    /// <summary>
    /// Get all hWnds of windows
    /// </summary>
    /// <returns>(hWnd,Title)</returns>
    public static List<WindowInfo> GetWindows() {
        var res = new List<WindowInfo>();
        EnumWindows((hWnd, lParam) => {
            if (!IsWindowValidForCapture(hWnd))
                return true;

            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (string.IsNullOrWhiteSpace(title))
                return true;

            uint processId;
            GetWindowThreadProcessId(hWnd, out processId);
            try {
                var wndInfo = new WindowInfo(hWnd, 
                    Process.GetProcessById((int)processId),
                    title);
                string name = wndInfo.Name;
                res.Add(wndInfo);
            } catch (Exception) {
                // ignored
            }
            return true;
        }, IntPtr.Zero);
        return res;
    }



    enum GetAncestorFlags {
        // Retrieves the parent window. This does not include the owner, as it does with the GetParent function.
        GetParent = 1,
        // Retrieves the root window by walking the chain of parent windows.
        GetRoot = 2,
        // Retrieves the owned root window by walking the chain of parent and owner windows returned by GetParent.
        GetRootOwner = 3
    }

    public enum GWL {
        GWL_WNDPROC = (-4),
        GWL_HINSTANCE = (-6),
        GWL_HWNDPARENT = (-8),
        GWL_STYLE = (-16),
        GWL_EXSTYLE = (-20),
        GWL_USERDATA = (-21),
        GWL_ID = (-12)
    }

    [Flags]
    private enum WindowStyles : uint {
        WS_BORDER = 0x800000,
        WS_CAPTION = 0xc00000,
        WS_CHILD = 0x40000000,
        WS_CLIPCHILDREN = 0x2000000,
        WS_CLIPSIBLINGS = 0x4000000,
        WS_DISABLED = 0x8000000,
        WS_DLGFRAME = 0x400000,
        WS_GROUP = 0x20000,
        WS_HSCROLL = 0x100000,
        WS_MAXIMIZE = 0x1000000,
        WS_MAXIMIZEBOX = 0x10000,
        WS_MINIMIZE = 0x20000000,
        WS_MINIMIZEBOX = 0x20000,
        WS_OVERLAPPED = 0x0,
        WS_OVERLAPPEDWINDOW = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_SIZEFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX,
        WS_POPUP = 0x80000000u,
        WS_POPUPWINDOW = WS_POPUP | WS_BORDER | WS_SYSMENU,
        WS_SIZEFRAME = 0x40000,
        WS_SYSMENU = 0x80000,
        WS_TABSTOP = 0x10000,
        WS_VISIBLE = 0x10000000,
        WS_VSCROLL = 0x200000
    }

    enum DWMWINDOWATTRIBUTE : uint {
        NCRenderingEnabled = 1,
        NCRenderingPolicy,
        TransitionsForceDisabled,
        AllowNCPaint,
        CaptionButtonBounds,
        NonClientRtlLayout,
        ForceIconicRepresentation,
        Flip3DPolicy,
        ExtendedFrameBounds,
        HasIconicBitmap,
        DisallowPeek,
        ExcludedFromPeek,
        Cloak,
        Cloaked,
        FreezeRepresentation
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    static extern IntPtr GetAncestor(IntPtr hwnd, GetAncestorFlags flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    // This static method is required because Win32 does not support
    // GetWindowLongPtr directly.
    // http://pinvoke.net/default.aspx/user32/GetWindowLong.html
    static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) {
        if (IntPtr.Size == 8)
            return GetWindowLongPtr64(hWnd, nIndex);
        else
            return GetWindowLongPtr32(hWnd, nIndex);
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE dwAttribute, out bool pvAttribute, int cbAttribute);

    public static bool IsWindowValidForCapture(IntPtr hwnd) {
        if (hwnd.ToInt32() == 0) {
            return false;
        }

        if (hwnd == GetShellWindow()) {
            return false;
        }

        if (!IsWindowVisible(hwnd)) {
            return false;
        }

        if (GetAncestor(hwnd, GetAncestorFlags.GetRoot) != hwnd) {
            return false;
        }

        var style = (WindowStyles)(uint)GetWindowLongPtr(hwnd, (int)GWL.GWL_STYLE).ToInt64();
        if (style.HasFlag(WindowStyles.WS_DISABLED)) {
            return false;
        }

        var cloaked = false;
        var hrTemp = DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.Cloaked, out cloaked, Marshal.SizeOf<bool>());
        if (hrTemp == 0 && cloaked) {
            return false;
        }

        return true;
    }
}

