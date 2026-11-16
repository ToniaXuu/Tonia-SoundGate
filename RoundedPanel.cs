using System.Drawing.Drawing2D;

namespace ToniaSoundGate;

/// <summary>自绘圆角面板，用于卡片式分区背景。</summary>
internal sealed class RoundedPanel : Panel
{
    public int Radius { get; set; } = 8;
    public Color? OutlineColor { get; set; }

    public RoundedPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiTheme.RoundedRect(rect, Radius);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(BackColor))
            e.Graphics.FillPath(brush, path);
        if (OutlineColor is { } outline)
        {
            using var pen = new Pen(outline);
            e.Graphics.DrawPath(pen, path);
        }
    }
}
