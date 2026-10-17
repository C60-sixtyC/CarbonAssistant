using DeskKeeper.Domain.Contracts;
using DeskKeeper.Domain.Layout;
using DeskKeeper.Domain.Models;
using DeskKeeper.Domain.Rules;
using DeskKeeper.Domain.Sorting;
using DeskKeeper.Infrastructure.Audit;
using DeskKeeper.Infrastructure.Storage;

namespace DeskKeeper.Application.UseCases;

public sealed record ArrangeItem(
    string Name,
    string FullPath,
    Zone Zone,
    bool Pinned,
    bool Excluded,
    string? ExcludeReason);

public sealed record ArrangePreview(
    string ProfileName,
    IReadOnlyList<ArrangeItem> Items,
    IReadOnlyList<LayoutSlot> PlannedSlots,
    IReadOnlyList<LayoutSlot> KeptSlots,
    IReadOnlyList<LayoutSlot> Overflow,
    int Columns,
    int FillRows,
    bool DegradedToTwoColumns,
    IReadOnlyList<string> Warnings,
    bool OneDriveDetected,
    bool? AutoArrangeEnabled);

public sealed record ArrangeResult(
    int Moved,
    int Kept,
    IReadOnlyList<string> Failures,
    bool Aborted,
    string? AbortReason);

/// <summary>
/// 上课整理用例：枚举 → 规则分区 → 布局计算 → 快照增量 diff → 沙盘预览 → 落位。
/// </summary>
public sealed class ArrangeUseCase
{
    private readonly IDesktopItemSource _source;
    private readonly IDesktopIconPositioner _positioner;
    private readonly ILayoutParametersProvider _layoutParameters;
    private readonly SnapshotStore _snapshots;
    private readonly AuditLogger _audit;
    private readonly INaturalStringComparer _comparer;
    private readonly IClock _clock;
    private readonly string _hostName;

    public ArrangeUseCase(
        IDesktopItemSource source,
        IDesktopIconPositioner positioner,
        ILayoutParametersProvider layoutParameters,
        SnapshotStore snapshots,
        AuditLogger audit,
        INaturalStringComparer comparer,
        IClock? clock = null,
        string? hostName = null)
    {
        _source = source;
        _positioner = positioner;
        _layoutParameters = layoutParameters;
        _snapshots = snapshots;
        _audit = audit;
        _comparer = comparer;
        _clock = clock ?? new SystemClock();
        _hostName = hostName ?? Environment.MachineName;
    }

    public ArrangePreview Preview(RuleProfile profile)
    {
        var warnings = new List<string>();
        var autoArrange = _positioner.TryDetectAutoArrange();
        if (autoArrange == true)
        {
            warnings.Add("系统「自动排列图标」已开启，执行前请先关闭，否则系统会覆盖布局结果。");
        }

        var engine = new RuleEngine(profile.Rules);
        var items = new List<ArrangeItem>();
        var zoneItems = new List<ZoneItem>();
        var oneDrive = false;

        var snapshot = _snapshots.Load().Snapshot;
        var pinnedPaths = (snapshot?.Items ?? new List<SnapshotItem>())
            .Where(i => i.Pinned)
            .Select(i => i.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _source.Enumerate())
        {
            if (item.Kind == DesktopItemKind.SystemIcon)
            {
                continue;
            }

            oneDrive |= item.IsOneDriveManaged;
            if (item.IsOneDriveManaged)
            {
                items.Add(new ArrangeItem(item.Name, item.FullPath, Zone.None, item.IsPinned, true, "OneDrive 桌面托管，已整体拦截"));
                continue;
            }

            if (item.IsReadOnly)
            {
                items.Add(new ArrangeItem(item.Name, item.FullPath, Zone.None, item.IsPinned, true, "公共桌面项，无权移动"));
                continue;
            }

            var decision = engine.Evaluate(item, _clock.Now);
            if (decision.Exclude)
            {
                items.Add(new ArrangeItem(item.Name, item.FullPath, decision.Zone, decision.Pin, true, $"命中排除规则（{decision.RuleId ?? decision.RuleOrder.ToString()}）"));
                continue;
            }

            var pinned = decision.Pin || pinnedPaths.Contains(item.FullPath);
            items.Add(new ArrangeItem(item.Name, item.FullPath, decision.Zone, pinned, false, null));
            zoneItems.Add(new ZoneItem(item with { IsPinned = pinned }, decision.Zone));
        }

        if (oneDrive)
        {
            warnings.Add("检测到 OneDrive 桌面托管，相关项已跳过。");
        }

        var p = _layoutParameters.Get();
        var plan = LayoutEngine.Compute(
            zoneItems,
            p.WorkAreaWidth,
            p.WorkAreaHeight,
            p.CellWidth,
            p.CellHeight,
            profile.Layout.ZoneRatio,
            profile.Layout.MinSlotsPerZone,
            profile.Layout.ReserveTitleRow,
            _comparer);

        // 增量 diff：快照中存在且未被用户改动的项保持原位（kept）。
        var snapshotMap = (snapshot?.Items ?? new List<SnapshotItem>())
            .ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);
        var kept = new List<LayoutSlot>();
        var planned = new List<LayoutSlot>();
        foreach (var slot in plan.Slots)
        {
            if (snapshotMap.TryGetValue(slot.Path, out var old) && old.Pinned)
            {
                kept.Add(new LayoutSlot(slot.Path, slot.Name, slot.Zone, old.XPct, old.YPct));
            }
            else
            {
                planned.Add(slot);
            }
        }

