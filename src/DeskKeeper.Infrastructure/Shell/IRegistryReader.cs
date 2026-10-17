namespace DeskKeeper.Infrastructure.Shell;

public interface IRegistryReader
{
    /// <summary>HKCU 下读取 DWORD；不存在或无权限返回 null。</summary>
    uint? GetDWord(string subKeyPath, string valueName);
}

public sealed class CurrentUserRegistryReader : IRegistryReader
{
    public uint? GetDWord(string subKeyPath, string valueName)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKeyPath);
            return key?.GetValue(valueName) switch
            {
                uint u => u,
                int i when i >= 0 => (uint)i,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }
}

