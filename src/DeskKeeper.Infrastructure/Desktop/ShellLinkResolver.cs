using System.Runtime.InteropServices;
using System.Text;

namespace DeskKeeper.Infrastructure.Desktop;

/// <summary>
/// .lnk 经 IShellLink 解析；.url 解析 URL= 行；
/// .appref-ms 视为应用部署引用（无稳定可解析目标，返回自身路径）。
/// </summary>
public sealed class ShellLinkResolver : IShellLinkResolver
{
    public string? ResolveTarget(string linkPath)
    {
        try
        {
            var ext = Path.GetExtension(linkPath).ToLowerInvariant();
            return ext switch
            {
                ".lnk" => ResolveLnk(linkPath),
                ".url" => ResolveUrl(linkPath),
                ".appref-ms" => linkPath,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveLnk(string linkPath)
    {
        var shellLink = (IShellLinkW)(object)new ShellLinkClass();
        ((IPersistFile)shellLink).Load(linkPath, 0);
        var sb = new StringBuilder(260);
        shellLink.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static string? ResolveUrl(string urlPath)
    {
        foreach (var line in File.ReadLines(urlPath))
        {
            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
            {
                return line[4..].Trim();
            }
        }

        return null;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLinkClass
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}

