using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using CursorBar.Core;

namespace CursorBar;

internal static class TrayIconRenderer
{
    public static Icon Create(double? primary, double? secondary, bool unlimited, bool dimmed)
    {
        var size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            var alpha = dimmed ? 115 : 255;
            var inset = new RectangleF(2.5f, 3.5f, size - 5f, size - 7f);
            if (unlimited)
            {
                DrawUnlimited(g, inset, alpha);
            }
            else
            {
                var top = new RectangleF(inset.X, inset.Y + 4, inset.Width, 11);
                var bottom = new RectangleF(inset.X, inset.Y + 18, inset.Width, 5);
                DrawTrack(g, top, alpha);
                DrawTrack(g, bottom, alpha);
                DrawFill(g, top, primary, Formatters.UsageColorThreshold(primary), alpha);
                DrawFill(g, bottom, secondary ?? 0, Formatters.UsageColorThreshold(secondary), alpha);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var created = Icon.FromHandle(handle);
            return (Icon)created.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void DrawTrack(Graphics g, RectangleF rect, int alpha)
    {
        using var brush = new SolidBrush(Color.FromArgb((int)(0.16 * alpha), 240, 240, 240));
        using var path = Rounded(rect);
        g.FillPath(brush, path);
    }

    private static void DrawFill(Graphics g, RectangleF rect, double? percent, UsageTone tone, int alpha)
    {
        if (percent is not double value || value <= 0) return;
        var width = Math.Max(rect.Height, rect.Width * (float)(Math.Clamp(value, 0, 100) / 100));
        var fill = new RectangleF(rect.X, rect.Y, width, rect.Height);
        using var brush = new SolidBrush(ColorFor(tone, alpha));
        using var path = Rounded(fill);
        g.FillPath(brush, path);
    }

    private static void DrawUnlimited(Graphics g, RectangleF rect, int alpha)
    {
        using var pen = new Pen(ColorFor(UsageTone.Ok, alpha), 2.4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawBezier(
            pen,
            new PointF(rect.Left + 1, rect.Top + rect.Height / 2),
            new PointF(rect.Left + rect.Width * 0.35f, rect.Top),
            new PointF(rect.Left + rect.Width * 0.65f, rect.Bottom),
            new PointF(rect.Right - 1, rect.Top + rect.Height / 2));
    }

    public static Color ColorFor(UsageTone tone, int alpha = 255) => tone switch
    {
        UsageTone.Ok => Color.FromArgb(alpha, 56, 199, 122),
        UsageTone.Watch => Color.FromArgb(alpha, 242, 194, 46),
        UsageTone.High => Color.FromArgb(alpha, 250, 148, 46),
        UsageTone.Critical => Color.FromArgb(alpha, 245, 82, 82),
        _ => Color.FromArgb(alpha, 160, 160, 160),
    };

    public static System.Windows.Media.Color MediaColor(UsageTone tone) => tone switch
    {
        UsageTone.Ok => System.Windows.Media.Color.FromRgb(56, 199, 122),
        UsageTone.Watch => System.Windows.Media.Color.FromRgb(242, 194, 46),
        UsageTone.High => System.Windows.Media.Color.FromRgb(250, 148, 46),
        UsageTone.Critical => System.Windows.Media.Color.FromRgb(245, 82, 82),
        _ => System.Windows.Media.Color.FromRgb(150, 150, 150),
    };

    private static GraphicsPath Rounded(RectangleF rect)
    {
        var radius = rect.Height;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, radius, radius, 90, 180);
        path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 180);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
