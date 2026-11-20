using System.Globalization;
using System.Text;

namespace Citaspp;

internal sealed class SettingsForm : Form
{
    private readonly AppointmentRepository _repository;
    private readonly Dictionary<DayOfWeek, CheckBox> _workDays = [];
    private readonly DateTimePicker _startTime = new();
    private readonly DateTimePicker _endTime = new();
    private readonly NumericUpDown _workerCount = new();
    private readonly DataGridView _services = new();
    private readonly DataGridView _holidays = new();
    private readonly DataGridView _workers = new();
    private List<Service> _serviceItems = [];
    private List<Holiday> _holidayItems = [];

    public SettingsForm(AppointmentRepository repository)
    {
        _repository = repository;
        Text = "Configuración de agenda";
        Width = 650;
        Height = 500;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildScheduleTab());
        tabs.TabPages.Add(BuildWorkersTab());
        tabs.TabPages.Add(BuildServicesTab());
        tabs.TabPages.Add(BuildHolidaysTab());

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        var save = new Button { Text = "Guardar", AutoSize = true };
        save.Click += (_, _) => SaveAll();
        var cancel = new Button { Text = "Cerrar", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        var contentCard = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        contentCard.Controls.Add(tabs);
        Controls.Add(contentCard);
        Controls.Add(buttons);
        LoadValues();
        AppTheme.Apply(this);
    }

