namespace ToniaSoundGate;

internal static class Program
{
    // 单实例 + 二次启动通知：第一个进程创建并持有该事件；
    // 后续进程 Set 它并立即退出，已运行实例收到后弹提示，避免用户"点了没反应"。
    private const string ActivateEventName = @"Local\Tonia-SoundGate-Activate";

    [STAThread]
    private static void Main()
    {
        using var activateEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: ActivateEventName,
            createdNew: out bool isFirstInstance);

        if (!isFirstInstance)
        {
            activateEvent.Set(); // 唤醒已运行实例，让它提示"程序已在后台运行"
            return;
        }

        ApplicationConfiguration.Initialize();
        // 必须在创建任何窗口之前安装：否则默认的异常对话框会取代我们的处置，
        // 用户随手一点"退出"，守护就静默消失了。
        CrashGuard.Install();

        Application.Run(new TrayAppContext(activateEvent));
    }
}
