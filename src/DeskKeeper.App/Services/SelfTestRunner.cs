using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DeskKeeper.Application.Mapping;
using DeskKeeper.Application.UseCases;
using DeskKeeper.Domain.Contracts;
using DeskKeeper.Domain.Models;
using DeskKeeper.Domain.Rules;
using DeskKeeper.Infrastructure.Audit;
using DeskKeeper.Infrastructure.Desktop;
using DeskKeeper.Infrastructure.Shell;
using DeskKeeper.Infrastructure.Storage;
using DeskKeeper.Infrastructure.Windows;

namespace DeskKeeper.App.Services;

/// <summary>
/// 内建自检：在临时目录跑通「清理→归档→撤销」与「排列→落位→快照」全链路，
/// 供无 GUI 环境验证（DeskKeeper.exe --selftest）。
/// </summary>
public static class SelfTestRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Run()
    {
        var details = new List<string>();
        try
        {
            var root = Path.Combine(Path.GetTempPath(), $"deskkeeper-selftest-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            try
            {
                TestCleanFlow(root, details);
                TestArrangeFlow(root, details);
                return JsonSerializer.Serialize(new { ok = true, details }, JsonOptions);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch
                {
                    // 清理临时目录失败不影响结论。
                }
            }
        }
        catch (Exception ex)
        {
            details.Add($"失败：{ex.Message}");
            return JsonSerializer.Serialize(new { ok = false, details }, JsonOptions);
        }
    }

    private static void TestCleanFlow(string root, List<string> details)
    {
        var scope = Path.Combine(root, "课件桌面");
        var archive = Path.Combine(root, "归档");
        var state = Path.Combine(root, "state");
        Directory.CreateDirectory(scope);
        Directory.CreateDirectory(archive);
        Directory.CreateDirectory(state);

        var old = DateTime.UtcNow.AddDays(-40);
        var recent = DateTime.UtcNow.AddDays(-1);
        var pptx = Path.Combine(scope, "公开课.pptx");
        File.WriteAllText(pptx, "课件内容");
        File.SetLastWriteTimeUtc(pptx, old);
        var notes = Path.Combine(scope, "讲义笔记.txt");
        File.WriteAllText(notes, "note");
        File.SetLastWriteTimeUtc(notes, old);
        var tmp = Path.Combine(scope, "临时下载.tmp");
        File.WriteAllText(tmp, new string('a', 512));
        File.SetLastWriteTimeUtc(tmp, old);
        var fresh = Path.Combine(scope, "新文件.txt");
        File.WriteAllText(fresh, "fresh");
        File.SetLastWriteTimeUtc(fresh, recent);
        var schedule = Path.Combine(scope, "下周课表.xlsx");
        File.WriteAllText(schedule, "schedule");
        File.SetLastWriteTimeUtc(schedule, old);

        var profile = ProfileFactory.ToDomain(
            "classroom",
            ConfigStore.CreateDefaultConfig().Profiles["classroom"]);
        profile = profile with
        {
            Cleanup = profile.Cleanup with { ArchiveRoot = archive },
        };

        var clean = new CleanUseCase(
            new DesktopEnumerator(new ShellLinkResolver(), new[] { (scope, "user") }),
            new FileSystemOps(),
            new RecycleBinProbe(new DictionaryRegistryReader()),
            new AuditLogger(Path.Combine(state, "logs"), clock: new FixedClock(DateTimeOffset.Now)),
            new UndoStore(state),
            clock: new FixedClock(DateTimeOffset.Now));

        var manifest = clean.MarkLocked(clean.BuildManifest(profile));
        Assert(manifest.ExcludedCount == 1, $"课表应被规则排除，实际 {manifest.ExcludedCount}", details);
        Assert(!manifest.OneDriveBlocked, "普通目录不应触发 OneDrive 拦截", details);
        Assert(!manifest.Items.Single(i => i.FullPath == pptx).Checked, "教学文档默认不应勾选", details);
        Assert(manifest.Items.Single(i => i.FullPath == tmp).Checked, "临时文件应勾选", details);
        Assert(manifest.Items.Single(i => i.FullPath == notes).Checked, "过期文件应勾选", details);
        Assert(!manifest.Items.Single(i => i.FullPath == fresh).Checked, "新文件不应勾选", details);

        var dryRun = clean.Execute(manifest, profile, dryRun: true);
        Assert(dryRun.DryRun, "空跑标记", details);
        Assert(File.Exists(notes) && File.Exists(tmp), "空跑不得移动文件", details);

        var result = clean.Execute(manifest, profile, dryRun: false);
        Assert(result.Cleaned == 2, $"应清理 2 项，实际 {result.Cleaned}", details);
        Assert(!File.Exists(notes) && !File.Exists(tmp), "归档后源文件应移走", details);
        Assert(File.Exists(pptx) && File.Exists(fresh), "受保护与新文件应保持不动", details);
        Assert(Directory.EnumerateDirectories(archive).Any(), "应生成归档批次目录", details);

        var restored = clean.RestoreLastClean();
        Assert(restored.Cleaned == 2, $"应还原 2 项，实际 {restored.Cleaned}", details);
        Assert(File.Exists(notes) && File.Exists(tmp), "撤销后文件应回到原处", details);
        Assert(File.Exists(Path.Combine(state, "logs", "audit.jsonl")), "应写审计日志", details);
        details.Add("清理→归档→撤销链路通过");
    }

    private static void TestArrangeFlow(string root, List<string> details)
    {
        var state = Path.Combine(root, "arrange-state");
        Directory.CreateDirectory(state);

        var items = new List<DesktopItem>
        {
            MakeItem("腾讯会议.lnk", DesktopItemKind.AppLink),
            MakeItem("讲义", DesktopItemKind.Folder),
            MakeItem("笔记.txt", DesktopItemKind.File),
        };
        var positioner = new RecordingPositioner { AutoArrange = false };
        var snapshots = new SnapshotStore(state);
        var profile = ProfileFactory.ToDomain(
            "classroom",
            ConfigStore.CreateDefaultConfig().Profiles["classroom"]);
        profile = profile with
        {
            Layout = profile.Layout with
            {
                PinThisPcTopLeft = false,
                PinRecycleBinTopLeftSecond = false,
            },
        };
        var arrange = new ArrangeUseCase(
            new ListItemSource(items),
            positioner,
            new FixedLayoutProvider(0, 0, 1920, 1080, 96, 96),
            snapshots,
            new AuditLogger(Path.Combine(state, "logs"), clock: new FixedClock(DateTimeOffset.Now)),
            new NaturalStringComparer(),
            clock: new FixedClock(DateTimeOffset.Now));

        var preview = arrange.Preview(profile);
        Assert(preview.PlannedSlots.Count == 3, $"应计划 3 个图标，实际 {preview.PlannedSlots.Count}", details);
        Assert(preview.AutoArrangeEnabled == false, "自动排列应为关闭", details);

        var applied = arrange.Apply(preview, profile);
        Assert(applied.Aborted == false, "整理不应被中止", details);
        Assert(applied.Moved == 3, $"应移动 3 个，实际 {applied.Moved}", details);
        Assert(!snapshots.HasPendingRollback, "成功后不应残留回滚标记", details);
        var saved = snapshots.Load().Snapshot!;
        Assert(saved.Items.Count == 3, $"快照应含 3 项，实际 {saved.Items.Count}", details);

        // 增量：固定一个图标后不再移动。
        saved.Items[0].Pinned = true;
        snapshots.Save(saved);
        var second = arrange.Preview(profile);
        Assert(second.KeptSlots.Count == 1, $"应保留 1 个固定图标，实际 {second.KeptSlots.Count}", details);
        var secondApply = arrange.Apply(second, profile);
        Assert(secondApply.Moved == 2, $"第二遍应只移动 2 个，实际 {secondApply.Moved}", details);
        details.Add("排列→落位→快照增量链路通过");
    }

    private static DesktopItem MakeItem(string name, DesktopItemKind kind) => new(
        name,
        Path.Combine(@"C:\Users\teacher\Desktop", name),
        kind,
        1024,
        DateTimeOffset.Now.AddDays(-1),
        DateTimeOffset.Now.AddDays(-1),
        DateTimeOffset.Now.AddDays(-1),
        false,
        false,
        false,
        kind == DesktopItemKind.AppLink ? @"C:\Program Files\App\app.exe" : null,
        false,
        "user");

    private static void Assert(bool condition, string message, List<string> details)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"自检失败：{message}");
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => Now = now;
        public DateTimeOffset Now { get; }
    }

    private sealed class DictionaryRegistryReader : IRegistryReader
    {
        public uint? GetDWord(string subKeyPath, string valueName) => null;
    }

    private sealed class ListItemSource : IDesktopItemSource
    {
        private readonly List<DesktopItem> _items;

        public ListItemSource(List<DesktopItem> items) => _items = items;

        public IReadOnlyList<DesktopItem> Enumerate() => _items;
    }

    private sealed class FixedLayoutProvider : ILayoutParametersProvider
    {
        private readonly LayoutParameters _parameters;

        public FixedLayoutProvider(double left, double top, double w, double h, double cellW, double cellH) =>
            _parameters = new LayoutParameters(left, top, w, h, cellW, cellH);

        public LayoutParameters Get() => _parameters;
    }

    private sealed class RecordingPositioner : IDesktopIconPositioner
    {
        public bool? AutoArrange { get; init; }
        public List<string> Moved { get; } = new();

        public bool TryMoveIcon(string path, string name, double x, double y, out string? error)
        {
            Moved.Add(path);
            error = null;
            return true;
        }

        public bool? TryDetectAutoArrange() => AutoArrange;

        public bool TryReadPositions(out IReadOnlyList<IconPosition> positions)
        {
            positions = Array.Empty<IconPosition>();
            return false;
        }
    }
}

