using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using ReactiveUI;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Views;

namespace v2rayN.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!Design.IsDesignMode)
            {
                AppManager.Instance.InitComponents();
                DataContext = StatusBarViewModel.Instance;
            }

            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;

            if (OperatingSystem.IsMacOS())
            {
                Current?.TryGetFeature<IActivatableLifetime>()?.Activated += OnMacOSActivated;
            }

            // ✅ سوال AutoRun بعد از بارگذاری کامل پنجره
            mainWindow.Loaded += async (s, e) =>
            {
                await CheckAndAskForAutoStart();
            };


        }

        base.OnFrameworkInitializationCompleted();
    }

    // ✅ متد سوال از کاربر
    private async Task CheckAndAskForAutoStart()
    {
        try
        {
            // اگر قبلاً سوال پرسیده شده یا در حال اجرا نیستیم، برگرد
            if (AppManager.Instance.Config.GuiItem.AutoRunAsked)
                return;

            // فقط در ویندوز این قابلیت را فعال کن
            if (!OperatingSystem.IsWindows())
                return;

            var mainWindow = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
            if (mainWindow == null)
                return;

            // اگر AutoRun فعال نیست، سوال بپرس
            if (!AppManager.Instance.Config.GuiItem.AutoRun)
            {
                bool result = await ShowQuestionDialog(
                    mainWindow,
                    "تنظیمات اجرای خودکار",
                    "آیا می‌خواهید v2rayN با دسترسی ادمین و به‌صورت خودکار هنگام بالا آمدن ویندوز اجرا شود؟\n\n" +
                    "✅ در صورت تایید، برنامه با بالاترین سطح دسترسی اجرا خواهد شد.\n" +
                    "❌ در صورت رد، همچنان می‌توانید بعداً از تنظیمات فعالش کنید."
                );

                if (result)
                {
                    // فعال‌سازی AutoRun
                    AppManager.Instance.Config.GuiItem.AutoRun = true;
                    AppManager.Instance.Config.GuiItem.AutoRunAsked = true;

                    // ذخیره تنظیمات
                    await ConfigHandler.SaveConfig(AppManager.Instance.Config);

                    // به‌روزرسانی Task Scheduler
                    await AutoStartupHandler.UpdateTask(AppManager.Instance.Config);

                    // پیام تایید
                    await ShowInfoDialog(
                        mainWindow,
                        "موفق",
                        "تنظیمات اعمال شد.\nاز دفعه بعد با بالا آمدن ویندوز اجرا خواهد شد."
                    );
                }
                else
                {
                    // ذخیره کن که کاربر رد کرده
                    AppManager.Instance.Config.GuiItem.AutoRunAsked = true;
                    await ConfigHandler.SaveConfig(AppManager.Instance.Config);
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("CheckAndAskForAutoStart", ex);
        }
    }

    // ✅ متد نمایش دیالوگ سوال
    private async Task<bool> ShowQuestionDialog(Window owner, string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();

        var btnYes = new Button { Content = "بله", Width = 100 };
        var btnNo = new Button { Content = "خیر", Width = 100 };

        var dialog = new Window
        {
            Title = title,
            Width = 450,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new Grid
            {
                RowDefinitions = new RowDefinitions("*, Auto"),
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        [Grid.RowProperty] = 0
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Spacing = 20,
                        [Grid.RowProperty] = 1,
                        Children = { btnYes, btnNo }
                    }
                }
            }
        };

        btnYes.Command = ReactiveCommand.Create(() => { tcs.SetResult(true); dialog.Close(); });
        btnNo.Command = ReactiveCommand.Create(() => { tcs.SetResult(false); dialog.Close(); });

        await dialog.ShowDialog(owner);
        return await tcs.Task;
    }

    // ✅ متد نمایش دیالوگ اطلاع‌رسانی
    private async Task ShowInfoDialog(Window owner, string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();

        var btnOk = new Button
        {
            Content = "باشه",
            Width = 100,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };

        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new Grid
            {
                RowDefinitions = new RowDefinitions("*, Auto"),
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        [Grid.RowProperty] = 0
                    },
                    btnOk
                }
            }
        };

        btnOk.Command = ReactiveCommand.Create(() => { tcs.SetResult(true); dialog.Close(); });

        await dialog.ShowDialog(owner);
        await tcs.Task;
    }

    #region MacOS Activation

    private void OnMacOSActivated(object? sender, ActivatedEventArgs args)
    {
        if (args.Kind != ActivationKind.Reopen)
        {
            return;
        }

        if ((ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        var isMiniaturized = MacAppUtils.IsWindowMiniaturized(mainWindow);

        Dispatcher.UIThread.Post(() =>
        {
            if (isMiniaturized)
            {
                RestoreMacOSAccessoryPolicyAfterMiniaturize(mainWindow);
                mainWindow.ShowHideWindow(true);
                return;
            }

            if (!AppManager.Instance.Config.UiItem.MacOSShowInDock)
            {
                MacAppUtils.SetActivationPolicyAccessory();
            }

            mainWindow.ShowHideWindow(true);
        });
    }

    private static void RestoreMacOSAccessoryPolicyAfterMiniaturize(MainWindow mainWindow)
    {
        if (AppManager.Instance.Config.UiItem.MacOSShowInDock)
        {
            return;
        }

        mainWindow
            .GetObservable(Window.WindowStateProperty)
            .Skip(1)
            .Where(state => state != WindowState.Minimized)
            .Take(1)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => QueueMacOSAccessoryPolicyRestore(mainWindow));
    }

    private static void QueueMacOSAccessoryPolicyRestore(MainWindow mainWindow)
    {
        DispatcherTimer.RunOnce(() => RestoreMacOSAccessoryPolicy(mainWindow), TimeSpan.FromMilliseconds(300));
    }

    private static void RestoreMacOSAccessoryPolicy(MainWindow mainWindow)
    {
        if (AppManager.Instance.Config.UiItem.MacOSShowInDock || MacAppUtils.IsWindowMiniaturized(mainWindow))
        {
            return;
        }

        MacAppUtils.SetActivationPolicyAccessory();
        mainWindow.Activate();
        mainWindow.Focus();
    }

    #endregion MacOS Activation

    #region App Event

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject != null)
        {
            Logging.SaveLog("CurrentDomain_UnhandledException", (Exception)e.ExceptionObject);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logging.SaveLog("TaskScheduler_UnobservedTaskException", e.Exception);
    }

    private async void MenuAddServerViaClipboardClick(object? sender, EventArgs e)
    {
        try
        {
            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: not null })
            {
                AppEvents.AddServerViaClipboardRequested.Publish();
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("MenuAddServerViaClipboardClick", ex);
        }
    }

    private async void MenuExit_Click(object? sender, EventArgs e)
    {
        await AppManager.Instance.AppExitAsync(false);
        AppManager.Instance.Shutdown(true);
    }

    #endregion App Event
}
