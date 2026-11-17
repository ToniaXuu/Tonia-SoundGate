using System.Drawing.Drawing2D;

namespace ToniaSoundGate;

/// <summary>Windows 11 设置风格的托盘弹出面板，替代系统自带 ContextMenuStrip。</summary>
internal sealed class TrayPopupForm : RoundedFormBase
{
    private enum Glyph { Clock, Shield, Power, Update, Exit }

    private readonly Func<string> _statusProvider;
    private readonly Func<bool> _authorizedProvider;
    private readonly Func<bool> _autoStartProvider;
    private readonly Action<int> _onGrant;
    private readonly Action _onGuard;
    private readonly Action<bool> _onAutoStart;
    private readonly Action _onCheckUpdate;
    private readonly Action _onExit;

    private readonly PictureBox _stateDot;
    private readonly Label _statusLabel;
    private readonly MenuRow _autoStartRow;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _closing;

    public TrayPopupForm(
        Func<string> statusProvider,
        Func<bool> authorizedProvider,
        Func<bool> autoStartProvider,
        Action<int> onGrant,
        Action onGuard,
        Action<bool> onAutoStart,
        Action onCheckUpdate,
        Action onExit)
    {
        _statusProvider = statusProvider;
        _authorizedProvider = authorizedProvider;
        _autoStartProvider = autoStartProvider;
        _onGrant = onGrant;
        _onGuard = onGuard;
        _onAutoStart = onAutoStart;
        _onCheckUpdate = onCheckUpdate;
        _onExit = onExit;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = UiTheme.Surface;
        // 430 → 490：新增一行 44px 的「检查更新」+ 一条 16px 分隔线
        ClientSize = new Size(360, 490);
        KeyPreview = true;

        // ---------- 状态卡 ----------
        var statusCard = new RoundedPanel
        {
            BackColor = UiTheme.SurfaceMuted,
            OutlineColor = UiTheme.Border,
            Location = new Point(12, 12),
            Size = new Size(336, 80),
            Radius = 8
        };
        Controls.Add(statusCard);

        _stateDot = new PictureBox
        {
            Image = CreateDot(UiTheme.Danger),
            Location = new Point(18, 32),
            Size = new Size(16, 16),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        statusCard.Controls.Add(_stateDot);

        statusCard.Controls.Add(new Label
        {
            Text = "Tonia-SoundGate",
            Font = UiTheme.FontSection,
            ForeColor = UiTheme.TextPrimary,
            Location = new Point(46, 16),
            Size = new Size(278, 34),
            AutoSize = false
        });
        _statusLabel = new Label
        {
            Text = " ",
            Font = UiTheme.FontCaption,
            ForeColor = UiTheme.TextSecondary,
            Location = new Point(46, 48),
            Size = new Size(278, 30),
            AutoSize = false
        };
        statusCard.Controls.Add(_statusLabel);

        // ---------- 菜单项 ----------
        // 注意顺序：一律先 Close() 让面板立即消失，再执行实际操作。
        // 授权/恢复守护会走 WASAPI COM（蓝牙设备上可能耗时数百毫秒），
        // 若先执行后关闭，用户会明显感到"点了没反应、面板卡住才消失"。
        //
        // 三个授权项末尾的「…」表示还会弹出确认框：解除静音属于高风险操作，
        // 必须二次确认，不能单击即生效（误触一次声音就外放了）。
        int y = 104;
        AddRow(new MenuRow("立即授权 30 分钟…", Glyph.Clock,
            () => { Close(); _onGrant(30); }), ref y);
        AddRow(new MenuRow("立即授权 1 小时…", Glyph.Clock,
            () => { Close(); _onGrant(60); }), ref y);
        AddRow(new MenuRow("立即授权 1 天…", Glyph.Clock,
            () => { Close(); _onGrant(1440); }), ref y);
        AddSeparator(ref y);
        AddRow(new MenuRow("立即恢复守护（静音）", Glyph.Shield,
            () => { Close(); _onGuard(); }), ref y);
        _autoStartRow = new MenuRow("开机自启", Glyph.Power,
            () =>
            {
                _onAutoStart(!_autoStartProvider());
                RefreshStatus();
            }, showCheck: true);
        AddRow(_autoStartRow, ref y);
        AddSeparator(ref y);
        // 唯一会联网的操作，独立成组：只有点击才发起一次请求，程序平时绝不触网。
        // 末尾「…」表示点击后还会有后续反馈（检查结果 / 提示框）。
        AddRow(new MenuRow("检查更新…", Glyph.Update,
            () => { Close(); _onCheckUpdate(); }), ref y);
        AddSeparator(ref y);
        AddRow(new MenuRow("退出", Glyph.Exit,
            () => { Close(); _onExit(); }), ref y);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => RefreshStatus();
    }

    private void AddRow(MenuRow row, ref int y)
    {
        row.Location = new Point(12, y);
        row.Size = new Size(336, 44);
        Controls.Add(row);
        y += 44;
    }

    private void AddSeparator(ref int y)
    {
        Controls.Add(new Panel
        {
            BackColor = UiTheme.Divider,
            Location = new Point(20, y + 8),
            Size = new Size(320, 1)
        });
        y += 16;
    }

    public void ShowAt(Point anchor)
    {
        RefreshStatus();
        var area = Screen.FromPoint(anchor).WorkingArea;
        int x = anchor.X - 70;
        int y = anchor.Y - Height - 10;
        if (y < area.Top) y = anchor.Y + 10;
        if (y + Height > area.Bottom) y = area.Bottom - Height - 8;
        if (x < area.Left) x = area.Left + 8;
        if (x + Width > area.Right) x = area.Right - Width - 8;
        Location = new Point(x, y);
        _timer.Start();
        Show();
        Activate();
    }

    private void RefreshStatus()
    {
        bool authorized = _authorizedProvider();
        _stateDot.Image = CreateDot(authorized
            ? Color.FromArgb(46, 160, 120) : UiTheme.Danger);
        _statusLabel.Text = UiTheme.Fit(_statusProvider(), UiTheme.FontCaption, 270);
        _autoStartRow.Checked = _autoStartProvider();
        _autoStartRow.Invalidate();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!_closing) Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
        base.OnKeyDown(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        _timer.Stop();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    private static Bitmap CreateDot(Color color)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 1, 1, 14, 14);
        return bmp;
    }

