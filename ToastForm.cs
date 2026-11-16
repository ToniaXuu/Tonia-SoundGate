using System.Drawing.Drawing2D;

namespace ToniaSoundGate;

internal enum ToastKind { Info, Success, Warning }

/// <summary>
/// 右下角轻量通知窗：不抢焦点、自动淡入淡出。
/// 用于操作结果反馈（授权 / 恢复守护 / 开机自启）与程序启动提示，
/// 解决"点完没反应""启动只在托盘冒个图标"的体验问题。
/// </summary>
internal sealed class ToastForm : Form
{
    private enum Phase { FadeIn, Hold, FadeOut }

    private const int ToastWidth = 408;
    private const int ToastHeight = 104;

    private readonly string _title;
    private readonly string _detail;
    private readonly ToastKind _kind;
    private readonly Color _accent;
    private readonly int _holdMs;
    private readonly System.Windows.Forms.Timer _anim;

    private Phase _phase = Phase.FadeIn;
    private DateTime _phaseStart;
    private bool _closing;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000;    // WS_EX_NOACTIVATE：不抢焦点，避免打断用户操作
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    public ToastForm(string title, string detail, ToastKind kind, int holdMs)
    {
        _title = title;
        _detail = detail;
        _kind = kind;
        _holdMs = Math.Max(1200, holdMs);
        _accent = kind switch
        {
            ToastKind.Success => Color.FromArgb(46, 160, 120),
            ToastKind.Warning => UiTheme.Danger,
            _ => UiTheme.Accent
        };

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = UiTheme.Surface;
        ClientSize = new Size(ToastWidth, ToastHeight);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;

        _anim = new System.Windows.Forms.Timer { Interval = 16 };
        _anim.Tick += OnAnimTick;

        Click += (_, _) => DismissNow();
    }

    /// <summary>在屏幕右下角显示（位于任务栏上方）。</summary>
    public void ShowToast()
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);
        Opacity = 0;
        _phase = Phase.FadeIn;
        _phaseStart = DateTime.Now;
        Show();
        Update(); // 立刻绘制一帧，确保后续耗时操作不会让通知"迟到"
        _anim.Start();
    }

    /// <summary>立即关闭（被新通知替换或用户点击时调用）。</summary>
    public void DismissNow()
    {
        if (_closing) return;
        _closing = true;
        _anim.Stop();
        if (!IsDisposed)
        {
            try { Opacity = 0; } catch { /* 已销毁，忽略 */ }
            Close();
        }
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        var elapsed = (DateTime.Now - _phaseStart).TotalMilliseconds;
        switch (_phase)
        {
            case Phase.FadeIn:
                Opacity = Math.Min(1, elapsed / 170.0);
                if (Opacity >= 1)
                {
                    _phase = Phase.Hold;
                    _phaseStart = DateTime.Now;
                }
                break;

            case Phase.Hold:
                if (elapsed >= _holdMs)
                {
                    _phase = Phase.FadeOut;
                    _phaseStart = DateTime.Now;
                }
                break;

            case Phase.FadeOut:
                Opacity = Math.Max(0, 1 - elapsed / 220.0);
                if (Opacity <= 0) DismissNow();
                break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiTheme.RoundedRect(rect, 10);
        using (var back = new SolidBrush(UiTheme.Surface))
            g.FillPath(back, path);

        // 左侧强调色条，裁剪在圆角内
        var saved = g.Clip;
        g.SetClip(path);
        using (var bar = new SolidBrush(_accent))
            g.FillRectangle(bar, 0, 0, 5, Height);
        g.Clip = saved;

        using (var border = new Pen(UiTheme.Border))
            g.DrawPath(border, path);

        var iconRect = new Rectangle(24, (Height - 28) / 2, 28, 28);
        using (var iconBg = new SolidBrush(_accent))
            g.FillEllipse(iconBg, iconRect);
        DrawSymbol(g, iconRect);

        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                                      TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(g, _title, UiTheme.FontSection,
            new Rectangle(66, 14, Width - 90, 38), UiTheme.TextPrimary, flags);
        TextRenderer.DrawText(g, _detail, UiTheme.FontCaption,
            new Rectangle(66, 52, Width - 90, 36), UiTheme.TextSecondary, flags);
    }

    private void DrawSymbol(Graphics g, Rectangle r)
    {
        using var white = new Pen(Color.White, 2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        float cx = r.X + r.Width / 2f;
        float cy = r.Y + r.Height / 2f;

        switch (_kind)
        {
            case ToastKind.Success:
                g.DrawLines(white, new[]
                {
                    new PointF(cx - 6, cy),
                    new PointF(cx - 2, cy + 4.5f),
                    new PointF(cx + 6.5f, cy - 4.5f)
                });
                break;

            case ToastKind.Warning:
                g.DrawLine(white, cx, cy - 7, cx, cy + 1.5f);
                using (var dot = new SolidBrush(Color.White))
                    g.FillEllipse(dot, cx - 1.4f, cy + 5, 2.8f, 2.8f);
                break;

            default:
                using (var dot = new SolidBrush(Color.White))
                    g.FillEllipse(dot, cx - 1.4f, cy - 7.5f, 2.8f, 2.8f);
                g.DrawLine(white, cx, cy - 2.5f, cx, cy + 6.5f);
                break;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        _anim.Stop();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _anim.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>全局通知调度：同一时刻只保留一个通知，新通知直接替换旧的，避免堆叠遮挡。</summary>
internal static class ToastService
{
    private static ToastForm? _current;

    public static void Show(string title, string detail,
        ToastKind kind = ToastKind.Info, int holdMs = 2600)
    {
        var previous = _current;
        _current = null;
        previous?.DismissNow();

        var toast = new ToastForm(title, detail, kind, holdMs);
        _current = toast;
        toast.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_current, toast)) _current = null;
        };
        toast.ShowToast();
    }
}
