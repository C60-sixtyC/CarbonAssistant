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
    private const uint LvifText = 0x0001;

    public bool TryMoveIcon(string path, string name, double x, double y, out string? error)
    {
        error = null;
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
                if (!string.Equals(text, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var lParam = MakeLParam((int)x, (int)y);
                SendMessage(listView, LvmSetItemPosition, new IntPtr(i), lParam);
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
                // 不同系统版本上自动排列对应不同位，两者都探测以保稳妥。
                return (flags & 0x00000001) != 0 || (flags & 0x80000000) != 0;
            }
        }
        catch
        {
            // 探测失败保持未知。
        }

        return null;
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
        var remote = OpenProcess(0x0010 /*PROCESS_VM_READ*/ | 0x0008 /*PROCESS_VM_OPERATION*/ | 0x0400 /*QUERY_INFORMATION*/, false, processId);
        if (remote == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            var buffer = VirtualAllocEx(remote, IntPtr.Zero, 1024, 0x1000 /*COMMIT*/, 0x04 /*READWRITE*/);
            if (buffer == IntPtr.Zero)
            {
                return string.Empty;
            }

            try
            {
                var item = new LVITEM
                {
                    mask = LvifText,
                    iItem = index,
                    iSubItem = 0,
                    pszText = buffer,
                    cchTextMax = 512,
                };
                var local = Marshal.AllocHGlobal(Marshal.SizeOf(item));
                try
                {
                    Marshal.StructureToPtr(item, local, false);
                    SendMessage(listView, LvmGetItemTextW, new IntPtr(index), local);
                    var bytes = new byte[1024];
                    ReadProcessMemory(remote, buffer, bytes, bytes.Length, out _);
                    return Encoding.Unicode.GetString(bytes).Split('\0')[0];
                }
                finally
                {
                    Marshal.FreeHGlobal(local);
                }
            }
            finally
            {
                VirtualFreeEx(remote, buffer, 0, 0x8000 /*MEM_RELEASE*/);
            }
        }
        finally
        {
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
    private static extern bool CloseHandle(IntPtr handle);
}

