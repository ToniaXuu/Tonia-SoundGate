namespace ToniaSoundGate;

/// <summary>弹窗结果：是否授权，以及授权时长。</summary>
internal readonly record struct PromptResult(bool Granted, TimeSpan Duration);

/// <summary>
/// 静音闸门状态机（强预防式）。
/// 守护态：系统必须保持静音；一旦检测到外部解除静音，立刻重新静音并弹窗请求授权。
/// 授权态：在有效期内允许外放；到期后自动静音回到守护态。
/// 通过事件驱动的静音监听 + 定时巡检双重保障，杜绝状态漂移导致的声音泄露。
/// </summary>
internal sealed class GateStateMachine
{
    private readonly AudioController _audio;
    private readonly object _gate = new();
    private DateTime? _authorizedUntil;
    private bool _promptOpen;

    /// <summary>请求 UI 弹出授权提示框。</summary>
    public event Action? PromptRequested;

    /// <summary>状态发生变化（授权开始/到期/取消），UI 据此刷新托盘。</summary>
    public event Action? StateChanged;

    public GateStateMachine(AudioController audio)
    {
        _audio = audio;
    }

    /// <summary>当前是否处于有效授权期。</summary>
    public bool IsAuthorized => _authorizedUntil.HasValue && DateTime.Now < _authorizedUntil.Value;

    /// <summary>授权到期时间；未处于授权态时为 null。</summary>
    public DateTime? AuthorizedUntil => IsAuthorized ? _authorizedUntil : null;

    /// <summary>弹窗是否正在显示。</summary>
    public bool IsPromptOpen
    {
        get { lock (_gate) return _promptOpen; }
    }

    /// <summary>程序启动：强制进入守护态。</summary>
    public void Start()
    {
        _authorizedUntil = null;
        DebugLog.Write("StateMachine.Start");
        EnforceMute();
        StateChanged?.Invoke();
    }

    /// <summary>静音状态变化回调（来自音频层，仅外部操作）。</summary>
    public void OnMuteChanged(bool isMuted)
    {
        Evaluate(isMuted);
    }

    /// <summary>定时巡检：检查授权是否到期，并复核静音状态，作为事件丢失时的兜底。</summary>
    public void Tick()
    {
        if (_authorizedUntil.HasValue && DateTime.Now >= _authorizedUntil.Value)
        {
            _authorizedUntil = null;
            StateChanged?.Invoke();
        }

        // 设备可能在授权期内被切换，巡检时重新读取真实静音状态
        Evaluate(_audio.IsMuted);
    }

    /// <summary>设备热插拔后由 UI 层调用，重新绑定音频端点。</summary>
    public void OnDeviceChanged()
    {
        // 新设备默认状态未知，回到守护态重新把关
        _authorizedUntil = null;
        EnforceMute();
        StateChanged?.Invoke();
    }

    /// <summary>弹窗关闭后由 UI 层调用，提交用户选择。</summary>
    public void OnPromptClosed(PromptResult result)
    {
        lock (_gate) _promptOpen = false;

        if (result.Granted && result.Duration > TimeSpan.Zero)
        {
            _authorizedUntil = DateTime.Now.Add(result.Duration);
            // 先进入授权态再解除静音，顺序不能颠倒：
            // 解除静音会触发事件，此时 IsAuthorized 必须为 true 才会放行而非再次拦截
            _audio.SetMuteSelf(false);
        }
        else
        {
            // 取消或无效时长：回到守护态并保持静音
            _authorizedUntil = null;
            EnforceMute();
        }

        StateChanged?.Invoke();
    }

    /// <summary>托盘菜单的快捷授权：直接进入授权态并解除静音。</summary>
    public void GrantManually(TimeSpan duration)
    {
        _authorizedUntil = DateTime.Now.Add(duration);
        _audio.SetMuteSelf(false);
        StateChanged?.Invoke();
    }

    /// <summary>托盘菜单操作：立即回到守护态并保持静音。</summary>
    public void ForceGuard()
    {
        _authorizedUntil = null;
        EnforceMute();
        StateChanged?.Invoke();
    }

    private void Evaluate(bool isMuted)
    {
        // 授权有效期内：放行任何外放行为
        if (IsAuthorized)
        {
            DebugLog.Write($"Evaluate: authorized, allow (isMuted={isMuted})");
            return;
        }

        // 无有效授权且已静音：安全，无需动作
        if (isMuted) return;

        // 无授权却处于未静音 → 立即静音，并请求弹窗（危险窗口仅在事件回调的毫秒级内）
        DebugLog.Write("Evaluate: NOT muted & no auth -> ENFORCE + PROMPT");
        EnforceMute();
        RequestPrompt();
    }

    private void EnforceMute()
    {
        // 直接写入静音，不再预读 IsMuted：在本机蓝牙设备上每次 COM 往返都要数百毫秒，
        // 省掉一次读取能把"发现问题→按下静音"压缩近一半。写入幂等，重复无害。
        _audio.SetMuteSelf(true);
    }

    private void RequestPrompt()
    {
        lock (_gate)
        {
            if (_promptOpen) return; // 已有弹窗在显示，避免重复
            _promptOpen = true;
        }
        PromptRequested?.Invoke();
    }
}