    private static void DrawGlyph(Graphics g, Glyph kind, int x, int y, Color color)
    {
        using var pen = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (kind)
        {
            case Glyph.Clock:
                g.DrawEllipse(pen, x, y, 16, 16);
                g.DrawLine(pen, x + 8, y + 4, x + 8, y + 8);
                g.DrawLine(pen, x + 8, y + 8, x + 11, y + 10);
                break;
            case Glyph.Shield:
                g.DrawPolygon(pen, new[]
                {
                    new Point(x + 8, y + 1), new Point(x + 15, y + 4),
                    new Point(x + 14, y + 10), new Point(x + 8, y + 15),
                    new Point(x + 2, y + 10), new Point(x + 1, y + 4)
                });
                break;
            case Glyph.Power:
                g.DrawArc(pen, x + 3, y + 2, 10, 9, 135, 270);
                g.DrawLine(pen, x + 8, y + 1, x + 8, y + 8);
                break;
            case Glyph.Update:
                // 向下箭头落到托盘线：表示"取回新版本"，与其余线性图标同一笔宽
                g.DrawLine(pen, x + 8, y + 1, x + 8, y + 10);
                g.DrawLine(pen, x + 4, y + 7, x + 8, y + 11);
                g.DrawLine(pen, x + 12, y + 7, x + 8, y + 11);
                g.DrawLine(pen, x + 2, y + 14, x + 14, y + 14);
                break;
            case Glyph.Exit:
                g.DrawLine(pen, x + 9, y + 3, x + 3, y + 3);
                g.DrawLine(pen, x + 3, y + 3, x + 3, y + 13);
                g.DrawLine(pen, x + 3, y + 13, x + 9, y + 13);
                g.DrawLine(pen, x + 7, y + 8, x + 15, y + 8);
                g.DrawLine(pen, x + 12, y + 5, x + 15, y + 8);
                g.DrawLine(pen, x + 12, y + 11, x + 15, y + 8);
                break;
        }
    }

    private sealed class MenuRow : Control
    {
        private readonly Glyph _glyph;
        private readonly Action _onClick;
        private bool _hover;
        private bool _pressed;

        public bool Checked { get; set; }
        public bool ShowCheck { get; }

        public MenuRow(string text, Glyph glyph, Action onClick, bool showCheck = false)
        {
            Text = text;
            _glyph = glyph;
            _onClick = onClick;
            ShowCheck = showCheck;
            // 普通 Control 子类不支持 Color.Transparent（会抛"控件不支持透明的背景色"）。
            // 用与父容器一致的纯色背景，视觉上等效且不会崩溃。
            BackColor = UiTheme.Surface;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (_pressed || _hover)
            {
                using var path = UiTheme.RoundedRect(rect, 6);
                using var brush = new SolidBrush(_pressed ? Color.FromArgb(224, 224, 224) : UiTheme.Hover);
                e.Graphics.FillPath(brush, path);
            }
            DrawGlyph(e.Graphics, _glyph, 14, (Height - 16) / 2, UiTheme.TextSecondary);
            var shown = UiTheme.Fit(Text, UiTheme.FontMenu, Width - 88);
            TextRenderer.DrawText(e.Graphics, shown, UiTheme.FontMenu,
                new Rectangle(44, 0, Width - 88, Height), UiTheme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (ShowCheck && Checked)
            {
                using var pen = new Pen(UiTheme.Accent, 2f)
                {
                    StartCap = LineCap.Round, EndCap = LineCap.Round
                };
                e.Graphics.DrawLines(pen, new[]
                {
                    new Point(Width - 36, Height / 2),
                    new Point(Width - 32, Height / 2 + 4),
                    new Point(Width - 24, Height / 2 - 5)
                });
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true; Invalidate(); base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
                Refresh(); // 同步重绘：确保按下反馈在面板关闭之前就画出来
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            DebugLog.Write($"MENU click: [{Text}]");
            _onClick();
        }
    }
}
