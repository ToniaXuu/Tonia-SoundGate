using System.Drawing.Drawing2D;

namespace ToniaSoundGate;

/// <summary>Win11 风格圆角按钮：自绘圆角背景、描边与文字，支持 hover / pressed 状态。</summary>
internal sealed class RoundedButton : Button
{
    private bool _hover;
    private bool _pressed;

    public int Radius { get; set; } = 6;
    public Color HoverBackColor { get; set; } = UiTheme.AccentHover;
    public Color PressedBackColor { get; set; } = UiTheme.AccentPressed;
    public Color? OutlineColor { get; set; }

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiTheme.RoundedRect(rect, Radius);

        Color fill;
        if (!Enabled) fill = Color.FromArgb(235, 235, 235);
        else if (_pressed) fill = PressedBackColor;
        else if (_hover) fill = HoverBackColor;
        else fill = BackColor;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(fill))
            e.Graphics.FillPath(brush, path);
        if (OutlineColor is { } outline)
        {
            using var pen = new Pen(outline);
            e.Graphics.DrawPath(pen, path);
        }

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding;
        TextRenderer.DrawText(e.Graphics, Text, Font, rect,
            Enabled ? ForeColor : UiTheme.TextTertiary, flags);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true; Invalidate(); base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) _pressed = true;
        Invalidate(); base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false; Invalidate(); base.OnMouseUp(e);
    }
}
