using Microsoft.Win32;

namespace ToniaSoundGate;

/// <summary>托盘应用上下文：无主窗口，常驻系统托盘，串联音频层、状态机与弹窗。</summary>
internal sealed class TrayAppContext : ApplicationContext
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Tonia-SoundGate";

    private readonly AudioController _audio;
    private readonly GateStateMachine _machine;
    private readonly AppSettings _settings;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _watchdog;
    // ApplicationContext 本身没有 InvokeRequired/BeginInvoke，用隐藏控件作为 UI 线程锚点，
    // 将音频回调线程（COM 回调）上的弹窗/刷新切回 UI 线程。
    private readonly Control _uiAnchor;
    // 二次启动通知：已有实例在运行时，新进程 Set 该事件，这里弹提示告知用户程序已在后台
    private readonly EventWaitHandle? _activateEvent;
    private Thread? _activateThread;
    // 必须用字段持有：局部 Timer 在构造函数返回后没有强引用，可能被 GC 回收导致启动提示不出现
    private readonly System.Windows.Forms.Timer _startupTip;
    private volatile bool _exiting;

    private TrayPopupForm? _popup;
    private string _statusText = "守护中（已静音）";

    public TrayAppContext(EventWaitHandle? activateEvent = null)
    {
        _activateEvent = activateEvent;
        _startupTip = new System.Windows.Forms.Timer { Interval = 600 };
        _uiAnchor = new Control();
        _uiAnchor.CreateControl();

        _settings = AppSettings.Load();
        _audio = new AudioController();
        _machine = new GateStateMachine(_audio);

        _machine.PromptRequested += ShowPrompt;
        _machine.StateChanged += RefreshTray;
        _audio.MuteChanged += OnMuteChanged;

        _tray = new NotifyIcon
        {
            Icon = TrayIcons.Guard,
            Visible = true,
            Text = "Tonia-SoundGate · 守护中"
        };
        _tray.MouseDown += OnTrayMouseDown;
        // 双击托盘只打开面板，绝不直接授权。
        // 之前双击 = 立即可解除静音，误触一下声音就外放了，这是不可接受的安全风险。
        _tray.DoubleClick += (_, _) => ShowTrayMenu();

        // 巡检定时器：快速轮询静音状态。实测部分蓝牙设备的 WASAPI 通知回调延迟可达 1 秒，
        // 因此巡检才是实际的第一道防线。200ms 间隔可将最坏泄露窗口压到 0.2 秒，CPU 开销可忽略。
        _watchdog = new System.Windows.Forms.Timer { Interval = 200 };
        _watchdog.Tick += OnWatchdogTick;

        _machine.Start();
        _watchdog.Start();
        RefreshTray();

        ShowStartupFeedback();
        StartActivateListener();
    }

    /// <summary>
    /// 启动反馈：程序是纯托盘应用，启动后若不提示，用户只会看到任务栏多了一个陌生图标。
    /// 延迟 600ms 等托盘图标注册完成，再用右下角通知说明"已启动 + 如何操作"。
    /// </summary>
    private void ShowStartupFeedback()
    {
        _startupTip.Tick += (_, _) =>
        {
            _startupTip.Stop();
            ToastService.Show("Tonia-SoundGate 已启动",
                "静音守护中 · 右键托盘图标可授权或退出", ToastKind.Success, 3400);
        };
        _startupTip.Start();
    }

    /// <summary>监听"重复启动"事件，让用户知道程序已经在后台运行，而不是以为没启动成功。</summary>
    private void StartActivateListener()
    {
        if (_activateEvent is null) return;

        _activateThread = new Thread(() =>
        {
            while (!_exiting)
            {
                if (!_activateEvent.WaitOne(500)) continue;
                if (_exiting) break;
                try
                {
                    _uiAnchor.BeginInvoke(new Action(() =>
                        ToastService.Show("程序已在后台运行",
                            "无需重复启动 · 右键托盘图标即可操作", ToastKind.Info)));
                }
                catch
                {
                    // 退出过程中 UI 锚点已销毁，忽略
                }
            }
        })
        {
            IsBackground = true,
            Name = "SoundGate-ActivateListener"
        };
        _activateThread.Start();
    }

    private void OnMuteChanged(bool isMuted) => _machine.OnMuteChanged(isMuted);

    private void OnTrayMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right) ShowTrayMenu();
    }

    private void ShowTrayMenu()
    {
        if (_uiAnchor.InvokeRequired)
        {
            _uiAnchor.BeginInvoke(new Action(ShowTrayMenu));
            return;
        }

        _popup?.Dispose();
        _popup = new TrayPopupForm(
            () => _statusText,
            () => _machine.IsAuthorized,
            () => _settings.StartWithWindows,
            RequestGrant,
            GuardNow,
            SetAutoStart,
            Exit);
        _popup.ShowAt(Cursor.Position);
    }

    private int _deviceCheckCounter;

    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        // 设备枚举较重，每 25 个 tick（约 5 秒）检测一次热插拔即可
        if (++_deviceCheckCounter >= 25)
        {
            _deviceCheckCounter = 0;
            if (_audio.DefaultDeviceChanged())
            {
                DebugLog.Write("Watchdog: 检测到默认设备切换，重新绑定");
                _audio.RebindDevice();
                _machine.OnDeviceChanged();
            }
        }
        // 每个 tick 都快速复核静音状态，这是实际的第一道防线
        _machine.Tick();
    }

    private void ShowPrompt()
    {
        // 弹窗必须在 UI 线程创建
        if (_uiAnchor.InvokeRequired)
        {
            _uiAnchor.BeginInvoke(new Action(ShowPrompt));
            return;
        }

        using var form = new PromptForm(_settings.LastDurationMinutes, _audio.DeviceName);
        var dialog = form.ShowDialog();
        var result = form.Result with { Granted = dialog == DialogResult.OK };

        if (result.Granted)
        {
            _settings.LastDurationMinutes = (int)result.Duration.TotalMinutes;
            _settings.Save();
            int minutes = (int)result.Duration.TotalMinutes;
            ToastService.Show("已解除静音", $"{FormatDuration(minutes)}后自动恢复静音", ToastKind.Success);
        }
        _machine.OnPromptClosed(result);
    }

    /// <summary>
    /// 请求解除静音：**必须**先经过一次独立确认，确认通过才真正放开。
    /// 托盘面板与双击托盘都只能走到这里，杜绝"点一下就外放"。
    /// </summary>
    private void RequestGrant(int minutes)
    {
        if (minutes <= 0) minutes = 1440;

        // 确认框必须在 UI 线程创建
        if (_uiAnchor.InvokeRequired)
        {
            _uiAnchor.BeginInvoke(new Action(() => RequestGrant(minutes)));
            return;
        }

        var label = FormatDuration(minutes);
        using var confirm = new ConfirmForm(
            "确认解除静音？",
            $"将允许外放 {label}，\n到期后自动恢复静音拦截。",
            "解除后声音会被周围人听到");
        var ok = confirm.ShowDialog() == DialogResult.OK && confirm.Confirmed;

        if (!ok)
        {
            ToastService.Show("已保持静音", "未解除静音，外放仍会被拦截", ToastKind.Info);
            return;
        }

        GrantNow(minutes);
    }

    /// <summary>真正执行授权（已通过确认），这里才碰音频层。</summary>
    private void GrantNow(int minutes)
    {
        _settings.LastDurationMinutes = minutes;
        _settings.Save();
        // 先给出结果反馈，再落到音频层：Toast 已经绘制到屏幕，后续 COM 调用再慢也不影响"点完就有反应"的观感
        ToastService.Show("已授权外放", $"{FormatDuration(minutes)}后自动恢复静音", ToastKind.Success);
        _machine.GrantManually(TimeSpan.FromMinutes(minutes));
    }

    private void GuardNow()
    {
        ToastService.Show("已恢复静音守护", "系统已静音，外放将被拦截", ToastKind.Info);
        _machine.ForceGuard();
    }

    /// <summary>把分钟数转成用户可读的时长文案。</summary>
    private static string FormatDuration(int minutes)
    {
        if (minutes >= 1440 && minutes % 1440 == 0) return $"{minutes / 1440} 天";
        if (minutes >= 60 && minutes % 60 == 0) return $"{minutes / 60} 小时";
        return $"{minutes} 分钟";
    }

    private void RefreshTray()
    {
        if (_tray is null) return;
        if (_uiAnchor.InvokeRequired)
        {
            _uiAnchor.BeginInvoke(new Action(RefreshTray));
            return;
        }

        var authorized = _machine.IsAuthorized;
        _tray.Icon = authorized ? TrayIcons.Allowed : TrayIcons.Guard;

        if (authorized && _machine.AuthorizedUntil is { } until)
        {
            _statusText = $"授权中 · 剩余 {FormatRemain(until - DateTime.Now)}";
        }
        else
        {
            _statusText = "守护中（已静音）";
        }
        _tray.Text = $"Tonia-SoundGate · {_statusText}";
    }

    private static string FormatRemain(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} 小时 {t.Minutes} 分";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes} 分 {t.Seconds} 秒";
        return $"{Math.Max(0, t.Seconds)} 秒";
    }

    private void SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("无法打开开机启动注册表项");

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                    throw new InvalidOperationException("无法获取程序路径");
                key.SetValue(AppName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }

            _settings.StartWithWindows = enabled;
            _settings.Save();
            ToastService.Show(enabled ? "已开启开机自启" : "已关闭开机自启",
                enabled ? "下次开机将自动启动静音守护" : "开机后不再自动启动",
                ToastKind.Success);
        }
        catch
        {
            // 写失败时回退勾选状态并明确告知，避免界面与实际不一致
            _settings.StartWithWindows = !enabled;
            _settings.Save();
            ToastService.Show("开机自启设置失败", "无法写入系统启动项，请检查权限", ToastKind.Warning);
        }
    }

    private void Exit()
    {
        _exiting = true;
        _watchdog.Stop();
        _popup?.Dispose();
        _popup = null;
        _tray.Visible = false;
        _tray.Dispose();
        _audio.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        _exiting = true;
        if (disposing)
        {
            _startupTip?.Dispose();
            _watchdog?.Dispose();
            _popup?.Dispose();
            _tray?.Dispose();
            _audio?.Dispose();
            _uiAnchor?.Dispose();
        }
        base.Dispose(disposing);
    }
}
