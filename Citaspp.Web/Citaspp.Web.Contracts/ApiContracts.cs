using System.ComponentModel.DataAnnotations;

namespace Citaspp.Web.Contracts;

/// <summary>Appointment data returned by the web API.</summary>
public sealed record AppointmentResponse(
    int Id,
    string CustomerName,
    string Phone,
    int WorkerId,
    int ServiceId,
    string ServiceName,
    int DurationMinutes,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

/// <summary>Data required to create an appointment.</summary>
public sealed record CreateAppointmentRequest
{
    [Required, StringLength(100)]
    public required string CustomerName { get; init; }

    [Required, RegularExpression(@"^(?:\d{9}|\d{12})$")]
    public required string Phone { get; init; }

    [Range(1, int.MaxValue)]
    public required int WorkerId { get; init; }

    [Range(1, int.MaxValue)]
    public required int ServiceId { get; init; }

    public required DateTimeOffset StartsAt { get; init; }
}

/// <summary>Data required to update an appointment.</summary>
public sealed record UpdateAppointmentRequest
{
    [Required, StringLength(100)]
    public required string CustomerName { get; init; }

    [Required, RegularExpression(@"^(?:\d{9}|\d{12})$")]
    public required string Phone { get; init; }

    [Range(1, int.MaxValue)]
    public required int WorkerId { get; init; }

    [Range(1, int.MaxValue)]
    public required int ServiceId { get; init; }

    public required DateTimeOffset StartsAt { get; init; }
}

/// <summary>Identifiers of appointments to delete together.</summary>
public sealed record DeleteAppointmentsRequest
{
    [Required, MinLength(1), MaxLength(500)]
    public required int[] AppointmentIds { get; init; }
}

/// <summary>Appointments to move together, preserving their relative date and time offsets.</summary>
public sealed record MoveAppointmentsRequest
{
    [Required, MinLength(1), MaxLength(500)]
    public required int[] AppointmentIds { get; init; }

    [Required]
    public required DateTimeOffset FirstAppointmentStartsAt { get; init; }
}

/// <summary>Number of appointments affected by a bulk operation.</summary>
public sealed record BulkAppointmentsResponse(int AffectedCount);

/// <summary>Worker data used to identify appointments in the schedule.</summary>
public sealed record WorkerResponse(int Id, string Name, string Color);

/// <summary>Service data used when creating or editing an appointment.</summary>
public sealed record ServiceResponse(int Id, string Name, int DurationMinutes);

/// <summary>Holiday data displayed in the schedule.</summary>
public sealed record HolidayResponse(DateTimeOffset Date, string Name);

/// <summary>Schedule settings used to render the weekly calendar.</summary>
public sealed record ScheduleSettingsResponse(
    TimeOnly StartTime,
    TimeOnly EndTime,
    string[] WorkDays,
    int WorkerCount);

/// <summary>Schedule and worker-count values to save.</summary>
public sealed record UpdateScheduleSettingsRequest
{
    [Required]
    public required TimeOnly StartTime { get; init; }

    [Required]
    public required TimeOnly EndTime { get; init; }

    [Required, MinLength(1), MaxLength(7)]
    public required string[] WorkDays { get; init; }

    [Range(1, 20)]
    public required int WorkerCount { get; init; }
}

/// <summary>Worker details to save; omitting the color keeps its existing value.</summary>
public sealed record UpdateWorkerRequest
{
    [Required, StringLength(100)]
    public required string Name { get; init; }

    [RegularExpression(@"^#[0-9A-Fa-f]{6}$")]
    public string? Color { get; init; }
}

/// <summary>Service name and appointment duration to create or update.</summary>
public sealed record SaveServiceRequest
{
    [Required, StringLength(100)]
    public required string Name { get; init; }

    [Range(15, 480)]
    public required int DurationMinutes { get; init; }
}

/// <summary>Holiday name to save for the date in the route.</summary>
public sealed record SaveHolidayRequest
{
    [Required, StringLength(100)]
    public required string Name { get; init; }
}
