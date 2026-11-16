using System.Drawing.Drawing2D;

namespace ToniaSoundGate;

/// <summary>
/// 自绘数字输入框：圆角边框 + 右侧上下箭头。
/// 替换系统 NumericUpDown —— 后者在高 DPI 下高度不随 Size 变化，
/// 内部文本框和箭头按钮会按错误比例裁切，且无法通过属性修正。
/// </summary>
internal sealed class NumberInput : Panel
{
    private const int ArrowZone = 32;

    private readonly TextBox _box;
    private int _min = 1;
    private int _max = 10080;
    private bool _hoverUp;
    private bool _hoverDown;

    public event EventHandler? ValueChanged;

    public NumberInput()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.Surface;

        _box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = UiTheme.FontBody,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.TextPrimary,
            TextAlign = HorizontalAlignment.Center
        };
        _box.TextChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        _box.Leave += (_, _) => ClampText();
        _box.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
        };
        Controls.Add(_box);
    }

    public int Minimum
    {
        get => _min;
        set { _min = value; ClampText(); }
    }

    public int Maximum
    {
        get => _max;
        set { _max = value; ClampText(); }
    }

    public int Value
    {
        get => int.TryParse(_box.Text.Trim(), out var v) ? Math.Clamp(v, _min, _max) : _min;
        set => _box.Text = Math.Clamp(value, _min, _max).ToString();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // TextBox 高度由字体决定，这里只负责垂直居中与水平留白，绝不给它固定高度
        int textHeight = _box.PreferredHeight;
        int textWidth = Math.Max(24, Width - ArrowZone - 14);
        _box.SetBounds(7, Math.Max(0, (Height - textHeight) / 2), textWidth, textHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiTheme.RoundedRect(rect, 6);
        using (var back = new SolidBrush(Enabled ? UiTheme.Surface : UiTheme.SurfaceMuted))
            g.FillPath(back, path);
        using (var border = new Pen(Enabled ? UiTheme.Border : UiTheme.Divider))
            g.DrawPath(border, path);

        int zoneX = Width - ArrowZone;
        using (var sep = new Pen(UiTheme.Divider))
            g.DrawLine(sep, zoneX, 5, zoneX, Height - 6);

        var upRect = new Rectangle(zoneX + 3, 3, ArrowZone - 6, Height / 2 - 5);
        var downRect = new Rectangle(zoneX + 3, Height / 2 + 2, ArrowZone - 6, Height / 2 - 5);

        if (Enabled && _hoverUp) FillHover(g, upRect);
        if (Enabled && _hoverDown) FillHover(g, downRect);

        var arrowColor = Enabled ? UiTheme.TextSecondary : UiTheme.TextTertiary;
        float cx = zoneX + ArrowZone / 2f;
        float upCy = upRect.Y + upRect.Height / 2f;
        float downCy = downRect.Y + downRect.Height / 2f;

        using var brush = new SolidBrush(arrowColor);
        g.FillPolygon(brush, new[]
        {
            new PointF(cx - 5, upCy + 3.5f),
            new PointF(cx + 5, upCy + 3.5f),
            new PointF(cx, upCy - 4f)
        });
        g.FillPolygon(brush, new[]
        {
            new PointF(cx - 5, downCy - 3.5f),
            new PointF(cx + 5, downCy - 3.5f),
            new PointF(cx, downCy + 4f)
        });
    }

    private static void FillHover(Graphics g, Rectangle r)
    {
        using var path = UiTheme.RoundedRect(r, 4);
        using var brush = new SolidBrush(UiTheme.Hover);
        g.FillPath(brush, path);
    }

    private int HitZone(int x, int y)
    {
        if (x < Width - ArrowZone) return 0;
        return y < Height / 2 ? 1 : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int zone = HitZone(e.X, e.Y);
        bool up = zone == 1;
        bool down = zone == -1;
        if (up != _hoverUp || down != _hoverDown)
        {
            _hoverUp = up;
            _hoverDown = down;
            Cursor = zone == 0 ? Cursors.IBeam : Cursors.Hand;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hoverUp || _hoverDown)
        {
            _hoverUp = false;
            _hoverDown = false;
            Invalidate();
        }
        Cursor = Cursors.Default;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            int zone = HitZone(e.X, e.Y);
            if (zone != 0)
            {
                Value += zone;
                _box.Focus();
                _box.SelectionStart = _box.TextLength;
            }
        }
        base.OnMouseDown(e);
    }

    private void ClampText()
    {
        if (!int.TryParse(_box.Text.Trim(), out var v)) v = _min;
        v = Math.Clamp(v, _min, _max);
        if (_box.Text != v.ToString()) _box.Text = v.ToString();
    }
}