        if (plan.DegradedToTwoColumns)
        {
            warnings.Add("屏幕过小，已降级为两列布局（应用+文件夹合并）。");
        }

        if (plan.Overflow.Count > 0)
        {
            warnings.Add($"{plan.Overflow.Count} 个图标超出布局容量，本次未落位。");
        }

        return new ArrangePreview(
            profile.Name,
            items,
            planned,
            kept,
            plan.Overflow,
            plan.Columns,
            plan.FillRows,
            plan.DegradedToTwoColumns,
            warnings,
            oneDrive,
            autoArrange);
    }

    public ArrangeResult Apply(ArrangePreview preview, RuleProfile profile, bool dryRun = false)
    {
        if (preview.AutoArrangeEnabled == true)
        {
            var abort = new ArrangeResult(0, 0, Array.Empty<string>(), true, "自动排列图标已开启，请先手动关闭。");
            Audit(profile, "arrange", dryRun: true, moved: 0, failures: Array.Empty<AuditFailure>(), aborted: abort.AbortReason);
            return abort;
        }

        if (dryRun)
        {
            Audit(profile, "arrange", dryRun: true, moved: preview.PlannedSlots.Count, failures: Array.Empty<AuditFailure>(), aborted: null);
            return new ArrangeResult(preview.PlannedSlots.Count, preview.KeptSlots.Count, Array.Empty<string>(), false, null);
        }

        // 回滚保护：执行前保存当前快照，成功后清除。
        var current = _snapshots.Load().Snapshot;
        _snapshots.SaveRollback(current ?? new Snapshot { SavedAt = _clock.Now });

        var failures = new List<string>();
        var moved = 0;
        var p = _layoutParameters.Get();
        foreach (var slot in preview.PlannedSlots)
        {
            var x = p.WorkAreaLeft + slot.XPct / 100.0 * p.WorkAreaWidth;
            var y = p.WorkAreaTop + slot.YPct / 100.0 * p.WorkAreaHeight;
            if (_positioner.TryMoveIcon(slot.Path, slot.Name, x, y, out var error))
            {
                moved++;
            }
            else
            {
                failures.Add($"{slot.Name}: {error}");
            }
        }

        PersistSnapshot(preview);
        _snapshots.ClearRollback();

        var auditFailures = failures.Select(f => new AuditFailure(f, "位置写入失败")).ToList();
        Audit(profile, "arrange", dryRun: false, moved, auditFailures, null);
        return new ArrangeResult(moved, preview.KeptSlots.Count, failures, false, null);
    }

    public ArrangeResult RestoreFromRollback(RuleProfile profile)
    {
        var rollback = _snapshots.LoadRollback();
        if (rollback is null)
        {
            return new ArrangeResult(0, 0, Array.Empty<string>(), true, "没有可回滚的快照。");
        }

        var failures = new List<string>();
        var moved = 0;
        var p = _layoutParameters.Get();
        foreach (var item in rollback.Items)
        {
            var x = p.WorkAreaLeft + item.XPct / 100.0 * p.WorkAreaWidth;
            var y = p.WorkAreaTop + item.YPct / 100.0 * p.WorkAreaHeight;
            if (_positioner.TryMoveIcon(item.Path, item.Name, x, y, out var error))
            {
                moved++;
            }
            else
            {
                failures.Add($"{item.Name}: {error}");
            }
        }

        _snapshots.ClearRollback();
        return new ArrangeResult(moved, 0, failures, false, null);
    }

    private void PersistSnapshot(ArrangePreview preview)
    {
        var p = _layoutParameters.Get();
        var snapshot = new Snapshot
        {
            SavedAt = _clock.Now,
            WorkAreaWidth = p.WorkAreaWidth,
            WorkAreaHeight = p.WorkAreaHeight,
        };

        foreach (var slot in preview.KeptSlots.Concat(preview.PlannedSlots))
        {
            snapshot.Items.Add(new SnapshotItem
            {
                Path = slot.Path,
                Name = slot.Name,
                Zone = ZoneToString(slot.Zone),
                XPct = slot.XPct,
                YPct = slot.YPct,
                Pinned = preview.Items.FirstOrDefault(i => i.FullPath == slot.Path)?.Pinned ?? false,
            });
        }

        _snapshots.Save(snapshot);
    }

    private void Audit(
        RuleProfile profile,
        string action,
        bool dryRun,
        int moved,
        IReadOnlyList<AuditFailure> failures,
        string? aborted)
    {
        _audit.Append(new AuditRecord(
            _clock.Now,
            _hostName,
            null,
            profile.Id,
            action,
            dryRun,
            moved,
            0,
            failures,
            ConfigHash.Compute(profile),
            aborted is null ? null : $"aborted:{aborted}"));
    }

    private static string ZoneToString(Zone zone) => zone switch
    {
        Zone.Apps => "apps",
        Zone.Folders => "folders",
        _ => "files",
    };
}

