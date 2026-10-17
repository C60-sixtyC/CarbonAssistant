using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace DeskKeeper.App.Services;

/// <summary>
/// 点击涟漪效果：在按钮模板中的 RippleHost 画布上，从点击处扩散并淡出。
/// </summary>
public static class Ripple
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(Ripple),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Button button)
        {
            if ((bool)e.NewValue)
            {
                button.PreviewMouseDown += OnPreviewMouseDown;
            }
            else
            {
                button.PreviewMouseDown -= OnPreviewMouseDown;
            }
        }
    }

    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (button.Template?.FindName("RippleHost", button) is not Canvas host)
        {
            return;
        }

        var position = e.GetPosition(host);
        var diameter = Math.Max(host.ActualWidth, host.ActualHeight) * 2.2;
        if (diameter <= 0)
        {
            diameter = 120;
        }

        var ripple = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0, 0),
        };
        var color = (button.Foreground as SolidColorBrush)?.Color ?? Colors.Gray;
        ripple.Fill = new SolidColorBrush(color) { Opacity = 0.24 };

        Canvas.SetLeft(ripple, position.X - diameter / 2);
        Canvas.SetTop(ripple, position.Y - diameter / 2);
        host.Children.Add(ripple);

        var scaleX = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var scaleY = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var fade = new DoubleAnimation(0.24, 0, TimeSpan.FromMilliseconds(260))
        {
            BeginTime = TimeSpan.FromMilliseconds(160),
        };

        scaleX.Completed += (_, _) => host.Children.Remove(ripple);
        ripple.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        ripple.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        ripple.Fill.BeginAnimation(Brush.OpacityProperty, fade);
    }
}

