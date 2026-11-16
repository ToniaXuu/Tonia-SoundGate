using System.Drawing.Drawing2D;

namespace ToniaSoundGate;

/// <summary>
/// 二次确认对话框：任何「解除静音」操作都必须先经过这里。
///
/// 安全侧设计（针对误触）：
/// 1. 默认焦点在「保持静音」，Enter 与 Esc 也一律走安全侧，键盘误触不会外放；
/// 2. 「确认解除」按钮延迟 700ms 才可点击，防止连点穿透两层界面；
/// 3. 20 秒无操作自动按「保持静音」关闭，框被晾着也不会留下未决状态；
/// 4. 居中弹出而非贴着鼠标弹出，鼠标必须主动移动过去才可能点到。
/// </summary>
internal sealed class ConfirmForm : RoundedFormBase
{
    private readonly RoundedButton _confirmBtn;
    private readonly System.Windows.Forms.Timer _armTimer;
    private readonly System.Windows.Forms.Timer _countdownTimer;
    private readonly Label _hintLabel;
    private readonly DateTime _autoCloseAt;
    private bool _closing;

    /// <summary>用户是否明确点了「确认解除」。</summary>
    public bool Confirmed { get; private set; }

    public ConfirmForm(string title, string actionText, string riskText)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.WindowBg;
        ForeColor = UiTheme.TextPrimary;
        Font = UiTheme.FontBody;
        ClientSize = new Size(520, 336);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;

        // ---------- 标题 ----------
        var icon = new PictureBox
        {
            Image = CreateAlertIcon(32),
            Location = new Point(28, 24),
            Size = new Size(32, 32),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        Controls.Add(icon);

        Controls.Add(new Label
        {
            Text = title,
            Font = UiTheme.FontTitle,
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(72, 22),
            Size = new Size(426, 44),
            AutoSize = false
        });

        // ---------- 说明卡片 ----------
        var card = new RoundedPanel
        {
            BackColor = UiTheme.DangerSoft,
            OutlineColor = Color.FromArgb(245, 220, 217),
            Location = new Point(24, 82),
            Size = new Size(472, 118),
            Radius = 8
        };
        Controls.Add(card);

        card.Controls.Add(new PictureBox
        {
            Image = CreateAlertIcon(20),
            Location = new Point(16, 18),
            Size = new Size(20, 20),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        });

        card.Controls.Add(new Label
        {
            Text = riskText,
            Font = UiTheme.FontSection,
            ForeColor = UiTheme.Danger,
            Location = new Point(46, 10),
            Size = new Size(408, 34),
            AutoSize = false
        });
        // 正文固定按两行排版（文案里带换行），高度必须容得下两行，
        // 否则高 DPI 下第二行会被裁掉。
        card.Controls.Add(new Label
        {
            Text = actionText,
            Font = UiTheme.FontBody,
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(46, 46),
            Size = new Size(408, 64),
            AutoSize = false
        });

        // 自动关闭提示放在卡片外，作为对话框的辅助说明
        _hintLabel = new Label
        {
            Text = " ",
            Font = UiTheme.FontCaption,
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(28, 210),
            Size = new Size(460, 30),
            AutoSize = false
        };
        Controls.Add(_hintLabel);

        // ---------- 底栏按钮 ----------
        Controls.Add(new Panel
        {
            BackColor = UiTheme.Divider,
            Location = new Point(24, 246),
            Size = new Size(472, 1)
        });

        var safeBtn = new RoundedButton
        {
            Text = "保持静音",
            Font = UiTheme.FontBody,
            Location = new Point(256, 266),
            Size = new Size(120, 48),
            Radius = 6,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.TextPrimary,
            OutlineColor = UiTheme.Border,
            HoverBackColor = UiTheme.Hover,
            PressedBackColor = Color.FromArgb(225, 225, 225)
        };
        Controls.Add(safeBtn);

        _confirmBtn = new RoundedButton
        {
            Text = "确认解除",
            Font = UiTheme.FontBody,
            Location = new Point(388, 266),
            Size = new Size(108, 48),
            Radius = 6,
            BackColor = UiTheme.Accent,
            ForeColor = Color.White,
            HoverBackColor = UiTheme.AccentHover,
            PressedBackColor = UiTheme.AccentPressed,
            Enabled = false // 延迟启用，防连点穿透
        };
        Controls.Add(_confirmBtn);

        // 默认按钮与 Esc 都落在安全侧：键盘误触不会解除静音
        AcceptButton = safeBtn;
        CancelButton = safeBtn;

        safeBtn.Click += (_, _) => CloseSafely();
        _confirmBtn.Click += (_, _) => CloseConfirmed();

        // 700ms 后才允许点击"确认解除"，避免面板上的连点直接穿透到确认框
        _armTimer = new System.Windows.Forms.Timer { Interval = 700 };
        _armTimer.Tick += (_, _) =>
        {
            _armTimer.Stop();
            _confirmBtn.Enabled = true;
        };

        // 20 秒无操作自动按安全侧关闭
        _autoCloseAt = DateTime.Now.AddSeconds(20);
        _countdownTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _countdownTimer.Tick += (_, _) =>
        {
            var remain = _autoCloseAt - DateTime.Now;
            if (remain <= TimeSpan.Zero)
            {
                CloseSafely();
                return;
            }
            _hintLabel.Text = $"{remain.TotalSeconds:0} 秒无操作将自动保持静音";
        };

        Shown += (_, _) =>
        {
            safeBtn.Focus();
            _armTimer.Start();
            _countdownTimer.Start();
        };
    }

    private void CloseSafely()
    {
        if (_closing) return;
        _closing = true;
        Confirmed = false;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void CloseConfirmed()
    {
        if (_closing) return;
        _closing = true;
        Confirmed = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) CloseSafely();
        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _armTimer?.Dispose();
            _countdownTimer?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static Bitmap CreateAlertIcon(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float d = size - 1;
        using (var brush = new SolidBrush(UiTheme.Danger))
            g.FillEllipse(brush, 0, 0, d, d);
        using var font = new Font("Segoe UI", size * 0.58f, FontStyle.Bold, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, "!", font, new Rectangle(0, 0, size, size), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return bmp;
    }
}
