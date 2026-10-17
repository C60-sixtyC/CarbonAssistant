using System.Security.Cryptography;
using System.Text;
using DeskKeeper.Domain.Contracts;
using DeskKeeper.Domain.Models;
using DeskKeeper.Domain.Rules;
using DeskKeeper.Infrastructure.Audit;
using DeskKeeper.Infrastructure.Shell;

namespace DeskKeeper.Application.UseCases;

public sealed record CleanManifestItem(
    string Name,
    string FullPath,
    DesktopItemKind Kind,
    long SizeBytes,
    DateTimeOffset ModifiedTime,
    string Reason,
    bool Checked,
    bool Locked,
    Disposal Disposal,
    bool ConfirmRequired);

public sealed record CleanManifest(
    string ProfileId,
    string Mode,
    IReadOnlyList<CleanManifestItem> Items,
    int ExcludedCount,
    bool OneDriveBlocked,
    string ManifestHash)
{
    public long CheckedBytes => Items.Where(i => i.Checked && !i.Locked).Sum(i => i.SizeBytes);
    public int CheckedCount => Items.Count(i => i.Checked && !i.Locked);
}

public sealed record PreCheckResult(
    bool NukeOnDeleteEnabled,
    long RecycleUsedBytes,
    long RecycleAvailableBytes,
    long PendingDeleteBytes,
    bool Recyclable,
    IReadOnlyList<string> Warnings);

public sealed record CleanResult(
    int Cleaned,
    long BytesCleaned,
    IReadOnlyList<AuditFailure> Failures,
    bool DryRun,
    string? BatchDirectory,
    bool Aborted,
    string? AbortReason);

/// <summary>
/// 下课清理用例：PreCheck → BuildManifest(dry-run) → Execute → Audit。
/// 归档优先、逐文件隔离、失败不影响其余项。
/// </summary>
public sealed class CleanUseCase
{
    private readonly IDesktopItemSource _source;
    private readonly IFileSystemOps _fileOps;
    private readonly RecycleBinProbe _recycleProbe;
    private readonly AuditLogger _audit;
    private readonly UndoStore _undo;
    private readonly IClock _clock;
    private readonly string _hostName;

    public CleanUseCase(
        IDesktopItemSource source,
        IFileSystemOps fileOps,
        RecycleBinProbe recycleProbe,
        AuditLogger audit,
        UndoStore undo,
        IClock? clock = null,
        string? hostName = null)
    {
        _source = source;
        _fileOps = fileOps;
        _recycleProbe = recycleProbe;
        _audit = audit;
        _undo = undo;
        _clock = clock ?? new SystemClock();
        _hostName = hostName ?? Environment.MachineName;
    }

    public CleanManifest BuildManifest(RuleProfile profile)
    {
        var engine = new RuleEngine(profile.Rules);
        var settings = profile.Cleanup;
        var items = new List<CleanManifestItem>();
        var excluded = 0;
        var oneDriveBlocked = false;

        foreach (var item in _source.Enumerate())
        {
            if (item.Kind == DesktopItemKind.SystemIcon)
            {
                continue;
            }

            if (item.IsOneDriveManaged)
            {
                oneDriveBlocked = true;
                continue;
            }

            var decision = engine.Evaluate(item, _clock.Now);
            if (decision.Exclude || decision.Disposal == Disposal.Skip)
            {
                excluded++;
                continue;
            }

            var ext = Path.GetExtension(item.FullPath);
            var isProtected = IsProtected(ext, item.FullPath, settings);
            var isJunk = IsJunk(item, settings);
            var stale = _clock.Now - item.ModifiedTime > TimeSpan.FromDays(settings.KeepDays);

            var (checkedNow, reason) = isProtected
                ? (false, "教学文档（受保护，默认不勾选）")
                : isJunk
                    ? (true, "临时文件（低价值）")
                    : stale
                        ? (true, $"超过 {settings.KeepDays} 天未修改")
                        : (false, "较新，未超过保留天数");

            items.Add(new CleanManifestItem(
                item.Name,
                item.FullPath,
                item.Kind,
                item.SizeBytes,
                item.ModifiedTime,
                reason,
                checkedNow,
                Locked: false,
                decision.Disposal,
                decision.ConfirmRequired));
        }

        // 清理清单默认按最后修改时间升序（最旧的在前，优先暴露候选）。
        var ordered = items.OrderBy(i => i.ModifiedTime).ToList();
        var hash = ComputeManifestHash(ordered);
        return new CleanManifest(profile.Id, settings.Mode, ordered, excluded, oneDriveBlocked, hash);
    }