    private TabPage BuildScheduleTab()
    {
        var page = new TabPage("Jornada");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = 5,
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddTimePicker(_startTime);
        AddTimePicker(_endTime);
        AddSettingRow(layout, 0, "Inicio de jornada:", _startTime);
        AddSettingRow(layout, 1, "Fin de jornada:", _endTime);
        _workerCount.Minimum = 1;
        _workerCount.Maximum = 20;
        AddSettingRow(layout, 2, "Trabajadores:", _workerCount);

        var days = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        foreach (var day in new[]
                 {
                     DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                     DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
                 })
        {
            var checkBox = new CheckBox { Text = DayName(day), AutoSize = true };
            _workDays.Add(day, checkBox);
            days.Controls.Add(checkBox);
        }
        AddSettingRow(layout, 3, "Días laborables:", days);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildWorkersTab()
    {
        var page = new TabPage("Trabajadores");
        ConfigureGrid(_workers);
        _workers.ReadOnly = false;
        _workers.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "WorkerName",
            HeaderText = "Nombre",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _workers.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "WorkerColor",
            HeaderText = "Color de agenda",
            Width = 150,
            Text = "Elegir color",
            UseColumnTextForButtonValue = true
        });
        _workers.Columns.Add(new DataGridViewTextBoxColumn { Name = "WorkerId", HeaderText = "Id", Width = 60, ReadOnly = true });
        _workers.Columns["WorkerId"]!.Visible = false;
        var workers = _repository.GetWorkers();
        foreach (var worker in workers)
        {
            var color = _repository.GetWorkerColor(worker.Id);
            var row = _workers.Rows[_workers.Rows.Add(worker.Name, "Elegir color", worker.Id)];
            row.Tag = worker.Id;
            row.Cells["WorkerColor"].Tag = color;
            row.Cells["WorkerColor"].Style.BackColor = ColorTranslator.FromHtml(color);
        }
        _workers.CellClick += (_, args) =>
        {
            if (args.RowIndex < 0 || _workers.Columns[args.ColumnIndex].Name != "WorkerColor")
            {
                return;
            }

            var cell = _workers.Rows[args.RowIndex].Cells[args.ColumnIndex];
            using var picker = new ColorDialog { Color = ColorTranslator.FromHtml((string)cell.Tag!) };
            if (picker.ShowDialog(this) == DialogResult.OK)
            {
                var color = ColorTranslator.ToHtml(picker.Color);
                cell.Tag = color;
                cell.Style.BackColor = picker.Color;
            }
        };
        page.Controls.Add(_workers);
        return page;
    }

    private TabPage BuildServicesTab()
    {
        var page = new TabPage("Servicios");
        ConfigureGrid(_services);
        _services.ReadOnly = false;
        _services.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Servicio", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _services.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Duración (min)", Width = 130 });

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(6) };
        var add = new Button { Text = "Añadir", AutoSize = true };
        add.Click += (_, _) => AddService();
        var delete = new Button { Text = "Eliminar seleccionado", AutoSize = true };
        delete.Click += (_, _) => DeleteService();
        buttons.Controls.Add(add);
        buttons.Controls.Add(delete);
        page.Controls.Add(_services);
        page.Controls.Add(buttons);
        return page;
    }

    private TabPage BuildHolidaysTab()
    {
        var page = new TabPage("Festivos");
        ConfigureGrid(_holidays);
        _holidays.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha", Width = 130 });
        _holidays.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Festivo", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(6) };
        var add = new Button { Text = "Añadir festivo", AutoSize = true };
        add.Click += (_, _) => AddHoliday();
        var import = new Button { Text = "Importar CSV", AutoSize = true };
        import.Click += (_, _) => ImportHolidays();
        var delete = new Button { Text = "Eliminar seleccionado", AutoSize = true };
        delete.Click += (_, _) => DeleteHoliday();
        buttons.Controls.Add(add);
        buttons.Controls.Add(import);
        buttons.Controls.Add(delete);
        page.Controls.Add(_holidays);
        page.Controls.Add(buttons);
        return page;
    }

    private void LoadValues()
    {
        var settings = _repository.GetSettings();
        _startTime.Value = DateTime.Today.Add(settings.StartTime);
        _endTime.Value = DateTime.Today.Add(settings.EndTime);
        _workerCount.Value = settings.WorkerCount;
        foreach (var (day, checkBox) in _workDays)
        {
            checkBox.Checked = settings.WorkDays.Contains(day);
        }

        _serviceItems = _repository.GetServices();
        _services.Rows.Clear();
        foreach (var service in _serviceItems)
        {
            var row = _services.Rows[_services.Rows.Add(service.Name, service.DurationMinutes)];
            row.Tag = service.Id;
        }

        _holidayItems = _repository.GetHolidays();
        _holidays.Rows.Clear();
        foreach (var holiday in _holidayItems)
        {
            _holidays.Rows.Add(holiday.Date.ToString("dd/MM/yyyy"), holiday.Name);
        }
    }

    private void SaveAll()
    {
        try
        {
            var settings = new ScheduleSettings
            {
                StartTime = _startTime.Value.TimeOfDay,
                EndTime = _endTime.Value.TimeOfDay,
                WorkerCount = (int)_workerCount.Value,
                WorkDays = _workDays.Where(pair => pair.Value.Checked).Select(pair => pair.Key).ToHashSet()
            };
            var serviceRows = new List<(int? Id, string Name, int Duration)>();
            foreach (DataGridViewRow row in _services.Rows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }

                var name = Convert.ToString(row.Cells[0].Value)?.Trim() ?? "";
                if (!int.TryParse(Convert.ToString(row.Cells[1].Value), out var duration))
                {
                    throw new ArgumentException($"Duración no válida para el servicio «{name}».");
                }

                if (string.IsNullOrWhiteSpace(name) || duration < 15 || duration > 480 || duration % 15 != 0)
                {
                    throw new ArgumentException($"El servicio «{name}» debe tener nombre y una duración de 15 a 480 minutos, en múltiplos de 15.");
                }

                serviceRows.Add((row.Tag is int id ? id : null, name, duration));
            }

            if (serviceRows.Select(row => row.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != serviceRows.Count)
            {
                throw new ArgumentException("No puede haber dos servicios con el mismo nombre.");
            }

            if (serviceRows.Count == 0)
            {
                throw new ArgumentException("Debe existir al menos un servicio.");
            }

            var workerNames = new List<(int Id, string Name, string Color)>();
            foreach (DataGridViewRow row in _workers.Rows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }

                var id = row.Tag is int workerId ? workerId : Convert.ToInt32(row.Cells["WorkerId"].Value);
                var name = Convert.ToString(row.Cells[0].Value)?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new ArgumentException("El nombre de cada trabajador es obligatorio.");
                }
                var color = Convert.ToString(row.Cells["WorkerColor"].Tag) ?? "#ADD8E6";
                workerNames.Add((id, name, color));
            }

            if (workerNames.Select(worker => worker.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != workerNames.Count)
            {
                throw new ArgumentException("No puede haber trabajadores con el mismo nombre.");
            }

            var holidayRows = new List<Holiday>();
            foreach (DataGridViewRow row in _holidays.Rows)
            {
                if (!DateTime.TryParse(Convert.ToString(row.Cells[0].Value), out var date))
                {
                    throw new ArgumentException("Hay una fecha de festivo no válida.");
                }

                var name = Convert.ToString(row.Cells[1].Value) ?? "";
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new ArgumentException("Escribe el nombre de todos los festivos.");
                }

                holidayRows.Add(new Holiday(date.Date, name.Trim()));
            }

            if (holidayRows.Select(holiday => holiday.Date).Distinct().Count() != holidayRows.Count)
            {
                throw new ArgumentException("No puede haber festivos duplicados en una misma fecha.");
            }

            _repository.SaveSettings(settings);
            foreach (var worker in workerNames)
            {
                _repository.SaveWorker(worker.Id, worker.Name, NormalizeWorkerColor(worker.Color));
            }
            var serviceIds = serviceRows.Where(row => row.Id.HasValue).Select(row => row.Id!.Value).ToHashSet();
            foreach (var removed in _serviceItems.Where(service => !serviceIds.Contains(service.Id)))
            {
                _repository.DeleteService(removed.Id);
            }
            foreach (var service in serviceRows)
            {
                _repository.SaveService(service.Id, service.Name, service.Duration);
            }

            var holidayDates = holidayRows.Select(holiday => holiday.Date).ToHashSet();
            foreach (var removed in _holidayItems.Where(holiday => !holidayDates.Contains(holiday.Date.Date)))
            {
                _repository.DeleteHoliday(removed.Date);
            }
            foreach (var holiday in holidayRows)
            {
                _repository.SaveHoliday(holiday.Date, holiday.Name);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            MessageBox.Show(this, exception.Message, "No se pudo guardar la configuración", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void AddService()
    {
        using var dialog = new ServiceEditorForm();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var service = dialog.ServiceValue;
            _services.Rows.Add(service.Name, service.DurationMinutes);
        }
    }

    private void DeleteService()
    {
        if (_services.CurrentRow is null || _services.CurrentRow.IsNewRow)
        {
            return;
        }

        var serviceName = Convert.ToString(_services.CurrentRow.Cells[0].Value);
        _services.Rows.Remove(_services.CurrentRow);
    }

    private void AddHoliday()
    {
        using var dialog = new HolidayEditorForm();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _holidays.Rows.Add(dialog.HolidayValue.Date.ToString("dd/MM/yyyy"), dialog.HolidayValue.Name);
        }
    }

    private void DeleteHoliday()
    {
        if (_holidays.CurrentRow is null || _holidays.CurrentRow.IsNewRow)
        {
            return;
        }

        var dateText = Convert.ToString(_holidays.CurrentRow.Cells[0].Value);
        _holidays.Rows.Remove(_holidays.CurrentRow);
    }

    private void ImportHolidays()
    {
        using var dialog = new HolidayImportDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var imported = ParseHolidayCsv(File.ReadAllText(dialog.FilePath, Encoding.UTF8));
            var updated = 0;
            foreach (var holiday in imported)
            {
                var existingRow = _holidays.Rows
                    .Cast<DataGridViewRow>()
                    .FirstOrDefault(row =>
                        !row.IsNewRow &&
                        DateTime.TryParseExact(
                            Convert.ToString(row.Cells[0].Value),
                            "dd/MM/yyyy",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out var existingDate) &&
                        existingDate.Date == holiday.Date);

                if (existingRow is null)
                {
                    _holidays.Rows.Add(holiday.Date.ToString("dd/MM/yyyy"), holiday.Name);
                }
                else
                {
                    existingRow.Cells[1].Value = holiday.Name;
                    updated++;
                }
            }

            MessageBox.Show(
                this,
                $"Se han importado {imported.Count} festivo(s). Fechas existentes actualizadas: {updated}.",
                "Importación completada",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            MessageBox.Show(this, exception.Message, "No se pudo importar el calendario", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static List<Holiday> ParseHolidayCsv(string contents)
    {
        var lines = contents.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var firstLine = lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
        if (firstLine is null)
        {
            throw new FormatException("El archivo CSV está vacío.");
        }

        var delimiter = firstLine.Count(character => character == ';') > firstLine.Count(character => character == ',')
            ? ';'
            : ',';
        var holidays = new List<Holiday>();
        var dates = new HashSet<DateTime>();
        var lineNumber = 0;
        var firstRecord = true;

        foreach (var line in lines)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ParseCsvLine(line, delimiter);
            if (firstRecord &&
                fields.Count == 2 &&
                (string.Equals(fields[0].Trim(), "fecha", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(fields[0].Trim(), "date", StringComparison.OrdinalIgnoreCase)))
            {
                firstRecord = false;
                continue;
            }
            firstRecord = false;

            if (fields.Count != 2)
            {
                throw new FormatException($"La línea {lineNumber} debe tener dos columnas: fecha y festivo.");
            }

            if (!DateTime.TryParseExact(
                    fields[0].Trim(),
                    new[] { "yyyy-MM-dd", "dd/MM/yyyy" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                throw new FormatException($"La fecha de la línea {lineNumber} no es válida. Usa AAAA-MM-DD o DD/MM/AAAA.");
            }

            var name = fields[1].Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new FormatException($"Falta el nombre del festivo en la línea {lineNumber}.");
            }

            if (!dates.Add(date.Date))
            {
                throw new FormatException($"La fecha {date:dd/MM/yyyy} aparece más de una vez en el archivo.");
            }

            holidays.Add(new Holiday(date.Date, name));
        }

        if (holidays.Count == 0)
        {
            throw new FormatException("El archivo no contiene filas de festivos.");
        }

        return holidays;
    }

    private static List<string> ParseCsvLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == delimiter && !inQuotes)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        if (inQuotes)
        {
            throw new FormatException("Hay comillas sin cerrar en una fila del archivo CSV.");
        }

        fields.Add(field.ToString());
        return fields;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
    }

    private static void AddTimePicker(DateTimePicker picker)
    {
        picker.Format = DateTimePickerFormat.Time;
        picker.ShowUpDown = true;
    }

    private static string NormalizeWorkerColor(string htmlColor)
    {
        var color = ColorTranslator.FromHtml(htmlColor);
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    internal static void AddSettingRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        control.Dock = DockStyle.Fill;
        layout.Controls.Add(control, 1, row);
    }

    private static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Lunes",
        DayOfWeek.Tuesday => "Martes",
        DayOfWeek.Wednesday => "Miércoles",
        DayOfWeek.Thursday => "Jueves",
        DayOfWeek.Friday => "Viernes",
        DayOfWeek.Saturday => "Sábado",
        _ => "Domingo"
    };
}

