namespace DeskKeeper.Infrastructure.Desktop;

/// <summary>
/// 检测本机是否真实维护「最后访问时间」：
/// 创建测试文件 → 写入 → 读取 → 再读值，若值未变化则判定不可用。
/// 不可用时 UI 必须禁用「久未打开」判据。
/// </summary>
public sealed class LastAccessProbe
{
    public static bool Probe(string? directory = null)
    {
        var dir = directory ?? Path.GetTempPath();
        var path = Path.Combine(dir, $"dk-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(path, "probe");
            var before = File.GetLastAccessTimeUtc(path);
            File.ReadAllText(path);
            var after = File.GetLastAccessTimeUtc(path);
            return after > before;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // 清理失败不影响结果。
            }
        }
    }
}

