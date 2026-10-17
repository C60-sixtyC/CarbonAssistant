using System;
using System.Windows;
using System.Windows.Controls;
using DeskKeeper.App.Services;

namespace DeskKeeper.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void OnClean(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow window)
        {
            window.NavigateClean();
        }
    }

    private void OnArrange(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = AppServices.LoadProfile();
            var (arrange, _) = AppServices.BuildUseCases();
            var preview = arrange.Preview(profile);
            if (preview.Warnings.Count > 0)
            {
                StatusText.Text = string.Join(Environment.NewLine, preview.Warnings);
            }

            var summary =
                $"沙盘预览：将移动 {preview.PlannedSlots.Count} 个图标，保留 {preview.KeptSlots.Count} 个固定/原位图标。"
                + (preview.Overflow.Count > 0 ? $"{Environment.NewLine}注意：{preview.Overflow.Count} 个图标超出容量，本次不动。" : "")
                + (preview.AutoArrangeEnabled == true ? $"{Environment.NewLine}警告：系统「自动排列图标」已开启，执行前请先关闭。" : "");
            var confirm = MessageBox.Show(
                summary + $"{Environment.NewLine}{Environment.NewLine}确认执行整理？",
                "上课 · 整理桌面",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            var result = arrange.Apply(preview, profile);
            StatusText.Text = result.Aborted
                ? $"已取消：{result.AbortReason}"
                : $"整理完成：移动 {result.Moved} 个，保留 {result.Kept} 个。桌面已清爽，辛苦了！";
            if (Window.GetWindow(this) is MainWindow window)
            {
                window.ShowStatus(result.Aborted ? "整理未执行" : "桌面整理完成");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"整理失败：{ex.Message}";
        }
    }
}

