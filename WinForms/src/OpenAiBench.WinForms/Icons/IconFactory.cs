using System.Drawing.Drawing2D;

namespace OpenAiBench.WinForms.Icons;

public enum IconKind
{
    New,
    Clone,
    Delete,
    Execute,
    Cancel,
    Benchmark,
    ExecuteAll,
    CancelAll,
    ComparisonGrid,
    Export,
    AddFile,
    RemoveFile,
    OpenFile,
    AddVariable,
    RemoveVariable,
    View,
    Compare,
    ClearHistory,
    Copy,
    Search
}

/// <summary>Small (16x16) toolbar glyphs drawn with GDI+ primitives — no external image assets needed.</summary>
public static class IconFactory
{
    private const int Size = 16;
    private static readonly Color Neutral = Color.FromArgb(70, 70, 70);
    private static readonly Color Green = Color.FromArgb(30, 130, 60);
    private static readonly Color Red = Color.FromArgb(180, 40, 40);
    private static readonly Color Blue = Color.FromArgb(30, 90, 170);

    private static readonly Dictionary<IconKind, Image> Cache = new();

    public static Image Get(IconKind kind)
    {
        if (!Cache.TryGetValue(kind, out var image))
        {
            image = Render(kind);
            Cache[kind] = image;
        }

        return image;
    }

    private static Bitmap Render(IconKind kind)
    {
        var bitmap = new Bitmap(Size, Size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        switch (kind)
        {
            case IconKind.New:
                DrawPage(g, Neutral);
                DrawPlusBadge(g, Blue);
                break;
            case IconKind.Clone:
                DrawPage(g, Neutral, dx: -2, dy: 1, scale: 0.8f);
                DrawPage(g, Neutral, dx: 1, dy: -1, scale: 0.8f);
                break;
            case IconKind.Delete:
            case IconKind.ClearHistory:
                DrawTrash(g, Red);
                break;
            case IconKind.Execute:
                DrawPlayTriangle(g, Green);
                break;
            case IconKind.ExecuteAll:
                DrawPlayTriangle(g, Green, offsetX: -3);
                DrawPlayTriangle(g, Green, offsetX: 3);
                break;
            case IconKind.Cancel:
            case IconKind.CancelAll:
                DrawStopSquare(g, Red);
                break;
            case IconKind.Benchmark:
                DrawStopwatch(g, Neutral);
                break;
            case IconKind.ComparisonGrid:
                DrawGrid(g, Neutral);
                break;
            case IconKind.Export:
                DrawDownloadArrow(g, Blue);
                break;
            case IconKind.AddFile:
                DrawPage(g, Neutral);
                DrawPlusBadge(g, Green);
                break;
            case IconKind.RemoveFile:
                DrawPage(g, Neutral);
                DrawMinusBadge(g, Red);
                break;
            case IconKind.OpenFile:
                DrawFolder(g, Neutral);
                break;
            case IconKind.AddVariable:
                DrawPlus(g, Green, 8, 8, 5);
                break;
            case IconKind.RemoveVariable:
                DrawMinus(g, Red, 8, 8, 5);
                break;
            case IconKind.View:
                DrawEye(g, Neutral);
                break;
            case IconKind.Compare:
                DrawSwapArrows(g, Neutral);
                break;
            case IconKind.Copy:
                DrawClipboard(g, Neutral);
                break;
            case IconKind.Search:
                DrawMagnifier(g, Neutral);
                break;
        }

        return bitmap;
    }

    private static void DrawPage(Graphics g, Color color, float dx = 0, float dy = 0, float scale = 1f)
    {
        using var pen = new Pen(color, 1.4f);
        var rect = new RectangleF(3 + dx, 1.5f + dy, 9 * scale, 12 * scale);
        g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        g.DrawLine(pen, rect.X + 2, rect.Y + 4, rect.X + rect.Width - 2, rect.Y + 4);
        g.DrawLine(pen, rect.X + 2, rect.Y + 7, rect.X + rect.Width - 2, rect.Y + 7);
    }

    private static void DrawPlusBadge(Graphics g, Color color) => DrawPlus(g, color, 12, 12, 4);

    private static void DrawMinusBadge(Graphics g, Color color) => DrawMinus(g, color, 12, 12, 4);

    private static void DrawPlus(Graphics g, Color color, float cx, float cy, float radius)
    {
        using var pen = new Pen(color, 2f);
        g.DrawLine(pen, cx - radius, cy, cx + radius, cy);
        g.DrawLine(pen, cx, cy - radius, cx, cy + radius);
    }

    private static void DrawMinus(Graphics g, Color color, float cx, float cy, float radius)
    {
        using var pen = new Pen(color, 2f);
        g.DrawLine(pen, cx - radius, cy, cx + radius, cy);
    }

    private static void DrawTrash(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.4f);
        g.DrawLine(pen, 2, 4, 14, 4);
        g.DrawLine(pen, 6, 1, 10, 1);
        g.DrawRectangle(pen, 3.5f, 4, 9, 10);
        g.DrawLine(pen, 6, 6.5f, 6, 12);
        g.DrawLine(pen, 8, 6.5f, 8, 12);
        g.DrawLine(pen, 10, 6.5f, 10, 12);
    }

