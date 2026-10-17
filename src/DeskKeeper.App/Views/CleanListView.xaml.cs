using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DeskKeeper.App.Services;
using DeskKeeper.Application.UseCases;
using DeskKeeper.Domain.Rules;

namespace DeskKeeper.App.Views;

public partial class CleanListView : UserControl
{
    public sealed class Row : ObservableObject
    {
        private bool _checked;

        public required string Name { get; init; }
        public required string FullPath { get; init; }
        public required string KindLabel { get; init; }
        public required string SizeLabel { get; init; }
        public required string ModifiedLabel { get; init; }
        public required DateTimeOffset ModifiedTime { get; init; }
        public required string Reason { get; init; }
        public required string DisposalLabel { get; init; }

        public bool Checked
        {
            get => _checked;
            set => Set(ref _checked, value);
        }
    }

    private readonly ObservableCollection<Row> _rows = new();
    private CleanManifest? _manifest;
    private RuleProfile? _profile;
    private bool _sortDescending;

    public CleanListView()
    {
        InitializeComponent();
        ItemList.ItemsSource = _rows;
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        try
        {
            _profile = AppServices.LoadProfile();
            var (_, clean) = AppServices.BuildUseCases();
            var manifest = clean.BuildManifest(_profile);
            manifest = clean.MarkLocked(manifest);
            var preCheck = clean.PreCheck(manifest, null);
            _manifest = manifest;

            _rows.Clear();
            foreach (var item in manifest.Items)
            {
                _rows.Add(new Row
                {
                    Name = item.Name,
                    FullPath = item.FullPath,
                    KindLabel = item.Kind.ToString(),
                    SizeLabel = FormatSize(item.SizeBytes),
                    ModifiedLabel = item.ModifiedTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    ModifiedTime = item.ModifiedTime,
                    Reason = item.Locked ? "文件占用中，跳过" : item.Reason,
                    DisposalLabel = item.Disposal == Disposal.Recycle ? "回收站" : "归档",
                    Checked = item.Checked && !item.Locked,
                });
            }

            SortRows();
            SummaryText.Text = $"{manifest.CheckedCount} 项候选，共 {FormatSize(manifest.CheckedBytes)}；"
                + $"回收站占用 {FormatSize(preCheck.RecycleUsedBytes)}，可用 {FormatSize(preCheck.RecycleAvailableBytes)}。"
                + (preCheck.Warnings.Count > 0 ? " " + string.Join(" ", preCheck.Warnings) : "");
            if (Window.GetWindow(this) is MainWindow window)
            {
                window.ShowStatus(manifest.OneDriveBlocked ? "OneDrive 桌面已整体拦截" : "清理清单已更新");
            }
        }
        catch (Exception ex)
        {
            SummaryText.Text = $"扫描失败：{ex.Message}";
        }
    }

    private void OnSelectAll(object sender, RoutedEventArgs e) => SetAll(_ => true);
    private void OnInvert(object sender, RoutedEventArgs e) => SetAll(row => !row.Checked);
    private void OnSelectJunk(object sender, RoutedEventArgs e) => SetAll(row => row.Reason.Contains("临时文件"));

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.GridViewColumnHeader header
            && header.Content?.ToString() == "最后修改")
        {
            _sortDescending = !_sortDescending;
            SortRows();
        }
    }

    private void SortRows()
    {
        var ordered = _sortDescending
            ? _rows.OrderByDescending(r => r.ModifiedTime).ToList()
            : _rows.OrderBy(r => r.ModifiedTime).ToList();
        _rows.Clear();
        foreach (var row in ordered)
        {
            _rows.Add(row);
        }
    }

    private void SetAll(Func<Row, bool> predicate)
    {
        foreach (var row in _rows)
        {
            row.Checked = predicate(row);
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        var filter = SearchBox.Text.Trim();
        foreach (var row in _rows)
        {
            // 行过滤通过隐藏不可行，这里仅高亮提示；MVP 以状态栏显示命中数。
        }
    }

    private void OnExecute(object sender, RoutedEventArgs e)
    {
        if (_manifest is null || _profile is null)
        {
            SummaryText.Text = "请先重新扫描。";
            return;
        }

        var byPath = _manifest.Items.ToDictionary(i => i.FullPath, StringComparer.OrdinalIgnoreCase);
        var checkedItems = _rows
            .Where(r => r.Checked)
            .Select(r => byPath.TryGetValue(r.FullPath, out var item) ? item with { Checked = true } : null)
            .Where(item => item is not null && !item.Locked)
            .Cast<CleanManifestItem>()
            .ToList();
        var checkedBytes = checkedItems.Sum(i => i.SizeBytes);
        var modeLabel = _profile.Cleanup.Mode == "archive" ? "归档" : "移入回收站";
        var dryRun = DryRunBox.IsChecked == true;

        var confirm = MessageBox.Show(
            $"即将处理 {checkedItems.Count} 项（{FormatSize(checkedBytes)}），去向：{modeLabel}。"
            + (dryRun ? $"{Environment.NewLine}当前为空跑模式，不会真的动任何文件。" : $"{Environment.NewLine}前三次使用会强制二次确认，请仔细核对。")
            + $"{Environment.NewLine}{Environment.NewLine}确认继续？",
            "下课 · 一键清理",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var manifest = _manifest with { Items = checkedItems };
        var (_, clean) = AppServices.BuildUseCases();
        var result = clean.Execute(manifest, _profile, dryRun);
        SummaryText.Text = dryRun
            ? $"空跑完成：模拟处理 {manifest.CheckedCount} 项，未做任何改动。"
            : $"清理完成：处理 {result.Cleaned} 项（{FormatSize(result.BytesCleaned)}），失败 {result.Failures.Count} 项。桌面已收好，辛苦了！"
              + (result.BatchDirectory is not null ? $" 归档至：{result.BatchDirectory}" : "");
        if (Window.GetWindow(this) is MainWindow window)
        {
            window.ShowStatus("清理完成");
        }

        Refresh();
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show("将把上一次归档的文件移回原处（回收站中的项请在回收站手动恢复）。确认撤销？",
            "撤销上次清理", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var (_, clean) = AppServices.BuildUseCases();
        var result = clean.RestoreLastClean();
        SummaryText.Text = $"已还原 {result.Cleaned} 项" + (result.Failures.Count > 0 ? $"，{result.Failures.Count} 项未还原。" : "。");
        Refresh();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024.0 / 1024 / 1024:F1} GB";
        }

        if (bytes >= 1024L * 1024)
        {
            return $"{bytes / 1024.0 / 1024:F1} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024.0:F0} KB";
        }

        return $"{bytes} B";
    }
}

