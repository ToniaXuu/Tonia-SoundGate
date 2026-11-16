namespace ToniaSoundGate;

/// <summary>轻量诊断日志，写入程序目录下的 soundgate.log，仅用于排查守护行为。</summary>
internal static class DebugLog
{
    // 默认关闭：交付版不写日志，避免授权期高频巡检造成不必要的磁盘 IO。
    // 排查问题时改为 true 重新编译即可，日志输出到程序目录的 soundgate.log。
    public static readonly bool Enabled = false;

    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "soundgate.log");
    private static readonly object Sync = new();

    public static void Write(string message)
    {
        if (!Enabled) return;
        try
        {
            lock (Sync)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不影响主功能
        }
    }
}
