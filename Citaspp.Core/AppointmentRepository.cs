using Microsoft.Data.Sqlite;

namespace Citaspp;

public sealed class AppointmentRepository
{
    private readonly string _connectionString;

    public AppointmentRepository(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
    }

    public void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS workers (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS services (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                duration_minutes INTEGER NOT NULL CHECK(duration_minutes > 0 AND duration_minutes % 15 = 0)
            );
            CREATE TABLE IF NOT EXISTS holidays (
                date TEXT PRIMARY KEY,
                name TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS appointments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                customer_name TEXT NOT NULL,
                phone TEXT NOT NULL,
                worker_id INTEGER NOT NULL REFERENCES workers(id),
                service_id INTEGER NOT NULL REFERENCES services(id),
                duration_minutes INTEGER NOT NULL,
                starts_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_appointments_worker_date
                ON appointments(worker_id, starts_at);
            """;
        command.ExecuteNonQuery();

        if (!AppointmentDurationExists(connection))
        {
            using var migration = connection.CreateCommand();
            migration.CommandText = """
                ALTER TABLE appointments ADD COLUMN duration_minutes INTEGER NOT NULL DEFAULT 15;
                UPDATE appointments
                SET duration_minutes = (
                    SELECT services.duration_minutes
                    FROM services
                    WHERE services.id = appointments.service_id
                );
                """;
            migration.ExecuteNonQuery();
        }

        if (GetWorkers().Count == 0)
        {
            SetWorkerCount(2);
        }

        if (GetServices().Count == 0)
        {
            SaveService(null, "Servicio 30 min", 30);
            SaveService(null, "Servicio 60 min", 60);
        }

        SaveSettingIfMissing("start_time", "09:00");
        SaveSettingIfMissing("end_time", "17:00");
        SaveSettingIfMissing("work_days", "1,2,3,4,5");
        SaveSettingIfMissing("worker_count", "2");
        SaveSettingIfMissing("holidays_seeded_2026", "0");
        SeedLeónHolidaysFor2026();
    }

    public ScheduleSettings GetSettings()
    {
        return new ScheduleSettings
        {
            StartTime = TimeSpan.Parse(GetSetting("start_time", "09:00")),
            EndTime = TimeSpan.Parse(GetSetting("end_time", "17:00")),
            WorkDays = GetSetting("work_days", "1,2,3,4,5")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse)
                .Select(day => (DayOfWeek)day)
                .ToHashSet(),
            WorkerCount = int.Parse(GetSetting("worker_count", "2"))
        };
    }

    public void SaveSettings(ScheduleSettings settings)
    {
        if (settings.StartTime >= settings.EndTime)
        {
            throw new ArgumentException("La hora de inicio debe ser anterior a la hora de fin.");
        }

        if (settings.WorkerCount < 1 || settings.WorkerCount > 20)
        {
            throw new ArgumentException("El número de trabajadores debe estar entre 1 y 20.");
        }

        SetWorkerCount(settings.WorkerCount);
        SetSetting("start_time", settings.StartTime.ToString(@"hh\:mm"));
        SetSetting("end_time", settings.EndTime.ToString(@"hh\:mm"));
        SetSetting("work_days", string.Join(",", settings.WorkDays.OrderBy(day => (int)day).Select(day => (int)day)));
        SetSetting("worker_count", settings.WorkerCount.ToString());
    }

    public List<Worker> GetWorkers()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name FROM workers ORDER BY id;";
        using var reader = command.ExecuteReader();
        var workers = new List<Worker>();
        while (reader.Read())
        {
            workers.Add(new Worker(reader.GetInt32(0), reader.GetString(1)));
        }

        return workers;
    }

    public List<Service> GetServices()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, duration_minutes FROM services ORDER BY name;";
        using var reader = command.ExecuteReader();
        var services = new List<Service>();
        while (reader.Read())
        {
            services.Add(new Service(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
        }

        return services;
    }

    public int SaveService(int? id, string name, int durationMinutes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Escribe un nombre para el servicio.");
        }

        if (durationMinutes < 15 || durationMinutes > 480 || durationMinutes % 15 != 0)
        {
            throw new ArgumentException("La duración debe ser de 15 a 480 minutos y múltiplo de 15.");
        }

        var trimmedName = name.Trim();
        if (GetServices().Any(service =>
                service.Id != id &&
                string.Equals(service.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConfigurationConflictException("Ya existe un servicio con ese nombre.");
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = id.HasValue
            ? "UPDATE services SET name = $name, duration_minutes = $duration WHERE id = $id;"
            : "INSERT INTO services(name, duration_minutes) VALUES($name, $duration);";
        command.Parameters.AddWithValue("$name", trimmedName);
        command.Parameters.AddWithValue("$duration", durationMinutes);
        if (id.HasValue)
        {
            command.Parameters.AddWithValue("$id", id.Value);
        }

        if (command.ExecuteNonQuery() == 0 && id.HasValue)
        {
            throw new KeyNotFoundException("No se encontró el servicio que se intenta editar.");
        }

        if (id.HasValue)
        {
            return id.Value;
        }

        using var idCommand = connection.CreateCommand();
        idCommand.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt32(idCommand.ExecuteScalar());
    }

    public bool DeleteService(int serviceId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM services
            WHERE id = $id
              AND NOT EXISTS (SELECT 1 FROM appointments WHERE appointments.service_id = services.id);
            """;
        command.Parameters.AddWithValue("$id", serviceId);
        if (command.ExecuteNonQuery() > 0)
        {
            return true;
        }

        if (!GetServices().Any(service => service.Id == serviceId))
        {
            return false;
        }

        throw new ConfigurationConflictException("No se puede eliminar un servicio que ya tiene citas reservadas.");
    }

    public List<Holiday> GetHolidays()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT date, name FROM holidays ORDER BY date;";
        using var reader = command.ExecuteReader();
        var holidays = new List<Holiday>();
        while (reader.Read())
        {
            holidays.Add(new Holiday(DateTime.Parse(reader.GetString(0)), reader.GetString(1)));
        }

        return holidays;
    }

