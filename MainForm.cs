namespace Citaspp;

internal sealed class MainForm : Form
{
    private readonly AppointmentRepository _repository;
    private readonly DateTimePicker _datePicker = new();
    private readonly Label _weekLabel = new();
    private readonly Label _title = new() { Text = "AGENDA" };
    private readonly DataGridView _agenda = new();
    private readonly Label _status = new();
    private readonly Label _appointmentCount = new();
    private readonly Label _workerCount = new();
    private readonly Label _serviceCount = new();
    private readonly Label _holidayCount = new();
    private int _hoveredStartRow = -1;
    private int _hoveredEndRow = -1;
    private int _hoveredColumn = -1;
    private TimeSpan _agendaStartTime;
    private List<Worker> _workers = [];
    private List<Appointment> _appointments = [];

    public MainForm(AppointmentRepository repository)
    {
        _repository = repository;
        Text = "Citas - Agenda semanal";
        Width = 1200;
        Height = 720;
        MinimumSize = new Size(900, 500);
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen;

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            WrapContents = false
        };

        _title.AutoSize = true;
        _title.Font = new Font("Segoe UI", 17, FontStyle.Bold);
        _title.ForeColor = AppTheme.AccentColor;
        _title.Padding = new Padding(8, 4, 12, 0);
        header.Controls.Add(_title);

        var previous = new Button { Text = "← Semana anterior", AutoSize = true };
        previous.Click += (_, _) =>
        {
            _datePicker.Value = _datePicker.Value.AddDays(-7);
        };
        header.Controls.Add(previous);

        var previousMonth = new Button { Text = "← Mes", AutoSize = true };
        previousMonth.Click += (_, _) =>
        {
            _datePicker.Value = _datePicker.Value.AddMonths(-1);
        };
        header.Controls.Add(previousMonth);

        header.Controls.Add(new Label { Text = "Fecha:", AutoSize = true, Padding = new Padding(4, 7, 0, 0) });
        _datePicker.Format = DateTimePickerFormat.Short;
        _datePicker.Width = 110;
        _datePicker.Value = DateTime.Today;
        _datePicker.ValueChanged += (_, _) => RefreshAgenda();
        header.Controls.Add(_datePicker);

        var next = new Button { Text = "Semana siguiente →", AutoSize = true };
        next.Click += (_, _) =>
        {
            _datePicker.Value = _datePicker.Value.AddDays(7);
        };
        header.Controls.Add(next);

        var nextMonth = new Button { Text = "Mes →", AutoSize = true };
        nextMonth.Click += (_, _) =>
        {
            _datePicker.Value = _datePicker.Value.AddMonths(1);
        };
        header.Controls.Add(nextMonth);

        _weekLabel.AutoSize = true;
        _weekLabel.Padding = new Padding(12, 7, 12, 0);
        header.Controls.Add(_weekLabel);

        var newAppointment = new Button { Text = "Nueva cita", AutoSize = true };
        newAppointment.Click += (_, _) => CreateAppointment();
        header.Controls.Add(newAppointment);

        var deleteAppointment = new Button { Text = "Eliminar cita", AutoSize = true };
        deleteAppointment.Click += (_, _) => RemoveSelectedAppointment();
        header.Controls.Add(deleteAppointment);

        var settings = new Button { Text = "Configuración", AutoSize = true };
        settings.Click += (_, _) => OpenSettings();
        header.Controls.Add(settings);

