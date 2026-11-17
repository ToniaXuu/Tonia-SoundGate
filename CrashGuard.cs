namespace ToniaSoundGate;

/// <summary>
/// 崩溃兜底。对一个"静音守护"程序来说，**静默消失是最严重的故障**：
/// 用户以为还在守护，实际上已经没有任何拦截，一旦开外放就直接泄露。
///
/// 因此这里保证两件事：
/// 1. 任何一次 UI 线程异常都**不会终止进程**——记日志并提示，守护继续；
/// 2. 任何异常（含无法拦截的后台线程异常）和每一次进程退出都会写进
///    %APPDATA%\Tonia-SoundGate\crash.log，让"程序莫名消失"有据可查，
///    而不是只剩用户一句"它就没了"。
/// </summary>
internal static class CrashGuard
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Tonia-SoundGate");

    private static readonly string LogPath = Path.Combine(LogDir, "crash.log");
    // 运行时标记：启动写入、正常退出删除。
    // 下次启动若发现它还在，说明上一次是被 TerminateProcess 强制结束的
    // （Windows 下强制终止不会触发 ProcessExit），而不是崩溃或主动退出。
    private static readonly string RunningFlagPath = Path.Combine(LogDir, "running.flag");
    private static readonly object Sync = new();
    // 必须带 BOM：日志是给人用记事本/PowerShell 直接看的，
    // 无 BOM 的 UTF-8 在这些工具里会显示成乱码，等于没留下线索。
    private static readonly System.Text.Encoding Utf8Bom =
        new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    // 防止异常处理器自身再抛异常导致递归
    private static bool _inHandler;
    private static int _uiErrorCount;

    /// <summary>安装全局异常钩子。必须在创建任何窗口之前调用。</summary>
    public static void Install()
    {
        // 默认行为是弹出 .NET 的异常对话框，用户一点"退出"守护就没了。
        // 改为统一走 ThreadException，自己决定如何处置。
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += OnUiThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        // 先判断上一次是否没走完退出流程——这是"程序凭空消失"最直接的判据。
        try
        {
            if (File.Exists(RunningFlagPath))
            {
                var prev = File.ReadAllText(RunningFlagPath, Utf8Bom).Trim();
                Write("[上次运行未正常结束] 未触发退出流程 → 属于被系统强制终止（TerminateProcess）、被任务管理器结束，或断电/蓝屏；不是程序崩溃，也不是用户点退出。");
                Write($"[上次运行未正常结束] 遗留标记: {prev}");
            }
            File.WriteAllText(RunningFlagPath, $"pid={Environment.ProcessId} 启动于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}", Utf8Bom);
        }
        catch
        {
            // 标记失败不影响主功能
        }

        Write($"===== 启动 v{typeof(CrashGuard).Assembly.GetName().Version?.ToString(3)} pid={Environment.ProcessId} =====");
    }

    /// <summary>UI 线程异常：记下来，但**绝不**让进程退出。</summary>
    private static void OnUiThreadException(object sender, ThreadExceptionEventArgs e)
    {
        _uiErrorCount++;
        Write($"[UI-异常 #{_uiErrorCount}] {e.Exception}");

        if (_inHandler) return;
        _inHandler = true;
        try
        {
            var detail = _uiErrorCount == 1
                ? "该操作已取消，静音守护仍在运行"
                : $"该操作已取消（累计 {_uiErrorCount} 次），详情见 crash.log";
            ToastService.Show("操作未完成", detail, ToastKind.Warning, 4000);
        }
        catch
        {
            // 连提示都失败时至少要保证进程活着
        }
        finally
        {
            _inHandler = false;
        }
    }

    /// <summary>非 UI 线程异常：无法阻止进程终止，但必须留下现场。</summary>
    private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        Write($"[后台线程致命异常] terminating={e.IsTerminating} {e.ExceptionObject}");
    }

    /// <summary>记录每一次正常/非正常的进程退出，用于判断"程序是崩了还是被关了"。</summary>
    private static void OnProcessExit(object? sender, EventArgs e)
    {
        Write($"===== 进程退出 exitcode={Environment.ExitCode} ====={Environment.NewLine}");
        try { File.Delete(RunningFlagPath); } catch { /* 删除失败会在下次启动被当作非正常结束 */ }
    }

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}", Utf8Bom);
                TrimIfTooLarge();
            }
        }
        catch
        {
            // 日志失败绝不能影响主功能
        }
    }

    /// <summary>日志超过 256KB 就只保留最近的部分，避免长期运行无限增长。</summary>
    private static void TrimIfTooLarge()
    {
        try
        {
            var info = new FileInfo(LogPath);
            if (!info.Exists || info.Length < 256 * 1024) return;
            var lines = File.ReadAllLines(LogPath, Utf8Bom);
            var keep = lines.Skip(Math.Max(0, lines.Length - 400)).ToArray();
            File.WriteAllLines(LogPath, keep, Utf8Bom);
        }
        catch
        {
            // 裁剪失败不影响记录
        }
    }
}