    public CleanManifest MarkLocked(CleanManifest manifest)
    {
        var updated = manifest.Items.Select(i => i with
        {
            Locked = i.Checked && !_fileOps.CanOpenExclusive(i.FullPath),
        }).ToList();
        return manifest with { Items = updated };
    }

    public PreCheckResult PreCheck(CleanManifest manifest, string? scopeRoot)
    {
        var warnings = new List<string>();
        var rootPath = scopeRoot is null ? null : Path.GetPathRoot(scopeRoot);
        var status = _recycleProbe.Probe(rootPath);
        var pending = manifest.CheckedBytes;

        if (status.NukeOnDeleteEnabled)
        {
            warnings.Add("本机关闭了回收站（NukeOnDelete=1），所有删除将永久生效，建议改用归档方式。");
        }

        var recyclable = !status.NukeOnDeleteEnabled && pending <= status.AvailableBytes;
        if (status.NukeOnDeleteEnabled || pending > status.AvailableBytes)
        {
            warnings.Add("待删体积超出回收站剩余容量，超出的文件将无法恢复，请改用归档。");
        }

        if (scopeRoot is not null && RecycleBinProbe.IsRemovableOrNetwork(scopeRoot))
        {
            warnings.Add("作用域位于可移动磁盘或网络路径，无回收站可用，将自动改为归档。");
            recyclable = false;
        }

        if (manifest.OneDriveBlocked)
        {
            warnings.Add("检测到 OneDrive 桌面托管，清理已整体拦截，避免云端文件被误删。");
        }

        return new PreCheckResult(
            status.NukeOnDeleteEnabled,
            status.UsedBytes,
            status.AvailableBytes,
            pending,
            recyclable,
            warnings);
    }

    public CleanResult Execute(
        CleanManifest manifest,
        RuleProfile profile,
        bool dryRun,
        string? scopeRoot = null,
        Func<string, bool>? confirm = null)
    {
        if (manifest.OneDriveBlocked)
        {
            return Aborted(manifest, profile, "OneDrive 桌面托管，已整体拦截。");
        }

        if (dryRun)
        {
            _audit.Append(new AuditRecord(
                _clock.Now, _hostName, null, profile.Id, "cleanup", true,
                manifest.CheckedCount, 0, Array.Empty<AuditFailure>(),
                ConfigHash.Compute(profile), manifest.ManifestHash));
            return new CleanResult(0, 0, Array.Empty<AuditFailure>(), true, null, false, null);
        }

        var mode = profile.Cleanup.Mode;
        if (scopeRoot is not null && RecycleBinProbe.IsRemovableOrNetwork(scopeRoot))
        {
            mode = "archive";
        }

        var targets = manifest.Items.Where(i => i.Checked && !i.Locked).ToList();
        var failures = new List<AuditFailure>();
        var undoEntries = new List<UndoEntry>();
        var cleaned = 0;
        long bytesCleaned = 0;
        string? batchDir = null;

        if (mode == "archive" && targets.Count > 0)
        {
            batchDir = CreateBatchDirectory(profile.Cleanup.ArchiveRoot);
        }

        foreach (var item in targets)
        {
            if (item.ConfirmRequired && confirm is not null && !confirm(item.FullPath))
            {
                failures.Add(new AuditFailure(item.FullPath, "用户取消"));
                continue;
            }

            try
            {
                if (mode == "recycle")
                {
                    _fileOps.Recycle(item.FullPath);
                    undoEntries.Add(new UndoEntry(
                        item.FullPath, null, "recycle", item.SizeBytes, _clock.Now));
                }
                else
                {
                    var dest = Path.Combine(batchDir!, item.Name);
                    _fileOps.MoveToArchive(item.FullPath, dest);
                    undoEntries.Add(new UndoEntry(
                        item.FullPath, dest, "archive", item.SizeBytes, _clock.Now));
                }

                cleaned++;
                bytesCleaned += item.SizeBytes;
            }
            catch (Exception ex)
            {
                failures.Add(new AuditFailure(item.FullPath, ex.Message));
            }
        }

        if (mode == "archive" && batchDir is not null)
        {
            EnforceArchiveCap(profile.Cleanup.ArchiveRoot, profile.Cleanup.ArchiveCapBytes);
        }

        _undo.Save(undoEntries);
        _audit.Append(new AuditRecord(
            _clock.Now, _hostName, null, profile.Id, "cleanup", false,
            cleaned, bytesCleaned, failures,
            ConfigHash.Compute(profile), manifest.ManifestHash));

        return new CleanResult(cleaned, bytesCleaned, failures, false, batchDir, false, null);
    }

