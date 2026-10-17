using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

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
        profile.Cleanup.KeepDays = Math.Clamp((int)KeepDaysSlider.Value, 1, 90);
        profile.Cleanup.ArchiveRoot = ArchiveRootBox.Text.Trim();
        profile.Cleanup.ArchiveCapGB = Math.Clamp(CapSlider.Value, 1, 20);
        App.ConfigStore.Save(App.Config);
        SaveResult.Text = "已保存（写入前自动备份 .bak）。";
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

