using System.Runtime.InteropServices;

namespace DeskKeeper.Infrastructure.Desktop;

/// <summary>
/// 通过 Shell.Application 拿到桌面已有的 IFolderView，
/// 用官方 SelectAndPositionItems / GetItemPosition 读写图标位置。
/// 相比直接发 LVM 消息，这条路与 Explorer 自身一致，不会“假成功”。
/// </summary>
internal sealed class DesktopFolderView : IDisposable
{
    private IFolderView? _view;
    private IShellFolder? _desktop;

    public static DesktopFolderView? TryCreate()
    {
        var instance = new DesktopFolderView();
        return instance.Initialize() ? instance : null;
    }

    public bool GetPidlForIndex(int index, out IntPtr pidl)
    {
        pidl = IntPtr.Zero;
        return _view is not null && _view.Item(index, out pidl) == 0 && pidl != IntPtr.Zero;
    }

    public bool GetPosition(int index, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (_view is null || !GetPidlForIndex(index, out var pidl))
        {
            return false;
        }

        try
        {
            if (_view.GetItemPosition(pidl, out var point) != 0)
            {
                return false;
            }

            x = point.X;
            y = point.Y;
            return true;
        }
        finally
        {
            FreePidl(pidl);
        }
    }

    public bool SetPosition(int index, int x, int y)
    {
        if (_view is null || !GetPidlForIndex(index, out var pidl))
        {
            return false;
        }

        try
        {
            var pidls = new[] { pidl };
            var points = new[] { new POINT { X = x, Y = y } };
            const uint svsiPositionItem = 0x0004;
            return _view.SelectAndPositionItems(1, pidls, points, svsiPositionItem) == 0;
        }
        finally
        {
            FreePidl(pidl);
        }
    }

    public void Dispose()
    {
        if (_view is not null && Marshal.IsComObject(_view))
        {
            Marshal.ReleaseComObject(_view);
        }

        if (_desktop is not null && Marshal.IsComObject(_desktop))
        {
            Marshal.ReleaseComObject(_desktop);
        }
    }

    private bool Initialize()
    {
        try
        {
            var hr = SHGetDesktopFolder(out _desktop);
            if (hr != 0 || _desktop is null)
            {
                return false;
            }

            var progman = FindProgman();
            if (progman == IntPtr.Zero)
            {
                return false;
            }

            var shellAppType = Type.GetTypeFromProgID("Shell.Application");
            if (shellAppType is null)
            {
                return false;
            }

            dynamic shellApp = Activator.CreateInstance(shellAppType)!;
            foreach (dynamic window in shellApp.Windows())
            {
                var hwnd = (long)(int)window.HWND;
                if (hwnd == progman.ToInt64())
                {
                    _view = (IFolderView)window.Document;
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr FindProgman()
    {
        return NativeMethodsForDesktop.FindWindowWithClass("Progman");
    }

    private static void FreePidl(IntPtr pidl)
    {
        if (pidl != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetDesktopFolder(out IShellFolder ppshf);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName(
            IntPtr hwnd,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
            out uint pchEaten,
            out IntPtr ppidl,
            out uint pdwAttributes);
    }

    [ComImport]
    [Guid("cde725b0-ccc9-4519-917e-325d72fab4ce")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        [PreserveSig] int GetCurrentViewMode(out uint pViewMode);
        [PreserveSig] int SetCurrentViewMode(uint viewMode);
        [PreserveSig] int GetFolder(ref Guid riid, out IntPtr ppv);
        [PreserveSig] int Item(int iItemIndex, out IntPtr ppidl);
        [PreserveSig] int ItemCount(uint uFlags, out int pcItems);
        [PreserveSig] int Items(uint uFlags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetSelectionMarkedItem(out int piItem);
        [PreserveSig] int GetFocusedItem(out int piItem);
        [PreserveSig] int GetItemPosition(IntPtr pidl, out POINT ppt);
        [PreserveSig] int GetSpacing(out POINT ppt);
        [PreserveSig] int GetDefaultSpacing(out POINT ppt);
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem(int iItem, uint dwFlags);
        [PreserveSig] int SelectAndPositionItems(uint cidl, IntPtr[] apidl, POINT[] apt, uint dwFlags);
    }
}

internal static class NativeMethodsForDesktop
{
    internal static IntPtr FindWindowWithClass(string className)
    {
        var result = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            var sb = new System.Text.StringBuilder(256);
            GetClassName(h, sb, 256);
            if (sb.ToString() == className)
            {
                result = h;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return result;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
}

