using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DeskKeeper.App.Services;
using DeskKeeper.App.Views;

namespace DeskKeeper.App;

public partial class MainWindow : Window
{
    private readonly HomeView _home = new();
    private readonly CleanListView _clean = new();
    private readonly SettingsView _settings = new();
    private readonly TrayIconController _tray;
    private readonly Random _random = new();
    private static readonly string[] StatusPhrases =
    {
        "数据飞刀已部署",
        "数据从不说谎",
        "有人要倒霉了",
        "他做初一，我做十五",
    };

    public MainWindow()
    {
        InitializeComponent();
        _tray = new TrayIconController(this);
        PageHost.Content = _home;
        PreviewMouseDown += OnAnyMouseDown;

        if (App.SnapshotStore.HasPendingRollback)
        {
            BannerText.Text = "检测到上次整理可能未完成，可在「清理清单」页撤销或恢复布局。";
        }
    }

    internal void ShowStatus(string text) => BannerText.Text = text;

    private void OnAnyMouseDown(object sender, MouseButtonEventArgs e) => ShowRandomStatus();

    private void ShowRandomStatus()
    {
        BannerText.Text = StatusPhrases[_random.Next(StatusPhrases.Length)];

        var bounce = new DoubleAnimationUsingKeyFrames();
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(1.18, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)))
        {
            EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 3 },
        });

        BannerText.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
        BannerText.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
    }

    private void OnNavHome(object sender, RoutedEventArgs e) => Navigate(0);
    private void OnNavClean(object sender, RoutedEventArgs e) => Navigate(1);
    private void OnNavSettings(object sender, RoutedEventArgs e) => Navigate(2);

    private void Navigate(int page)
    {
        PageHost.Content = page switch
        {
            1 => _clean,
            2 => _settings,
            _ => _home,
        };
        NavHome.Style = (Style)FindResource(page == 0 ? "BigButton" : "GhostButton");
        NavClean.Style = (Style)FindResource(page == 1 ? "BigButton" : "GhostButton");
        NavSettings.Style = (Style)FindResource(page == 2 ? "BigButton" : "GhostButton");
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray.Dispose();
        base.OnClosed(e);
    }
}

