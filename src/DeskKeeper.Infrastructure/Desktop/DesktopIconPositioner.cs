using System.Runtime.InteropServices;
using System.Text;
using DeskKeeper.Domain.Contracts;

namespace DeskKeeper.Infrastructure.Desktop;

/// <summary>
/// 通过桌面 SysListView32 消息移动图标（LVM_SETITEMPOSITION）。
/// 找不到窗口或失败时返回 false，绝不抛异常拖垮调用链。
/// </summary>
public sealed class DesktopIconPositioner : IDesktopIconPositioner
{
    private const uint LvmFirst = 0x1000;
    private const uint LvmGetItemCount = LvmFirst + 4;
    private const uint LvmGetItemTextW = LvmFirst + 115;
    private const uint LvmSetItemPosition = LvmFirst + 15;
    private const uint LvmGetItemPosition = LvmFirst + 16;
    private const uint LvifText = 0x0001;

    public bool TryMoveIcon(string path, string name, double x, double y, out string? error)
    {
        error = null;
        using var folderView = DesktopFolderView.TryCreate();
        try
        {
            var listView = FindDesktopListView();
            if (listView == IntPtr.Zero)
            {
                error = "未找到桌面图标列表窗口";
                return false;
            }

            var count = SendMessage(listView, LvmGetItemCount, IntPtr.Zero, IntPtr.Zero).ToInt32();
            for (var i = 0; i < count; i++)
            {
                var text = GetItemText(listView, i);
                // 桌面标签对快捷方式与“隐藏已知扩展名”的文件不显示扩展名，
                // 因此同时比较完整文件名与去扩展名后的名字。
                var bareName = Path.GetFileNameWithoutExtension(name);
                if (!string.Equals(text, name, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(text, bareName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var lParam = MakeLParam((int)x, (int)y);
                if (folderView is not null && folderView.SetPosition(i, (int)x, (int)y))
                {
                    InvalidateRect(listView, IntPtr.Zero, true);
                    return true;
                }

                var applied = SendMessage(listView, LvmSetItemPosition, new IntPtr(i), lParam).ToInt32();
                if (applied == 0)
                {
                    error = "系统拒绝移动该图标";
                    return false;
                }

                InvalidateRect(listView, IntPtr.Zero, true);
                return true;
            }

            error = $"桌面列表中未找到图标：{name}";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool? TryDetectAutoArrange()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Bags\1\Desktop");
            if (key?.GetValue("FFlags") is int flags)
            {
                // 0x1000 为桌面「自动排列图标」，0x1 为兼容旧版本的备用位。
                return (flags & 0x00001000) != 0 || (flags & 0x00000001) != 0;
            }
        }
        catch
        {
            // 探测失败保持未知。
        }

        return null;
    }

    public bool TryReadPositions(out IReadOnlyList<IconPosition> positions)
    {
        positions = Array.Empty<IconPosition>();
        using var folderView = DesktopFolderView.TryCreate();
        try
        {
            var listView = FindDesktopListView();
            if (listView == IntPtr.Zero)
            {
                return false;
            }

            var count = SendMessage(listView, LvmGetItemCount, IntPtr.Zero, IntPtr.Zero).ToInt32();
            var result = new List<IconPosition>(count);
            for (var i = 0; i < count; i++)
            {
                var name = GetItemText(listView, i);
                if (name.Length == 0)
                {
                    continue;
                }

                if (folderView is not null && folderView.GetPosition(i, out var x, out var y))
                {
                    result.Add(new IconPosition(name, x, y));
                    continue;
                }

                var packed = SendMessage(listView, LvmGetItemPosition, new IntPtr(i), IntPtr.Zero).ToInt64();
                var lx = (short)(packed & 0xFFFF);
                var ly = (short)((packed >> 16) & 0xFFFF);
                result.Add(new IconPosition(name, lx, ly));
            }

            positions = result;
            if (result.Count > 1 && result.All(p => p.X == 0 && p.Y == 0))
            {
                positions = Array.Empty<IconPosition>();
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryGetDpiScale(out double scale)
    {
        scale = 1.0;
        try
        {
            var listView = FindDesktopListView();
            if (listView == IntPtr.Zero)
            {
                return false;
            }

            var target = GetDpiForWindow(listView);
            var source = GetDpiForSystem();
            if (target == 0 || source == 0)
            {
                return false;
            }

            scale = Math.Clamp((double)target / source, 0.25, 4.0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryGetSpacing(out double width, out double height)
    {
        width = 0;
        height = 0;
        using var folderView = DesktopFolderView.TryCreate();
        if (folderView is not null && folderView.TryGetSpacing(out var w, out var h))
        {
            width = w;
            height = h;
            return true;
        }

        return false;
    }

    private static IntPtr FindDesktopListView()
    {
        var progman = FindWindow("Progman", null);
        var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView == IntPtr.Zero)
        {
            // Win11 桌面可能挂在 WorkerW 下。
            IntPtr worker = IntPtr.Zero;
            while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
            {
                defView = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero)
                {
                    break;
                }
            }
        }

        return defView == IntPtr.Zero
            ? IntPtr.Zero
            : FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
    }

    private static string GetItemText(IntPtr listView, int index)
    {
        GetWindowThreadProcessId(listView, out var processId);
        const uint processVmRead = 0x0010;
        const uint processVmWrite = 0x0020;
        const uint processVmOperation = 0x0008;
        const uint processQueryInformation = 0x0400;
        var remote = OpenProcess(
            processVmRead | processVmWrite | processVmOperation | processQueryInformation,
            false,
            processId);
        if (remote == IntPtr.Zero)
        {
            return string.Empty;
        }

        const uint memCommit = 0x1000;
        const uint pageReadWrite = 0x04;
        const uint memRelease = 0x8000;
        const int charCapacity = 512;
        var textBuffer = IntPtr.Zero;
        var itemBuffer = IntPtr.Zero;

        try
        {
            textBuffer = VirtualAllocEx(remote, IntPtr.Zero, (uint)(charCapacity * 2), memCommit, pageReadWrite);
            var itemSize = Marshal.SizeOf<LVITEM>();
            itemBuffer = VirtualAllocEx(remote, IntPtr.Zero, (uint)itemSize, memCommit, pageReadWrite);
            if (textBuffer == IntPtr.Zero || itemBuffer == IntPtr.Zero)
            {
                return string.Empty;
            }

            var item = new LVITEM
            {
                mask = LvifText,
                iItem = index,
                iSubItem = 0,
                pszText = textBuffer,
                cchTextMax = charCapacity,
            };
            if (!WriteProcessMemory(remote, itemBuffer, ref item, itemSize, out _))
            {
                return string.Empty;
            }

            SendMessage(listView, LvmGetItemTextW, new IntPtr(index), itemBuffer);
            var bytes = new byte[charCapacity * 2];
            if (!ReadProcessMemory(remote, textBuffer, bytes, bytes.Length, out _))
            {
                return string.Empty;
            }

            return Encoding.Unicode.GetString(bytes).Split('\0')[0];
        }
        finally
        {
            if (textBuffer != IntPtr.Zero)
            {
                VirtualFreeEx(remote, textBuffer, 0, memRelease);
            }

            if (itemBuffer != IntPtr.Zero)
            {
                VirtualFreeEx(remote, itemBuffer, 0, memRelease);
            }

            CloseHandle(remote);
        }
    }

    private static IntPtr MakeLParam(int x, int y) => new((y << 16) | (x & 0xFFFF));

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LVITEM
    {
        public uint mask;
        public int iItem;
        public int iSubItem;
        public uint state;
        public uint stateMask;
        public IntPtr pszText;
        public int cchTextMax;
        public int iImage;
        public IntPtr lParam;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, uint size, uint type, uint protect);

    [DllImport("kernel32.dll")]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, uint size, uint type);

    [DllImport("kernel32.dll")]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out int read);

    [DllImport("kernel32.dll")]
    private static extern bool WriteProcessMemory(
        IntPtr process,
        IntPtr address,
        ref LVITEM buffer,
        int size,
        out int written);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}

