using System.Drawing.Drawing2D;

namespace ToniaSoundGate;

/// <summary>
/// 授权提示框：Windows 11 设置风格。卡片化分区、充足留白，
/// 提供常用时长快捷选项 + 自定义分钟数，默认选中上次使用时长（首次为一天）。
/// 注意：高 DPI（本机 175%）下字体按物理像素渲染，所有文字容器高度必须留足余量，
/// 否则 Label 会按固定高度裁剪文字上下边缘。
/// </summary>
internal sealed class PromptForm : RoundedFormBase
{
    private readonly RadioButton[] _presetRadios;
    private readonly NumberInput _customMinutes;
    private readonly RadioButton _customRadio;
    private readonly Label _countdownLabel;
    private readonly DateTime _autoCloseAt;
    private readonly System.Windows.Forms.Timer _allowArmTimer;

    private static readonly (string Label, int Minutes)[] Presets =
    {
        ("30 分钟", 30),
        ("1 小时", 60),
        ("4 小时", 240),
        ("1 天（推荐）", 1440),
    };

    /// <summary>用户提交的结果。</summary>
    public PromptResult Result { get; private set; } = new(false, TimeSpan.FromDays(1));

    public PromptForm(int lastDurationMinutes, string deviceName)
    {
        Text = "外放拦截 · Tonia-SoundGate";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.WindowBg;
        ForeColor = UiTheme.TextPrimary;
        Font = UiTheme.FontBody;
        ClientSize = new Size(560, 610);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;

        // ---------- 顶部标题区 ----------
        var headerIcon = new PictureBox
        {
            Image = CreateHeaderIcon(),
            Location = new Point(28, 24),
            Size = new Size(36, 36),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        Controls.Add(headerIcon);

        Controls.Add(new Label
        {
            Text = "外放拦截",
            Font = UiTheme.FontTitle,
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(76, 20),
            Size = new Size(440, 46),
            AutoSize = false
        });
        Controls.Add(new Label
        {
            Text = "Tonia-SoundGate · 声音守护",
            Font = UiTheme.FontCaption,
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(76, 66),
            Size = new Size(440, 32),
            AutoSize = false
        });

        // ---------- 警告说明卡片 ----------
        var warningCard = new RoundedPanel
        {
            BackColor = UiTheme.DangerSoft,
            OutlineColor = Color.FromArgb(245, 220, 217),
            Location = new Point(24, 112),
            Size = new Size(512, 124),
            Radius = 8
        };
        Controls.Add(warningCard);

        var warnIcon = new PictureBox
        {
            Image = CreateWarningIcon(),
            Location = new Point(16, 18),
            Size = new Size(20, 20),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        warningCard.Controls.Add(warnIcon);

        warningCard.Controls.Add(new Label
        {
            Text = "检测到外放未静音",
            Font = UiTheme.FontSection,
            ForeColor = UiTheme.Danger,
            Location = new Point(48, 16),
            Size = new Size(450, 36),
            AutoSize = false
        });
        warningCard.Controls.Add(new Label
        {
            Text = "为避免声音泄露，系统已自动静音。",
            Font = UiTheme.FontBody,
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(48, 54),
            Size = new Size(450, 34),
            AutoSize = false
        });
        warningCard.Controls.Add(new Label
        {
            Text = $"播放设备：{UiTheme.Fit(deviceName, UiTheme.FontCaption, 430)}",
            Font = UiTheme.FontCaption,
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(48, 90),
            Size = new Size(450, 30),
            AutoSize = false
        });

        // ---------- 时长选择区 ----------
        Controls.Add(new Label
        {
            Text = "允许解除静音的时长",
            Font = UiTheme.FontSection,
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(28, 250),
            Size = new Size(420, 38),
            AutoSize = false
        });

        var optionsCard = new RoundedPanel
        {
            BackColor = UiTheme.Surface,
            OutlineColor = UiTheme.Border,
            Location = new Point(24, 296),
            Size = new Size(512, 176),
            Radius = 8
        };
        Controls.Add(optionsCard);

        _presetRadios = new RadioButton[Presets.Length];
        int[] xs = { 22, 268 };
        int[] ys = { 20, 70 };
        for (int i = 0; i < Presets.Length; i++)
        {
            var radio = new RadioButton
            {
                Text = Presets[i].Label,
                Font = UiTheme.FontBody,
                ForeColor = UiTheme.TextPrimary,
                BackColor = UiTheme.Surface,
                Location = new Point(xs[i % 2], ys[i / 2]),
                Size = new Size(240, 38),
                Tag = Presets[i].Minutes,
                Cursor = Cursors.Hand
            };
            optionsCard.Controls.Add(radio);
            _presetRadios[i] = radio;
        }

        _customRadio = new RadioButton
        {
            Text = "自定义：",
            Font = UiTheme.FontBody,
            ForeColor = UiTheme.TextPrimary,
            BackColor = UiTheme.Surface,
            Location = new Point(22, 124),
            Size = new Size(140, 44),
            AutoSize = false,
            Cursor = Cursors.Hand
        };
        optionsCard.Controls.Add(_customRadio);

        _customMinutes = new NumberInput
        {
            Minimum = 1,
            Maximum = 10080, // 最多 7 天
            Value = 60,
            Location = new Point(158, 122),
            Size = new Size(122, 44)
        };
        optionsCard.Controls.Add(_customMinutes);

        optionsCard.Controls.Add(new Label
        {
            Text = "分钟",
            Font = UiTheme.FontBody,
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(288, 126),
            Size = new Size(70, 36),
            AutoSize = false
        });

        _customRadio.CheckedChanged += (_, _) => _customMinutes.Enabled = _customRadio.Checked;

        // 恢复上次选择
        int restored = lastDurationMinutes > 0 ? lastDurationMinutes : 1440;
        bool matchedPreset = false;
        for (int i = 0; i < Presets.Length; i++)
        {
            if (Presets[i].Minutes == restored)
            {
                _presetRadios[i].Checked = true;
                matchedPreset = true;
                break;
            }
        }
        if (!matchedPreset)
        {
            _customRadio.Checked = true;
            _customMinutes.Value = restored;
        }
        else
        {
            _customMinutes.Enabled = false;
        }

        // ---------- 底栏 ----------
        var line = new Panel
        {
            BackColor = UiTheme.Divider,
            Location = new Point(24, 488),
            Size = new Size(512, 1)
        };
        Controls.Add(line);

        _countdownLabel = new Label
        {
            Text = " ",
            Font = UiTheme.FontCaption,
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(28, 504),
            Size = new Size(420, 32),
            AutoSize = false
        };
        Controls.Add(_countdownLabel);

        var cancelBtn = new RoundedButton
        {
            Text = "保持静音",
            Font = UiTheme.FontBody,
            DialogResult = DialogResult.Cancel,
            Location = new Point(304, 546),
            Size = new Size(120, 48),
            Radius = 6,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.TextPrimary,
            OutlineColor = UiTheme.Border,
            HoverBackColor = UiTheme.Hover,
            PressedBackColor = Color.FromArgb(225, 225, 225)
        };
        Controls.Add(cancelBtn);

        var allowBtn = new RoundedButton
        {
            Text = "允许外放",
            Font = UiTheme.FontBody,
            DialogResult = DialogResult.OK,
            Location = new Point(436, 546),
            Size = new Size(100, 48),
            Radius = 6,
            BackColor = UiTheme.Accent,
            ForeColor = Color.White,
            HoverBackColor = UiTheme.AccentHover,
            PressedBackColor = UiTheme.AccentPressed
        };
        Controls.Add(allowBtn);

        // 安全侧默认：Enter 与 Esc 一律等于「保持静音」。
        // 这个弹窗是被动弹出的，用户可能正在打字，若 Enter 落在"允许外放"上，
        // 一次误敲键盘就把声音放出去了——所以默认按钮必须是静音那一侧。
        AcceptButton = cancelBtn;
        CancelButton = cancelBtn;
        KeyPreview = true;

        allowBtn.Click += (_, _) => CommitResult(true);
        cancelBtn.Click += (_, _) => CommitResult(false);

        // 「允许外放」延迟 700ms 才可点击：防止用户在别处连点、弹窗恰好在鼠标下方时被穿透确认
        allowBtn.Enabled = false;
        _allowArmTimer = new System.Windows.Forms.Timer { Interval = 700 };
        _allowArmTimer.Tick += (_, _) =>
        {
            _allowArmTimer.Stop();
            allowBtn.Enabled = true;
        };
        Shown += (_, _) => _allowArmTimer.Start();

        // 30 秒后自动按"保持静音"关闭，避免弹窗长期滞留
        _autoCloseAt = DateTime.Now.AddSeconds(30);
        var autoCloseTimer = new System.Windows.Forms.Timer { Interval = 500 };
        autoCloseTimer.Tick += (_, _) =>
        {
            var remain = _autoCloseAt - DateTime.Now;
            if (remain <= TimeSpan.Zero)
            {
                autoCloseTimer.Stop();
                CommitResult(false);
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            _countdownLabel.Text = $"无操作将在 {remain.TotalSeconds:0} 秒后自动保持静音";
            _countdownLabel.ForeColor = remain.TotalSeconds <= 10
                ? UiTheme.Danger : UiTheme.TextSecondary;
        };
        autoCloseTimer.Start();
        FormClosed += (_, _) =>
        {
            autoCloseTimer.Dispose();
            _allowArmTimer.Dispose();
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            CommitResult(false);
            DialogResult = DialogResult.Cancel;
            Close();
        }
        base.OnKeyDown(e);
    }

    private void CommitResult(bool granted)
    {
        int minutes = 1440; // 默认一天
        if (_customRadio.Checked)
        {
            minutes = _customMinutes.Value;
        }
        else
        {
            for (int i = 0; i < Presets.Length; i++)
            {
                if (_presetRadios[i].Checked)
                {
                    minutes = Presets[i].Minutes;
                    break;
                }
            }
        }
        Result = new PromptResult(granted, TimeSpan.FromMinutes(minutes));
    }

    private static Bitmap CreateHeaderIcon()
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(UiTheme.Accent))
            g.FillPath(bg, UiTheme.RoundedRect(new Rectangle(0, 0, 31, 31), 8));
        using var white = new SolidBrush(Color.White);
        g.FillPolygon(white, new[]
        {
            new PointF(7, 13), new PointF(11, 13), new PointF(16, 9),
            new PointF(16, 23), new PointF(11, 19), new PointF(7, 19)
        });
        using var pen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, 18, 10, 25, 22);
        return bmp;
    }

    private static Bitmap CreateWarningIcon()
    {
        var bmp = new Bitmap(18, 18);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(UiTheme.Danger))
            g.FillEllipse(brush, 0, 0, 17, 17);
        TextRenderer.DrawText(g, "!", new Font("Segoe UI", 10F, FontStyle.Bold),
            new Rectangle(0, 0, 18, 18), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return bmp;
    }
}
