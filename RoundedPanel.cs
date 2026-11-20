using System.Drawing.Drawing2D;

namespace Citaspp;

internal sealed class RoundedPanel : Panel
{
    private Color _fillColor = Color.FromArgb(37, 46, 67);
    private Color _outlineColor = Color.FromArgb(82, 93, 121);

    public Color FillColor
    {
        get => _fillColor;
        set
        {
            _fillColor = value;
            Invalidate();
        }
    }

    public Color OutlineColor
    {
        get => _outlineColor;
        set
        {
            _outlineColor = value;
            Invalidate();
        }
    }

    public RoundedPanel()
    {
        DoubleBuffered = true;
        Padding = new Padding(8);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateRegion();
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        base.OnPaintBackground(eventArgs);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = AppTheme.CreateRoundedPath(
            new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)),
            18);
        var lighter = Color.FromArgb(
            Math.Min(255, FillColor.R + 7),
            Math.Min(255, FillColor.G + 7),
            Math.Min(255, FillColor.B + 9));
        using var fill = new LinearGradientBrush(ClientRectangle, FillColor, lighter, 135);
        using var outline = new Pen(OutlineColor, 1);
        eventArgs.Graphics.FillPath(fill, path);
        eventArgs.Graphics.DrawPath(outline, path);
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        if (Width < 2 || Height < 2)
        {
            return;
        }

        using var path = AppTheme.CreateRoundedPath(
            new Rectangle(0, 0, Width, Height),
            18);
        var oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();
    }
}