        _agenda.Dock = DockStyle.Fill;
        _agenda.ReadOnly = true;
        _agenda.AllowUserToAddRows = false;
        _agenda.AllowUserToDeleteRows = false;
        _agenda.RowHeadersVisible = false;
        _agenda.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _agenda.MultiSelect = false;
        _agenda.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _agenda.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _agenda.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _agenda.CellClick += (_, args) =>
        {
            if (args.RowIndex >= 0 && args.ColumnIndex > 0)
            {
                OpenAppointmentAt(args.RowIndex, args.ColumnIndex, allowCreate: false);
            }
        };
        _agenda.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex >= 0 && args.ColumnIndex > 0 &&
                _agenda.Rows[args.RowIndex].Cells[args.ColumnIndex].Value is null)
            {
                OpenAppointmentAt(args.RowIndex, args.ColumnIndex);
            }
        };
        _agenda.CellMouseEnter += (_, args) =>
        {
            if (args.RowIndex < 0 || args.ColumnIndex < 1 ||
                _agenda.Rows[args.RowIndex].Cells[args.ColumnIndex].Value is null)
            {
                return;
            }

            var cell = _agenda.Rows[args.RowIndex].Cells[args.ColumnIndex];
            if (cell.Tag is not ValueTuple<DateTime, int> selected)
            {
                return;
            }

            var slot = selected.Item1.Add(_agendaStartTime).AddMinutes(args.RowIndex * 15);
            var appointment = _appointments.FirstOrDefault(item =>
                item.WorkerId == selected.Item2 &&
                slot >= item.StartsAt &&
                slot < item.EndsAt);
            if (appointment is null)
            {
                return;
            }

            ClearHoveredAppointment();
            var dayStart = appointment.StartsAt.Date.Add(_agendaStartTime);
            _hoveredStartRow = (int)(appointment.StartsAt - dayStart).TotalMinutes / 15;
            _hoveredEndRow = _hoveredStartRow + appointment.DurationMinutes / 15;
            _hoveredColumn = args.ColumnIndex;
            _agenda.Cursor = Cursors.Hand;
            InvalidateHoveredAppointment();
        };
        _agenda.CellMouseLeave += (_, args) =>
        {
            if (_hoveredColumn != args.ColumnIndex ||
                args.RowIndex < _hoveredStartRow ||
                args.RowIndex >= _hoveredEndRow)
            {
                return;
            }

            ClearHoveredAppointment();
        };
        _agenda.CellPainting += PaintHoveredAppointment;

        var headerCard = new RoundedPanel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(6) };
        headerCard.Controls.Add(header);

        var metricsCard = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 112,
            Padding = new Padding(8, 4, 8, 8),
            ColumnCount = 4,
            RowCount = 1
        };
        for (var index = 0; index < 4; index++)
        {
            metricsCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }
        metricsCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        metricsCard.Controls.Add(CreateMetricCard("CITAS ESTA SEMANA", _appointmentCount, AppTheme.AccentColor), 0, 0);
        metricsCard.Controls.Add(CreateMetricCard("TRABAJADORES", _workerCount, AppTheme.LimeColor), 1, 0);
        metricsCard.Controls.Add(CreateMetricCard("SERVICIOS", _serviceCount, AppTheme.OrangeColor), 2, 0);
        metricsCard.Controls.Add(CreateMetricCard("FESTIVOS", _holidayCount, AppTheme.PinkColor), 3, 0);

        var agendaCard = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(10), Margin = new Padding(10) };
        agendaCard.Controls.Add(_agenda);

        _status.Dock = DockStyle.Fill;
        _status.Padding = new Padding(12, 4, 12, 0);
        var statusCard = new RoundedPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8, 0, 8, 0) };
        statusCard.Controls.Add(_status);

        Controls.Add(agendaCard);
        Controls.Add(statusCard);
        Controls.Add(metricsCard);
        Controls.Add(headerCard);
        AppTheme.Apply(this);
        RefreshAgenda();
    }

    private void RefreshAgenda()
    {
        ClearHoveredAppointment();
        _agenda.Cursor = Cursors.Default;

        var settings = _repository.GetSettings();
        _agendaStartTime = settings.StartTime;
        _workers = _repository.GetWorkers();

        var monday = StartOfWeek(_datePicker.Value.Date);
        var sunday = monday.AddDays(7);
        _appointments = _repository.GetAppointments(monday, sunday);
        _weekLabel.Text = $"Semana: {monday:dd/MM/yyyy} - {sunday.AddDays(-1):dd/MM/yyyy}";

        var holidaysByDate = _repository.GetHolidays().ToDictionary(holiday => holiday.Date.Date);
        var appointmentsByWorkerAndDate = _appointments
            .GroupBy(appointment => (appointment.WorkerId, Date: appointment.StartsAt.Date))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var workerColors = _workers.ToDictionary(
            worker => worker.Id,
            worker => ColorTranslator.FromHtml(_repository.GetWorkerColor(worker.Id)));
        var services = _repository.GetServices();
        _appointmentCount.Text = _appointments.Count.ToString();
        _workerCount.Text = _workers.Count.ToString();
        _serviceCount.Text = services.Count.ToString();
        _holidayCount.Text = holidaysByDate.Keys.Count(date => date >= monday && date < sunday).ToString();

        _agenda.SuspendLayout();
        try
        {
            _agenda.Columns.Clear();
            _agenda.Rows.Clear();
            _agenda.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Hora",
                HeaderText = "Hora",
                Width = 70,
                Frozen = true
            });

            for (var dayOffset = 0; dayOffset < 7; dayOffset++)
            {
                var date = monday.AddDays(dayOffset);
                foreach (var worker in _workers)
                {
                    _agenda.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        Name = $"Day{dayOffset}_Worker{worker.Id}",
                        HeaderText = $"{DayName(date.DayOfWeek)} {date:dd/MM}\r\n{worker.Name}",
                        AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                        MinimumWidth = 75,
                        SortMode = DataGridViewColumnSortMode.NotSortable
                    });
                }
            }

            var current = monday.Add(settings.StartTime);
            var endTime = settings.EndTime;

            while (current.TimeOfDay < endTime)
            {
                var row = _agenda.Rows.Add(current.ToString("HH:mm"));
                _agenda.Rows[row].DefaultCellStyle.BackColor = row % 2 == 0
                    ? Color.FromArgb(41, 49, 71)
                    : Color.FromArgb(47, 57, 80);
                _agenda.Rows[row].DefaultCellStyle.ForeColor = Color.FromArgb(243, 246, 252);

                for (var dayOffset = 0; dayOffset < 7; dayOffset++)
                {
                    var date = monday.AddDays(dayOffset);
                    holidaysByDate.TryGetValue(date, out var holiday);
                    var isWorkingDay = settings.WorkDays.Contains(date.DayOfWeek) && holiday is null;

                    for (var workerIndex = 0; workerIndex < _workers.Count; workerIndex++)
                    {
                        var worker = _workers[workerIndex];
                        var columnIndex = 1 + dayOffset * _workers.Count + workerIndex;
                        var slot = date.Add(current.TimeOfDay);
                        var cell = _agenda.Rows[row].Cells[columnIndex];
                        cell.Tag = (Date: date, WorkerId: worker.Id);

                        if (!isWorkingDay)
                        {
                            cell.Value = null;
                            cell.Style.BackColor = Color.FromArgb(145, 61, 88);
                            continue;
                        }

                        if (!appointmentsByWorkerAndDate.TryGetValue((worker.Id, date), out var dayAppointments))
                        {
                            continue;
                        }

                        var appointment = dayAppointments.FirstOrDefault(item =>
                            slot >= item.StartsAt && slot < item.EndsAt);
                        if (appointment is null)
                        {
                            continue;
                        }

                        var isStart = slot == appointment.StartsAt;
                        cell.Value = isStart
                            ? $"{appointment.CustomerName}\r\n{appointment.ServiceName}\r\n{appointment.Phone}"
                            : "Ocupado";
                        cell.Style.BackColor = workerColors[worker.Id];
                        cell.Style.ForeColor = GetContrastingTextColor(workerColors[worker.Id]);
                        cell.ToolTipText = $"{appointment.CustomerName} · {appointment.ServiceName} · {appointment.Phone}";
                    }
                }

                current = current.AddMinutes(15);
            }
        }
        finally
        {
            _agenda.ResumeLayout();
        }

        _agenda.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCells);
        var appointmentCount = _appointments.Count;
        _status.Text = $"{appointmentCount} cita(s) en la semana · Doble clic en una franja libre para reservar";
    }

    private static RoundedPanel CreateMetricCard(string title, Label value, Color accent)
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(5),
            Padding = new Padding(12)
        };
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 56));

        var indicator = new Panel
        {
            Size = new Size(16, 16),
            BackColor = accent,
            Anchor = AnchorStyles.None
        };
        using (var path = AppTheme.CreateRoundedPath(new Rectangle(0, 0, 16, 16), 8))
        {
            indicator.Region = new Region(path);
        }
        content.Controls.Add(indicator, 0, 0);
        content.SetRowSpan(indicator, 2);

        var caption = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
        };
        content.Controls.Add(caption, 1, 0);

        value.Dock = DockStyle.Fill;
        value.Font = new Font("Segoe UI", 22, FontStyle.Bold);
        value.ForeColor = accent;
        value.TextAlign = ContentAlignment.MiddleLeft;
        content.Controls.Add(value, 1, 1);
        card.Controls.Add(content);
        return card;
    }

    private void CreateAppointment(int? rowIndex = null, int? columnIndex = null)
    {
        var settings = _repository.GetSettings();
        DateTime requestedStart;
        int? workerId = null;

        if (rowIndex.HasValue && columnIndex.HasValue)
        {
            var cell = _agenda.Rows[rowIndex.Value].Cells[columnIndex.Value];
            if (cell.Value is not null)
            {
                return;
            }

            if (cell.Tag is not ValueTuple<DateTime, int> selected)
            {
                return;
            }

            requestedStart = selected.Item1.Add(settings.StartTime).AddMinutes(rowIndex.Value * 15);
            workerId = selected.Item2;
        }
        else
        {
            requestedStart = _datePicker.Value.Date.Add(settings.StartTime);
            workerId = _workers.FirstOrDefault()?.Id;
        }

        ShowBookingDialog(requestedStart, workerId);
    }

    private void OpenAppointmentAt(int rowIndex, int columnIndex, bool allowCreate = true)
    {
        var cell = _agenda.Rows[rowIndex].Cells[columnIndex];
        if (cell.Tag is not ValueTuple<DateTime, int> selected)
        {
            return;
        }

        var settings = _repository.GetSettings();
        var selectedTime = selected.Item1.Add(settings.StartTime).AddMinutes(rowIndex * 15);
        var appointment = _appointments.FirstOrDefault(item =>
            item.WorkerId == selected.Item2 &&
            selectedTime >= item.StartsAt &&
            selectedTime < item.EndsAt);

        if (appointment is not null)
        {
            ShowAppointmentDialog(appointment);
        }
        else if (allowCreate && cell.Value is null)
        {
            ShowBookingDialog(selectedTime, selected.Item2);
        }
    }

    private void ShowBookingDialog(DateTime start, int? workerId)
    {
        if (!ConfirmWorkingDate(start.Date))
        {
            return;
        }

        using var dialog = new BookingForm(_repository, start.Date, start, workerId);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                _repository.CreateAppointment(
                    dialog.CustomerName,
                    dialog.Phone,
                    dialog.WorkerId,
                    dialog.ServiceId,
                    dialog.StartsAt);
                RefreshAgenda();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                MessageBox.Show(this, exception.Message, "No se pudo guardar la cita", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private void ShowAppointmentDialog(Appointment appointment)
    {
        if (!ConfirmWorkingDate(appointment.StartsAt.Date))
        {
            return;
        }

        using var dialog = new BookingForm(
            _repository,
            appointment.StartsAt.Date,
            appointment.StartsAt,
            appointment.WorkerId,
            appointment);

        var result = dialog.ShowDialog(this);
        try
        {
            if (result == DialogResult.OK)
            {
                _repository.UpdateAppointment(
                    appointment.Id,
                    dialog.CustomerName,
                    dialog.Phone,
                    dialog.WorkerId,
                    dialog.ServiceId,
                    dialog.StartsAt);
                RefreshAgenda();
            }
            else if (result == DialogResult.Yes)
            {
                _repository.DeleteAppointment(appointment.Id);
                RefreshAgenda();
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(this, exception.Message, "No se pudo actualizar la cita", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private bool ConfirmWorkingDate(DateTime date)
    {
        var settings = _repository.GetSettings();
        var holiday = _repository.GetHolidays().FirstOrDefault(item => item.Date.Date == date.Date);
        if (holiday is null && settings.WorkDays.Contains(date.DayOfWeek))
        {
            return true;
        }

        var reason = holiday is null ? "no está configurado como jornada laboral" : $"es festivo ({holiday.Name})";
        MessageBox.Show(
            this,
            $"El {date:dd/MM/yyyy} {reason}. No se puede crear ni editar una cita ese día.",
            "Día no laborable",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return false;
    }

    private void PaintHoveredAppointment(object? sender, DataGridViewCellPaintingEventArgs args)
    {
        if (args.RowIndex < _hoveredStartRow ||
            args.RowIndex >= _hoveredEndRow ||
            args.ColumnIndex != _hoveredColumn ||
            args.RowIndex < 0 ||
            args.ColumnIndex < 1)
        {
            return;
        }

        var graphics = args.Graphics;
        if (graphics is null)
        {
            return;
        }

        args.Paint(args.ClipBounds, args.PaintParts & ~DataGridViewPaintParts.Border);
        var bounds = args.CellBounds;
        using var pen = new Pen(AppTheme.AccentColor, 3);
        var left = bounds.Left + 1.5f;
        var right = bounds.Right - 1.5f;
        if (args.RowIndex == _hoveredStartRow)
        {
            graphics.DrawLine(pen, left, bounds.Top + 1.5f, right, bounds.Top + 1.5f);
        }

        if (args.RowIndex == _hoveredEndRow - 1)
        {
            graphics.DrawLine(pen, left, bounds.Bottom - 1.5f, right, bounds.Bottom - 1.5f);
        }

        graphics.DrawLine(pen, left, bounds.Top + 1.5f, left, bounds.Bottom - 1.5f);
        graphics.DrawLine(pen, right, bounds.Top + 1.5f, right, bounds.Bottom - 1.5f);
        args.Handled = true;
    }

    private void ClearHoveredAppointment()
    {
        if (_hoveredColumn >= 1)
        {
            InvalidateHoveredAppointment();
        }

        _hoveredStartRow = -1;
        _hoveredEndRow = -1;
        _hoveredColumn = -1;
        _agenda.Cursor = Cursors.Default;
    }

    private void InvalidateHoveredAppointment()
    {
        for (var row = _hoveredStartRow; row < _hoveredEndRow; row++)
        {
            if (row >= 0 && row < _agenda.Rows.Count)
            {
                _agenda.InvalidateCell(_hoveredColumn, row);
            }
        }
    }

    private void RemoveSelectedAppointment()
    {
        if (_agenda.CurrentCell is null || _agenda.CurrentCell.ColumnIndex < 1)
        {
            MessageBox.Show(this, "Selecciona una franja ocupada.", "Eliminar cita", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var cell = _agenda.CurrentCell;
        if (cell.Tag is not ValueTuple<DateTime, int> selected)
        {
            return;
        }

        var settings = _repository.GetSettings();
        var start = selected.Item1.Add(settings.StartTime).AddMinutes(cell.RowIndex * 15);
        var appointment = _appointments.FirstOrDefault(item =>
            item.WorkerId == selected.Item2 &&
            start >= item.StartsAt &&
            start < item.EndsAt);

        if (appointment is null)
        {
            MessageBox.Show(this, "Selecciona una franja ocupada.", "Eliminar cita", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show(
            this,
            $"¿Eliminar la cita de {appointment.CustomerName}?",
            "Confirmar eliminación",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (result == DialogResult.Yes)
        {
            _repository.DeleteAppointment(appointment.Id);
            RefreshAgenda();
        }
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(_repository);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            RefreshAgenda();
        }
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var offset = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.Date.AddDays(-offset);
    }

    private static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Lun",
        DayOfWeek.Tuesday => "Mar",
        DayOfWeek.Wednesday => "Mié",
        DayOfWeek.Thursday => "Jue",
        DayOfWeek.Friday => "Vie",
        DayOfWeek.Saturday => "Sáb",
        _ => "Dom"
    };

    private static Color GetContrastingTextColor(Color background)
    {
        var brightness = (background.R * 299 + background.G * 587 + background.B * 114) / 1000;
        return brightness >= 145
            ? Color.FromArgb(28, 35, 52)
            : Color.FromArgb(243, 246, 252);
    }
}