    public void SaveHoliday(DateTime date, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Escribe el nombre del festivo.");
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO holidays(date, name) VALUES($date, $name)
            ON CONFLICT(date) DO UPDATE SET name = excluded.name;
            """;
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$name", name.Trim());
        command.ExecuteNonQuery();
    }

    public bool DeleteHoliday(DateTime date)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM holidays WHERE date = $date;";
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
        return command.ExecuteNonQuery() > 0;
    }

    public List<Appointment> GetAppointments(DateTime date)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.id, a.customer_name, a.phone, a.worker_id, a.service_id,
                   s.name, a.duration_minutes, a.starts_at
            FROM appointments a
            JOIN services s ON s.id = a.service_id
            WHERE a.starts_at >= $start AND a.starts_at < $end
            ORDER BY a.starts_at;
            """;
        command.Parameters.AddWithValue("$start", date.Date.ToString("yyyy-MM-dd'T'00:00:00"));
        command.Parameters.AddWithValue("$end", date.Date.AddDays(1).ToString("yyyy-MM-dd'T'00:00:00"));
        using var reader = command.ExecuteReader();
        var appointments = new List<Appointment>();
        while (reader.Read())
        {
            appointments.Add(new Appointment(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetInt32(6),
                DateTime.Parse(reader.GetString(7))));
        }

        return appointments;
    }

    public List<Appointment> GetAppointments(DateTime startDate, DateTime endDate)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.id, a.customer_name, a.phone, a.worker_id, a.service_id,
                   s.name, a.duration_minutes, a.starts_at
            FROM appointments a
            JOIN services s ON s.id = a.service_id
            WHERE a.starts_at >= $start AND a.starts_at < $end
            ORDER BY a.starts_at;
            """;
        command.Parameters.AddWithValue("$start", startDate.ToString("yyyy-MM-dd'T'HH:mm:ss"));
        command.Parameters.AddWithValue("$end", endDate.ToString("yyyy-MM-dd'T'HH:mm:ss"));
        using var reader = command.ExecuteReader();
        var appointments = new List<Appointment>();
        while (reader.Read())
        {
            appointments.Add(new Appointment(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetInt32(6),
                DateTime.Parse(reader.GetString(7))));
        }

        return appointments;
    }

    public Appointment? GetAppointment(int appointmentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.id, a.customer_name, a.phone, a.worker_id, a.service_id,
                   s.name, a.duration_minutes, a.starts_at
            FROM appointments a
            JOIN services s ON s.id = a.service_id
            WHERE a.id = $id;
            """;
        command.Parameters.AddWithValue("$id", appointmentId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new Appointment(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetInt32(6),
            DateTime.Parse(reader.GetString(7)));
    }

