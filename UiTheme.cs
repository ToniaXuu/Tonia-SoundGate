using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ToniaSoundGate;

/// <summary>统一 UI 主题：Windows 11 设置（浅色）风格的配色、字体与圆角绘制。</summary>
internal static class UiTheme
{
    // 颜色：取自 Fluent / Windows 11 浅色主题
    public static readonly Color WindowBg = Color.FromArgb(243, 243, 243);
    public static readonly Color Surface = Color.FromArgb(255, 255, 255);
    public static readonly Color SurfaceMuted = Color.FromArgb(249, 249, 249);
    public static readonly Color TextPrimary = Color.FromArgb(0, 0, 0);
    public static readonly Color TextSecondary = Color.FromArgb(97, 97, 97);
    public static readonly Color TextTertiary = Color.FromArgb(138, 138, 138);
    public static readonly Color Accent = Color.FromArgb(0, 103, 192);
    public static readonly Color AccentHover = Color.FromArgb(0, 90, 158);
    public static readonly Color AccentPressed = Color.FromArgb(0, 78, 140);
    public static readonly Color AccentSoft = Color.FromArgb(232, 241, 251);
    public static readonly Color Danger = Color.FromArgb(196, 43, 28);
    public static readonly Color DangerSoft = Color.FromArgb(253, 243, 242);
    public static readonly Color Divider = Color.FromArgb(229, 229, 229);
    public static readonly Color Hover = Color.FromArgb(240, 240, 240);
    public static readonly Color Border = Color.FromArgb(225, 225, 225);

    public static readonly Font FontTitle = new("Microsoft YaHei UI", 15F, FontStyle.Bold);
    public static readonly Font FontSection = new("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
    public static readonly Font FontBody = new("Microsoft YaHei UI", 9.5F);
    public static readonly Font FontCaption = new("Microsoft YaHei UI", 8.5F);
    public static readonly Font FontMenu = new("Microsoft YaHei UI", 9.5F);

    /// <summary>创建圆角矩形路径，调用方负责 Dispose。</summary>
    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(1, radius * 2);
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>按可用宽度截断文本并加省略号。</summary>
    public static string Fit(string text, Font font, int maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (TextRenderer.MeasureText(text, font).Width <= maxWidth) return text;
        var s = text;
        while (s.Length > 1 && TextRenderer.MeasureText(s + "…", font).Width > maxWidth)
            s = s[..^1];
        return s + "…";
    }
}

/// <summary>带 Windows 11 原生圆角与阴影的窗体基类（Win10 回退到 Region 圆角）。</summary>
internal abstract class RoundedFormBase : Form
{
    protected virtual int CornerRadius => 10;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2（Win11 原生圆角）
        int pref = 2;
        int hr = NativeMethods.DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int));
        if (hr != 0)
        {
            using var path = UiTheme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
            Region = new Region(path);
        }
    }
}

internal static class NativeMethods
{
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