    private static void DrawPlayTriangle(Graphics g, Color color, float offsetX = 0)
    {
        using var brush = new SolidBrush(color);
        var points = new[] { new PointF(4.5f + offsetX, 2), new PointF(4.5f + offsetX, 14), new PointF(13 + offsetX, 8) };
        g.FillPolygon(brush, points);
    }

    private static void DrawStopSquare(Graphics g, Color color)
    {
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, 4, 4, 8, 8);
    }

    private static void DrawStopwatch(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.4f);
        g.DrawEllipse(pen, 2.5f, 3.5f, 10, 10);
        g.DrawLine(pen, 6.5f, 1, 9.5f, 1);
        g.DrawLine(pen, 8, 1, 8, 2.5f);
        g.DrawLine(pen, 7.5f, 8.5f, 10.5f, 5.5f);
        g.DrawLine(pen, 7.5f, 8.5f, 7.5f, 6.5f);
    }

    private static void DrawGrid(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.4f);
        g.DrawRectangle(pen, 2, 2, 12, 12);
        g.DrawLine(pen, 2, 6.5f, 14, 6.5f);
        g.DrawLine(pen, 2, 10.5f, 14, 10.5f);
        g.DrawLine(pen, 6.5f, 2, 6.5f, 14);
        g.DrawLine(pen, 10.5f, 2, 10.5f, 14);
    }

    private static void DrawDownloadArrow(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, 8, 1.5f, 8, 9.5f);
        g.DrawLine(pen, 4.5f, 6.5f, 8, 10);
        g.DrawLine(pen, 11.5f, 6.5f, 8, 10);
        g.DrawLine(pen, 2.5f, 13, 13.5f, 13);
    }

    private static void DrawFolder(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.4f);
        g.DrawLine(pen, 2, 4, 6, 4);
        g.DrawLine(pen, 6, 4, 7.5f, 5.5f);
        g.DrawRectangle(pen, 2, 5.5f, 12, 8);
        g.DrawLine(pen, 2, 5.5f, 14, 5.5f);
    }

    private static void DrawEye(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.4f);
        var path = new GraphicsPath();
        path.AddArc(2, 4, 12, 8, 180, 180);
        path.AddArc(2, 4, 12, 8, 0, 180);
        g.DrawPath(pen, path);
        g.DrawEllipse(pen, 6.5f, 6.5f, 3, 3);
    }

    private static void DrawSwapArrows(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, 2.5f, 5, 12, 5);
        g.DrawLine(pen, 9.5f, 2.5f, 12, 5);
        g.DrawLine(pen, 9.5f, 7.5f, 12, 5);

        g.DrawLine(pen, 13.5f, 11, 4, 11);
        g.DrawLine(pen, 6.5f, 8.5f, 4, 11);
        g.DrawLine(pen, 6.5f, 13.5f, 4, 11);
    }

    private static void DrawClipboard(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.4f);
        g.DrawRectangle(pen, 3.5f, 3, 9, 11);
        g.DrawRectangle(pen, 6, 1.5f, 4, 2.5f);
        g.DrawLine(pen, 5.5f, 7, 10.5f, 7);
        g.DrawLine(pen, 5.5f, 9.5f, 10.5f, 9.5f);
        g.DrawLine(pen, 5.5f, 12, 9, 12);
    }

    private static void DrawMagnifier(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round };
        g.DrawEllipse(pen, 2.5f, 2.5f, 7, 7);
        g.DrawLine(pen, 8.5f, 8.5f, 13, 13);
    }
}
