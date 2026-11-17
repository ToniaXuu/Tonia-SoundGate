using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace ToniaSoundGate;

/// <summary>
/// 发现新版本时的中性提示框：沿用 Win11 圆角视觉，但不复用 ConfirmForm 的红色警告语义。
/// 默认按钮为「稍后再说」，Enter / Esc 一律走不打开浏览器的安全侧；
/// 只有用户明确点击「前往下载页」才启动浏览器。
/// 本窗体本身不联网——联网只发生在用户点「检查更新」那一刻。
/// </summary>
internal sealed class UpdateNoticeForm : RoundedFormBase
{
    private readonly string _releaseUrl;
    private bool _opening;

    public UpdateNoticeForm(string currentVersion, string latestVersion, string releaseUrl)
    {
        _releaseUrl = releaseUrl;

        Text = "发现新版本";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.WindowBg;
        ForeColor = UiTheme.TextPrimary;
        Font = UiTheme.FontBody;
        ClientSize = new Size(480, 286);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;

        // ---------- 标题 ----------
        var icon = new PictureBox
        {
            Image = CreateUpdateIcon(30),
            Location = new Point(26, 22),
            Size = new Size(30, 30),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        Controls.Add(icon);
        Controls.Add(new Label
        {
            Text = "发现新版本",
            Font = UiTheme.FontTitle,
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(68, 20),
            Size = new Size(386, 38),
            AutoSize = false
        });

        // ---------- 版本卡片（蓝色中性风格，与风险警告区分） ----------
        var card = new RoundedPanel
        {
            BackColor = UiTheme.AccentSoft,
            OutlineColor = Color.FromArgb(200, 222, 244),
            Location = new Point(24, 72),
            Size = new Size(432, 116),
            Radius = 8
        };
        Controls.Add(card);

        card.Controls.Add(new Label
        {
            Text = "当前版本",
            Font = UiTheme.FontCaption,
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(22, 20),
            Size = new Size(120, 24),
            AutoSize = false
        });
        card.Controls.Add(new Label
        {
            Text = "v" + currentVersion,
            Font = UiTheme.FontSection,
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(22, 44),
            Size = new Size(160, 30),
            AutoSize = false
        });
        card.Controls.Add(new Label
        {
            Text = "→",
            Font = UiTheme.FontSection,
            ForeColor = UiTheme.Accent,
            Location = new Point(196, 46),
            Size = new Size(32, 30),
            AutoSize = false
        });
        card.Controls.Add(new Label
        {
            Text = "最新版本",
            Font = UiTheme.FontCaption,
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(238, 20),
            Size = new Size(160, 24),
            AutoSize = false
        });
        card.Controls.Add(new Label
        {
            Text = "v" + latestVersion,
            Font = UiTheme.FontSection,
            ForeColor = UiTheme.Accent,
            Location = new Point(238, 44),
            Size = new Size(170, 30),
            AutoSize = false
        });
        card.Controls.Add(new Label
        {
            Text = "打开浏览器后由你自行下载并安装，程序不会自动下载或改动任何文件。",
            Font = UiTheme.FontCaption,
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(22, 82),
            Size = new Size(392, 28),
            AutoSize = false
        });

        // ---------- 底栏按钮 ----------
        var laterBtn = new RoundedButton
        {
            Text = "稍后再说",
            Font = UiTheme.FontBody,
            Location = new Point(216, 210),
            Size = new Size(112, 46),
            Radius = 6,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.TextPrimary,
            OutlineColor = UiTheme.Border,
            HoverBackColor = UiTheme.Hover,
            PressedBackColor = Color.FromArgb(225, 225, 225)
        };
        Controls.Add(laterBtn);

        var openBtn = new RoundedButton
        {
            Text = "前往下载页",
            Font = UiTheme.FontBody,
            Location = new Point(340, 210),
            Size = new Size(116, 46),
            Radius = 6,
            BackColor = UiTheme.Accent,
            ForeColor = Color.White,
            HoverBackColor = UiTheme.AccentHover,
            PressedBackColor = UiTheme.AccentPressed
        };
        Controls.Add(openBtn);

        // 默认按钮落在安全侧：键盘误触不会打开浏览器
        AcceptButton = laterBtn;
        CancelButton = laterBtn;

        laterBtn.Click += (_, _) => CloseSafely();
        openBtn.Click += (_, _) => OpenRelease();
    }

    private void CloseSafely()
    {
        if (_opening) return;
        _opening = true;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void OpenRelease()
    {
        if (_opening) return;
        _opening = true;
        try
        {
            Process.Start(new ProcessStartInfo(_releaseUrl) { UseShellExecute = true });
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"UpdateNoticeForm: 打开浏览器失败 {ex.Message}");
            // 打不开也给用户明确反馈，而不是静默无反应
            ToastService.Show("无法打开浏览器", "请手动访问 GitHub 上的 Releases 页面", ToastKind.Warning, 4000);
            DialogResult = DialogResult.Cancel;
        }
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) CloseSafely();
        base.OnKeyDown(e);
    }

    private static Bitmap CreateUpdateIcon(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float d = size - 1;
        using (var brush = new SolidBrush(UiTheme.Accent))
            g.FillEllipse(brush, 0, 0, d, d);
        using var pen = new Pen(Color.White, 2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        // 环形箭头（更新语义）
        g.DrawArc(pen, 5, 5, size - 10, size - 10, -40, 280);
        g.DrawLines(pen, new[]
        {
            new PointF(size - 8, 5),
            new PointF(size - 9, 11),
            new PointF(size - 15, 9)
        });
        return bmp;
    }
}
