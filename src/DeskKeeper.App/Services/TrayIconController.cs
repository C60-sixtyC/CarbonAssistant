using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace DeskKeeper.App.Services;

public sealed class TrayIconController : IDisposable
{
    private readonly NotifyIcon _icon;
    private bool _reallyExit;

    public TrayIconController(Window window)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开面板", null, (_, _) =>
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        });
        menu.Items.Add("退出", null, (_, _) =>
        {
            _reallyExit = true;
            window.Close();
        });

        _icon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "讲台助手 DeskKeeper",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) =>
        {
            window.Show();
            window.Activate();
        };

        window.Closing += (_, e) =>
        {
            if (_reallyExit || System.Windows.Application.Current.ShutdownMode == System.Windows.ShutdownMode.OnExplicitShutdown)
            {
                return;
            }

            e.Cancel = true;
            window.Hide();
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}

