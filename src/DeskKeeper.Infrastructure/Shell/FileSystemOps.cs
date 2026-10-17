using System.Security.Cryptography;
using Microsoft.VisualBasic.FileIO;

namespace DeskKeeper.Infrastructure.Shell;

public interface IFileSystemOps
{
    void MoveToArchive(string source, string destination);

    /// <summary>移入回收站（可恢复），UI 静默。</summary>
    void Recycle(string path);

    string ComputeSha256(string path);

    /// <summary>独占打开探测；被占用返回 false。</summary>
    bool CanOpenExclusive(string path);

    bool IsSameVolume(string pathA, string pathB);
}

/// <summary>
/// 文件搬运与回收实现：
/// 同卷直接 Move；跨卷复制 + SHA256 校验 + 移源进回收站；
/// 删除一律走回收站（FOF_ALLOWUNDO 语义），绝不直接永久删除业务文件。
/// </summary>
public sealed class FileSystemOps : IFileSystemOps
{
    public void MoveToArchive(string source, string destination)
    {
        var destDir = Path.GetDirectoryName(destination);
        if (destDir is not null)
        {
            Directory.CreateDirectory(destDir);
        }

        if (IsSameVolume(source, destination))
        {
            if (Directory.Exists(source))
            {
                Directory.Move(source, destination);
            }
            else
            {
                File.Move(source, destination);
            }

            return;
        }

        // 跨卷：复制 + 校验 + 移源进回收站（可恢复，安全兜底）。
        CopyWithVerify(source, destination);
        Recycle(source);
    }

    public void Recycle(string path)
    {
        if (Directory.Exists(path))
        {
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        else
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
    }

    public string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool CanOpenExclusive(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool IsSameVolume(string pathA, string pathB)
    {
        var rootA = Path.GetPathRoot(Path.GetFullPath(pathA));
        var rootB = Path.GetPathRoot(Path.GetFullPath(pathB));
        if (string.IsNullOrEmpty(rootA) || string.IsNullOrEmpty(rootB))
        {
            return false;
        }

        if (rootA.StartsWith(@"\\", StringComparison.Ordinal) || rootB.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // UNC 路径保守判定：视为不同卷，走复制+校验路径。
            return false;
        }

        try
        {
            return string.Equals(
                new DriveInfo(rootA).Name,
                new DriveInfo(rootB).Name,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void CopyWithVerify(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            CopyDirectoryRecursive(source, destination);
            return;
        }

        File.Copy(source, destination, overwrite: true);
        var sourceHash = SHA256.HashData(File.OpenRead(source));
        var destHash = SHA256.HashData(File.OpenRead(destination));
        if (!sourceHash.AsSpan().SequenceEqual(destHash))
        {
            File.Delete(destination);
            throw new IOException($"跨卷复制校验失败：{source}");
        }
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", System.IO.SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, destination, StringComparison.Ordinal));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", System.IO.SearchOption.AllDirectories))
        {
            var dest = file.Replace(source, destination, StringComparison.Ordinal);
            File.Copy(file, dest, overwrite: true);
            var sourceHash = SHA256.HashData(File.OpenRead(file));
            var destHash = SHA256.HashData(File.OpenRead(dest));
            if (!sourceHash.AsSpan().SequenceEqual(destHash))
            {
                throw new IOException($"跨卷复制校验失败：{file}");
            }
        }
    }
}

