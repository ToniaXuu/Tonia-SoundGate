using System.Reflection;
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
    // tooltip 缓存：只为避免每 tick 都往 shell 写同一串文字（NotifyIcon.Text 的 setter 会 P/Invoke）。
    // 它不是"状态源"，真实状态一律由 ComputeStatusText() 现算。
    private string _tooltipCache = "";
    private int _tooltipCounter;

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
            // 必须传方法组而非缓存字符串：面板每秒刷新一次，
            // 若给的是快照，倒计时会永远停在打开面板那一刻。
            ComputeStatusText,
            () => _machine.IsAuthorized,
            () => _settings.StartWithWindows,
            RequestGrant,
            GuardNow,
            SetAutoStart,
            CheckForUpdates,
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

        // 每 5 个 tick（约 1 秒）同步一次托盘 tooltip 的剩余时间，与面板保持一致；
        // 文案没变化就不写，避免每秒一次无谓的 shell 调用。
        if (++_tooltipCounter >= 5)
        {
            _tooltipCounter = 0;
            SyncTooltip();
        }
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

    /// <summary>
    /// 检查更新：这是全程序唯一会发起网络请求的入口，且必须用户主动点击菜单才会触发。
    /// 启动、巡检、托盘刷新等任何其它路径都不触网，兑现"完全在本机运行"的承诺。
    /// 请求放线程池执行，结果切回 UI 线程再弹窗；期间用户退出则静默丢弃结果。
    /// </summary>
    private void CheckForUpdates()
    {
        ToastService.Show("正在检查更新", "仅此次主动检查会联网 · 最多等待 8 秒", ToastKind.Info, 2200);

        _ = Task.Run(async () =>
        {
            var result = await UpdateChecker.CheckAsync();
            if (_exiting || _uiAnchor.IsDisposed) return;
            try
            {
                _uiAnchor.BeginInvoke(new Action(() => ShowUpdateResult(result)));
            }
            catch
            {
                // 退出过程中 UI 锚点已销毁，忽略
            }
        });
    }

    private void ShowUpdateResult(UpdateCheckResult result)
    {
        if (!result.Success)
        {
            ToastService.Show("检查更新失败", result.Error ?? "请稍后重试", ToastKind.Warning, 4000);
            return;
        }

        if (!result.HasUpdate)
        {
            ToastService.Show("已是最新版本", $"当前 v{result.LatestVersion}", ToastKind.Success, 2800);
            return;
        }

        // 发现新版本：弹中性提示框，默认「稍后再说」，只有用户明确点击才打开浏览器
        using var notice = new UpdateNoticeForm(
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0",
            result.LatestVersion ?? "",
            result.ReleaseUrl ?? "https://github.com/ToniaXuu/Tonia-SoundGate/releases");
        notice.ShowDialog();
    }

    /// <summary>把分钟数转成用户可读的时长文案。</summary>
    private static string FormatDuration(int minutes)
    {
        if (minutes >= 1440 && minutes % 1440 == 0) return $"{minutes / 1440} 天";
        if (minutes >= 60 && minutes % 60 == 0) return $"{minutes / 60} 小时";
        return $"{minutes} 分钟";
    }

    /// <summary>
    /// 现算状态文案。**不要改回缓存字段**：托盘面板每秒刷新、tooltip 也需随时间变化。
    /// 曾用 _statusText 缓存快照（只在 StateChanged 时写入），结果授权期间状态不再变化、
    /// 快照不再更新，面板倒计时就僵在授权那一刻（表现为"剩余 29 分 59 秒"永远不动）。
    /// </summary>
    private string ComputeStatusText()
    {
        if (_machine.IsAuthorized && _machine.AuthorizedUntil is { } until)
        {
            var remain = until - DateTime.Now;
            if (remain < TimeSpan.Zero) remain = TimeSpan.Zero;
            return $"授权中 · 剩余 {FormatRemain(remain)}";
        }
        return "守护中（已静音）";
    }

    /// <summary>把当前状态写进托盘 tooltip。状态切换时立即写，其余时间由巡检每秒同步。</summary>
    private void SyncTooltip()
    {
        if (_tray is null) return;
        var text = $"Tonia-SoundGate · {ComputeStatusText()}";
        // NotifyIcon.Text 上限 63 字符，超长会抛异常；本方法跑在巡检回调里，
        // 抛出去没人接就会终止程序，所以宁可丢掉应用名前缀也不冒险。
        if (text.Length > 63) text = ComputeStatusText();
        if (text == _tooltipCache) return; // 文案没变就不碰 shell
        _tooltipCache = text;
        _tray.Text = text;
    }

    private void RefreshTray()
    {
        if (_tray is null) return;
        if (_uiAnchor.InvokeRequired)
        {
            _uiAnchor.BeginInvoke(new Action(RefreshTray));
            return;
        }

        _tray.Icon = _machine.IsAuthorized ? TrayIcons.Allowed : TrayIcons.Guard;
        SyncTooltip();
    }

    /// <summary>
    /// 剩余时长文案。秒数**向上取整**：刚授权时显示"30 分 0 秒"而不是"29 分 59 秒"，
    /// 且最后一秒内仍显示"1 秒"，真正归零才显示"0 秒"，避免一进来就少一秒的观感。
    /// </summary>
    private static string FormatRemain(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        long total = (long)Math.Ceiling(t.TotalSeconds);
        if (total >= 3600) return $"{total / 3600} 小时 {total % 3600 / 60} 分";
        if (total >= 60) return $"{total / 60} 分 {total % 60} 秒";
        return $"{total} 秒";
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
        // 记进 crash.log：这样"程序没了"时能一眼分清是用户自己点的退出，
        // 还是异常/外部因素导致的消失。
        CrashGuard.Write("退出：用户从托盘菜单选择「退出」，主动结束守护");
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
