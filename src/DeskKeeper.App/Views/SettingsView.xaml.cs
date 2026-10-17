using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DeskKeeper.App.Services;

namespace DeskKeeper.App.Views;

public partial class SettingsView : UserControl
{
    private bool _loading;

    public SettingsView()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        AboutVersionText.Text = $"版本 {version?.ToString(3) ?? "1.0.0"} · 离线可用 · 单文件发布";
        Loaded += (_, _) => LoadFromConfig();
    }

    private void LoadFromConfig()
    {
        _loading = true;
        var profile = App.Config.Profiles[App.Config.ActiveProfile];
        ModeArchive.IsChecked = profile.Cleanup.Mode == "archive";
        ModeRecycle.IsChecked = profile.Cleanup.Mode == "recycle";
        PinThisPcBox.IsChecked = profile.Layout.PinThisPcTopLeft;
        PinRecycleBox.IsChecked = profile.Layout.PinRecycleBinTopLeftSecond;
        LoadAccentOptions();
        KeepDaysSlider.Value = profile.Cleanup.KeepDays;
        ArchiveRootBox.Text = profile.Cleanup.ArchiveRoot;
        CapSlider.Value = Math.Clamp(profile.Cleanup.ArchiveCapGB, 1, 20);
        KeepDaysLabel.Text = $"保留最近 {profile.Cleanup.KeepDays} 天（其余进入候选）";
        CapLabel.Text = $"归档上限 {profile.Cleanup.ArchiveCapGB:F0} GB（超出自动清理最早批次）";
        RuleList.Items.Clear();
        foreach (var rule in profile.Rules)
        {
            RuleList.Items.Add($"{rule.Order,5}  {RuleSummary(rule)}");
        }

        _loading = false;
    }

    private void LoadAccentOptions()
    {
        AccentBox.Items.Clear();
        AddAccentItem("自动（跟随壁纸）", null);
        var selectedIndex = 0;
        foreach (var color in WallpaperAccent.ExtractPalette())
        {
            AddAccentItem(Hex(color), color);
        }

        if (!string.IsNullOrWhiteSpace(App.Config.AccentColor))
        {
            var existing = AccentBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => i.Tag is Color c && Hex(c).Equals(App.Config.AccentColor, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                selectedIndex = AccentBox.Items.IndexOf(existing);
            }
            else if (App.ParseHex(App.Config.AccentColor) is { } saved)
            {
                AddAccentItem(Hex(saved), saved);
                selectedIndex = AccentBox.Items.Count - 1;
            }
        }

        AccentBox.SelectedIndex = selectedIndex;
    }

    private void AddAccentItem(string label, Color? color)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Rectangle
        {
            Width = 22,
            Height = 22,
            Margin = new Thickness(0, 0, 10, 0),
            Fill = color is { } c
                ? new SolidColorBrush(c)
                : new SolidColorBrush(Color.FromRgb(0x0F, 0x6C, 0xBD)),
        });
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        AccentBox.Items.Add(new ComboBoxItem { Content = panel, Tag = color });
    }

    private void OnAccentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AccentBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (item.Tag is Color selected)
        {
            App.SetAccent(selected);
            App.Config.AccentColor = Hex(selected);
        }
        else
        {
            var automatic = WallpaperAccent.Extract();
            App.SetAccent(automatic);
            App.Config.AccentColor = null;
        }
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private void OnKeepDaysChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loading)
        {
            KeepDaysLabel.Text = $"保留最近 {e.NewValue:F0} 天（其余进入候选）";
        }
    }

    private void OnCapChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loading)
        {
            CapLabel.Text = $"归档上限 {e.NewValue:F0} GB（超出自动清理最早批次）";
        }
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        // 仅保存时生效。
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var profile = App.Config.Profiles[App.Config.ActiveProfile];
        profile.Cleanup.Mode = ModeArchive.IsChecked == true ? "archive" : "recycle";
        profile.Layout.PinThisPcTopLeft = PinThisPcBox.IsChecked == true;
        profile.Layout.PinRecycleBinTopLeftSecond = PinRecycleBox.IsChecked == true;
        profile.Cleanup.KeepDays = Math.Clamp((int)KeepDaysSlider.Value, 1, 90);
        profile.Cleanup.ArchiveRoot = ArchiveRootBox.Text.Trim();
        profile.Cleanup.ArchiveCapGB = Math.Clamp(CapSlider.Value, 1, 20);
        App.ConfigStore.Save(App.Config);
        SaveResult.Text = "已保存（写入前自动备份 .bak）。";
    }

    private void OnUndoArrange(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = AppServices.LoadProfile();
            var (arrange, _) = AppServices.BuildUseCases();
            var result = arrange.RestoreLastLayout(profile);
            SaveResult.Text = result.Aborted
                ? $"回退失败：{result.AbortReason}"
                : $"已回退 {result.Moved} 个图标"
                  + (result.Failures.Count > 0 ? $"，{result.Failures.Count} 个未回退。" : "。");
        }
        catch (Exception ex)
        {
            SaveResult.Text = $"回退失败：{ex.Message}";
        }
    }

    private static string RuleSummary(DeskKeeper.Infrastructure.Storage.RuleDto rule)
    {
        var match = new List<string>();
        if (rule.Match.Kind is not null)
        {
            match.Add($"kind={rule.Match.Kind}");
        }

        if (rule.Match.Ext is { Count: > 0 })
        {
            match.Add($"ext={string.Join(",", rule.Match.Ext)}");
        }

        if (rule.Match.NameGlob is not null)
        {
            match.Add($"glob={rule.Match.NameGlob}");
        }

        if (rule.Match.SizeMaxKB is not null)
        {
            match.Add($"size≤{rule.Match.SizeMaxKB}KB");
        }

        if (match.Count == 0)
        {
            match.Add("全部");
        }

        var action = rule.Action.Exclude ? "排除" : $"去 {rule.Action.Zone ?? "files"} 区";
        if (rule.Action.Disposal is not null)
        {
            action += $"/{rule.Action.Disposal}";
        }

        return $"[{string.Join(" & ", match)}] → {action}";
    }
}

