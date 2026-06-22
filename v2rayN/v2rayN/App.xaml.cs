namespace v2rayN;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static EventWaitHandle ProgramStarted;

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    /// <summary>
    /// Open only one process
    /// </summary>
    /// <param name="e"></param>
    protected override void OnStartup(StartupEventArgs e)
    {
        var exePathKey = Utils.GetMd5(Utils.GetExePath());

        var rebootas = e.Args.Any(t => t == Global.RebootAs);
        ProgramStarted = new EventWaitHandle(false, EventResetMode.AutoReset, exePathKey, out var bCreatedNew);
        if (!rebootas && !bCreatedNew)
        {
            ProgramStarted.Set();
            Environment.Exit(0);
            return;
        }

        if (!AppManager.Instance.InitApp())
        {
            UI.Show($"Loading GUI configuration file is abnormal,please restart the application{Environment.NewLine}加载GUI配置文件异常,请重启应用");
            Environment.Exit(0);
            return;
        }

        AppManager.Instance.InitComponents();

        RxAppBuilder.CreateReactiveUIBuilder()
            .WithWpf()
            .BuildApp();

        base.OnStartup(e);
        // ✅ سوال AutoRun
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            await CheckAndAskForAutoStart();
        }), DispatcherPriority.ContextIdle);
    }
    private async Task CheckAndAskForAutoStart()
    {
        try
        {
            if (AppManager.Instance.Config.GuiItem.AutoRunAsked)
                return;

            if (!Utils.IsWindows())
                return;

            if (!AppManager.Instance.Config.GuiItem.AutoRun)
            {
                var result = MessageBox.Show(
                    "آیا می‌خواهید v2rayN با دسترسی ادمین و به‌صورت خودکار هنگام بالا آمدن ویندوز اجرا شود؟\n\n" +
                    "✅ در صورت تایید، برنامه با بالاترین سطح دسترسی اجرا خواهد شد.\n" +
                    "❌ در صورت رد، همچنان می‌توانید بعداً از تنظیمات فعالش کنید.",
                    "تنظیمات اجرای خودکار",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    AppManager.Instance.Config.GuiItem.AutoRun = true;
                    AppManager.Instance.Config.GuiItem.AutoRunAsked = true;
                    ConfigHandler.SaveConfig(AppManager.Instance.Config);

                    await AutoStartupHandler.UpdateTask(AppManager.Instance.Config);

                    MessageBox.Show(
                        "تنظیمات اعمال شد.\nاز دفعه بعد با بالا آمدن ویندوز اجرا خواهد شد.",
                        "موفق",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
                else
                {
                    AppManager.Instance.Config.GuiItem.AutoRunAsked = true;
                    ConfigHandler.SaveConfig(AppManager.Instance.Config);
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("CheckAndAskForAutoStart", ex);
        }
    }
    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logging.SaveLog("App_DispatcherUnhandledException", e.Exception);
        e.Handled = true;
    }

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

    protected override void OnExit(ExitEventArgs e)
    {
        Logging.SaveLog("OnExit");
        base.OnExit(e);
        Process.GetCurrentProcess().Kill();
    }
}
