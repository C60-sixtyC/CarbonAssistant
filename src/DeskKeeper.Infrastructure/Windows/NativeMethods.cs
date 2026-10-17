using System.Runtime.InteropServices;

namespace DeskKeeper.Infrastructure.Windows;

internal static class NativeMethods
{
    public static readonly Guid FolderIdDesktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    public static readonly Guid FolderIdPublicDesktop = new("C4AA340D-F20B-4863-AFEF-F87EF2E6BA25");

    private const uint SpiGetWorkArea = 0x0030;
    private const uint SpiIconHorizontalSpacing = 0x000D;
    private const uint SpiIconVerticalSpacing = 0x0018;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHGetKnownFolderPath(
        in Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out string ppszPath);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int StrCmpLogicalW(string psz1, string psz2);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        ref RECT pvParam,
        uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        ref int pvParam,
        uint fWinIni);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern uint GetDriveType(string? lpRootPathName);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    public static (RECT area, int cellW, int cellH) GetLayoutParameters()
    {
        var area = new RECT();
        var cellW = 75;
        var cellH = 75;
        SystemParametersInfo(SpiGetWorkArea, 0, ref area, 0);
        SystemParametersInfo(SpiIconHorizontalSpacing, 0, ref cellW, 0);
        SystemParametersInfo(SpiIconVerticalSpacing, 0, ref cellH, 0);
        return (area, Math.Max(cellW, 1), Math.Max(cellH, 1));
    }
}

