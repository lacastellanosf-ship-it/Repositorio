namespace Citaspp;

public sealed record Worker(int Id, string Name);

public sealed record Service(int Id, string Name, int DurationMinutes);

public sealed record Appointment(
    int Id,
    string CustomerName,
    string Phone,
    int WorkerId,
    int ServiceId,
    string ServiceName,
    int DurationMinutes,
    DateTime StartsAt)
{
    public DateTime EndsAt => StartsAt.AddMinutes(DurationMinutes);
}

public sealed record Holiday(DateTime Date, string Name);

public sealed class AppointmentConflictException(string message) : InvalidOperationException(message);

public sealed class ConfigurationConflictException(string message) : InvalidOperationException(message);

public sealed class ScheduleSettings
{
    public TimeSpan StartTime { get; set; } = new(9, 0, 0);
    public TimeSpan EndTime { get; set; } = new(17, 0, 0);
    public HashSet<DayOfWeek> WorkDays { get; set; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
    public int WorkerCount { get; set; } = 2;
}
