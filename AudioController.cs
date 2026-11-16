using NAudio.CoreAudioApi;

namespace ToniaSoundGate;

/// <summary>
/// 音频端点控制层：负责读取/设置系统静音状态，并监听静音状态变化。
/// 关键职责之一是区分"程序自身的静音/解除静音操作"与"用户或其他程序的外部解除静音"，
/// 避免自己触发的操作造成事件回环误判。
/// </summary>
internal sealed class AudioController : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private MMDevice? _device;
    private AudioEndpointVolume? _volume;
    private string _boundDeviceId = string.Empty;
    // NAudio 的 OnVolumeNotification 是异步回调，不能用"调用栈是否正在 SetMuteSelf"来判断自身操作
    // （回调到达时调用栈早已退出）。改用"期望值 + 时间戳"：自身设置后记录期望静音值，
    // 事件回来时若在窗口内且值匹配，则认定为自身操作。
    private readonly object _selfLock = new();
    private bool? _expectedMute;
    private long _expectedAtTicks;
    private bool _disposed;

    // 实测蓝牙设备通知回调可延迟近 2 秒，窗口需覆盖该延迟
    private static readonly long SelfEventWindowTicks = TimeSpan.FromMilliseconds(3500).Ticks;

    /// <summary>当系统静音状态因外部操作发生变化时触发（自身操作引发的回环已被过滤）。参数为当前是否静音。</summary>
    public event Action<bool>? MuteChanged;

    public AudioController()
    {
        _enumerator = new MMDeviceEnumerator();
        AttachDevice();
        DebugLog.Write($"AudioController 初始化，绑定设备=[{_device?.FriendlyName}] id={_boundDeviceId} mute={_volume?.Mute}");
    }

    /// <summary>当前默认播放设备是否处于静音。无法获取设备时按"已静音"保守处理。</summary>
    public bool IsMuted
    {
        get
        {
            try
            {
                EnsureDevice();
                return _volume?.Mute ?? true;
            }
            catch
            {
                return true; // 读不到就当作静音，安全优先
            }
        }
    }

    /// <summary>当前"系统默认播放设备"的名字。每次重新枚举，用于检测设备切换。</summary>
    public string CurrentDefaultDeviceName
    {
        get
        {
            try
            {
                using var dev = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                return dev?.FriendlyName ?? "未知设备";
            }
            catch
            {
                return "未知设备";
            }
        }
    }

    /// <summary>程序实际绑定并监听的设备名。</summary>
    public string DeviceName => _device?.FriendlyName ?? "未知设备";

    /// <summary>默认设备是否已切换到另一台（与当前绑定不一致）。</summary>
    public bool DefaultDeviceChanged()
    {
        try
        {
            using var dev = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            return dev is null || dev.ID != _boundDeviceId;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>由程序自身发起的静音设置。先登记期望值，再改状态，以正确识别随后异步到达的自身事件。</summary>
    public void SetMuteSelf(bool mute)
    {
        try
        {
            EnsureDevice();
            if (_volume is null) return;

            // 不做预读比较（每次 COM 读取在此设备上都很慢），直接登记期望值并写入。
            // 重复写同一状态的开销远小于一次额外的 COM 往返。
            lock (_selfLock)
            {
                _expectedMute = mute;
                _expectedAtTicks = DateTime.UtcNow.Ticks;
            }

            DebugLog.Write($"SELF set mute={mute} on [{_device?.FriendlyName}]");
            _volume.Mute = mute;
        }
        catch
        {
            // 设备可能已被拔出，忽略；下次 EnsureDevice 会重新绑定
        }
    }

    /// <summary>
    /// 判断传入的静音事件是否属于"程序自身操作"引发的回环。
    /// 由于事件异步到达，这里用期望值+时间窗比对：值匹配且在窗口内即视为自身操作，
    /// 并将该期望值消费掉（一次设置只对应一次自身事件），避免误吞后续真正的外部事件。
    /// </summary>
    private bool IsSelfEvent(bool eventMuted)
    {
        lock (_selfLock)
        {
            if (_expectedMute.HasValue
                && _expectedMute.Value == eventMuted
                && DateTime.UtcNow.Ticks - _expectedAtTicks <= SelfEventWindowTicks)
            {
                _expectedMute = null; // 消费掉，防止重复吞事件
                return true;
            }
        }
        return false;
    }

    private void AttachDevice()
    {
        try
        {
            if (_volume is not null)
                _volume.OnVolumeNotification -= OnVolumeNotification;
            _device?.Dispose();

            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            _boundDeviceId = _device?.ID ?? string.Empty;
            _volume = _device?.AudioEndpointVolume;
            if (_volume is not null)
                _volume.OnVolumeNotification += OnVolumeNotification;

            DebugLog.Write($"AttachDevice -> [{_device?.FriendlyName}] id={_boundDeviceId} mute={_volume?.Mute}");
        }
        catch (Exception ex)
        {
            _volume = null;
            DebugLog.Write($"AttachDevice FAILED: {ex.Message}");
        }
    }

    private void EnsureDevice()
    {
        if (_volume is not null) return;
        AttachDevice();
    }

    /// <summary>设备热插拔后重新绑定默认播放设备。</summary>
    public void RebindDevice()
    {
        DebugLog.Write($"RebindDevice (was [{_device?.FriendlyName}]) -> now [{CurrentDefaultDeviceName}]");
        AttachDevice();
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        bool self = IsSelfEvent(data.Muted);
        DebugLog.Write($"EVENT OnVolumeNotification muted={data.Muted} self={self} device=[{_device?.FriendlyName}]");
        // 只对外部发起的静音变化通知状态机；自身操作引发的回环在此截断
        if (!self)
            MuteChanged?.Invoke(data.Muted);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_volume is not null)
            _volume.OnVolumeNotification -= OnVolumeNotification;
        _volume?.Dispose();
        _device?.Dispose();
        _enumerator.Dispose();
    }
}
