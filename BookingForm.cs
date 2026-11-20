namespace Citaspp;

internal sealed class BookingForm : Form
{
    private readonly TextBox _customerName = new();
    private readonly TextBox _phone = new();
    private readonly ComboBox _worker = new();
    private readonly ComboBox _service = new();
    private readonly DateTimePicker _date = new();
    private readonly ComboBox _time = new();
    private readonly List<Worker> _workers;
    private readonly List<Service> _services;
    private readonly Appointment? _appointment;

    public string CustomerName => _customerName.Text.Trim();
    public string Phone => _phone.Text.Trim();
    public int WorkerId => ((Worker)_worker.SelectedItem!).Id;
    public int ServiceId => ((Service)_service.SelectedItem!).Id;
    public DateTime StartsAt => _date.Value.Date.Add(TimeSpan.Parse((string)_time.SelectedItem!));
    public int? AppointmentId => _appointment?.Id;

    public BookingForm(AppointmentRepository repository, DateTime date, DateTime start, int? workerId, Appointment? appointment = null)
    {
        _appointment = appointment;
        Text = appointment is null ? "Reservar cita" : "Editar cita";
        Width = 430;
        Height = 330;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        _workers = repository.GetWorkers();
        _services = repository.GetServices();
        var settings = repository.GetSettings();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 7
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 6; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        }
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddRow(layout, 0, "Cliente:", _customerName);
        AddRow(layout, 1, "Teléfono:", _phone);

        _worker.DropDownStyle = ComboBoxStyle.DropDownList;
        _worker.DataSource = _workers;
        _worker.DisplayMember = nameof(Worker.Name);
        if (workerId.HasValue)
        {
            _worker.SelectedItem = _workers.FirstOrDefault(item => item.Id == workerId.Value);
        }
        AddRow(layout, 2, "Trabajador:", _worker);

        _service.DropDownStyle = ComboBoxStyle.DropDownList;
        _service.DataSource = _services;
        _service.DisplayMember = nameof(Service.Name);
        if (appointment is not null)
        {
            _service.SelectedItem = _services.FirstOrDefault(item => item.Id == appointment.ServiceId);
        }
        AddRow(layout, 3, "Servicio:", _service);

        _date.Format = DateTimePickerFormat.Short;
        _date.Value = date;
        AddRow(layout, 4, "Fecha:", _date);

        _time.DropDownStyle = ComboBoxStyle.DropDownList;
        for (var slot = settings.StartTime; slot < settings.EndTime; slot += TimeSpan.FromMinutes(15))
        {
            _time.Items.Add(slot.ToString(@"hh\:mm"));
        }
        var preferredTime = start.TimeOfDay.ToString(@"hh\:mm");
        _time.SelectedItem = _time.Items.Contains(preferredTime) ? preferredTime : _time.Items.Count > 0 ? _time.Items[0] : null;
        AddRow(layout, 5, "Hora:", _time);

        if (appointment is not null)
        {
            _customerName.Text = appointment.CustomerName;
            _phone.Text = appointment.Phone;
        }

        _customerName.MaxLength = 100;
        _phone.MaxLength = 12;
        _phone.KeyPress += (_, args) =>
        {
            if (!char.IsControl(args.KeyChar) && !char.IsDigit(args.KeyChar))
            {
                args.Handled = true;
            }
        };

        var save = new Button { Text = "Guardar", AutoSize = true };
        save.Click += SaveAppointment;

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill
        };
        if (appointment is not null)
        {
            var delete = new Button { Text = "Eliminar", AutoSize = true };
            delete.Click += (_, _) =>
            {
                var result = MessageBox.Show(
                    this,
                    $"¿Eliminar la cita de {appointment.CustomerName}?",
                    "Confirmar eliminación",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    DialogResult = DialogResult.Yes;
                    Close();
                }
            };
            buttons.Controls.Add(delete);
        }
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 1, 6);
        AcceptButton = save;
        CancelButton = cancel;
        var card = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        card.Controls.Add(layout);
        Controls.Add(card);
        AppTheme.Apply(this);

        if (_services.Count == 0 || _workers.Count == 0)
        {
            MessageBox.Show(this, "Configura al menos un servicio y un trabajador antes de reservar.", "Configuración incompleta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.Cancel;
        }
    }

    private void SaveAppointment(object? sender, EventArgs args)
    {
        if (string.IsNullOrWhiteSpace(CustomerName))
        {
            MessageBox.Show(this, "Escribe el nombre del cliente.", "Dato obligatorio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _customerName.Focus();
            return;
        }

        if (Phone.Length != 9 && Phone.Length != 12)
        {
            MessageBox.Show(this, "El teléfono debe tener 9 dígitos o 12 dígitos con prefijo internacional.", "Teléfono no válido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _phone.Focus();
            return;
        }

        if (_worker.SelectedItem is null || _service.SelectedItem is null || _time.SelectedItem is null)
        {
            MessageBox.Show(this, "Selecciona trabajador, servicio y hora.", "Dato obligatorio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        control.Dock = DockStyle.Fill;
        layout.Controls.Add(control, 1, row);
    }
}