internal sealed class HolidayImportDialog : Form
{
    private readonly Label _selectedFile = new()
    {
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft
    };

    public string FilePath { get; private set; } = "";

    public HolidayImportDialog()
    {
        Text = "Importar calendario de festivos";
        Width = 560;
        Height = 350;
        MinimumSize = new Size(480, 320);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 115));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Text = "Selecciona un archivo CSV UTF-8 con dos columnas: fecha y nombre del festivo. Se admite coma o punto y coma; la cabecera es opcional. Fechas admitidas: AAAA-MM-DD o DD/MM/AAAA.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var example = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 10),
            Text = "fecha,nombre\r\n2026-01-01,Año Nuevo\r\n2026-01-06,Epifanía del Señor"
        };
        layout.Controls.Add(example, 0, 1);
        layout.Controls.Add(_selectedFile, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        var cancel = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
        var select = new Button { Text = "Seleccionar archivo CSV...", AutoSize = true };
        select.Click += SelectFile;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(select);
        layout.Controls.Add(buttons, 0, 3);

        CancelButton = cancel;
        var card = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        card.Controls.Add(layout);
        Controls.Add(card);
        AppTheme.Apply(this);
    }

    private void SelectFile(object? sender, EventArgs args)
    {
        using var picker = new OpenFileDialog
        {
            Title = "Seleccionar calendario de festivos CSV",
            Filter = "Archivos CSV (*.csv)|*.csv",
            CheckFileExists = true,
            Multiselect = false
        };

        if (picker.ShowDialog(this) == DialogResult.OK)
        {
            FilePath = picker.FileName;
            _selectedFile.Text = FilePath;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

internal sealed class ServiceEditorForm : Form
{
    private readonly TextBox _name = new();
    private readonly NumericUpDown _duration = new() { Minimum = 15, Maximum = 480, Increment = 15, Value = 30 };

    public Service ServiceValue => new(0, _name.Text.Trim(), (int)_duration.Value);

    public ServiceEditorForm()
    {
        Text = "Añadir servicio";
        Width = 360;
        Height = 190;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        SettingsForm.AddSettingRow(layout, 0, "Nombre:", _name);
        SettingsForm.AddSettingRow(layout, 1, "Duración (min):", _duration);
        var save = new Button { Text = "Añadir", DialogResult = DialogResult.OK };
        layout.Controls.Add(save, 1, 2);
        AcceptButton = save;
        var card = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        card.Controls.Add(layout);
        Controls.Add(card);
        AppTheme.Apply(this);
    }
}

internal sealed class HolidayEditorForm : Form
{
    private readonly DateTimePicker _date = new();
    private readonly TextBox _name = new();

    public Holiday HolidayValue => new(_date.Value.Date, _name.Text.Trim());

    public HolidayEditorForm()
    {
        Text = "Añadir festivo";
        Width = 360;
        Height = 190;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        _date.Format = DateTimePickerFormat.Short;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        SettingsForm.AddSettingRow(layout, 0, "Fecha:", _date);
        SettingsForm.AddSettingRow(layout, 1, "Nombre:", _name);
        var save = new Button { Text = "Añadir", DialogResult = DialogResult.OK };
        layout.Controls.Add(save, 1, 2);
        AcceptButton = save;
        var card = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        card.Controls.Add(layout);
        Controls.Add(card);
        AppTheme.Apply(this);
    }
}
