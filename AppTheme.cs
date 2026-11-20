using System.Drawing.Drawing2D;

namespace Citaspp;

internal static class AppTheme
{
    private static readonly Color Background = Color.FromArgb(31, 39, 57);
    private static readonly Color Surface = Color.FromArgb(37, 46, 67);
    private static readonly Color ControlSurface = Color.FromArgb(47, 57, 81);
    private static readonly Color HeaderSurface = Color.FromArgb(28, 35, 52);
    private static readonly Color Accent = Color.FromArgb(0, 205, 232);
    private static readonly Color AccentHover = Color.FromArgb(70, 224, 242);
    private static readonly Color Lime = Color.FromArgb(202, 222, 20);
    private static readonly Color Orange = Color.FromArgb(255, 151, 24);
    private static readonly Color Purple = Color.FromArgb(145, 70, 226);
    private static readonly Color Pink = Color.FromArgb(235, 57, 120);
    private static readonly Color Foreground = Color.FromArgb(243, 246, 252);
    private static readonly Color MutedForeground = Color.FromArgb(183, 192, 211);
    private static readonly Color Border = Color.FromArgb(82, 93, 121);
    private static readonly Color DestructiveSurface = Color.FromArgb(77, 41, 58);
    private static readonly Color DestructiveForeground = Color.FromArgb(255, 182, 203);
    private static readonly Font UiFont = new("Segoe UI", 9F);
    private static readonly Font GridFont = new("Segoe UI", 9F);

    public static Color AccentColor => Accent;
    public static Color LimeColor => Lime;
    public static Color OrangeColor => Orange;
    public static Color PurpleColor => Purple;
    public static Color PinkColor => Pink;

    public static void Apply(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Foreground;
        form.Font = UiFont;
        ApplyControl(form);
    }

    private static void ApplyControl(Control control)
    {
        switch (control)
        {
            case DataGridView grid:
                grid.BackgroundColor = Background;
                grid.BorderStyle = BorderStyle.None;
                grid.GridColor = Border;
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
                grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderSurface;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Accent;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = HeaderSurface;
                grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Accent;
                grid.ColumnHeadersDefaultCellStyle.Font = UiFont;
                grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(5);
                grid.DefaultCellStyle.BackColor = ControlSurface;
                grid.DefaultCellStyle.ForeColor = Foreground;
                grid.DefaultCellStyle.SelectionBackColor = Accent;
                grid.DefaultCellStyle.SelectionForeColor = HeaderSurface;
                grid.DefaultCellStyle.Font = GridFont;
                grid.DefaultCellStyle.Padding = new Padding(4);
                grid.AlternatingRowsDefaultCellStyle.BackColor = Surface;
                grid.RowTemplate.DefaultCellStyle.BackColor = ControlSurface;
                break;
            case RoundedPanel card:
                card.FillColor = Surface;
                card.OutlineColor = Border;
                card.BackColor = Background;
                break;
            case TabControl tabs:
                tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                tabs.ItemSize = new Size(108, 32);
                tabs.SizeMode = TabSizeMode.Fixed;
                tabs.DrawItem += (_, args) =>
                {
                    if (args.Index < 0)
                    {
                        return;
                    }

                    var selected = args.Index == tabs.SelectedIndex;
                    var backColor = selected ? ControlSurface : Background;
                    var foreColor = selected ? Accent : MutedForeground;
                    using var backgroundBrush = new SolidBrush(backColor);
                    using var foregroundBrush = new SolidBrush(foreColor);
                    args.Graphics.FillRectangle(backgroundBrush, args.Bounds);
                    TextRenderer.DrawText(
                        args.Graphics,
                        tabs.TabPages[args.Index].Text,
                        UiFont,
                        args.Bounds,
                        foreColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                };
                control.BackColor = Background;
                control.ForeColor = Foreground;
                break;
            case Button button:
                StyleButton(button);
                break;
            case TextBoxBase textBox:
                textBox.BackColor = ControlSurface;
                textBox.ForeColor = Foreground;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox comboBox:
                comboBox.BackColor = ControlSurface;
                comboBox.ForeColor = Foreground;
                comboBox.FlatStyle = FlatStyle.Flat;
                break;
            case DateTimePicker dateTimePicker:
                dateTimePicker.CalendarMonthBackground = ControlSurface;
                dateTimePicker.CalendarForeColor = Foreground;
                dateTimePicker.CalendarTitleBackColor = HeaderSurface;
                dateTimePicker.CalendarTitleForeColor = Accent;
                dateTimePicker.CalendarTrailingForeColor = MutedForeground;
                dateTimePicker.BackColor = ControlSurface;
                dateTimePicker.ForeColor = Foreground;
                break;
            case NumericUpDown numericUpDown:
                numericUpDown.BackColor = ControlSurface;
                numericUpDown.ForeColor = Foreground;
                break;
            case CheckBox checkBox:
                checkBox.BackColor = Color.Transparent;
                checkBox.ForeColor = Foreground;
                break;
            case Label label:
                label.BackColor = Color.Transparent;
                if (label.ForeColor == SystemColors.ControlText)
                {
                    label.ForeColor = MutedForeground;
                }
                break;
            case TabPage tabPage:
                tabPage.BackColor = Background;
                tabPage.ForeColor = Foreground;
                break;
            case FlowLayoutPanel or TableLayoutPanel:
                control.BackColor = Surface;
                control.ForeColor = Foreground;
                break;
            case Panel panel:
                if (panel.BackColor == SystemColors.Control)
                {
                    panel.BackColor = Surface;
                }
                control.ForeColor = Foreground;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyControl(child);
        }
    }

    private static void StyleButton(Button button)
    {
        var text = button.Text.Trim();
        var isDestructive = text.StartsWith("Eliminar", StringComparison.OrdinalIgnoreCase);
        var isPrimary = text is "Nueva cita" or "Guardar" or "Añadir" or "Añadir festivo" or "Añadir servicio"
            or "Importar CSV" or "Seleccionar archivo CSV...";
        var accent = isDestructive
            ? Pink
            : text.Contains("Mes", StringComparison.OrdinalIgnoreCase)
                ? Orange
                : text.Contains("Semana", StringComparison.OrdinalIgnoreCase)
                    ? Lime
                    : text is "Configuración" or "Editar" ? Purple : Accent;

        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = isPrimary || isDestructive || text.Contains("Mes", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Semana", StringComparison.OrdinalIgnoreCase)
            ? accent
            : Border;
        button.FlatAppearance.MouseOverBackColor = isDestructive ? DestructiveSurface : AccentHover;
        button.Font = UiFont;
        SetRoundedButtonRegion(button);

        if (isDestructive)
        {
            button.BackColor = DestructiveSurface;
            button.ForeColor = DestructiveForeground;
        }
        else if (isPrimary)
        {
            button.BackColor = accent;
            button.ForeColor = HeaderSurface;
        }
        else
        {
            button.BackColor = ControlSurface;
            button.ForeColor = accent == Accent ? Foreground : accent;
        }
    }

    internal static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        var path = new GraphicsPath();
        path.StartFigure();
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void SetRoundedButtonRegion(Button button)
    {
        void UpdateRegion(object? sender, EventArgs args)
        {
            if (button.Width < 2 || button.Height < 2)
            {
                return;
            }

            using var path = CreateRoundedPath(
                new Rectangle(0, 0, button.Width, button.Height),
                Math.Min(16, button.Height / 2));
            var oldRegion = button.Region;
            button.Region = new Region(path);
            oldRegion?.Dispose();
        }

        button.SizeChanged += UpdateRegion;
        UpdateRegion(button, EventArgs.Empty);
    }
}