    public CleanResult RestoreLastClean()
    {
        var entries = _undo.Load();
        var failures = new List<AuditFailure>();
        var restored = 0;
        long bytes = 0;

        foreach (var entry in entries)
        {
            try
            {
                if (entry.Disposal == "archive" && entry.MovedToPath is not null)
                {
                    _fileOps.MoveToArchive(entry.MovedToPath, entry.OriginalPath);
                    restored++;
                    bytes += entry.SizeBytes;
                }
                else
                {
                    failures.Add(new AuditFailure(
                        entry.OriginalPath, "已在回收站，请从回收站手动恢复"));
                }
            }
            catch (Exception ex)
            {
                failures.Add(new AuditFailure(entry.OriginalPath, ex.Message));
            }
        }

        _undo.Save(new List<UndoEntry>());
        return new CleanResult(restored, bytes, failures, false, null, false, null);
    }

    private static CleanResult Aborted(CleanManifest manifest, RuleProfile profile, string reason)
    {
        return new CleanResult(0, 0, Array.Empty<AuditFailure>(), false, null, true, reason);
    }

    private static bool IsProtected(string ext, string fullPath, CleanupSettings settings)
    {
        if (!settings.ProtectedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(settings.TeachingPathContains)
            || fullPath.Contains(settings.TeachingPathContains, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJunk(DesktopItem item, CleanupSettings settings)
    {
        var name = item.Name;
        return settings.JunkExtensions.Any(e => Glob.IsMatch(name, e))
            && item.SizeBytes <= (long)(settings.JunkMaxKB * 1024);
    }

    private static string CreateBatchDirectory(string archiveRoot)
    {
        Directory.CreateDirectory(archiveRoot);
        var date = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        var existing = Directory.EnumerateDirectories(archiveRoot, date + "_第*次清理").Count();
        var dir = Path.Combine(archiveRoot, $"{date}_第{existing + 1}次清理");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void EnforceArchiveCap(string archiveRoot, long capBytes)
    {
        if (capBytes <= 0)
        {
            return;
        }

        var batches = Directory.EnumerateDirectories(archiveRoot)
            .Select(d => new DirectoryInfo(d))
            .OrderBy(d => d.LastWriteTimeUtc)
            .ToList();
        while (TotalSize(batches) > capBytes && batches.Count > 1)
        {
            var oldest = batches[0];
            batches.RemoveAt(0);
            try
            {
                FileSystemDirectoryRecycle(oldest.FullName);
            }
            catch
            {
                // 容量控制失败不阻塞主流程，记入下一轮审计。
            }
        }
    }

    private static long TotalSize(IReadOnlyList<DirectoryInfo> dirs) =>
        dirs.Sum(d => EnumerateAllFilesSafe(d).Sum(f =>
        {
            try
            {
                return f.Length;
            }
            catch
            {
                return 0;
            }
        }));

    private static IEnumerable<FileInfo> EnumerateAllFilesSafe(DirectoryInfo dir)
    {
        try
        {
            return dir.EnumerateFiles("*", SearchOption.AllDirectories);
        }
        catch
        {
            return Array.Empty<FileInfo>();
        }
    }

    private static void FileSystemDirectoryRecycle(string path) =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
            path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

    private static string ComputeManifestHash(IReadOnlyList<CleanManifestItem> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items.Where(i => i.Checked && !i.Locked))
        {
            sb.AppendLine($"{item.FullPath}|{item.SizeBytes}|{item.ModifiedTime:O}");
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
}

