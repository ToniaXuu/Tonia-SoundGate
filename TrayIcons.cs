using System.Drawing.Drawing2D;

namespace ToniaSoundGate;

/// <summary>程序化绘制托盘图标：守护态（静音/锁定）与授权态（放行/开启），不依赖外部图片资源。</summary>
internal static class TrayIcons
{
    private static Icon? _guardIcon;
    private static Icon? _allowedIcon;

    public static Icon Guard => _guardIcon ??= Build(allowed: false);
    public static Icon Allowed => _allowedIcon ??= Build(allowed: true);

    private static Icon Build(bool allowed)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // 圆形底盘
        var baseColor = allowed ? Color.FromArgb(46, 160, 120) : Color.FromArgb(220, 90, 90);
        using var baseBrush = new SolidBrush(baseColor);
        g.FillEllipse(baseBrush, 2, 2, 28, 28);

        // 喇叭图形
        using var white = new SolidBrush(Color.White);
        var speaker = new PointF[]
        {
            new(9, 13), new(13, 13), new(18, 9),
            new(18, 23), new(13, 19), new(9, 19)
        };
        g.FillPolygon(white, speaker);

        using var pen = new Pen(Color.White, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (allowed)
        {
            // 声波：允许外放
            g.DrawArc(pen, 19, 11, 7, 10, -55, 110);
            g.DrawArc(pen, 22, 8, 11, 16, -55, 110);
        }
        else
        {
            // 斜杠：静音拦截
            g.DrawLine(pen, 20, 10, 27, 22);
        }

        return Icon.FromHandle(bmp.GetHicon());
    }
}