    public string GetWorkerColor(int id)
    {
        var defaults = new[] { "#13CBE8", "#FF9718", "#C9DE17", "#9346E2", "#E83C7D" };
        return GetSetting($"worker_color_{id}", defaults[(id - 1) % defaults.Length]);
    }

    public void SaveWorker(int id, string name, string color)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre del trabajador no puede estar vacío.");
        }

        var trimmedColor = color.Trim();
        if (trimmedColor.Length != 7 ||
            trimmedColor[0] != '#' ||
            trimmedColor.Skip(1).Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("El color debe estar en formato hexadecimal #RRGGBB.");
        }

        var validatedColor = trimmedColor.ToUpperInvariant();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE workers SET name = $name WHERE id = $id;";
            command.Parameters.AddWithValue("$name", name.Trim());
            command.Parameters.AddWithValue("$id", id);
            if (command.ExecuteNonQuery() == 0)
            {
                throw new ArgumentException("El trabajador seleccionado ya no existe.");
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO settings(key, value) VALUES($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                """;
            command.Parameters.AddWithValue("$key", $"worker_color_{id}");
            command.Parameters.AddWithValue("$value", validatedColor);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void SaveWorkerName(int id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre del trabajador no puede estar vacío.");
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE workers SET name = $name WHERE id = $id;";
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() == 0)
        {
            throw new KeyNotFoundException("El trabajador seleccionado ya no existe.");
        }
    }

    public int CreateAppointment(
        string customerName,
        string phone,
        int workerId,
        int serviceId,
        DateTime startsAt,
        bool allowSpecialDays = false)
        => SaveAppointment(null, customerName, phone, workerId, serviceId, startsAt, allowSpecialDays);

    public void UpdateAppointment(
        int appointmentId,
        string customerName,
        string phone,
        int workerId,
        int serviceId,
        DateTime startsAt,
        bool allowSpecialDays = false)
        => SaveAppointment(appointmentId, customerName, phone, workerId, serviceId, startsAt, allowSpecialDays);

    private int SaveAppointment(
        int? appointmentId,
        string customerName,
        string phone,
        int workerId,
        int serviceId,
        DateTime startsAt,
        bool allowSpecialDays)
    {
        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(phone))
        {
            throw new ArgumentException("El nombre y el teléfono del cliente son obligatorios.");
        }

        if (phone.Any(character => !char.IsDigit(character)))
        {
            throw new ArgumentException("El teléfono solo puede contener dígitos.");
        }

        if (phone.Length != 9 && phone.Length != 12)
        {
            throw new ArgumentException("Introduce un teléfono de 9 dígitos o uno internacional de 12 dígitos.");
        }

        if (appointmentId is null && startsAt <= DateTime.Now)
        {
            var message = startsAt.Date < DateTime.Today
                ? "No se pueden crear citas en días pasados."
                : "No se pueden crear citas en una hora que ya ha pasado.";
            throw new ArgumentException(message);
        }

        var settings = GetSettings();
        var worker = GetWorkers().FirstOrDefault(candidate => candidate.Id == workerId)
            ?? throw new ArgumentException("El trabajador seleccionado ya no está disponible.");
        var service = GetServices().FirstOrDefault(candidate => candidate.Id == serviceId)
            ?? throw new ArgumentException("El servicio seleccionado ya no está disponible.");
        var start = startsAt;
        var end = start.AddMinutes(service.DurationMinutes);

        var isWeekend = start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var isHoliday = GetHolidays().Any(holiday => holiday.Date.Date == start.Date);
        if (!settings.WorkDays.Contains(start.DayOfWeek) &&
            !(allowSpecialDays && (isWeekend || isHoliday)))
        {
            throw new ArgumentException("Ese día no está configurado como jornada laboral.");
        }

        if (isHoliday && !allowSpecialDays)
        {
            throw new ArgumentException("No se pueden reservar citas en un día festivo.");
        }

        if (start.TimeOfDay < settings.StartTime || end.TimeOfDay > settings.EndTime)
        {
            throw new ArgumentException("La cita queda fuera del horario de trabajo.");
        }

        if (start.Minute % 15 != 0 || start.Second != 0 || start.Millisecond != 0)
        {
            throw new ArgumentException("Las citas deben comenzar en una franja de 15 minutos.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT a.starts_at, a.duration_minutes
            FROM appointments a
            WHERE a.worker_id = $worker
              AND ($exclude_id IS NULL OR a.id <> $exclude_id)
              AND julianday(a.starts_at) < julianday($end)
              AND julianday(a.starts_at, '+' || a.duration_minutes || ' minutes') > julianday($start)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$worker", worker.Id);
        command.Parameters.AddWithValue("$exclude_id", appointmentId.HasValue ? appointmentId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$start", start.ToString("yyyy-MM-dd'T'HH:mm:ss"));
        command.Parameters.AddWithValue("$end", end.ToString("yyyy-MM-dd'T'HH:mm:ss"));
        if (command.ExecuteScalar() is not null)
        {
            throw new AppointmentConflictException("Ese trabajador ya tiene una cita que se solapa con ese horario.");
        }

        command.Parameters.Clear();
        command.CommandText = appointmentId.HasValue
            ? """
              UPDATE appointments
              SET customer_name = $customer, phone = $phone, worker_id = $worker,
                  service_id = $service, duration_minutes = $duration, starts_at = $start
              WHERE id = $id;
              """
            : """
              INSERT INTO appointments(customer_name, phone, worker_id, service_id, duration_minutes, starts_at)
              VALUES($customer, $phone, $worker, $service, $duration, $start);
              """;
        command.Parameters.AddWithValue("$customer", customerName.Trim());
        command.Parameters.AddWithValue("$phone", phone.Trim());
        command.Parameters.AddWithValue("$worker", worker.Id);
        command.Parameters.AddWithValue("$service", service.Id);
        command.Parameters.AddWithValue("$duration", service.DurationMinutes);
        command.Parameters.AddWithValue("$start", start.ToString("yyyy-MM-dd'T'HH:mm:ss"));
        if (appointmentId.HasValue)
        {
            command.Parameters.AddWithValue("$id", appointmentId.Value);
        }
        var affectedRows = command.ExecuteNonQuery();
        if (appointmentId.HasValue && affectedRows == 0)
        {
            throw new KeyNotFoundException("No se encontró la cita que se intenta editar.");
        }

        var savedAppointmentId = appointmentId ?? ReadLastInsertId(connection, transaction);
        transaction.Commit();
        return savedAppointmentId;
    }

    public bool DeleteAppointment(int appointmentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM appointments WHERE id = $id;";
        command.Parameters.AddWithValue("$id", appointmentId);
        return command.ExecuteNonQuery() > 0;
    }

    public int DeleteAppointments(IReadOnlyCollection<int> appointmentIds)
    {
        var ids = appointmentIds.Distinct().ToArray();
        if (ids.Length == 0 || ids.Any(id => id <= 0))
        {
            throw new ArgumentException("Selecciona al menos una cita válida.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var parameters = ids.Select((_, index) => $"$id{index}").ToArray();
        using (var countCommand = connection.CreateCommand())
        {
            countCommand.Transaction = transaction;
            countCommand.CommandText = $"SELECT COUNT(*) FROM appointments WHERE id IN ({string.Join(", ", parameters)});";
            for (var index = 0; index < ids.Length; index++)
            {
                countCommand.Parameters.AddWithValue(parameters[index], ids[index]);
            }

            var found = Convert.ToInt32(countCommand.ExecuteScalar());
            if (found != ids.Length)
            {
                throw new KeyNotFoundException("No se encontraron todas las citas seleccionadas.");
            }
        }

        using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = $"DELETE FROM appointments WHERE id IN ({string.Join(", ", parameters)});";
        for (var index = 0; index < ids.Length; index++)
        {
            deleteCommand.Parameters.AddWithValue(parameters[index], ids[index]);
        }

        var deleted = deleteCommand.ExecuteNonQuery();
        transaction.Commit();
        return deleted;
    }

    public int MoveAppointments(IReadOnlyCollection<int> appointmentIds, DateTime firstAppointmentStartsAt)
    {
        var ids = appointmentIds.Distinct().ToArray();
        if (ids.Length == 0 || ids.Any(id => id <= 0))
        {
            throw new ArgumentException("Selecciona al menos una cita válida.");
        }

        var settings = GetSettings();
        var holidays = GetHolidays().Select(holiday => holiday.Date.Date).ToHashSet();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var parameters = ids.Select((_, index) => $"$id{index}").ToArray();
        var selected = new List<(int Id, int WorkerId, int Duration, DateTime StartsAt)>();
        using (var selectedCommand = connection.CreateCommand())
        {
            selectedCommand.Transaction = transaction;
            selectedCommand.CommandText = $"""
                SELECT id, worker_id, duration_minutes, starts_at
                FROM appointments
                WHERE id IN ({string.Join(", ", parameters)});
                """;
            for (var index = 0; index < ids.Length; index++)
            {
                selectedCommand.Parameters.AddWithValue(parameters[index], ids[index]);
            }

            using var reader = selectedCommand.ExecuteReader();
            while (reader.Read())
            {
                selected.Add((
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    DateTime.Parse(reader.GetString(3))));
            }
        }

        if (selected.Count != ids.Length)
        {
            throw new KeyNotFoundException("No se encontraron todas las citas seleccionadas.");
        }

        if (firstAppointmentStartsAt.Second != 0 ||
            firstAppointmentStartsAt.Millisecond != 0 ||
            firstAppointmentStartsAt.Minute % 15 != 0)
        {
            throw new ArgumentException("Las citas deben comenzar en una franja de 15 minutos.");
        }

        var offset = firstAppointmentStartsAt - selected.Min(appointment => appointment.StartsAt);
        var moved = selected
            .Select(appointment => (
                appointment.Id,
                appointment.WorkerId,
                appointment.Duration,
                StartsAt: appointment.StartsAt.Add(offset)))
            .ToArray();

        foreach (var appointment in moved)
        {
            var end = appointment.StartsAt.AddMinutes(appointment.Duration);
            var isWeekend = appointment.StartsAt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var isHoliday = holidays.Contains(appointment.StartsAt.Date);
            if (!settings.WorkDays.Contains(appointment.StartsAt.DayOfWeek) && !isWeekend && !isHoliday)
            {
                throw new ArgumentException("El nuevo horario incluye un día no laborable que no es festivo ni fin de semana.");
            }

            if (appointment.StartsAt.TimeOfDay < settings.StartTime || end.TimeOfDay > settings.EndTime)
            {
                throw new ArgumentException("El nuevo horario queda fuera de la jornada laboral.");
            }
        }

        for (var first = 0; first < moved.Length; first++)
        {
            var firstEnd = moved[first].StartsAt.AddMinutes(moved[first].Duration);
            for (var second = first + 1; second < moved.Length; second++)
            {
                var secondEnd = moved[second].StartsAt.AddMinutes(moved[second].Duration);
                if (moved[first].WorkerId == moved[second].WorkerId &&
                    moved[first].StartsAt < secondEnd &&
                    firstEnd > moved[second].StartsAt)
                {
                    throw new AppointmentConflictException("Las citas seleccionadas se solaparían entre sí al moverlas.");
                }
            }
        }

        var selectedIds = ids.ToHashSet();
        var existing = new List<(int Id, int WorkerId, int Duration, DateTime StartsAt)>();
        using (var existingCommand = connection.CreateCommand())
        {
            existingCommand.Transaction = transaction;
            existingCommand.CommandText = "SELECT id, worker_id, duration_minutes, starts_at FROM appointments;";
            using var reader = existingCommand.ExecuteReader();
            while (reader.Read())
            {
                existing.Add((
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    DateTime.Parse(reader.GetString(3))));
            }
        }

        foreach (var appointment in moved)
        {
            var end = appointment.StartsAt.AddMinutes(appointment.Duration);
            if (existing.Any(other =>
                    !selectedIds.Contains(other.Id) &&
                    other.WorkerId == appointment.WorkerId &&
                    other.StartsAt < end &&
                    other.StartsAt.AddMinutes(other.Duration) > appointment.StartsAt))
            {
                throw new AppointmentConflictException("El nuevo horario se solapa con otra cita del trabajador.");
            }
        }

        using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = "UPDATE appointments SET starts_at = $start WHERE id = $id;";
        var startParameter = updateCommand.Parameters.Add("$start", SqliteType.Text);
        var idParameter = updateCommand.Parameters.Add("$id", SqliteType.Integer);
        foreach (var appointment in moved)
        {
            startParameter.Value = appointment.StartsAt.ToString("yyyy-MM-dd'T'HH:mm:ss");
            idParameter.Value = appointment.Id;
            updateCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        return moved.Length;
    }

    private static int ReadLastInsertId(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static bool AppointmentDurationExists(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(appointments);";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1) == "duration_minutes")
            {
                return true;
            }
        }

        return false;
    }

    private string GetSetting(string key, string defaultValue)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string ?? defaultValue;
    }

    private void SaveSettingIfMissing(string key, string value)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO settings(key, value) VALUES($key, $value);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private void SetSetting(string key, string value)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(key, value) VALUES($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private void SetWorkerCount(int count)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COUNT(*) FROM workers
                WHERE id > $count
                  AND EXISTS (SELECT 1 FROM appointments WHERE appointments.worker_id = workers.id);
                """;
            command.Parameters.AddWithValue("$count", count);
            if (Convert.ToInt32(command.ExecuteScalar()) > 0)
            {
                throw new ArgumentException("No se puede reducir el número de trabajadores por debajo de los que tienen citas reservadas.");
            }

            for (var id = 1; id <= count; id++)
            {
                command.Parameters.Clear();
                command.CommandText = """
                    INSERT OR IGNORE INTO workers(id, name) VALUES($id, $name);
                    """;
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$name", $"Trabajador {id}");
                command.ExecuteNonQuery();
            }

            command.Parameters.Clear();
            command.CommandText = "DELETE FROM workers WHERE id > $count;";
            command.Parameters.AddWithValue("$count", count);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private void SeedLeónHolidaysFor2026()
    {
        if (GetSetting("holidays_seeded_2026", "0") == "1")
        {
            return;
        }

        if (GetHolidays().Count > 0)
        {
            SetSetting("holidays_seeded_2026", "1");
            return;
        }

        var holidays = new[]
        {
            new Holiday(new DateTime(2026, 1, 1), "Año Nuevo"),
            new Holiday(new DateTime(2026, 1, 6), "Epifanía del Señor"),
            new Holiday(new DateTime(2026, 4, 2), "Jueves Santo"),
            new Holiday(new DateTime(2026, 4, 3), "Viernes Santo"),
            new Holiday(new DateTime(2026, 4, 23), "Día de Castilla y León"),
            new Holiday(new DateTime(2026, 5, 1), "Fiesta del Trabajo"),
            new Holiday(new DateTime(2026, 6, 24), "San Juan (local León)"),
            new Holiday(new DateTime(2026, 8, 15), "Asunción de la Virgen"),
            new Holiday(new DateTime(2026, 10, 5), "San Froilán (local León)"),
            new Holiday(new DateTime(2026, 10, 12), "Fiesta Nacional de España"),
            new Holiday(new DateTime(2026, 11, 2), "Traslado de Todos los Santos"),
            new Holiday(new DateTime(2026, 12, 7), "Traslado del Día de la Constitución"),
            new Holiday(new DateTime(2026, 12, 8), "Inmaculada Concepción"),
            new Holiday(new DateTime(2026, 12, 25), "Navidad")
        };

        foreach (var holiday in holidays)
        {
            SaveHoliday(holiday.Date, holiday.Name);
        }

        SetSetting("holidays_seeded_2026", "1");
    }
}
