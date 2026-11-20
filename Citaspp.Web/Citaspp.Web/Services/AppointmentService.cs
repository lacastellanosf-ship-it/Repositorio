using Citaspp.Web.Contracts;

namespace Citaspp.Web.Services;

public sealed class AppointmentService(AppointmentRepository repository) : IAppointmentService
{
    public AppointmentResponse[] GetAppointments(DateTimeOffset start, DateTimeOffset end)
        => repository.GetAppointments(start.DateTime, end.DateTime).Select(Map).ToArray();

    public AppointmentResponse? GetAppointment(int id)
        => repository.GetAppointment(id) is { } appointment ? Map(appointment) : null;

    public AppointmentResponse CreateAppointment(CreateAppointmentRequest request)
    {
        var id = repository.CreateAppointment(
            request.CustomerName,
            request.Phone,
            request.WorkerId,
            request.ServiceId,
            request.StartsAt.DateTime,
            allowSpecialDays: true);

        return GetAppointment(id)
            ?? throw new InvalidOperationException("No se pudo recuperar la cita recién creada.");
    }

    public AppointmentResponse? UpdateAppointment(int id, UpdateAppointmentRequest request)
    {
        if (GetAppointment(id) is null)
        {
            return null;
        }

        repository.UpdateAppointment(
            id,
            request.CustomerName,
            request.Phone,
            request.WorkerId,
            request.ServiceId,
            request.StartsAt.DateTime,
            allowSpecialDays: true);

        return GetAppointment(id);
    }

    public bool DeleteAppointment(int id) => repository.DeleteAppointment(id);

    public int DeleteAppointments(int[] appointmentIds)
        => repository.DeleteAppointments(appointmentIds);

    public int MoveAppointments(MoveAppointmentsRequest request)
        => repository.MoveAppointments(
            request.AppointmentIds,
            request.FirstAppointmentStartsAt.DateTime);

    public WorkerResponse[] GetWorkers()
        => repository.GetWorkers()
            .Select(worker => new WorkerResponse(worker.Id, worker.Name, repository.GetWorkerColor(worker.Id)))
            .ToArray();

    public ServiceResponse[] GetServices()
        => repository.GetServices()
            .Select(service => new ServiceResponse(service.Id, service.Name, service.DurationMinutes))
            .ToArray();

    public HolidayResponse[] GetHolidays()
        => repository.GetHolidays()
            .Select(holiday => new HolidayResponse(ToOffset(holiday.Date), holiday.Name))
            .ToArray();

    public ScheduleSettingsResponse GetSettings()
    {
        var settings = repository.GetSettings();
        return new ScheduleSettingsResponse(
            TimeOnly.FromTimeSpan(settings.StartTime),
            TimeOnly.FromTimeSpan(settings.EndTime),
            settings.WorkDays.OrderBy(day => (int)day).Select(day => day.ToString()).ToArray(),
            settings.WorkerCount);
    }

    public void UpdateSettings(UpdateScheduleSettingsRequest request)
    {
        if (request.EndTime <= request.StartTime)
        {
            throw new ArgumentException("La hora de inicio debe ser anterior a la hora de fin.");
        }

        var workDays = new HashSet<DayOfWeek>();
        foreach (var dayName in request.WorkDays)
        {
            if (!Enum.TryParse(dayName, ignoreCase: true, out DayOfWeek day) || !Enum.IsDefined(day))
            {
                throw new ArgumentException("Hay un día laborable no válido.");
            }

            if (!workDays.Add(day))
            {
                throw new ArgumentException("No se pueden repetir los días laborables.");
            }
        }

        repository.SaveSettings(new ScheduleSettings
        {
            StartTime = request.StartTime.ToTimeSpan(),
            EndTime = request.EndTime.ToTimeSpan(),
            WorkDays = workDays,
            WorkerCount = request.WorkerCount
        });
    }

    public WorkerResponse? UpdateWorker(int id, UpdateWorkerRequest request)
    {
        var workers = repository.GetWorkers();
        if (!workers.Any(worker => worker.Id == id))
        {
            return null;
        }

        if (workers.Any(worker =>
                worker.Id != id &&
                string.Equals(worker.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConfigurationConflictException("Ya existe un trabajador con ese nombre.");
        }

        if (request.Color is null)
        {
            repository.SaveWorkerName(id, request.Name);
        }
        else
        {
            repository.SaveWorker(id, request.Name, request.Color);
        }

        return new WorkerResponse(id, request.Name.Trim(), repository.GetWorkerColor(id));
    }

    public ServiceResponse CreateService(SaveServiceRequest request)
    {
        var id = repository.SaveService(null, request.Name, request.DurationMinutes);
        return MapService(repository.GetServices().Single(service => service.Id == id));
    }

    public ServiceResponse? UpdateService(int id, SaveServiceRequest request)
    {
        if (!repository.GetServices().Any(service => service.Id == id))
        {
            return null;
        }

        repository.SaveService(id, request.Name, request.DurationMinutes);
        return MapService(repository.GetServices().Single(service => service.Id == id));
    }

    public bool DeleteService(int id) => repository.DeleteService(id);

    public HolidayResponse SaveHoliday(DateOnly date, SaveHolidayRequest request)
    {
        var holidayDate = date.ToDateTime(TimeOnly.MinValue);
        repository.SaveHoliday(holidayDate, request.Name);
        return new HolidayResponse(ToOffset(holidayDate), request.Name.Trim());
    }

    public bool DeleteHoliday(DateOnly date)
        => repository.DeleteHoliday(date.ToDateTime(TimeOnly.MinValue));

    private static AppointmentResponse Map(Appointment appointment)
    {
        var startsAt = ToOffset(appointment.StartsAt);
        return new AppointmentResponse(
            appointment.Id,
            appointment.CustomerName,
            appointment.Phone,
            appointment.WorkerId,
            appointment.ServiceId,
            appointment.ServiceName,
            appointment.DurationMinutes,
            startsAt,
            startsAt.AddMinutes(appointment.DurationMinutes));
    }

    private static ServiceResponse MapService(Service service)
        => new(service.Id, service.Name, service.DurationMinutes);

    private static DateTimeOffset ToOffset(DateTime value)
    {
        var local = DateTime.SpecifyKind(value, DateTimeKind.Local);
        return new DateTimeOffset(local);
    }
}
