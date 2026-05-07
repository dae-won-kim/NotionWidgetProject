using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WidgetDesktop;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private TrayIcon?   _trayIcon;

    internal static WindowIcon AppIcon { get; } = CreateAppIcon();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _mainWindow      = new MainWindow { Icon = AppIcon };
            desktop.MainWindow = _mainWindow;
            _mainWindow.Show();

            SetupTrayIcon();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TrayIcon
        {
            Icon        = AppIcon,
            ToolTipText = "Daily Checklist",
            IsVisible   = true
        };

        var showItem = new NativeMenuItem("보이기 / 숨기기");
        showItem.Click += (_, _) => ToggleMainWindow();

        bool autoOn = GetAutoStart();
        var autoStart = new NativeMenuItem(autoOn ? "☑ 시작 시 자동 실행" : "☐ 시작 시 자동 실행");
        autoStart.Click += (_, _) =>
        {
            autoOn = !autoOn;
            SetAutoStart(autoOn);
            autoStart.Header = autoOn ? "☑ 시작 시 자동 실행" : "☐ 시작 시 자동 실행";
        };

        var exitItem = new NativeMenuItem("종료");
        exitItem.Click += (_, _) => ExitApp();

        var menu = new NativeMenu();
        menu.Add(showItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(autoStart);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exitItem);

        _trayIcon.Menu     = menu;
        _trayIcon.Clicked += (_, _) => ToggleMainWindow();

        var icons = new TrayIcons();
        icons.Add(_trayIcon);
        TrayIcon.SetIcons(this, icons);
    }

    private void ToggleMainWindow()
    {
        if (_mainWindow == null) return;
        if (_mainWindow.IsVisible)
        {
            _mainWindow.SaveWindowState();
            _mainWindow.Hide();
        }
        else
        {
            _mainWindow.Show();
            _mainWindow.Activate();
        }
    }

    internal void ExitApp()
    {
        _mainWindow?.AllowClose();
        _mainWindow?.Close();
        _trayIcon?.Dispose();
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private static WindowIcon CreateAppIcon()
    {
        var bmp = new WriteableBitmap(
            new PixelSize(32, 32), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);

        using (var fb = bmp.Lock())
        {
            var addr = fb.Address;
            const double cx = 15.5, cy = 15.5, r = 14.5;
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                double dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy > r * r) continue;
                int i = (y * 32 + x) * 4;
                Marshal.WriteByte(addr, i,     217); // B
                Marshal.WriteByte(addr, i + 1, 40);  // G  → #6D28D9 purple
                Marshal.WriteByte(addr, i + 2, 109); // R
                Marshal.WriteByte(addr, i + 3, 255); // A
            }
        }

        using var ms = new MemoryStream();
        bmp.Save(ms);
        ms.Position = 0;
        return new WindowIcon(ms);
    }

#pragma warning disable CA1416
    private static bool GetAutoStart()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("NotionWidget") != null;
        }
        catch { return false; }
    }

    private static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (enable)
            {
                var exe = Environment.ProcessPath ?? "";
                if (!string.IsNullOrEmpty(exe))
                    key?.SetValue("NotionWidget", $"\"{exe}\"");
            }
            else
            {
                key?.DeleteValue("NotionWidget", throwOnMissingValue: false);
            }
        }
        catch { }
    }
#pragma warning restore CA1416
}
