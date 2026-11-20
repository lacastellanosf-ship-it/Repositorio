using Citaspp.Web.Contracts;
using Citaspp.Web.Middleware;
using Citaspp.Web.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Citaspp.Web.Endpoints;

public static class AppointmentEndpoints
{
    public static void MapAppointmentEndpoints(this WebApplication app)
    {
        var appointments = app.MapGroup("/api/appointments")
            .WithTags("Appointments")
            .AddEndpointFilter<RequestValidationFilter>();

        appointments.MapGet("/", (Results<Ok<AppointmentResponse[]>, BadRequest> (
            DateTimeOffset start,
            DateTimeOffset end,
            IAppointmentService service,
            CancellationToken cancellationToken) =>
            start >= end
                ? TypedResults.BadRequest()
                : TypedResults.Ok(service.GetAppointments(start, end)))
            )
            .WithName("GetAppointments")
            .WithSummary("Get appointments within a date range")
            .WithDescription("Returns appointments from start inclusive to end exclusive.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        appointments.MapGet("/{id:int}", (Results<Ok<AppointmentResponse>, NotFound> (
            int id,
            IAppointmentService service,
            CancellationToken cancellationToken) =>
            service.GetAppointment(id) is { } appointment
                ? TypedResults.Ok(appointment)
                : TypedResults.NotFound()))
            .WithName("GetAppointmentById")
            .WithSummary("Get an appointment by ID")
            .Produces<AppointmentResponse>()
            .Produces(StatusCodes.Status404NotFound);

        appointments.MapPost("/", (
            CreateAppointmentRequest request,
            IAppointmentService service,
            CancellationToken cancellationToken) =>
        {
            var appointment = service.CreateAppointment(request);
            return TypedResults.Created($"/api/appointments/{appointment.Id}", appointment);
        })
            .WithName("CreateAppointment")
            .WithSummary("Create an appointment")
            .Produces<AppointmentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        appointments.MapPut("/{id:int}", (Results<Ok<AppointmentResponse>, NotFound> (
            int id,
            UpdateAppointmentRequest request,
            IAppointmentService service,
            CancellationToken cancellationToken) =>
            service.UpdateAppointment(id, request) is { } appointment
                ? TypedResults.Ok(appointment)
                : TypedResults.NotFound()))
            .WithName("UpdateAppointment")
            .WithSummary("Update an appointment")
            .Produces<AppointmentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        appointments.MapDelete("/{id:int}", (Results<NoContent, NotFound> (
            int id,
            IAppointmentService service,
            CancellationToken cancellationToken) =>
            service.DeleteAppointment(id)
                ? TypedResults.NoContent()
                : TypedResults.NotFound()))
            .WithName("DeleteAppointment")
            .WithSummary("Delete an appointment")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        appointments.MapPost("/bulk-delete", (
            DeleteAppointmentsRequest request,
            IAppointmentService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(new BulkAppointmentsResponse(service.DeleteAppointments(request.AppointmentIds))))
            .WithName("DeleteAppointments")
            .WithSummary("Delete multiple appointments atomically")
            .Produces<BulkAppointmentsResponse>()
            .ProducesValidationProblem();

        appointments.MapPost("/move", (
            MoveAppointmentsRequest request,
            IAppointmentService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(new BulkAppointmentsResponse(service.MoveAppointments(request))))
            .WithName("MoveAppointments")
            .WithSummary("Move multiple appointments while preserving their relative offsets")
            .Produces<BulkAppointmentsResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGet("/api/workers", (
            IAppointmentService service,
            CancellationToken cancellationToken) => TypedResults.Ok(service.GetWorkers()))
            .WithName("GetWorkers")
            .WithTags("Schedule")
            .WithSummary("Get workers and their calendar colors");

        app.MapGet("/api/services", (
            IAppointmentService service,
            CancellationToken cancellationToken) => TypedResults.Ok(service.GetServices()))
            .WithName("GetServices")
            .WithTags("Schedule")
            .WithSummary("Get available appointment services");

        app.MapGet("/api/holidays", (
            IAppointmentService service,
            CancellationToken cancellationToken) => TypedResults.Ok(service.GetHolidays()))
            .WithName("GetHolidays")
            .WithTags("Schedule")
            .WithSummary("Get configured holidays");

        app.MapGet("/api/settings", (
            IAppointmentService service,
            CancellationToken cancellationToken) => TypedResults.Ok(service.GetSettings()))
            .WithName("GetScheduleSettings")
            .WithTags("Schedule")
            .WithSummary("Get weekly schedule settings");
    }
}
